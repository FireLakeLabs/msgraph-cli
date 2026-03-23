# Phase 3 Design: OneDrive + Tasks

**Date:** 2026-03-23
**Status:** Approved
**Branch:** `feat/phase-3-drive-tasks`

## Context

Phase 2 (mail write + calendar CRUD) is complete with full test coverage. Phase 3 adds OneDrive file management and Microsoft To Do task management, plus the cross-cutting `--dry-run` flag for write operations.

## Decisions

- **No `--progress` flag** for uploads in this phase. The CLI is primarily agent-consumed; progress bars aren't useful for JSON consumers.
- **`--dry-run` applies to all write operations** across both Drive and Todo commands for consistent behavior.
- **Model naming**: Use `TaskListInfo` and `TodoTaskItem` to avoid collision with `System.Threading.Tasks.Task` and `System.Collections.Generic.List`. (PRD uses `TaskList`/`TodoTask` — update PRD to match after implementation.)
- **`--dry-run` excludes `download`** — download only reads from Graph API; the local file write is not a Graph write operation.
- **Scopes already registered** — `ScopeRegistry` already has `drive` (Files.Read/ReadWrite) and `todo` (Tasks.Read/ReadWrite) entries from Phase 1. No changes needed.

## Architecture

Same three-layer pattern as Phase 2:

```
DriveCommands.cs  →  IDriveService / DriveService  →  GraphServiceClient
TasksCommands.cs  →  ITasksService / TasksService  →  GraphServiceClient
```

## DriveService

### Interface: `IDriveService`

| Method | Parameters | Returns | Graph Endpoint |
|---|---|---|---|
| `ListChildrenAsync` | `folderId?`, `path?`, `max?`, `ct` | `IReadOnlyList<DriveItemSummary>` | `GET /me/drive/root/children`, `/items/{id}/children`, or `/root:/{path}:/children` |
| `SearchAsync` | `query`, `max?`, `ct` | `IReadOnlyList<DriveItemSummary>` | `GET /me/drive/root/search(q='...')` |
| `GetItemAsync` | `itemId`, `ct` | `DriveItemDetail` | `GET /me/drive/items/{id}` |
| `DownloadAsync` | `itemId?`, `path?`, `outputPath`, `ct` | `DriveItemSummary` | `GET /me/drive/items/{id}/content` or `/root:/{path}:/content` |
| `UploadAsync` | `localPath`, `remotePath`, `ct` | `DriveItemSummary` | Simple PUT ≤4MB, `createUploadSession` >4MB |
| `CreateFolderAsync` | `name`, `parentPath?`, `ct` | `DriveItemSummary` | `POST /me/drive/root/children` |
| `MoveAsync` | `itemId`, `destinationPath`, `ct` | `DriveItemSummary` | `PATCH /me/drive/items/{id}` (parentReference) |
| `RenameAsync` | `itemId`, `newName`, `ct` | `DriveItemSummary` | `PATCH /me/drive/items/{id}` (name) |
| `DeleteAsync` | `itemId`, `ct` | `void` | `DELETE /me/drive/items/{id}` |

### Upload Strategy

- Files ≤ 4MB: simple `PUT /me/drive/root:/{path}:/content` with file bytes
- Files > 4MB: `POST /me/drive/root:/{path}:/createUploadSession` → `LargeFileUploadTask` from the Graph SDK
- Auto-detected based on `FileInfo.Length`
- Upload session failures are not retried in this phase; the operation fails with an appropriate error

### Download Strategy

- `DownloadAsync` accepts `outputPath` and writes the stream to disk internally, handling disposal
- Exactly one of `itemId` or `path` must be provided (validated in the service layer)
- Returns `DriveItemSummary` of the downloaded item for confirmation output

### Path Resolution

- `--path "/Documents"` resolves via `/me/drive/root:/{path}:` syntax
- `--folder <id>` resolves via `/me/drive/items/{id}`
- No option = root (`/me/drive/root/children`)

## TasksService

### Interface: `ITasksService`

| Method | Parameters | Returns | Graph Endpoint |
|---|---|---|---|
| `ListTaskListsAsync` | `max?`, `ct` | `IReadOnlyList<TaskListInfo>` | `GET /me/todo/lists` |
| `CreateTaskListAsync` | `displayName`, `ct` | `TaskListInfo` | `POST /me/todo/lists` |
| `ListTasksAsync` | `listId`, `status?`, `max?`, `ct` | `IReadOnlyList<TodoTaskItem>` | `GET /me/todo/lists/{id}/tasks` with optional `$filter` |
| `GetTaskAsync` | `listId`, `taskId`, `ct` | `TodoTaskItem` | `GET /me/todo/lists/{id}/tasks/{taskId}` |
| `CreateTaskAsync` | `listId`, `request`, `ct` | `TodoTaskItem` | `POST /me/todo/lists/{id}/tasks` |
| `UpdateTaskAsync` | `listId`, `taskId`, `request`, `ct` | `TodoTaskItem` | `PATCH /me/todo/lists/{id}/tasks/{taskId}` |
| `SetTaskStatusAsync` | `listId`, `taskId`, `completed`, `ct` | `TodoTaskItem` | `PATCH ...` (status field) |
| `DeleteTaskAsync` | `listId`, `taskId`, `ct` | `void` | `DELETE /me/todo/lists/{id}/tasks/{taskId}` |

