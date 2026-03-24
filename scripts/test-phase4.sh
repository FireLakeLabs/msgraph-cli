#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────
# Phase 4 Manual Test Script — Excel Workbook + Document Operations
# ─────────────────────────────────────────────────────────────────────
#
# Prerequisites:
#   - Authenticated 1Password session (op signin)
#   - Valid Graph API credentials in 1Password vault
#   - Run: dotnet build  (or use published binary)
#   - Run: python3 scripts/create-test-fixtures.py  (generates test fixtures)
#
# Optional environment variables (override auto-uploaded fixtures):
#   EXCEL_ITEM_ID       — drive item ID of an .xlsx file (sheets/get/update tests)
#   EXCEL_APPEND_ITEM_ID — drive item ID of an .xlsx with Table1 (append test)
#   DOCX_ITEM_ID        — drive item ID of a .docx file (export/cat tests)
#   PPTX_ITEM_ID        — drive item ID of a .pptx file (cat test)
#
# Usage:
#   ./scripts/test-phase4.sh            # interactive, pauses between sections
#   ./scripts/test-phase4.sh --json     # all output as JSON
#   DRY_RUN=1 ./scripts/test-phase4.sh  # dry-run mode (no writes)
#
# ─────────────────────────────────────────────────────────────────────

set -euo pipefail

DOTNET_RUN="dotnet run --project src/MsGraphCli --"
GLOBAL_FLAGS=""

# ── Test file IDs — set these to known files in your OneDrive ──
EXCEL_ITEM_ID="${EXCEL_ITEM_ID:-}"
EXCEL_APPEND_ITEM_ID="${EXCEL_APPEND_ITEM_ID:-}"
DOCX_ITEM_ID="${DOCX_ITEM_ID:-}"
PPTX_ITEM_ID="${PPTX_ITEM_ID:-}"

if [[ "${1:-}" == "--json" ]]; then
    GLOBAL_FLAGS="$GLOBAL_FLAGS --json"
fi

if [[ "${DRY_RUN:-}" == "1" ]]; then
    GLOBAL_FLAGS="$GLOBAL_FLAGS --dry-run"
    echo "=== DRY RUN MODE — no changes will be made ==="
    echo
fi

pass=0
fail=0
skip=0

# ── Fixture upload/cleanup ──
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
FIXTURE_DIR="${SCRIPT_DIR}/../tests/fixtures"
UPLOAD_PREFIX="/msgraph-cli-test-fixtures"
UPLOADED_IDS=()

upload_fixture() {
    local file="$1"
    local remote_name="$2"
    local id
    id=$($DOTNET_RUN --json drive upload "$file" --path "${UPLOAD_PREFIX}/${remote_name}" 2>/dev/null \
        | python3 -c "import sys,json; print(json.load(sys.stdin)['item']['id'])")
    UPLOADED_IDS+=("$id")
    echo "$id"
}

