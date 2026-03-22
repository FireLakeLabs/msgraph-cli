# AGENTS.md — msgraph-cli

## Overview

`msgraph` is a CLI for Microsoft 365 via the Microsoft Graph API. It outputs structured JSON (with `--json`) or human-friendly tables. Tokens are stored in 1Password.

## Commands

### Auth

| Command | Description |
|---|---|
| `msgraph auth setup` | Store Entra ID client ID + tenant ID in 1Password |
| `msgraph auth login [--services mail,calendar] [--readonly] [--device-code]` | Authenticate interactively |
| `msgraph auth status` | Show current auth state |
| `msgraph auth logout` | Clear cached tokens |
| `msgraph auth scopes` | List all service scopes |

### Mail

| Command | Description |
|---|---|
| `msgraph mail list [--folder inbox] [--max 25]` | List messages in a folder |
| `msgraph mail search "<query>" [--max 25]` | Search messages (KQL syntax) |
| `msgraph mail get <messageId> [--format summary\|full]` | Read a specific message |
| `msgraph mail folders list` | List mail folders with counts |

## Global Flags

| Flag | Env Var | Effect |
|---|---|---|
| `--json` | `MSGRAPH_JSON=1` | JSON output to stdout |
| `--plain` | `MSGRAPH_PLAIN=1` | TSV output to stdout |
| `--verbose` | `MSGRAPH_VERBOSE=1` | Debug logging to stderr |
| `--beta` | `MSGRAPH_BETA=1` | Use Graph beta endpoint |

## Output Contract

- **Data** → stdout (JSON object with named collection, never bare array)
- **Diagnostics** → stderr (progress, errors, auth prompts)
- **Dates** → ISO 8601 UTC in JSON mode

## JSON Output Schema

```json
// mail list / search
{ "messages": [{ "id": "", "from": "", "subject": "", "receivedDateTime": "", "isRead": false, "hasAttachments": false, "preview": "" }] }

// mail get
{ "message": { "id": "", "from": "", "toRecipients": [], "ccRecipients": [], "subject": "", "receivedDateTime": "", "isRead": false, "hasAttachments": false, "bodyText": "", "bodyHtml": "" } }

// mail folders list
{ "folders": [{ "id": "", "displayName": "", "totalItemCount": 0, "unreadItemCount": 0 }] }

// auth status
{ "userEmail": "", "tenantId": "", "tokenExpiry": "", "grantedScopes": [], "isAuthenticated": true }
```

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | General error |
| 2 | Authentication failure (token expired, scope missing, 1Password unavailable) |
| 3 | Resource not found (404) |
| 4 | Permission denied (403) |
| 5 | Rate limited (429) |
| 10 | Command blocked by allowlist |

## Agent Invocation Patterns

```bash
# Read inbox as JSON
msgraph mail list --json --max 10

# Search and pipe to jq
msgraph mail search "from:boss@company.com" --json | jq '.messages[] | .subject'

# Check auth before running
msgraph auth status --json | jq '.isAuthenticated'

# Restrict agent to read-only mail
MSGRAPH_ENABLE_COMMANDS=mail msgraph mail list --json
```

## Sandboxing

Set `MSGRAPH_ENABLE_COMMANDS=mail` (or `--enable-commands mail`) to restrict which top-level commands the agent can invoke. Any command not in the list returns exit code 10.
