#!/usr/bin/env bash
# setup-1password.sh — One-time setup for msgraph-cli 1Password vault
#
# This script:
#   1. Verifies the op CLI is available and signed in
#   2. Creates the msgraph-cli vault if it doesn't exist
#   3. Creates placeholder items for app-registration and msal-token-cache
#
# Prerequisites:
#   - 1Password CLI: https://developer.1password.com/docs/cli/get-started/
#   - Signed in:     eval $(op signin)
#
# Usage:
#   ./setup-1password.sh
#   ./setup-1password.sh --client-id YOUR_CLIENT_ID --tenant-id YOUR_TENANT_ID

set -euo pipefail

VAULT_NAME="msgraph-cli"
CLIENT_ID=""
TENANT_ID=""

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --client-id) CLIENT_ID="$2"; shift 2 ;;
        --tenant-id) TENANT_ID="$2"; shift 2 ;;
        --help|-h)
            echo "Usage: $0 [--client-id ID] [--tenant-id ID]"
            echo ""
            echo "Sets up 1Password vault and items for msgraph-cli."
            echo "If --client-id and --tenant-id are not provided, you will be prompted."
            exit 0
            ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

echo "=== msgraph-cli 1Password Setup ==="
echo ""

# Check op is available
if ! command -v op &> /dev/null; then
    echo "ERROR: 1Password CLI (op) is not installed."
    echo "  Install: https://developer.1password.com/docs/cli/get-started/"
    exit 1
fi

# Check op is signed in
if ! op whoami &> /dev/null; then
    echo "ERROR: Not signed in to 1Password CLI."
    echo "  Run: eval \$(op signin)"
    exit 1
fi

echo "✓ 1Password CLI available and signed in."

# Create vault if it doesn't exist
if op vault get "$VAULT_NAME" &> /dev/null; then
    echo "✓ Vault '$VAULT_NAME' already exists."
else
    echo "  Creating vault '$VAULT_NAME'..."
    op vault create "$VAULT_NAME"
    echo "✓ Vault '$VAULT_NAME' created."
fi

# Prompt for client ID and tenant ID if not provided
if [[ -z "$CLIENT_ID" ]]; then
    echo ""
    echo "You need an Entra ID (Azure AD) app registration."
    echo "  1. Go to: https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade"
    echo "  2. Click 'New registration'"
    echo "  3. Name: msgraph-cli"
    echo "  4. Supported account types: Single tenant"
    echo "  5. Redirect URI: Public client → http://localhost"
    echo "  6. Under 'Authentication' → Enable 'Allow public client flows'"
    echo "  7. Under 'API permissions' → Add: User.Read, Mail.Read, offline_access"
    echo ""
    read -rp "Enter Client ID (Application ID): " CLIENT_ID
fi

if [[ -z "$TENANT_ID" ]]; then
    read -rp "Enter Tenant ID: " TENANT_ID
fi

if [[ -z "$CLIENT_ID" || -z "$TENANT_ID" ]]; then
    echo "ERROR: Both Client ID and Tenant ID are required."
    exit 1
fi

# Store app registration
if op item get "app-registration" --vault "$VAULT_NAME" &> /dev/null; then
    echo "  Updating existing app-registration item..."
    op item edit "app-registration" \
        "client-id=$CLIENT_ID" \
        "tenant-id=$TENANT_ID" \
        --vault "$VAULT_NAME" > /dev/null
else
    echo "  Creating app-registration item..."
    op item create \
        --category Login \
        --title "app-registration" \
        --vault "$VAULT_NAME" \
        "client-id=$CLIENT_ID" \
        "tenant-id=$TENANT_ID" > /dev/null
fi

echo "✓ App registration stored in 1Password."
echo ""
echo "=== Setup complete ==="
echo ""
echo "Next steps:"
echo "  cd spike/MsalOnePasswordSpike"
echo "  dotnet run                  # Interactive browser login"
echo "  dotnet run -- --device-code # Device code flow (headless/SSH)"
echo ""
echo "On the first run, a browser will open for Microsoft login."
echo "On subsequent runs, tokens will be loaded from 1Password (no browser)."
