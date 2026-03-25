#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
PROJECT="$REPO_ROOT/src/FireLakeLabs.MsGraphCli"
PUBLISH_DIR="$REPO_ROOT/publish"

TARGETS=("linux-x64" "linux-arm64")

for rid in "${TARGETS[@]}"; do
    echo "Publishing for $rid..."
    dotnet publish "$PROJECT" \
        -c Release \
        -r "$rid" \
        --self-contained \
        -p:PublishSingleFile=true \
        -o "$PUBLISH_DIR/$rid"
    echo "  -> $PUBLISH_DIR/$rid/msgraph"
done

echo ""
echo "Published binaries:"
for rid in "${TARGETS[@]}"; do
    size=$(du -h "$PUBLISH_DIR/$rid/msgraph" | cut -f1)
    echo "  $rid: $size ($PUBLISH_DIR/$rid/msgraph)"
done
