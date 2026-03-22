# msgraph-cli

A fast, script-friendly CLI for Microsoft 365 via the Microsoft Graph API. JSON-first output, least-privilege auth, and secure 1Password-backed credential storage.

Built for unattended agent use alongside interactive terminal access.

## Features

- **Mail** — search, list, read messages and folders
- **Calendar** — list, create, update events; check free/busy *(planned)*
- **OneDrive** — list, search, upload, download files *(planned)*
- **To Do** — manage task lists and tasks *(planned)*
- **Excel / Word / PowerPoint** — read and export via Graph API *(planned)*
- **Secure credential storage** — 1Password CLI integration, no secrets on disk
- **Least-privilege auth** — request only the Graph scopes you need
- **Agent-friendly** — JSON output, command allowlisting, structured exit codes

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [1Password CLI (`op`)](https://developer.1password.com/docs/cli/get-started/)
- A Microsoft Entra ID (Azure AD) app registration

## Quick Start

### 1. Create an Entra ID App Registration

1. Go to [Azure Portal → App Registrations](https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade)
2. **New registration** → Name: `msgraph-cli`, Single tenant, Redirect URI: `http://localhost` (Public client)
3. Under **Authentication** → Enable **Allow public client flows**
4. Under **API permissions** → Add: `User.Read`, `Mail.Read`, `offline_access`

### 2. Set Up 1Password

```bash
# Sign in to 1Password CLI
eval $(op signin)

# Run setup (creates vault, stores client ID + tenant ID)
dotnet run --project src/MsGraphCli -- auth setup
```

### 3. Authenticate

```bash
# Interactive browser login
dotnet run --project src/MsGraphCli -- auth login

# Headless (SSH, containers)
dotnet run --project src/MsGraphCli -- auth login --device-code

# Read-only, mail only
dotnet run --project src/MsGraphCli -- auth login --services mail --readonly
```

### 4. Use It

```bash
# List recent mail
msgraph mail list --max 10

# Search mail as JSON
msgraph mail search "from:boss@company.com" --json

# Check auth status
msgraph auth status --json
```

## Build

```bash
dotnet build
dotnet test

# Publish self-contained binary
dotnet publish src/MsGraphCli -c Release -r linux-x64 --self-contained -o ./publish
```

## Agent Usage

For AI agent integration (NetClaw, Claude Code, etc.), use `--json` mode and the command allowlist:

```bash
# Restrict agent to mail commands only
MSGRAPH_ENABLE_COMMANDS=mail msgraph mail list --json --max 10

# Parse results
msgraph mail search "quarterly report" --json | jq '.messages[].subject'
```

See [AGENTS.md](AGENTS.md) for the full agent integration guide.

## License

Private / Personal Use — Not for public distribution.
