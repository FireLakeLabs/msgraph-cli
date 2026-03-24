#!/usr/bin/env bash
set -uo pipefail

# Generate command reference documentation from msgraph --help output.
# Usage: ./scripts/generate-docs.sh [path-to-msgraph-binary]

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
OUT="$REPO_ROOT/docs/command-reference.md"

if [[ -n "${1:-}" ]]; then
    MSGRAPH="$1"
else
    MSGRAPH="$REPO_ROOT/publish/linux-arm64/msgraph"
    if [[ ! -x "$MSGRAPH" ]]; then
        echo "No published binary found. Build first with: scripts/publish.sh"
        exit 1
    fi
fi

VERSION=$("$MSGRAPH" version 2>/dev/null || echo "unknown")

get_subcommands() {
    local cmd="$1"
    "$MSGRAPH" $cmd --help 2>/dev/null \
        | sed -n '/Commands:/,$ p' \
        | tail -n +2 \
        | awk 'NF {print $1}' \
        || true
}

write_help() {
    local cmd="$1"
    local title="$2"
    echo "$title"
    echo ""
    echo '```'
    "$MSGRAPH" $cmd --help 2>/dev/null || true
    echo '```'
    echo ""
}

: > "$OUT"  # Truncate

{
    echo "# msgraph-cli Command Reference"
    echo ""
    echo "**Version:** $VERSION"
    echo ""
    echo "---"
    echo ""
    echo "## Global Options"
    echo ""
    echo '```'
    "$MSGRAPH" --help 2>/dev/null | sed -n '/Options:/,/Commands:/p' | head -n -1 || true
    echo '```'
    echo ""
    echo "## Commands"
    echo ""
    echo '```'
    "$MSGRAPH" --help 2>/dev/null | sed -n '/Commands:/,$ p' || true
    echo '```'
    echo ""
    echo "---"
    echo ""
} >> "$OUT"

CMD_GROUPS=("auth" "mail" "calendar" "drive" "todo" "excel" "docs" "config" "completions")

for group in "${CMD_GROUPS[@]}"; do
    {
        write_help "$group" "## msgraph $group"

        subcommands=$(get_subcommands "$group")
        for sub in $subcommands; do
            [[ -z "$sub" ]] && continue
            write_help "$group $sub" "### msgraph $group $sub"
        done

        echo "---"
        echo ""
    } >> "$OUT"
done

echo "Generated: $OUT"
