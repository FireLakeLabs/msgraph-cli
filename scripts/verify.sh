#!/usr/bin/env bash
set -euo pipefail

# Smoke test suite for msgraph-cli.
# Usage: ./scripts/verify.sh [path-to-msgraph-binary]

MSGRAPH="${1:-msgraph}"
PASSED=0
FAILED=0

check() {
    local desc="$1"
    shift
    if "$@" > /dev/null 2>&1; then
        echo "  PASS: $desc"
        PASSED=$((PASSED + 1))
    else
        echo "  FAIL: $desc"
        FAILED=$((FAILED + 1))
    fi
}

check_output() {
    local desc="$1"
    local expected="$2"
    shift 2
    local output
    output=$("$@" 2>/dev/null) || true
    if echo "$output" | grep -q "$expected"; then
        echo "  PASS: $desc"
        PASSED=$((PASSED + 1))
    else
        echo "  FAIL: $desc (expected '$expected' in output)"
        FAILED=$((FAILED + 1))
    fi
}

check_regex() {
    local desc="$1"
    local pattern="$2"
    shift 2
    local output
    output=$("$@" 2>/dev/null) || true
    if echo "$output" | grep -Eq "$pattern"; then
        echo "  PASS: $desc"
        PASSED=$((PASSED + 1))
    else
        echo "  FAIL: $desc (output did not match pattern)"
        FAILED=$((FAILED + 1))
    fi
}

echo "=== msgraph-cli smoke tests ==="
echo "Binary: $MSGRAPH"
echo ""

# Basic commands
check "version" $MSGRAPH version
check_regex "version output format" '[0-9]+\.[0-9]+\.[0-9]+' $MSGRAPH version
check "help" $MSGRAPH --help
check "config path" $MSGRAPH config path
check "config list" $MSGRAPH config list
check "config list --json" $MSGRAPH --json config list
check "config get" $MSGRAPH config get defaultOutputFormat
check "completions bash" $MSGRAPH completions bash
check "completions zsh" $MSGRAPH completions zsh
check "completions fish" $MSGRAPH completions fish
check "auth scopes" $MSGRAPH auth scopes

# Auth (requires 1Password)
if $MSGRAPH auth status > /dev/null 2>&1; then
    echo ""
    echo "--- Live API tests (1Password available) ---"
    check "auth status" $MSGRAPH auth status
    check_output "auth status --json" "isAuthenticated" $MSGRAPH --json auth status
    check "mail list" $MSGRAPH --json mail list --max 1
    check "calendar list" $MSGRAPH --json calendar list
    check "drive ls" $MSGRAPH --json drive ls --max 1
    check "todo lists" $MSGRAPH --json todo lists
else
    echo ""
    echo "--- Skipping live API tests (1Password not available) ---"
fi

echo ""
echo "=== Results: $PASSED passed, $FAILED failed ==="

if [[ $FAILED -gt 0 ]]; then
    exit 1
fi
