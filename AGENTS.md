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
| `msgraph mail send --to <addr> --subject <subj> --body <text>` | Send a message (supports `--body-html`, `--cc`, `--bcc`, `--attach`) |
| `msgraph mail reply <messageId> --body <text>` | Reply to a message |
| `msgraph mail reply-all <messageId> --body <text>` | Reply all |
| `msgraph mail forward <messageId> --to <addr> --body <text>` | Forward a message |
| `msgraph mail move <messageId> --folder <name>` | Move message to folder |
| `msgraph mail mark-read <messageId>` | Mark as read |
| `msgraph mail mark-unread <messageId>` | Mark as unread |
| `msgraph mail attachments <messageId>` | List attachments |
| `msgraph mail attachments <messageId> --download --out-dir <dir>` | Download attachments |

### Calendar

| Command | Description |
|---|---|
| `msgraph calendar list` | List calendars |
| `msgraph calendar events [--today\|--week\|--days N\|--from/--to]` | List events in a time range |
| `msgraph calendar get <eventId>` | Get event details |
| `msgraph calendar search "<query>" [--days 30]` | Search events |
| `msgraph calendar create --subject <s> --start <dt> --end <dt>` | Create event (supports `--attendees`, `--location`, `--all-day`, `--body`) |
| `msgraph calendar update <eventId> [--subject\|--start\|--end\|...]` | Update event fields |
| `msgraph calendar delete <eventId>` | Delete event |
| `msgraph calendar respond <eventId> --status accept\|decline\|tentative` | Respond to invitation |
| `msgraph calendar freebusy --from <dt> --to <dt>` | Check availability |

### OneDrive

| Command | Description |
|---|---|
| `msgraph drive ls [--path <path>\|--folder <id>]` | List folder contents |
| `msgraph drive search "<query>" [--max 20]` | Search files |
| `msgraph drive get <itemId>` | Get item details |
| `msgraph drive download <itemId> --out <path>` | Download file |
| `msgraph drive upload <file> --path <remotePath>` | Upload file (auto-resumable for >4MB) |
| `msgraph drive mkdir "<name>" --path <parentPath>` | Create folder |
| `msgraph drive move <itemId> --destination <path>` | Move item |
| `msgraph drive rename <itemId> "<newName>"` | Rename item |
| `msgraph drive delete <itemId>` | Delete item |

### To Do

| Command | Description |
|---|---|
| `msgraph todo lists` | List task lists |
| `msgraph todo lists create "<name>"` | Create task list |
| `msgraph todo list <listId> [--status incomplete\|completed]` | List tasks in a list |
| `msgraph todo get <listId> <taskId>` | Get task details |
| `msgraph todo add <listId> --title "<title>" [--due <date>]` | Create task |
| `msgraph todo update <listId> <taskId> [--title\|--due\|...]` | Update task |
| `msgraph todo done <listId> <taskId>` | Mark task complete |
| `msgraph todo undo <listId> <taskId>` | Mark task incomplete |
| `msgraph todo delete <listId> <taskId>` | Delete task |

### Excel

| Command | Description |
|---|---|
| `msgraph excel sheets <itemId>` | List worksheets |
| `msgraph excel get <itemId> --sheet "<name>" --range "<range>"` | Read cell range |
| `msgraph excel update <itemId> --sheet "<name>" --range "<range>" --values '<json>'` | Update cells |
| `msgraph excel append <itemId> --sheet "<name>" --table "<table>" --values '<json>'` | Append rows to table |

### Documents

| Command | Description |
|---|---|
| `msgraph docs export <itemId> --format pdf --out <path>` | Export to PDF |
| `msgraph docs cat <itemId>` | Extract text content as markdown |

### Config

| Command | Description |
|---|---|
| `msgraph config path` | Show config file location |
| `msgraph config list` | Show all configuration |
| `msgraph config get <key>` | Get config value |
| `msgraph config set <key> <value>` | Set config value |

### Shell Completions

| Command | Description |
|---|---|
| `msgraph completions bash` | Generate bash completion script |
| `msgraph completions zsh` | Generate zsh completion script |
| `msgraph completions fish` | Generate fish completion script |

## Global Flags

| Flag | Env Var | Effect |
|---|---|---|
| `--json` | `MSGRAPH_JSON=1` | JSON output to stdout |
| `--plain` | `MSGRAPH_PLAIN=1` | TSV output to stdout |
| `--verbose` | `MSGRAPH_VERBOSE=1` | Debug logging to stderr |
| `--beta` | `MSGRAPH_BETA=1` | Use Graph beta endpoint |
| `--read-only` | — | Fail if operation requires write scope |
| `--dry-run` | — | Show what would be done without executing |
| `--max <n>` | — | Maximum results to return |
| `--enable-commands <csv>` | `MSGRAPH_ENABLE_COMMANDS` | Command allowlist for sandboxing |

## Output Contract