cleanup_fixtures() {
    if [[ ${#UPLOADED_IDS[@]} -gt 0 ]]; then
        echo
        echo "── Cleaning up uploaded fixtures..."
        for id in "${UPLOADED_IDS[@]}"; do
            $DOTNET_RUN drive delete "$id" 2>/dev/null || true
        done
    fi
}
trap cleanup_fixtures EXIT

run_test() {
    local name="$1"
    shift
    echo "── TEST: $name"
    echo "   CMD:  msgraph $GLOBAL_FLAGS $*"
    if output=$($DOTNET_RUN $GLOBAL_FLAGS "$@" 2>&1); then
        echo "   PASS"
        echo "$output" | head -20
        pass=$((pass + 1))
    else
        echo "   FAIL (exit $?)"
        echo "$output" | head -10
        fail=$((fail + 1))
    fi
    echo
}

run_test_expect_fail() {
    local name="$1"
    local expected_exit="$2"
    shift 2
    echo "── TEST: $name (expect exit $expected_exit)"
    echo "   CMD:  msgraph $GLOBAL_FLAGS $*"
    if output=$($DOTNET_RUN $GLOBAL_FLAGS "$@" 2>&1); then
        echo "   FAIL (expected failure but succeeded)"
        fail=$((fail + 1))
    else
        local actual_exit=$?
        if [[ "$actual_exit" -eq "$expected_exit" ]]; then
            echo "   PASS (exit $actual_exit as expected)"
            pass=$((pass + 1))
        else
            echo "   FAIL (exit $actual_exit, expected $expected_exit)"
            fail=$((fail + 1))
        fi
    fi
    echo
}

pause() {
    if [[ -z "$GLOBAL_FLAGS" && -t 0 ]]; then
        read -rp "Press Enter to continue (Ctrl+C to abort)..."
        echo
    fi
}

# ─────────────────────────────────────────────────────────────────────
echo "========================================"
echo "  PHASE 4 TEST: HELP & CLI STRUCTURE"
echo "========================================"
echo

run_test "excel --help" excel --help
run_test "docs --help" docs --help
run_test "excel sheets --help" excel sheets --help
run_test "excel get --help" excel get --help
run_test "excel update --help" excel update --help
run_test "excel append --help" excel append --help
run_test "docs export --help" docs export --help
run_test "docs cat --help" docs cat --help

pause

# ─────────────────────────────────────────────────────────────────────
echo "========================================"
echo "  PHASE 4 TEST: READ-ONLY ENFORCEMENT"
echo "========================================"
echo

run_test_expect_fail "read-only blocks excel update" 10 --read-only excel update fake-id --sheet Sheet1 --range A1 --values '[["x"]]'
run_test_expect_fail "read-only blocks excel append" 10 --read-only excel append fake-id --sheet Sheet1 --table Table1 --values '[["x"]]'

pause

# ─────────────────────────────────────────────────────────────────────
echo "========================================"
echo "  PHASE 4 TEST: DRY-RUN MODE"
echo "========================================"
echo

run_test "dry-run excel update" --dry-run excel update fake-id --sheet Sheet1 --range A1:B1 --values '[["X","Y"]]'
run_test "dry-run excel append" --dry-run excel append fake-id --sheet Sheet1 --table Table1 --values '[["Bob",88]]'

pause

# ─────────────────────────────────────────────────────────────────────
echo "========================================"
echo "  PHASE 4 TEST: EXCEL OPERATIONS"
echo "========================================"
echo

if [[ -z "$EXCEL_ITEM_ID" && -f "$FIXTURE_DIR/test-workbook.xlsx" ]]; then
    echo "── Uploading test-workbook.xlsx fixture..."
    EXCEL_ITEM_ID=$(upload_fixture "$FIXTURE_DIR/test-workbook.xlsx" "test-workbook.xlsx")
    echo "   Uploaded as $EXCEL_ITEM_ID"
    echo
fi

if [[ -z "$EXCEL_ITEM_ID" ]]; then
    echo "EXCEL_ITEM_ID not set and no fixture available — skipping Excel live tests (sheets/get/update)"
    skip=$((skip + 4))
else
    run_test "excel sheets" excel sheets "$EXCEL_ITEM_ID"
    run_test "excel get range" excel get "$EXCEL_ITEM_ID" --sheet Sheet1 --range "A1:D5"

    if [[ "${DRY_RUN:-}" != "1" ]]; then
        run_test "excel update range" excel update "$EXCEL_ITEM_ID" --sheet Sheet1 --range "Z1:Z1" --values '[["test"]]'
    else
        skip=$((skip + 1))
    fi

    run_test "excel sheets (beta)" --beta excel sheets "$EXCEL_ITEM_ID"
fi

# ── Excel append (requires a workbook with a real table) ──
if [[ -z "$EXCEL_APPEND_ITEM_ID" && -f "$FIXTURE_DIR/append-test.xlsx" ]]; then
    echo "── Uploading append-test.xlsx fixture..."
    EXCEL_APPEND_ITEM_ID=$(upload_fixture "$FIXTURE_DIR/append-test.xlsx" "append-test.xlsx")
    echo "   Uploaded as $EXCEL_APPEND_ITEM_ID"
    echo
fi

if [[ -n "$EXCEL_APPEND_ITEM_ID" ]]; then
    if [[ "${DRY_RUN:-}" != "1" ]]; then
        run_test "excel append" excel append "$EXCEL_APPEND_ITEM_ID" \
            --sheet Sheet1 --table Table1 --values '[["TestUser",99,"A+"]]'
    else
        skip=$((skip + 1))
    fi
else
    echo "EXCEL_APPEND_ITEM_ID not set and no fixture available — skipping append test"
    skip=$((skip + 1))
fi

pause

# ─────────────────────────────────────────────────────────────────────
echo "========================================"
echo "  PHASE 4 TEST: DOCUMENT OPERATIONS"
echo "========================================"
echo

if [[ -z "$DOCX_ITEM_ID" && -f "$FIXTURE_DIR/test-doc.docx" ]]; then
    echo "── Uploading test-doc.docx fixture..."
    DOCX_ITEM_ID=$(upload_fixture "$FIXTURE_DIR/test-doc.docx" "test-doc.docx")
    echo "   Uploaded as $DOCX_ITEM_ID"
    echo
fi

if [[ -z "$DOCX_ITEM_ID" ]]; then
    echo "DOCX_ITEM_ID not set and no fixture available — skipping document live tests"
    skip=$((skip + 3))
else
    EXPORT_PATH="/tmp/test-export-phase4-$$.pdf"
    run_test "docs export to PDF" docs export "$DOCX_ITEM_ID" --format pdf --out "$EXPORT_PATH"

    if [[ -f "$EXPORT_PATH" ]]; then
        echo "   Exported file size: $(wc -c < "$EXPORT_PATH") bytes"
        rm -f "$EXPORT_PATH"
    fi

    run_test "docs cat (markdown)" docs cat "$DOCX_ITEM_ID"
    run_test "docs cat --json" --json docs cat "$DOCX_ITEM_ID"
fi

if [[ -z "$PPTX_ITEM_ID" && -f "$FIXTURE_DIR/test-slide.pptx" ]]; then
    echo "── Uploading test-slide.pptx fixture..."
    PPTX_ITEM_ID=$(upload_fixture "$FIXTURE_DIR/test-slide.pptx" "test-slide.pptx")
    echo "   Uploaded as $PPTX_ITEM_ID"
    echo
fi

if [[ -n "${PPTX_ITEM_ID:-}" ]]; then
    run_test "docs cat (pptx)" docs cat "$PPTX_ITEM_ID"
else
    echo "PPTX_ITEM_ID not set and no fixture available — skipping pptx tests"
    skip=$((skip + 1))
fi

pause

# ─────────────────────────────────────────────────────────────────────
echo "========================================"
echo "  RESULTS"
echo "========================================"
echo
echo "  Passed:  $pass"
echo "  Failed:  $fail"
echo "  Skipped: $skip"
echo

if [[ $fail -gt 0 ]]; then
    echo "  SOME TESTS FAILED"
    exit 1
else
    echo "  ALL TESTS PASSED"
fi
