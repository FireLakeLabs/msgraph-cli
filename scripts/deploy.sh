#!/usr/bin/env bash
set -euo pipefail

# Deploy msgraph-cli to a remote host.
# Usage: ./scripts/deploy.sh <host> [rid]
#
# Examples:
#   ./scripts/deploy.sh cheesy-mc-agent-host linux-arm64
#   ./scripts/deploy.sh user@192.168.1.100 linux-x64

if [[ $# -lt 1 ]]; then
    echo "Usage: $0 <host> [rid]"
    echo "  host  SSH target (e.g., cheesy-mc-agent-host)"
    echo "  rid   Runtime ID (default: linux-arm64)"
    exit 1
fi

HOST="$1"
RID="${2:-linux-arm64}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
PUBLISH_DIR="$REPO_ROOT/publish/$RID"
BINARY="$PUBLISH_DIR/msgraph"
REMOTE_DIR="/usr/local/bin"

echo "=== Deploy msgraph-cli ==="
echo "Host: $HOST"
echo "RID:  $RID"
echo ""

# Step 1: Publish
echo "--- Publishing for $RID ---"
dotnet publish "$REPO_ROOT/src/FireLakeLabs.MsGraphCli" \
    -c Release \
    -r "$RID" \
    --self-contained \
    -p:PublishSingleFile=true \
    -o "$PUBLISH_DIR"

SIZE=$(du -h "$BINARY" | cut -f1)
echo "Binary: $BINARY ($SIZE)"
echo ""

# Step 2: Copy to remote
echo "--- Copying to $HOST ---"
scp "$BINARY" "$HOST:/tmp/msgraph"
ssh "$HOST" "sudo mv /tmp/msgraph $REMOTE_DIR/msgraph && sudo chmod +x $REMOTE_DIR/msgraph"
echo "Installed to $HOST:$REMOTE_DIR/msgraph"
echo ""

# Step 3: Verify remotely
echo "--- Running verification on $HOST ---"
scp "$SCRIPT_DIR/verify.sh" "$HOST:/tmp/verify-msgraph.sh"
ssh "$HOST" "chmod +x /tmp/verify-msgraph.sh && /tmp/verify-msgraph.sh $REMOTE_DIR/msgraph"

echo ""
echo "=== Deploy complete ==="
