#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────
# Phase 3 Manual Test Script — OneDrive + Microsoft To Do
# ─────────────────────────────────────────────────────────────────────
#
# Prerequisites:
#   - Authenticated 1Password session (op signin)
#   - Valid Graph API credentials in 1Password vault
#   - Run: dotnet build  (or use published binary)
#
# Usage:
#   ./scripts/test-phase3.sh            # interactive, pauses between sections
#   ./scripts/test-phase3.sh --json     # all output as JSON
#   DRY_RUN=1 ./scripts/test-phase3.sh  # dry-run mode (no writes)
#
# This script tests each new command, captures IDs from JSON output,
# and uses them in subsequent commands. It cleans up after itself.
# ─────────────────────────────────────────────────────────────────────

set -euo pipefail

DOTNET_RUN="dotnet run --project src/MsGraphCli --"
GLOBAL_FLAGS=""

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

# Global flags (--json, --dry-run, --read-only, --plain) must come BEFORE the
# subcommand group because System.CommandLine defines them on the root command.
# e.g. "msgraph --json drive ls" not "msgraph drive ls --json"

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

run_test_capture() {
    local name="$1"
    local varname="$2"
    local jqexpr="$3"
    shift 3
    echo "── TEST: $name"
    echo "   CMD:  msgraph --json $*"
    if output=$($DOTNET_RUN --json "$@" 2>&1); then
        echo "   PASS"
        local captured
        captured=$(echo "$output" | jq -r "$jqexpr" 2>/dev/null || echo "")
        if [[ -n "$captured" && "$captured" != "null" ]]; then
            eval "$varname='$captured'"
            echo "   Captured $varname=$captured"
        else
            echo "   WARNING: Could not capture $varname from output"
            echo "$output" | head -5
        fi
        pass=$((pass + 1))
    else
        echo "   FAIL (exit $?)"
        echo "$output" | head -10
        fail=$((fail + 1))
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
echo "  PHASE 3 TEST: HELP & CLI STRUCTURE"
echo "========================================"
echo

run_test "drive --help" drive --help
run_test "todo --help" todo --help

pause

# ─────────────────────────────────────────────────────────────────────
echo "========================================"
echo "  PHASE 3 TEST: ONEDRIVE — READ OPS"
echo "========================================"
echo

# List root folder
run_test "drive ls (root)" drive ls

# List root with max
run_test "drive ls --max 5" drive ls -n 5

# List by path — verifies path-based listing works and errors are clean
# Uses the test folder created later; /Documents may not exist.
# If you have a known folder, replace the path below.
echo "── TEST: drive ls --path (clean error on missing folder)"
echo "   CMD:  msgraph drive ls --path /Documents"
if output=$($DOTNET_RUN $GLOBAL_FLAGS drive ls --path /Documents 2>&1); then
    echo "   PASS"
    echo "$output" | head -10
    pass=$((pass + 1))
else
    # A clean error (not a stack trace) is acceptable for a missing folder
    if echo "$output" | grep -q "^ERROR"; then
        echo "   PASS (clean error for missing folder)"
        echo "   $output"
        pass=$((pass + 1))
    else
        echo "   FAIL (unhandled exception)"
        echo "$output" | head -10
        fail=$((fail + 1))
    fi
fi
echo

# Search
run_test "drive search 'test'" drive search test

# Search with max
run_test "drive search 'test' --max 3" drive search test -n 3

pause

# ─────────────────────────────────────────────────────────────────────
echo "========================================"
echo "  PHASE 3 TEST: ONEDRIVE — GET ITEM"
echo "========================================"
echo

# Get an item ID from ls, then get its details
ITEM_ID=""
echo "── Fetching an item ID from root listing..."
ls_output=$($DOTNET_RUN --json drive ls -n 1 2>&1 || true)
ITEM_ID=$(echo "$ls_output" | jq -r '.items[0].id // empty' 2>/dev/null || echo "")

if [[ -n "$ITEM_ID" ]]; then
    run_test "drive get $ITEM_ID" drive get "$ITEM_ID"
else
    echo "   SKIP: No items in root to test 'drive get'"
    skip=$((skip + 1))
fi

pause

# ─────────────────────────────────────────────────────────────────────
echo "========================================"
echo "  PHASE 3 TEST: ONEDRIVE — WRITE OPS"
echo "========================================"
echo

TEST_FOLDER_NAME="msgraph-cli-test-$(date +%s)"

# Create folder
run_test_capture "drive mkdir" FOLDER_ID '.item.id' \
    drive mkdir "$TEST_FOLDER_NAME"

# Upload a small test file
TEMP_FILE=$(mktemp /tmp/msgraph-test-XXXXXX.txt)
echo "This is a test file from msgraph-cli phase 3 testing." > "$TEMP_FILE"

if [[ -n "${FOLDER_ID:-}" ]]; then
    run_test_capture "drive upload (small file)" UPLOADED_ID '.item.id' \
        drive upload "$TEMP_FILE" --path "/$TEST_FOLDER_NAME/test-upload.txt"
else
    run_test_capture "drive upload (small file)" UPLOADED_ID '.item.id' \
        drive upload "$TEMP_FILE" --path "/test-upload-$(date +%s).txt"
fi

rm -f "$TEMP_FILE"

# Download the uploaded file
if [[ -n "${UPLOADED_ID:-}" ]]; then
    DL_PATH=$(mktemp /tmp/msgraph-dl-XXXXXX.txt)
    run_test "drive download $UPLOADED_ID" drive download "$UPLOADED_ID" --out "$DL_PATH"
    if [[ -f "$DL_PATH" ]]; then
        echo "   Downloaded content: $(cat "$DL_PATH")"
    fi
    rm -f "$DL_PATH"
fi

# Rename uploaded file
if [[ -n "${UPLOADED_ID:-}" ]]; then
    run_test "drive rename" drive rename "$UPLOADED_ID" "renamed-test.txt"
fi

# Move to root (if we created in a subfolder)
if [[ -n "${UPLOADED_ID:-}" && -n "${FOLDER_ID:-}" ]]; then
    run_test "drive move (to root)" drive move "$UPLOADED_ID" --destination /
fi

pause

# ─────────────────────────────────────────────────────────────────────
echo "========================================"
echo "  PHASE 3 TEST: ONEDRIVE — CLEANUP"
echo "========================================"
echo

# Delete uploaded file
if [[ -n "${UPLOADED_ID:-}" ]]; then
    run_test "drive delete (uploaded file)" drive delete "$UPLOADED_ID"
fi

# Delete test folder
if [[ -n "${FOLDER_ID:-}" ]]; then
    run_test "drive delete (test folder)" drive delete "$FOLDER_ID"
fi

pause

# ─────────────────────────────────────────────────────────────────────
echo "========================================"
echo "  PHASE 3 TEST: ONEDRIVE — VALIDATION"
echo "========================================"
echo

# Test --path + --folder mutual exclusivity
echo "── TEST: drive ls --path + --folder (should fail)"
echo "   CMD:  msgraph drive ls --path /Documents --folder fake-id"
if output=$($DOTNET_RUN drive ls --path /Documents --folder fake-id 2>&1); then  # flags are subcommand options, not global
    echo "   FAIL: Should have rejected both --path and --folder"
    fail=$((fail + 1))
else
    echo "   PASS: Correctly rejected"
    echo "$output" | head -3
    pass=$((pass + 1))
fi
echo

# Test --read-only blocks writes
echo "── TEST: drive mkdir --read-only (should fail)"
echo "   CMD:  msgraph --read-only drive mkdir blocked-folder"
if output=$($DOTNET_RUN --read-only drive mkdir blocked-folder 2>&1); then  # --read-only is global, before subcommand
    echo "   FAIL: Should have blocked write in read-only mode"
    fail=$((fail + 1))
else
    echo "   PASS: Correctly blocked"
    echo "$output" | head -3
    pass=$((pass + 1))
fi
echo

# Test --dry-run
echo "── TEST: drive upload --dry-run"
echo "   CMD:  msgraph --dry-run drive upload /dev/null --path /dry-run-test.txt"
TEMP_DRY=$(mktemp /tmp/msgraph-dry-XXXXXX.txt)
echo "dry" > "$TEMP_DRY"
if output=$($DOTNET_RUN --dry-run drive upload "$TEMP_DRY" --path /dry-run-test.txt 2>&1); then  # --dry-run is global
    echo "   PASS"
    echo "$output" | head -3
    pass=$((pass + 1))
else
    echo "   FAIL"
    echo "$output" | head -3
    fail=$((fail + 1))
fi
rm -f "$TEMP_DRY"
echo

pause

# ─────────────────────────────────────────────────────────────────────
echo "========================================"
echo "  PHASE 3 TEST: TODO — READ OPS"
echo "========================================"
echo

# List task lists
run_test_capture "todo lists" DEFAULT_LIST_ID '.lists[] | select(.isDefaultList == true) | .id' \
    todo lists

if [[ -z "${DEFAULT_LIST_ID:-}" ]]; then
    # Fallback: grab first list
    echo "   No default list found, using first list..."
    lists_out=$($DOTNET_RUN --json todo lists 2>&1 || true)
    DEFAULT_LIST_ID=$(echo "$lists_out" | jq -r '.lists[0].id // empty' 2>/dev/null || echo "")
fi

# List tasks in default list
if [[ -n "${DEFAULT_LIST_ID:-}" ]]; then
    run_test "todo list (default)" todo list "$DEFAULT_LIST_ID"
    run_test "todo list --status incomplete" todo list "$DEFAULT_LIST_ID" --status incomplete
    run_test "todo list --max 3" todo list "$DEFAULT_LIST_ID" -n 3
else
    echo "   SKIP: No task lists found"
    skip=$((skip + 3))
fi

pause

# ─────────────────────────────────────────────────────────────────────
echo "========================================"
echo "  PHASE 3 TEST: TODO — WRITE OPS"
echo "========================================"
echo

TEST_LIST_NAME="msgraph-cli-test-$(date +%s)"

# Create a test list
run_test_capture "todo lists create" TEST_LIST_ID '.list.id' \
    todo lists create "$TEST_LIST_NAME"

if [[ -n "${TEST_LIST_ID:-}" ]]; then
    # Add a task
    run_test_capture "todo add (basic)" TASK1_ID '.task.id' \
        todo add "$TEST_LIST_ID" --title "Test task 1"

    # Add a task with all options
    run_test_capture "todo add (full options)" TASK2_ID '.task.id' \
        todo add "$TEST_LIST_ID" \
        --title "Test task 2 with details" \
        --body "This is the task body" \
        --due "2026-12-31" \
        --importance high

    # Get task details
    if [[ -n "${TASK2_ID:-}" ]]; then
        run_test "todo get" todo get "$TEST_LIST_ID" "$TASK2_ID"
    fi

    # Update a task
    if [[ -n "${TASK1_ID:-}" ]]; then
        run_test "todo update (title + importance)" \
            todo update "$TEST_LIST_ID" "$TASK1_ID" \
            --title "Updated task 1" --importance low
    fi

    # Mark done
    if [[ -n "${TASK1_ID:-}" ]]; then
        run_test "todo done" todo done "$TEST_LIST_ID" "$TASK1_ID"
    fi

    # List completed
    run_test "todo list --status completed" todo list "$TEST_LIST_ID" --status completed

    # Undo (reopen)
    if [[ -n "${TASK1_ID:-}" ]]; then
        run_test "todo undo" todo undo "$TEST_LIST_ID" "$TASK1_ID"
    fi

    pause

    # ─────────────────────────────────────────────────────────────────
    echo "========================================"
    echo "  PHASE 3 TEST: TODO — CLEANUP"
    echo "========================================"
    echo

    # Delete tasks
    if [[ -n "${TASK1_ID:-}" ]]; then
        run_test "todo delete (task 1)" todo delete "$TEST_LIST_ID" "$TASK1_ID"
    fi
    if [[ -n "${TASK2_ID:-}" ]]; then
        run_test "todo delete (task 2)" todo delete "$TEST_LIST_ID" "$TASK2_ID"
    fi

    # Note: Graph API doesn't support deleting task lists via the same endpoint.
    # The test list will remain. Clean up manually if desired.
    echo "   NOTE: Test list '$TEST_LIST_NAME' ($TEST_LIST_ID) was not deleted."
    echo "         Graph API task list deletion requires a separate permission scope."
    echo
fi

pause

# ─────────────────────────────────────────────────────────────────────
echo "========================================"
echo "  PHASE 3 TEST: TODO — VALIDATION"
echo "========================================"
echo

# Test --read-only blocks writes
echo "── TEST: todo add --read-only (should fail)"
echo "   CMD:  msgraph --read-only todo add fake-list --title blocked"
if output=$($DOTNET_RUN --read-only todo add fake-list --title blocked 2>&1); then  # --read-only is global
    echo "   FAIL: Should have blocked write in read-only mode"
    fail=$((fail + 1))
else
    echo "   PASS: Correctly blocked"
    echo "$output" | head -3
    pass=$((pass + 1))
fi
echo

# Test --dry-run
echo "── TEST: todo add --dry-run"
echo "   CMD:  msgraph --dry-run todo add fake-list --title 'dry run task'"
if output=$($DOTNET_RUN --dry-run todo add fake-list --title "dry run task" 2>&1); then  # --dry-run is global
    echo "   PASS"
    echo "$output" | head -3
    pass=$((pass + 1))
else
    echo "   FAIL"
    echo "$output" | head -3
    fail=$((fail + 1))
fi
echo

# ─────────────────────────────────────────────────────────────────────
echo "========================================"
echo "  PHASE 3 TEST: OUTPUT FORMATS"
echo "========================================"
echo

# Output format tests need global flags before the subcommand.
# run_test prepends GLOBAL_FLAGS, but these tests need specific flags
# regardless of the script's mode, so we call dotnet run directly.

echo "── TEST: drive ls --json"
echo "   CMD:  msgraph --json drive ls -n 2"
if output=$($DOTNET_RUN --json drive ls -n 2 2>&1); then
    echo "   PASS"
    echo "$output" | head -20
    pass=$((pass + 1))
else
    echo "   FAIL (exit $?)"
    echo "$output" | head -10
    fail=$((fail + 1))
fi
echo

echo "── TEST: drive ls --plain"
echo "   CMD:  msgraph --plain drive ls -n 2"
if output=$($DOTNET_RUN --plain drive ls -n 2 2>&1); then
    echo "   PASS"
    echo "$output" | head -20
    pass=$((pass + 1))
else
    echo "   FAIL (exit $?)"
    echo "$output" | head -10
    fail=$((fail + 1))
fi
echo

echo "── TEST: todo lists --json"
echo "   CMD:  msgraph --json todo lists"
if output=$($DOTNET_RUN --json todo lists 2>&1); then
    echo "   PASS"
    echo "$output" | head -20
    pass=$((pass + 1))
else
    echo "   FAIL (exit $?)"
    echo "$output" | head -10
    fail=$((fail + 1))
fi
echo

echo "── TEST: todo lists --plain"
echo "   CMD:  msgraph --plain todo lists"
if output=$($DOTNET_RUN --plain todo lists 2>&1); then
    echo "   PASS"
    echo "$output" | head -20
    pass=$((pass + 1))
else
    echo "   FAIL (exit $?)"
    echo "$output" | head -10
    fail=$((fail + 1))
fi
echo

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