- **Data** → stdout (JSON object with named collection, never bare array)
- **Diagnostics** → stderr (progress, errors, auth prompts)
- **Dates** → ISO 8601 UTC in JSON mode

## JSON Output Schema

```json
// auth status
{ "userEmail": "", "tenantId": "", "tokenExpiry": "", "grantedScopes": [], "isAuthenticated": true }

// mail list / search
{ "messages": [{ "id": "", "from": "", "subject": "", "receivedDateTime": "", "isRead": false, "hasAttachments": false, "preview": "" }] }

// mail get
{ "message": { "id": "", "from": "", "toRecipients": [], "ccRecipients": [], "subject": "", "receivedDateTime": "", "isRead": false, "hasAttachments": false, "bodyText": "", "bodyHtml": "" } }

// mail folders list
{ "folders": [{ "id": "", "displayName": "", "totalItemCount": 0, "unreadItemCount": 0 }] }

// mail attachments
{ "attachments": [{ "id": "", "name": "", "contentType": "", "size": 0 }] }

// calendar list
{ "calendars": [{ "id": "", "name": "", "color": "", "isDefault": false }] }

// calendar events / search
{ "events": [{ "id": "", "subject": "", "start": "", "end": "", "isAllDay": false, "location": "", "organizerEmail": "", "isOrganizer": false, "responseStatus": "", "isCancelled": false }] }

// calendar get
{ "event": { "id": "", "subject": "", "start": "", "end": "", "isAllDay": false, "location": "", "organizerEmail": "", "bodyText": "", "bodyHtml": "", "attendees": [{ "email": "", "name": "", "responseStatus": "" }], "onlineMeetingUrl": "", "recurrence": "" } }

// calendar freebusy
{ "schedules": [{ "email": "", "slots": [{ "start": "", "end": "", "status": "" }] }] }

// drive ls / search
{ "items": [{ "id": "", "name": "", "mimeType": "", "size": 0, "lastModified": "", "isFolder": false, "webUrl": "" }] }

// drive get
{ "item": { "id": "", "name": "", "mimeType": "", "size": 0, "created": "", "lastModified": "", "isFolder": false, "webUrl": "", "parentPath": "", "downloadUrl": "" } }

// todo lists
{ "lists": [{ "id": "", "displayName": "", "isDefaultList": false }] }

// todo list (tasks)
{ "tasks": [{ "id": "", "title": "", "body": "", "status": "", "dueDate": "", "completedDate": "", "created": "", "lastModified": "", "importance": "" }] }

// excel sheets
{ "worksheets": [{ "id": "", "name": "", "visibility": "", "position": 0 }] }

// excel get
{ "range": { "address": "", "rowCount": 0, "columnCount": 0, "values": [[]], "formulas": [[]], "numberFormats": [[]] } }

// docs export
{ "export": { "itemId": "", "format": "", "outputPath": "", "bytesWritten": 0 } }

// docs cat
{ "content": { "markdown": "", "images": [{ "fileName": "", "contentType": "" }] } }
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
| 10 | Command blocked by allowlist or read-only violation |

## Agent Invocation Patterns

```bash
# Read inbox as JSON
msgraph mail list --json --max 10

# Search and pipe to jq
msgraph mail search "from:boss@company.com" --json | jq '.messages[] | .subject'

# Today's events
msgraph calendar events --today --json | jq '.events[] | .subject'

# List OneDrive root
msgraph drive ls --json | jq '.items[] | .name'

# Download a file
msgraph drive download "$ITEM_ID" --out ./report.pdf --json

# Check auth before running
msgraph auth status --json | jq '.isAuthenticated'

# Read-only mode (write commands will fail)
msgraph --read-only mail list --json

# Dry-run a send (shows what would happen, doesn't execute)
msgraph --dry-run mail send --to user@example.com --subject "Test" --body "Hello" --json

# Restrict agent to mail + calendar only
MSGRAPH_ENABLE_COMMANDS=mail,calendar msgraph mail list --json
```

## Sandboxing

Set `MSGRAPH_ENABLE_COMMANDS=mail,calendar` (or `--enable-commands mail,calendar`) to restrict which top-level commands the agent can invoke. Any command not in the list returns exit code 10.

Available service names: `mail`, `calendar`, `drive`, `todo`, `excel`, `docs`, `config`, `auth`.

Use `--read-only` to prevent all write operations regardless of the allowlist.

## Required Scopes

Add only the delegated permissions you need to your Entra ID app registration:

| Always Required | Mail | Calendar | OneDrive / Excel / Docs | To Do |
|---|---|---|---|---|
| `User.Read` | `Mail.Read` | `Calendars.Read` | `Files.Read` | `Tasks.Read` |
| `offline_access` | `Mail.Send` | `Calendars.ReadWrite` | `Files.ReadWrite` | `Tasks.ReadWrite` |
| | `Mail.ReadWrite` | | | |
