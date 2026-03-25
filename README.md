# msgraph-cli

A fast, script-friendly CLI for Microsoft 365 via the Microsoft Graph API. JSON-first output, least-privilege auth, and secure 1Password-backed credential storage.

Built for unattended agent use alongside interactive terminal access.

## Features

- **Mail** — search, list, read, send, reply, forward, move, mark read/unread, attachments
- **Calendar** — list calendars, CRUD events, respond to invitations, check free/busy
- **OneDrive** — list, search, upload (resumable for >4MB), download, mkdir, move, rename, delete
- **To Do** — manage task lists, CRUD tasks, mark complete/incomplete
- **Excel** — list worksheets, read/update ranges, append table rows (sessionless)
- **Word / PowerPoint** — export to PDF, extract text content
- **Secure credential storage** — 1Password CLI integration, no secrets on disk
- **Least-privilege auth** — request only the Graph scopes you need
- **Agent-friendly** — JSON output, command allowlisting, read-only mode, dry-run, structured exit codes
- **Shell completions** — bash, zsh, fish

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [1Password CLI (`op`)](https://developer.1password.com/docs/cli/get-started/)
- A Microsoft Entra ID (Azure AD) app registration

## Quick Start

### 1. Create an Entra ID App Registration

1. Go to [Azure Portal → App Registrations](https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade)
2. **New registration** → Name: `msgraph-cli`, Single tenant, Redirect URI: `http://localhost` (Public client)
3. Under **Authentication** → Enable **Allow public client flows**
4. Under **API permissions** → Add the delegated permissions for the services you need:

| Always Required | Mail | Calendar | OneDrive / Excel / Docs | To Do |
|---|---|---|---|---|
| `User.Read` | `Mail.Read` | `Calendars.Read` | `Files.Read` | `Tasks.Read` |
| `offline_access` | `Mail.Send` | `Calendars.ReadWrite` | `Files.ReadWrite` | `Tasks.ReadWrite` |
| | `Mail.ReadWrite` | | | |

> **Tip:** Start with `User.Read`, `offline_access`, and the read scopes for the services you need. Add write scopes later with `msgraph auth login --services mail,calendar`.

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

# Today's calendar events
msgraph calendar events --today --json

# List OneDrive files
msgraph drive ls --json

# List To Do tasks
msgraph todo lists --json

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
