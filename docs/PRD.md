# PRD: msgraph-cli

**A fast, script-friendly CLI for Microsoft 365 via the Microsoft Graph API.**

| Field | Value |
|---|---|
| **Author** | Aaron (Fire Lake Labs) |
| **Status** | Active — Phase 1 complete, Phases 2–5 in progress |
| **Repository** | [FireLakeLabs/msgraph-cli](https://github.com/FireLakeLabs/msgraph-cli) |
| **Target Platform** | .NET 10, Linux primary (cross-platform capable) |
| **License** | MIT |
| **Inspired By** | https://github.com/steipete/gogcli and https://github.com/googleworkspace/cli |

---

## 1. Problem Statement

AI agents like NetClaw need programmatic, unattended access to Microsoft 365 services (mail, calendar, files, tasks) on behalf of a user. The official Microsoft CLI tooling (`mgc`, the Microsoft Graph CLI) is general-purpose but verbose, not optimized for agent consumption, and lacks opinionated patterns for secure credential management or least-privilege scoping.

What's needed is a focused, security-first CLI that:

- Exposes the M365 operations an agent actually needs, with sensible defaults.
- Outputs structured JSON for machine consumption alongside human-friendly tables.
- Manages OAuth tokens securely via 1Password CLI, suitable for headless/unattended operation.
- Follows least-privilege principles, requesting only the Graph API permissions required.
- Is architected for future extraction into a reusable .NET library that NetClaw or other projects can reference directly.

---

## 2. Goals and Non-Goals

### Goals

- Secure, unattended agent access to Microsoft 365 as a specific user via delegated OAuth flow with offline refresh tokens.
- 1Password CLI integration as the secrets backend, with separate read-only and read-write vaults.
- Least-privilege auth with granular scope selection per service, read-only mode support.
- JSON-first agent interface with `--json` flag, plus human-friendly default output.
- Read-heavy core with selective write operations (send mail, create/update events).
- Clean separation of concerns enabling future library extraction.
- Single-user, single-tenant — personal tool for one user's Microsoft 365 account.

### Non-Goals

- Multi-tenant / multi-user support.
- Public distribution via NuGet, Homebrew, or similar.
- MCP server mode — CLI + JSON output is sufficient for agent integration.
- Full parity with gogcli — functional equivalence for overlapping services, not a 1:1 port.
- GUI or web interface.
- App-only (client credentials) auth — delegated flow is correct for single-user access.
- Teams messaging support (explicitly deferred).

---

## 3. Target Users

| User | Context |
|---|---|
| **Aaron (direct)** | Interactive terminal use for managing M365 resources. |
| **NetClaw agent** | Shells out to `msgraph` commands, parses JSON output, performs M365 operations on Aaron's behalf. |
| **Claude Code / AI agents** | Any agent runtime that can execute CLI commands and parse JSON. |

---

## 4. Architecture

### 4.1 High-Level Design

```
┌─────────────────────────────────────────────────┐
│                   CLI Layer                      │
│  System.CommandLine 2.0.5 command tree           │
│  Output formatting (table / JSON / plain)        │
│  Global flags (--json, --verbose, --beta, etc.)  │
└──────────────────────┬──────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────┐
│                 Service Layer                    │
│  MailService, CalendarService, DriveService,     │
│  TasksService, OfficeDocsService                 │
│  (Stateless, injectable, testable)               │
└──────────────────────┬──────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────┐
│              Graph Client Layer                  │
│  Microsoft.Graph SDK (v5+)                       │
│  MsalAccessTokenProvider (IAccessTokenProvider)  │
│  Retry, rate-limit handling, pagination          │
└──────────────────────┬──────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────┐
│               Auth / Secrets Layer               │
│  Two-vault 1Password architecture                │
│  MSAL.NET token cache (Base64 blob in 1Password) │
│  Delegated OAuth + PKCE + refresh token flow     │
└─────────────────────────────────────────────────┘
```

### 4.2 Project Structure

```
msgraph-cli/
├── src/
│   ├── MsGraphCli/                    # CLI entry point + commands
│   │   ├── Program.cs
│   │   ├── GlobalOptions.cs
│   │   ├── Commands/
│   │   │   ├── AuthCommands.cs        ✅ Phase 1
│   │   │   ├── MailCommands.cs        ✅ Phase 1 (read), Phase 2 (write)
│   │   │   ├── CalendarCommands.cs    ○ Phase 2
│   │   │   ├── DriveCommands.cs       ○ Phase 3
│   │   │   ├── TasksCommands.cs       ○ Phase 3
│   │   │   └── OfficeDocsCommands.cs  ○ Phase 4
│   │   └── Output/
│   │       └── OutputFormatters.cs    ✅ Phase 1
│   │
│   ├── MsGraphCli.Core/              # Library layer (future NuGet candidate)
│   │   ├── Auth/
│   │   │   ├── ISecretStore.cs        ✅ Phase 1
│   │   │   ├── OnePasswordSecretStore.cs ✅ Phase 1
│   │   │   ├── GraphAuthProvider.cs   ✅ Phase 1
│   │   │   ├── TokenCacheHelper.cs    ✅ Phase 1
│   │   │   └── ScopeRegistry.cs       ✅ Phase 1
│   │   ├── Config/
│   │   │   └── AppConfig.cs           ✅ Phase 1
│   │   ├── Exceptions/
│   │   │   └── MsGraphCliException.cs ✅ Phase 1
│   │   ├── Graph/
│   │   │   ├── GraphClientFactory.cs  ✅ Phase 1
│   │   │   └── PaginationHelper.cs    ○ Phase 2
│   │   ├── Models/
│   │   │   ├── MailModels.cs          ✅ Phase 1
│   │   │   ├── CalendarModels.cs      ○ Phase 2
│   │   │   ├── DriveModels.cs         ○ Phase 3
│   │   │   ├── TaskModels.cs          ○ Phase 3
│   │   │   └── OfficeDocsModels.cs    ○ Phase 4
│   │   └── Services/
│   │       ├── MailService.cs         ✅ Phase 1 (read), Phase 2 (write)
│   │       ├── CalendarService.cs     ○ Phase 2
│   │       ├── DriveService.cs        ○ Phase 3
│   │       ├── TasksService.cs        ○ Phase 3
│   │       └── OfficeDocsService.cs   ○ Phase 4
│   │
│   └── MsGraphCli.Tests/
│       ├── Unit/
│       │   ├── ScopeRegistryTests.cs  ✅ Phase 1
│       │   └── TokenCacheHelperTests.cs ✅ Phase 1
│       └── Integration/
│
├── spike/                             ✅ Completed (MSAL + 1Password validated)
├── AGENTS.md                          ✅ Phase 1
├── CLAUDE.md                          ✅ Phase 1
├── PRD.md                             ← This document
└── README.md                          ✅ Phase 1
```

### 4.3 Key Design Decisions

| Decision | Rationale |
|---|---|
| **System.CommandLine 2.0.5** | Official .NET CLI framework, now stable. Uses `SetAction` API with `ParseResult`. |
| **Separate Core library from CLI** | Services testable without CLI bootstrapping. NetClaw can reference Core directly later. |
| **1Password CLI exclusively** | Single-user tool. `ISecretStore` interface provides the seam if this changes. |
| **Two-vault architecture** | `msgraph-cli` vault (read-only, app config) and `netclaw-rw` vault (read-write, token cache). Principle of least privilege for the token store. |
| **MSAL cache as Base64 blob** | Full MSAL token cache serialized to a single 1Password Secure Note. MSAL handles rotation and multi-resource tokens internally. |
| **Delegated OAuth with PKCE** | Public client, no client secret needed. One interactive login, then refresh token carries indefinitely. |
| **v1.0 default + `--beta` flag** | v1.0 is stable. Beta opt-in per invocation for richer data when needed. |
| **Sessionless Excel** | CLI invocations are short-lived. No workbook session lifecycle to manage. |
| **Binary name: `msgraph`** | Concise, no collision with Microsoft's `mgc`. |

---

## 5. Authentication and Security

### 5.1 OAuth Flow

Delegated permissions with `offline_access` scope:

1. **One-time interactive login**: `msgraph auth login` opens browser (or device code for headless). Authorization code + PKCE flow.
2. **Refresh token persisted**: MSAL token cache serialized as Base64 blob, stored in 1Password `netclaw-rw` vault as a Secure Note (`msal-token-cache`).
3. **Unattended token refresh**: Each invocation reads cache from 1Password, MSAL acquires token silently via cached refresh token. New tokens written back on rotation.

### 5.2 App Registration (Microsoft Entra ID)

| Setting | Value |
|---|---|
| **Application type** | Public client (mobile & desktop) |
| **Redirect URI** | `http://localhost` (loopback for desktop OAuth) |
| **Supported account types** | Single tenant |
| **Allow public client flows** | Yes |
| **Client secret** | None (public client uses PKCE) |

### 5.3 1Password Vault Architecture

| Vault | Access | Contents |
|---|---|---|
| `msgraph-cli` | Read-only | `ms-graph-app-registration` item with `client-id` and `tenant-id` fields |
| `netclaw-rw` | Read-write | `msal-token-cache` Secure Note with Base64-encoded MSAL V3 cache blob |

### 5.4 Scope Strategy: Least Privilege

Scopes are declared per service and requested only when that service is used.

| Service | Read Scopes | Write Scopes |
|---|---|---|
| **Mail** | `Mail.Read` | `Mail.Send`, `Mail.ReadWrite` |
| **Calendar** | `Calendars.Read` | `Calendars.ReadWrite` |
| **OneDrive** | `Files.Read` | `Files.ReadWrite` |
| **To Do** | `Tasks.Read` | `Tasks.ReadWrite` |
| **Excel** | `Files.Read` (via Drive) | `Files.ReadWrite` (via Drive) |
| **Word/PowerPoint** | `Files.Read` (via Drive) | `Files.ReadWrite` (via Drive) |
| **User Profile** | `User.Read` | — |
| **Always included** | `offline_access`, `User.Read` | — |

The `--readonly` flag restricts to read scopes only. The `--services` flag requests only specific service scopes at login time.

### 5.5 Security Hardening

| Concern | Mitigation |
|---|---|
| **Token at rest** | MSAL cache stored exclusively in 1Password vault (encrypted by 1Password). Separate RW vault limits blast radius. |
| **Token in transit** | All Graph API calls over HTTPS. `op` CLI uses local socket to 1Password agent. |
| **Token in memory** | Access token held only for CLI invocation duration. No temp files. |
| **Scope creep** | ScopeRegistry enforces minimum permissions. `--readonly` flag available. |
| **Command allowlist** | `--enable-commands` flag restricts which commands an agent can invoke. |
| **Public client** | PKCE protects auth code flow. No client secret to leak. |
| **Audit trail** | `--verbose` logs API calls to stderr (never tokens). 1Password provides its own audit log. |

---

## 6. Service Specifications

### 6.1 Mail (Outlook)

**Status:** ✅ Read operations complete. ○ Write operations Phase 2.

#### Read Operations (✅ Implemented)

```bash
msgraph mail list [--folder inbox] [--max 25]
msgraph mail search "<query>" [--max 25]
msgraph mail get <messageId> [--format summary|full]
msgraph mail folders list
```

#### Write Operations (○ Phase 2)

```bash
# Send
msgraph mail send --to user@example.com --subject "Hello" --body "Plain text"
msgraph mail send --to user@example.com --subject "Hello" --body-html "<p>Rich body</p>"
msgraph mail send --to a@b.com --cc c@d.com --subject "FYI" --body "See attached" --attach ./report.pdf

# Reply / forward
msgraph mail reply <messageId> --body "Thanks for the update"
msgraph mail reply-all <messageId> --body "Acknowledged"
msgraph mail forward <messageId> --to other@example.com --body "FYI"

# Organize
msgraph mail move <messageId> --folder "Archive"
msgraph mail mark-read <messageId>
msgraph mail mark-unread <messageId>

# Attachments
msgraph mail attachments <messageId>
msgraph mail attachments <messageId> --download --out-dir ./attachments
```

**Graph API endpoints:**
- `POST /me/sendMail`
- `POST /me/messages/{id}/reply`
- `POST /me/messages/{id}/replyAll`
- `POST /me/messages/{id}/forward`
- `POST /me/messages/{id}/move`
- `PATCH /me/messages/{id}` (isRead)
- `GET /me/messages/{id}/attachments`
- `GET /me/messages/{id}/attachments/{id}/$value`

**Required additional scopes:** `Mail.Send`, `Mail.ReadWrite`

### 6.2 Calendar

**Status:** ○ Phase 2

#### Read Operations

```bash
# List calendars
msgraph calendar list

# List events with time filters
msgraph calendar events --today
msgraph calendar events --tomorrow
msgraph calendar events --week
msgraph calendar events --days 3
msgraph calendar events --from 2025-07-01 --to 2025-07-31

# Get specific event
msgraph calendar get <eventId>

# Search events
msgraph calendar search "standup" --days 30

# Check availability
msgraph calendar freebusy --from 2025-07-15T09:00:00 --to 2025-07-15T17:00:00
```

#### Write Operations

```bash
# Create event
msgraph calendar create \
  --subject "Team Sync" \
  --start 2025-07-15T10:00:00 \
  --end 2025-07-15T10:30:00 \
  --attendees "alice@example.com,bob@example.com" \
  --location "Teams" \
  --body "Weekly sync agenda"

# All-day event
msgraph calendar create --subject "Holiday" --start 2025-07-04 --all-day

# Update
msgraph calendar update <eventId> --subject "Updated Sync" --start 2025-07-15T11:00:00

# Respond to invitation
msgraph calendar respond <eventId> --status accept
msgraph calendar respond <eventId> --status decline --message "Conflict"
msgraph calendar respond <eventId> --status tentative

# Delete
msgraph calendar delete <eventId>
```

**Graph API endpoints:**
- `GET /me/calendars`
- `GET /me/calendarView` (events in range, uses `startDateTime` / `endDateTime` query params)
- `GET /me/events/{id}`
- `POST /me/events`
- `PATCH /me/events/{id}`
- `DELETE /me/events/{id}`
- `POST /me/events/{id}/accept` | `decline` | `tentativelyAccept`
- `POST /me/calendar/getSchedule` (free/busy)

**Models:**

```csharp
public record CalendarInfo(string Id, string Name, string Color, bool IsDefaultCalendar);

public record CalendarEventSummary(
    string Id, string Subject, DateTimeOffset Start, DateTimeOffset End,
    bool IsAllDay, string? Location, string? OrganizerEmail,
    bool IsOrganizer, string? ResponseStatus, bool IsCancelled);

public record CalendarEventDetail(
    string Id, string Subject, DateTimeOffset Start, DateTimeOffset End,
    bool IsAllDay, string? Location, string? BodyText, string? BodyHtml,
    string? OrganizerEmail, IReadOnlyList<AttendeeInfo> Attendees,
    bool IsOnlineMeeting, string? OnlineMeetingUrl, string? Recurrence);

public record AttendeeInfo(string Email, string? Name, string ResponseStatus);

public record FreeBusySlot(DateTimeOffset Start, DateTimeOffset End, string Status);
```

**Notes on implementation:**
- `calendarView` is preferred over `events` for time-range queries because it expands recurring events into instances.
- Time zone handling: accept user's local TZ on input flags, convert to UTC for Graph API, display in local TZ for table output, ISO 8601 UTC for JSON output.
- `--today`, `--tomorrow`, `--week`, `--days N` are convenience sugar that compute `--from`/`--to` ranges internally.

### 6.3 OneDrive / Files

**Status:** ○ Phase 3

#### Read Operations

```bash
msgraph drive ls
msgraph drive ls --path "/Documents"
msgraph drive ls --folder <folderId>
msgraph drive search "quarterly report" --max 20
msgraph drive get <itemId>
msgraph drive download <itemId> --out ./report.pdf
msgraph drive download --path "/Documents/report.pdf" --out ./report.pdf
```

#### Write Operations

```bash
# Upload (simple, < 4MB)
msgraph drive upload ./report.pdf --path "/Documents/report.pdf"

# Upload (resumable, large files — auto-detected based on file size)
msgraph drive upload ./large-file.zip --path "/Backups/large-file.zip"

# Organize
msgraph drive mkdir "New Folder" --path "/Documents"
msgraph drive move <itemId> --destination "/Archive"
msgraph drive rename <itemId> "new-name.pdf"
msgraph drive delete <itemId>
```

**Graph API endpoints:**
- `GET /me/drive/root/children`
- `GET /me/drive/items/{id}/children`
- `GET /me/drive/root/search(q='...')`
- `GET /me/drive/items/{id}` / `GET /me/drive/items/{id}/content`
- `PUT /me/drive/root:/{path}:/content` (simple upload ≤ 4MB)
- `POST /me/drive/items/{id}/createUploadSession` (resumable upload > 4MB)
- `POST /me/drive/root/children` (create folder)
- `PATCH /me/drive/items/{id}` (move/rename)
- `DELETE /me/drive/items/{id}`

**Models:**

```csharp
public record DriveItemSummary(
    string Id, string Name, string? MimeType, long? Size,
    DateTimeOffset? LastModified, bool IsFolder, string? WebUrl);

public record DriveItemDetail(
    string Id, string Name, string? MimeType, long? Size,
    DateTimeOffset? Created, DateTimeOffset? LastModified,
    bool IsFolder, string? WebUrl, string? ParentPath,
    string? DownloadUrl);
```

**Notes on implementation:**
- Simple upload (PUT) for files ≤ 4MB. Resumable upload session for anything larger. The service should auto-detect based on file size.
- `--path` resolves via `/me/drive/root:/{path}:` syntax for path-based access.
- Consider adding `--progress` flag for large uploads (stream upload progress to stderr).

### 6.4 To Do / Tasks

**Status:** ○ Phase 3

```bash
# Task lists
msgraph todo lists
msgraph todo lists create "Project Tasks"

# Tasks
msgraph todo list <listId> [--status incomplete|completed]
msgraph todo get <listId> <taskId>
msgraph todo add <listId> --title "Review PR #42" --due 2025-07-20
msgraph todo update <listId> <taskId> --title "Updated title"
msgraph todo done <listId> <taskId>
msgraph todo undo <listId> <taskId>
msgraph todo delete <listId> <taskId>
```

**Graph API endpoints:**
- `GET /me/todo/lists`
- `POST /me/todo/lists`
- `GET /me/todo/lists/{id}/tasks`
- `GET /me/todo/lists/{id}/tasks/{taskId}`
- `POST /me/todo/lists/{id}/tasks`
- `PATCH /me/todo/lists/{id}/tasks/{taskId}`
- `DELETE /me/todo/lists/{id}/tasks/{taskId}`

**Models:**

```csharp
public record TaskList(string Id, string DisplayName, bool IsDefaultList);

public record TodoTask(
    string Id, string Title, string? Body, string Status,
    DateTimeOffset? DueDate, DateTimeOffset? CompletedDate,
    DateTimeOffset? Created, DateTimeOffset? LastModified,
    string Importance);
```

### 6.5 Office Documents (Excel, Word, PowerPoint)

**Status:** ○ Phase 4

Excel operations use sessionless mode (each request is independent).

#### Excel

```bash
msgraph excel sheets <itemId>
msgraph excel get <itemId> --sheet "Sheet1" --range "A1:D20"
msgraph excel update <itemId> --sheet "Sheet1" --range "A1:B2" --values '[["Name","Score"],["Alice",95]]'
msgraph excel append <itemId> --sheet "Sheet1" --table "Table1" --values '[["Bob",88]]'
```

#### Word / PowerPoint

```bash
msgraph docs export <itemId> --format pdf --out ./document.pdf
msgraph docs cat <itemId>
```

**Graph API endpoints:**
- `GET /me/drive/items/{id}/workbook/worksheets`
- `GET /me/drive/items/{id}/workbook/worksheets/{name}/range(address='...')`
- `PATCH /me/drive/items/{id}/workbook/worksheets/{name}/range(address='...')`
- `POST /me/drive/items/{id}/workbook/tables/{name}/rows/add`
- `GET /me/drive/items/{id}/content?format=pdf`

---

## 7. Output Formatting

### 7.1 Principles

- **Data to stdout, diagnostics to stderr.** Always.
- **Consistent JSON structure.** Top-level object with a named collection (never bare array). Include pagination cursor when applicable.
- **ISO 8601 dates in JSON.** Human tables can use localized formatting.
- **No color in JSON/plain mode.** Auto-detection via TTY check, with `--color` override.

### 7.2 Formats

| Format | Flag | Behavior |
|---|---|---|
| **Table** | (default when TTY) | Spectre.Console tables with color. Truncated fields. |
| **JSON** | `--json` | Indented JSON via System.Text.Json. camelCase properties. |
| **Plain** | `--plain` | Tab-separated values. No color, no truncation. ISO 8601 dates. |

### 7.3 JSON Error Schema

When `--json` is active, errors go to stderr as structured JSON:

```json
{
  "error": {
    "code": "AuthenticationRequired",
    "message": "Refresh token expired. Run: msgraph auth login --services mail",
    "details": {
      "missingScopes": ["Mail.Read"]
    }
  }
}
```

---

## 8. Global Flags and Configuration

### 8.1 Global Flags

| Flag | Env Var | Description |
|---|---|---|
| `--json` | `MSGRAPH_JSON=1` | JSON output |
| `--plain` | `MSGRAPH_PLAIN=1` | Tab-separated output |
| `--color <mode>` | `MSGRAPH_COLOR` | `auto`, `always`, `never` |
| `--verbose` | `MSGRAPH_VERBOSE=1` | Verbose logging to stderr |
| `--beta` | `MSGRAPH_BETA=1` | Use Graph beta endpoint |
| `--dry-run` | — | Show what would be done (write commands) |
| `--readonly` | — | Fail if operation requires write scope |
| `--max <n>` | — | Maximum results to return |
| `--enable-commands <csv>` | `MSGRAPH_ENABLE_COMMANDS` | Command allowlist for sandboxing |

### 8.2 Configuration File

Located at `~/.config/msgraph-cli/config.json` (respects `XDG_CONFIG_HOME`):

```json
{
  "configVault": "msgraph-cli",
  "tokenCacheVault": "netclaw-rw",
  "defaultOutputFormat": "table",
  "defaultTimezone": "America/Chicago",
  "enableCommands": null,
  "verbose": false
}
```

---

## 9. Exit Codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | General error |
| `2` | Authentication failure (token expired, scope insufficient, 1Password unavailable) |
| `3` | Resource not found (404) |
| `4` | Permission denied (403) |
| `5` | Rate limited (429 — retry-after header available) |
| `10` | Command not allowed (blocked by `--enable-commands`) |

---

## 10. Cross-Cutting Concerns

### 10.1 Command Allowlist (Agent Sandboxing)

```bash
# Restrict agent to mail + calendar
MSGRAPH_ENABLE_COMMANDS=mail,calendar msgraph mail list --json
```

Applied as System.CommandLine middleware. Commands not in the list return exit code 10.

### 10.2 Rate Limiting

- Inspect `Retry-After` header on 429 responses.
- Automatic retry with exponential backoff (max 3 retries).
- Report throttling to stderr in verbose mode.
- Exit code 5 if retries exhausted.

### 10.3 Pagination

- Generic `PaginationHelper` for iterating Graph paged responses.
- Respects `--max` flag.
- JSON output includes `nextLink` when available for cursor-based pagination.

### 10.4 `--readonly` Enforcement

- If `--readonly` is set and a write command is invoked, fail immediately with a clear error message listing the required write scopes.
- Checked at the CLI middleware level before hitting the service layer.

### 10.5 `--dry-run` for Write Operations

- Print the operation that would be performed (including the Graph API request body) to stderr.
- Return exit code 0 without executing.
- Useful for agent validation before executing destructive operations.

---

## 11. Implementation Phases

### Phase 1: Foundation — ✅ COMPLETE

**Validated:**
- Project structure (CLI + Core + Tests).
- 1Password two-vault integration (spike validated, production code working).
- `msgraph auth setup` / `login` / `status` / `logout` / `scopes`.
- `msgraph mail list` / `search` / `get` / `folders list`.
- Output formatting (JSON, table, plain).
- Global flags (`--json`, `--verbose`, `--color`).
- Unit tests (ScopeRegistry, TokenCacheHelper, InMemorySecretStore).
- AGENTS.md, CLAUDE.md, README.md.

### Phase 2: Mail Write + Calendar

**Deliverables:**

Mail write commands:
- [ ] `msgraph mail send` (plain text, HTML, attachments, CC/BCC)
- [ ] `msgraph mail reply` / `reply-all` / `forward`
- [ ] `msgraph mail move`
- [ ] `msgraph mail mark-read` / `mark-unread`
- [ ] `msgraph mail attachments` (list + download)

Calendar commands (full CRUD):
- [ ] `msgraph calendar list` (list calendars)
- [ ] `msgraph calendar events` (with `--today`, `--week`, `--from`/`--to`)
- [ ] `msgraph calendar get` / `search`
- [ ] `msgraph calendar create` (attendees, location, all-day, recurrence)
- [ ] `msgraph calendar update` / `delete`
- [ ] `msgraph calendar respond` (accept/decline/tentative)
- [ ] `msgraph calendar freebusy`

Cross-cutting:
- [ ] `--readonly` enforcement middleware
- [ ] `--enable-commands` allowlist middleware
- [ ] Rate-limit retry handler (429 + exponential backoff)
- [ ] `PaginationHelper` for multi-page responses
- [ ] Add `Mail.Send` and `Mail.ReadWrite` to ScopeRegistry
- [ ] Add `Calendars.Read` and `Calendars.ReadWrite` to ScopeRegistry

Testing:
- [ ] Unit tests for CalendarService with mocked Graph client
- [ ] Unit tests for mail write operations
- [ ] Unit tests for command allowlist middleware
- [ ] Integration tests for mail send (gated behind `MSGRAPH_LIVE=1`)

### Phase 3: OneDrive + Tasks

**Deliverables:**
- [ ] Full drive command set (ls, search, download, upload, mkdir, move, rename, delete)
- [ ] Resumable upload for files > 4MB (auto-detected)
- [ ] Full todo command set (lists, tasks CRUD, complete/uncomplete)
- [ ] `--dry-run` flag for write operations
- [ ] Unit tests for DriveService and TasksService
- [ ] Integration tests (gated)

### Phase 4: Office Documents

**Deliverables:**
- [ ] Excel: list worksheets, read ranges, update cells, append rows (sessionless)
- [ ] Word/PowerPoint: export to PDF, text extraction
- [ ] `--beta` flag wiring (swap Graph base URL per invocation)
- [ ] Unit tests for OfficeDocsService

### Phase 5: Polish

**Deliverables:**
- [ ] Shell completions (bash, zsh, fish) via System.CommandLine built-in support
- [ ] `msgraph config` command set (get, set, list, path)
- [ ] Integration test suite (opt-in, live Graph API)
- [ ] Full command reference documentation (generated or handwritten)
- [ ] Self-contained binary publishing for linux-x64/arm64
- [ ] Verification on `cheesy-mc-agent-host` or equivalent

---

## 12. Dependencies

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Graph` | 5.103.0+ | Microsoft Graph SDK for .NET |
| `Microsoft.Identity.Client` | 4.83.1+ | MSAL.NET (OAuth 2.0 + token management) |
| `System.CommandLine` | 2.0.5 | CLI framework (stable release) |
| `Spectre.Console` | 0.49.1+ | Table formatting and rich terminal output |
| `System.Text.Json` | (built-in) | JSON serialization |

**External tools:**
- `op` (1Password CLI) — must be installed and authenticated.

**Runtime:**
- .NET 10 SDK (Linux, x64 or arm64).

---

## 13. Testing Strategy

| Layer | Approach |
|---|---|
| **Unit tests** | Service layer with mocked `GraphServiceClient`. Auth layer with `InMemorySecretStore`. |
| **Integration tests** | Gated behind `MSGRAPH_LIVE=1`. Hit real Graph API with authenticated 1Password session. |
| **CLI tests** | Invoke compiled binary, assert stdout JSON, stderr messages, exit codes. |

---

## 14. Resolved Design Decisions

| # | Question | Decision | Rationale |
|---|---|---|---|
| 1 | MSAL cache strategy | Full MSAL cache as single Base64 blob in 1Password Secure Note. | MSAL handles rotation, multi-resource tokens, cache invalidation internally. Proven in spike. |
| 2 | Graph API version | v1.0 default. `--beta` global flag for opt-in beta endpoint. | Beta has richer data for some resources. Flag makes it per-invocation. |
| 3 | Teams support | Deferred. Not in any current phase. | Slack is primary messaging channel. Can be added as future phase. |
| 4 | Excel session model | Sessionless. Each request independent. | CLI invocations are short-lived. No lifecycle management needed. |
| 5 | Binary name | `msgraph` | Concise, self-documenting, no collision with Microsoft's `mgc`. |
| 6 | Vault architecture | Two vaults: `msgraph-cli` (read-only config) + `netclaw-rw` (read-write token cache). | Principle of least privilege. Token cache needs RW; app config doesn't. |

---

## 15. References

- [Microsoft Graph REST API v1.0](https://learn.microsoft.com/en-us/graph/api/overview?view=graph-rest-1.0)
- [Microsoft Graph .NET SDK](https://github.com/microsoftgraph/msgraph-sdk-dotnet)
- [MSAL.NET documentation](https://learn.microsoft.com/en-us/entra/msal/dotnet/)
- [MSAL.NET token cache serialization](https://learn.microsoft.com/en-us/entra/msal/dotnet/how-to/token-cache-serialization)
- [Microsoft Graph permissions reference](https://learn.microsoft.com/en-us/graph/permissions-reference)
- [gogcli (inspiration)](https://github.com/steipete/gogcli)
- [System.CommandLine 2.0.5](https://www.nuget.org/packages/System.CommandLine)
- [1Password CLI](https://developer.1password.com/docs/cli/)