### Status Filter

`--status incomplete|completed` maps to OData `$filter`:
- `incomplete` → `status ne 'completed'`
- `completed` → `status eq 'completed'`

## Models

### DriveModels.cs

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

### TaskModels.cs

```csharp
public record TaskListInfo(string Id, string DisplayName, bool IsDefaultList);

public record TodoTaskItem(
    string Id, string Title, string? Body, string Status,
    DateTimeOffset? DueDate, DateTimeOffset? CompletedDate,
    DateTimeOffset? Created, DateTimeOffset? LastModified,
    string Importance);

public record TodoTaskCreateRequest(
    string Title,
    string? Body = null,
    DateTimeOffset? DueDate = null,
    string Importance = "normal");

public record TodoTaskUpdateRequest(
    string? Title = null,
    string? Body = null,
    DateTimeOffset? DueDate = null,
    string? Importance = null);
```

## CLI Commands

### Drive Commands

```
msgraph drive
├── ls [--path <path>] [--folder <id>] [--max <n>]
├── search <query> [--max <n>]
├── get <itemId>
├── download <itemId> --out <path>
│   └── download --path <remotePath> --out <localPath>
├── upload <localPath> --path <remotePath>
├── mkdir <name> [--path <parentPath>]
├── move <itemId> --destination <path>
├── rename <itemId> <newName>
└── delete <itemId>
```

### Todo Commands

```
msgraph todo
├── lists [list task lists]
├── lists create <displayName>
├── list <listId> [--status incomplete|completed] [--max <n>]
├── get <listId> <taskId>
├── add <listId> --title <title> [--due <date>] [--body <text>] [--importance low|normal|high]
├── update <listId> <taskId> [--title] [--due] [--body] [--importance]
├── done <listId> <taskId>
├── undo <listId> <taskId>
└── delete <listId> <taskId>
```

## `--dry-run` Flag

- Added as global option in `GlobalOptions`
- Each write command handler checks the flag before calling the service
- When active: prints structured description of the operation to stderr, returns exit code 0
- Format: `[DRY RUN] Would <action>: <details>` to stderr
- JSON mode: writes `{"dryRun": true, "action": "<verb>", "details": {...}}` to stdout

## CommandGuard Extensions

New write commands added to read-only enforcement:

**Drive:** `drive upload`, `drive mkdir`, `drive move`, `drive rename`, `drive delete`
**Todo:** `todo lists create`, `todo add`, `todo update`, `todo done`, `todo undo`, `todo delete`

## Files

### New
| File | Content |
|---|---|
| `src/MsGraphCli.Core/Models/DriveModels.cs` | DriveItemSummary, DriveItemDetail |
| `src/MsGraphCli.Core/Models/TaskModels.cs` | TaskListInfo, TodoTaskItem, create/update requests |
| `src/MsGraphCli.Core/Services/DriveService.cs` | IDriveService interface + DriveService implementation |
| `src/MsGraphCli.Core/Services/TasksService.cs` | ITasksService interface + TasksService implementation |
| `src/MsGraphCli/Commands/DriveCommands.cs` | All drive subcommands |
| `src/MsGraphCli/Commands/TasksCommands.cs` | All todo subcommands |
| `src/MsGraphCli.Tests/Unit/DriveServiceTests.cs` | Unit tests with MockGraphHandler |
| `src/MsGraphCli.Tests/Unit/TasksServiceTests.cs` | Unit tests with MockGraphHandler |

### Modified
| File | Changes |
|---|---|
| `src/MsGraphCli/Program.cs` | Register drive + todo command groups |
| `src/MsGraphCli/GlobalOptions.cs` | Add `DryRun` option |
| `src/MsGraphCli/Output/OutputFormatters.cs` | Add WriteDriveItemTable, WriteDriveItemDetailTable, WriteTaskListTable, WriteTodoTaskTable |
| `src/MsGraphCli/Middleware/CommandGuard.cs` | Extend WriteCommands set |
| `src/MsGraphCli.Tests/Unit/CommandGuardTests.cs` | Add test cases for new write commands |

## Testing

- **DriveServiceTests**: ListChildren (3 resolution modes), Search, GetItem, Upload (simple + size threshold), CreateFolder, Move, Rename, Delete, error paths
- **TasksServiceTests**: ListTaskLists, CreateTaskList, ListTasks (with status filter), GetTask, CreateTask, UpdateTask, SetTaskStatus (done/undo), DeleteTask, error paths
- **CommandGuardTests**: Extend with all new write commands for read-only enforcement
- **Integration test placeholders**: gated behind `MSGRAPH_LIVE=1`

## Verification

1. `dotnet build` — zero warnings
2. `dotnet test` — all tests pass
3. Manual smoke test: `dotnet run --project src/MsGraphCli -- drive ls --json` (requires auth)
4. Verify `--dry-run` on a write command prints expected output without executing
