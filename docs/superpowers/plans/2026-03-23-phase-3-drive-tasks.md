# Phase 3: OneDrive + Tasks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add OneDrive file management and Microsoft To Do task management commands with `--dry-run` support.

**Scope note:** `--dry-run` is implemented for Phase 3 commands only. Retrofitting it into existing Mail/Calendar write commands is deferred to a future task.

**Architecture:** Two new services (DriveService, TasksService) in Core layer, two new command files in CLI layer, following the established three-layer pattern. Cross-cutting `--dry-run` flag added as global option.

**Tech Stack:** .NET 10, C# latest, Microsoft.Graph SDK v5+, System.CommandLine 2.0.5, xUnit, FluentAssertions

**Spec:** `docs/superpowers/specs/2026-03-23-phase-3-drive-tasks-design.md`

---

### Task 1: Models

**Files:**
- Create: `src/FireLakeLabs.MsGraphCli.Core/Models/DriveModels.cs`
- Create: `src/FireLakeLabs.MsGraphCli.Core/Models/TaskModels.cs`

- [ ] **Step 1: Create DriveModels.cs**

```csharp
namespace FireLakeLabs.MsGraphCli.Core.Models;

public record DriveItemSummary(
    string Id, string Name, string? MimeType, long? Size,
    DateTimeOffset? LastModified, bool IsFolder, string? WebUrl);

public record DriveItemDetail(
    string Id, string Name, string? MimeType, long? Size,
    DateTimeOffset? Created, DateTimeOffset? LastModified,
    bool IsFolder, string? WebUrl, string? ParentPath,
    string? DownloadUrl);
```

- [ ] **Step 2: Create TaskModels.cs**

```csharp
namespace FireLakeLabs.MsGraphCli.Core.Models;

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

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

- [ ] **Step 4: Commit**

```bash
git add src/FireLakeLabs.MsGraphCli.Core/Models/DriveModels.cs src/FireLakeLabs.MsGraphCli.Core/Models/TaskModels.cs
git commit -m "feat: Add Drive and Task model records for Phase 3"
```

---

### Task 2: DriveService

**Files:**
- Create: `src/FireLakeLabs.MsGraphCli.Core/Services/DriveService.cs`
- Reference: `src/FireLakeLabs.MsGraphCli.Core/Services/MailService.cs` (pattern to follow)
- Reference: `src/FireLakeLabs.MsGraphCli.Core/Services/CalendarService.cs` (pattern to follow)

The service contains both the `IDriveService` interface and the `DriveService` implementation in the same file (matching existing pattern).

- [ ] **Step 1: Create DriveService.cs with interface and implementation**

Interface methods:
- `ListChildrenAsync(string? folderId, string? path, int? max, CancellationToken)` → `IReadOnlyList<DriveItemSummary>`
- `SearchAsync(string query, int? max, CancellationToken)` → `IReadOnlyList<DriveItemSummary>`
- `GetItemAsync(string itemId, CancellationToken)` → `DriveItemDetail`
- `DownloadAsync(string? itemId, string? path, string outputPath, CancellationToken)` → `DriveItemSummary`
- `UploadAsync(string localPath, string remotePath, CancellationToken)` → `DriveItemSummary`
- `CreateFolderAsync(string name, string? parentPath, CancellationToken)` → `DriveItemSummary`
- `MoveAsync(string itemId, string destinationPath, CancellationToken)` → `DriveItemSummary`
- `RenameAsync(string itemId, string newName, CancellationToken)` → `DriveItemSummary`
- `DeleteAsync(string itemId, CancellationToken)` → `void`

Implementation details:
- Constructor takes `GraphServiceClient`
- `ListChildrenAsync`: Three branches — if `folderId` set, use `/items/{id}/children`; if `path` set, use `/root:/{path}:/children`; else use `/root/children`. Default max=50.
- `SearchAsync`: Use `_client.Me.Drive.Root.SearchWithQ(query)` fluent API. Default max=25.
- `GetItemAsync`: Use `_client.Me.Drive.Items[itemId].GetAsync()`. Throw `ResourceNotFoundException` if null.
- `DownloadAsync`: Validate exactly one of `itemId`/`path` is provided. Get item metadata first (for filename/details), then get content stream, write to `outputPath` using `File.Create` + `Stream.CopyToAsync`. Return `DriveItemSummary`.
- `UploadAsync`: Check `FileInfo.Length`. If ≤ 4MB, read bytes and PUT via `_client.Me.Drive.Root.ItemWithPath(remotePath).Content.PutAsync(stream)`. If > 4MB, create upload session via `_client.Me.Drive.Root.ItemWithPath(remotePath).CreateUploadSession.PostAsync()` then use `LargeFileUploadTask`. Return `DriveItemSummary` of the created item.
- `CreateFolderAsync`: POST a new `DriveItem { Name = name, Folder = new Folder() }` to `/root/children` or `/root:/{parentPath}:/children`.
- `MoveAsync`: Resolve destination path to get parent item ID via `/root:/{destinationPath}:`, then PATCH the item with `ParentReference = { Id = parentId }`.
- `RenameAsync`: PATCH `_client.Me.Drive.Items[itemId]` with `new DriveItem { Name = newName }`.
- `DeleteAsync`: DELETE `_client.Me.Drive.Items[itemId]`.
- Private helper `MapSummary(DriveItem)` and `MapDetail(DriveItem)` for DTO mapping.

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/FireLakeLabs.MsGraphCli.Core/Services/DriveService.cs
git commit -m "feat: Add DriveService with OneDrive file operations"
```

---

### Task 3: TasksService

**Files:**
- Create: `src/FireLakeLabs.MsGraphCli.Core/Services/TasksService.cs`

The service contains both the `ITasksService` interface and the `TasksService` implementation.

- [ ] **Step 1: Create TasksService.cs with interface and implementation**

Interface methods:
- `ListTaskListsAsync(int? max, CancellationToken)` → `IReadOnlyList<TaskListInfo>`
- `CreateTaskListAsync(string displayName, CancellationToken)` → `TaskListInfo`
- `ListTasksAsync(string listId, string? status, int? max, CancellationToken)` → `IReadOnlyList<TodoTaskItem>`
- `GetTaskAsync(string listId, string taskId, CancellationToken)` → `TodoTaskItem`
- `CreateTaskAsync(string listId, TodoTaskCreateRequest request, CancellationToken)` → `TodoTaskItem`
- `UpdateTaskAsync(string listId, string taskId, TodoTaskUpdateRequest request, CancellationToken)` → `TodoTaskItem`
- `SetTaskStatusAsync(string listId, string taskId, bool completed, CancellationToken)` → `TodoTaskItem`
- `DeleteTaskAsync(string listId, string taskId, CancellationToken)` → `void`

Implementation details:
- Constructor takes `GraphServiceClient`
- `ListTaskListsAsync`: `_client.Me.Todo.Lists.GetAsync()` with `$top`. Default max=100.
- `CreateTaskListAsync`: POST `new TodoTaskList { DisplayName = displayName }`. Throw `MsGraphCliException("CreateFailed")` if null.
- `ListTasksAsync`: GET `_client.Me.Todo.Lists[listId].Tasks.GetAsync()`. If `status` is "completed", add `$filter=status eq 'completed'`; if "incomplete", add `$filter=status ne 'completed'`. Default max=50.
- `GetTaskAsync`: GET single task. Throw `ResourceNotFoundException` if null.
- `CreateTaskAsync`: POST `new TodoTask { Title, Body = { Content, ContentType=text }, DueDateTime, Importance }`. Only set fields that are non-null in the request. Throw `MsGraphCliException("CreateFailed")` if null.
- `UpdateTaskAsync`: PATCH with only non-null fields from request. Throw `MsGraphCliException("UpdateFailed")` if null.
- `SetTaskStatusAsync`: PATCH `status` to `"completed"` or `"notStarted"`. Throw `MsGraphCliException("UpdateFailed")` if null.
- `DeleteTaskAsync`: DELETE the task.
- Private helpers `MapTaskList(TodoTaskList)`, `MapTask(TodoTask)` for DTO mapping.

Note: Graph API uses `Microsoft.Graph.Models.TodoTaskList` and `Microsoft.Graph.Models.TodoTask`. The `DueDateTime` field on `TodoTask` is a `DateTimeTimeZone` (same as Calendar events). The `Importance` field is an enum `Microsoft.Graph.Models.Importance` (low, normal, high).

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/FireLakeLabs.MsGraphCli.Core/Services/TasksService.cs
git commit -m "feat: Add TasksService with Microsoft To Do operations"
```

---

### Task 4: DriveServiceTests

**Files:**
- Create: `src/FireLakeLabs.MsGraphCli.Tests/Unit/DriveServiceTests.cs`
- Reference: `src/FireLakeLabs.MsGraphCli.Tests/Unit/MailServiceTests.cs` (pattern to follow)
- Reference: `src/FireLakeLabs.MsGraphCli.Tests/Unit/Helpers/GraphMockHelper.cs`

- [ ] **Step 1: Write DriveServiceTests**

Follow the established `MockGraphHandler` pattern from `MailServiceTests.cs`:
- Create handler → enqueue JSON → create `GraphServiceClient` via `MockGraphHandler.CreateClient()` → create `DriveService` → call method → assert on result and captured request.

Test cases to cover:

**ListChildrenAsync:**
- `ListChildren_NoOptions_CallsRootChildren` — no folderId or path, verify URL contains `/root/children`
- `ListChildren_WithFolderId_CallsItemChildren` — folderId="folder-123", verify URL contains `/items/folder-123/children`
- `ListChildren_WithPath_CallsPathChildren` — path="/Documents", verify URL contains path-based resolution
- `ListChildren_DefaultMax_Uses50` — verify `$top=50`
- `ListChildren_MapsFieldsCorrectly` — return drive items, verify `DriveItemSummary` fields
- `ListChildren_NullResponse_ReturnsEmptyList`

**SearchAsync:**
- `Search_CallsSearchEndpoint` — verify URL contains search term
- `Search_DefaultMax_Uses25`

**GetItemAsync:**
- `GetItem_ReturnsDetail` — verify all `DriveItemDetail` fields mapped including ParentPath, DownloadUrl
- `GetItem_NullResponse_ThrowsResourceNotFoundException`

**DownloadAsync:**
- `Download_ByItemId_WritesToFile` — create temp output path, verify file written
- `Download_BothItemIdAndPath_ThrowsMsGraphCliException` — both provided, expect error
- `Download_NeitherItemIdNorPath_ThrowsMsGraphCliException` — neither provided, expect error

**UploadAsync:**
- `Upload_SmallFile_UsesPut` — create temp file <4MB, verify PUT method used
- `Upload_ReturnsItemSummary` — verify returned DriveItemSummary

**CreateFolderAsync:**
- `CreateFolder_PostsWithFolderProperty` — verify POST body contains folder property and name
- `CreateFolder_WithParentPath_UsesPathEndpoint`

**MoveAsync:**
- `Move_PatchesParentReference` — verify PATCH to correct endpoint

**RenameAsync:**
- `Rename_PatchesName` — verify PATCH body contains new name

**DeleteAsync:**
- `Delete_CallsDeleteEndpoint` — verify DELETE method on correct URL

- [ ] **Step 2: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~DriveServiceTests"`
Expected: All tests pass

- [ ] **Step 3: Commit**

```bash
git add src/FireLakeLabs.MsGraphCli.Tests/Unit/DriveServiceTests.cs
git commit -m "test: Add DriveService unit tests"
```

---

### Task 5: TasksServiceTests

**Files:**
- Create: `src/FireLakeLabs.MsGraphCli.Tests/Unit/TasksServiceTests.cs`

- [ ] **Step 1: Write TasksServiceTests**

Test cases:

**ListTaskListsAsync:**
- `ListTaskLists_ReturnsTaskListInfos` — verify mapping of Id, DisplayName, IsDefaultList
- `ListTaskLists_NullResponse_ReturnsEmptyList`

**CreateTaskListAsync:**
- `CreateTaskList_PostsToListsEndpoint` — verify POST, body contains displayName
- `CreateTaskList_ReturnsCreatedList`
- `CreateTaskList_NullResponse_ThrowsMsGraphCliException`

**ListTasksAsync:**
- `ListTasks_NoStatusFilter_GetsAllTasks` — verify no `$filter` in query
- `ListTasks_StatusCompleted_FiltersCompleted` — verify `$filter` contains `status eq 'completed'`
- `ListTasks_StatusIncomplete_FiltersIncomplete` — verify `$filter` contains `status ne 'completed'`
- `ListTasks_DefaultMax_Uses50`
- `ListTasks_MapsFieldsCorrectly` — verify all TodoTaskItem fields

**GetTaskAsync:**
- `GetTask_ReturnsTaskDetail`
- `GetTask_NullResponse_ThrowsResourceNotFoundException`

**CreateTaskAsync:**
- `CreateTask_MinimalRequest_PostsTitle` — only title set
- `CreateTask_FullRequest_IncludesAllFields` — title, body, due date, importance

**UpdateTaskAsync:**
- `UpdateTask_PartialUpdate_PatchesOnlyProvidedFields`

**SetTaskStatusAsync:**
- `SetTaskStatus_Completed_SetsStatusCompleted` — verify PATCH body has `status: completed`
- `SetTaskStatus_NotCompleted_SetsStatusNotStarted` — verify PATCH body has `status: notStarted`

**DeleteTaskAsync:**
- `DeleteTask_CallsDeleteEndpoint` — verify DELETE on correct URL

- [ ] **Step 2: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~TasksServiceTests"`
Expected: All tests pass

- [ ] **Step 3: Create integration test placeholders**

Create `src/FireLakeLabs.MsGraphCli.Tests/Integration/DriveIntegrationTests.cs` and `src/FireLakeLabs.MsGraphCli.Tests/Integration/TasksIntegrationTests.cs` following the existing pattern in `CalendarIntegrationTests.cs` — a single `[Fact(Skip = "...")]` placeholder method gated behind `MSGRAPH_LIVE=1`.

- [ ] **Step 4: Commit**

```bash
git add src/FireLakeLabs.MsGraphCli.Tests/Unit/TasksServiceTests.cs src/FireLakeLabs.MsGraphCli.Tests/Integration/DriveIntegrationTests.cs src/FireLakeLabs.MsGraphCli.Tests/Integration/TasksIntegrationTests.cs
git commit -m "test: Add TasksService unit tests and integration test placeholders"
```

---

### Task 6: Cross-cutting — GlobalOptions, CommandGuard, and tests

**Files:**
- Modify: `src/FireLakeLabs.MsGraphCli/GlobalOptions.cs`
- Modify: `src/FireLakeLabs.MsGraphCli/Program.cs` (add --dry-run option registration)
- Modify: `src/FireLakeLabs.MsGraphCli/Middleware/CommandGuard.cs`
- Modify: `src/FireLakeLabs.MsGraphCli.Tests/Unit/CommandGuardTests.cs`

- [ ] **Step 1: Add DryRun to GlobalOptions**

Change the record to add the new option:

```csharp
public sealed record GlobalOptions(
    Option<bool> Json,
    Option<bool> Plain,
    Option<bool> Verbose,
    Option<bool> Beta,
    Option<bool> ReadOnly,
    Option<bool> DryRun
);
```

- [ ] **Step 2: Register --dry-run in Program.cs**

Add after the `readOnlyOption` line:

```csharp
var dryRunOption = new Option<bool>("--dry-run") { Description = "Show what would be done without executing" };
rootCommand.Options.Add(dryRunOption);
```

Update the `GlobalOptions` constructor call to include `dryRunOption`:

```csharp
var globalContext = new GlobalOptions(jsonOption, plainOption, verboseOption, betaOption, readOnlyOption, dryRunOption);
```

- [ ] **Step 3: Extend CommandGuard.WriteCommands**

Add new entries to the `WriteCommands` HashSet in `CommandGuard.cs`:

```csharp
"drive upload",
"drive mkdir",
"drive move",
"drive rename",
"drive delete",
"todo lists create",
"todo add",
"todo update",
"todo done",
"todo undo",
"todo delete",
```

- [ ] **Step 4: Add CommandGuard test cases**

Add new `[InlineData]` entries to the existing `EnforceReadOnly_WriteCommand_WhenReadOnly_Throws` theory in `CommandGuardTests.cs`:

```csharp
[InlineData("drive upload")]
[InlineData("drive mkdir")]
[InlineData("drive move")]
[InlineData("drive rename")]
[InlineData("drive delete")]
[InlineData("todo lists create")]
[InlineData("todo add")]
[InlineData("todo update")]
[InlineData("todo done")]
[InlineData("todo undo")]
[InlineData("todo delete")]
```

And new `[InlineData]` for read commands that should NOT throw:

```csharp
[InlineData("drive ls")]
[InlineData("drive search")]
[InlineData("drive get")]
[InlineData("drive download")]
[InlineData("todo lists")]
[InlineData("todo list")]
[InlineData("todo get")]
```

- [ ] **Step 5: Build and run tests**

Run: `dotnet build && dotnet test --filter "FullyQualifiedName~CommandGuardTests"`
Expected: All tests pass, 0 warnings

- [ ] **Step 6: Commit**

```bash
git add src/FireLakeLabs.MsGraphCli/GlobalOptions.cs src/FireLakeLabs.MsGraphCli/Program.cs src/FireLakeLabs.MsGraphCli/Middleware/CommandGuard.cs src/FireLakeLabs.MsGraphCli.Tests/Unit/CommandGuardTests.cs
git commit -m "feat: Add --dry-run global option and extend CommandGuard for Phase 3 commands"
```

---

### Task 7: Output Formatters

**Files:**
- Modify: `src/FireLakeLabs.MsGraphCli/Output/OutputFormatters.cs`

- [ ] **Step 1: Add table writer methods to TableOutputFormatter**

Add after the existing `WriteFreeBusyTable` method:

`WriteDriveItemTable(IReadOnlyList<DriveItemSummary>)` — columns: NAME, SIZE, MODIFIED, TYPE (folder/file), ID
`WriteDriveItemDetailTable(DriveItemDetail)` — single-item display with all fields
`WriteTaskListTable(IReadOnlyList<TaskListInfo>)` — columns: NAME, DEFAULT, ID
`WriteTodoTaskTable(IReadOnlyList<TodoTaskItem>)` — columns: TITLE, STATUS, DUE, IMPORTANCE, ID

Use the existing `Truncate()` helper. Format sizes using the same KB/MB pattern from `WriteAttachmentTable`. Format dates using `yyyy-MM-dd HH:mm` pattern. Use green/red/yellow for status colors.

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/FireLakeLabs.MsGraphCli/Output/OutputFormatters.cs
git commit -m "feat: Add table formatters for Drive items and Todo tasks"
```

---

### Task 8: DriveCommands

**Files:**
- Create: `src/FireLakeLabs.MsGraphCli/Commands/DriveCommands.cs`
- Reference: `src/FireLakeLabs.MsGraphCli/Commands/CalendarCommands.cs` (pattern to follow)

- [ ] **Step 1: Create DriveCommands.cs**

Follow the `CalendarCommands.cs` pattern exactly:
- Static `Build(GlobalOptions global)` method returns the `drive` command with all subcommands
- Private `CreateServiceContext(ParseResult parseResult, GlobalOptions global, bool readOnly = true)` helper creates `DriveService` instance + `IOutputFormatter`
  - The `readOnly` parameter controls OAuth scope selection (read vs read-write). It is NOT the `--read-only` CLI flag.
  - Read commands call `CreateServiceContext(parseResult, global)` (defaults to `readOnly: true`)
  - Write commands call `CreateServiceContext(parseResult, global, readOnly: false)` to get `Files.ReadWrite` scope
  - Uses `ScopeRegistry.GetScopes(["drive"], readOnly: readOnly)` for scopes
  - Follow the exact pattern from `CalendarCommands.CreateServiceContext` (lines 33-52 of CalendarCommands.cs)
- Each subcommand method (BuildLs, BuildSearch, BuildGet, BuildDownload, BuildUpload, BuildMkdir, BuildMove, BuildRename, BuildDelete) follows the established pattern:
  1. Define arguments/options
  2. Create command
  3. Use `SetGuardedAction(command, global, async (parseResult, ct) => { ... })`
  4. Inside handler: call `CommandGuard.EnforceReadOnly()` for write commands
  5. For write commands: check `parseResult.GetValue(global.DryRun)` — if true, write dry-run output to stderr (or JSON to stdout) and return early
  6. Call service method
  7. Format output (JSON via formatter, table via static method, plain via tab-separated)

Dry-run pattern for write commands:
```csharp
if (parseResult.GetValue(global.DryRun))
{
    bool isJson = parseResult.GetValue(global.Json);
    if (isJson)
    {
        formatter.WriteResult(new { dryRun = true, action = "upload", details = new { localPath, remotePath } }, Console.Out);
    }
    else
    {
        Console.Error.WriteLine($"[DRY RUN] Would upload: {localPath} → {remotePath}");
    }
    return;
}
```

Commands to implement:
- `drive ls` — read, uses `ListChildrenAsync`, options: `--path`, `--folder`, `--max`
- `drive search` — read, uses `SearchAsync`, argument: `query`, option: `--max`
- `drive get` — read, uses `GetItemAsync`, argument: `itemId`
- `drive download` — **read command** (no dry-run, no read-only guard), uses `DownloadAsync`, argument: `itemId` (optional), options: `--path`, `--out` (required). Per spec, download reads from Graph and writes locally, which is not a Graph write operation.
- `drive upload` — write, uses `UploadAsync`, argument: `localPath`, option: `--path` (required)
- `drive mkdir` — write, uses `CreateFolderAsync`, argument: `name`, option: `--path`
- `drive move` — write, uses `MoveAsync`, argument: `itemId`, option: `--destination` (required)
- `drive rename` — write, uses `RenameAsync`, arguments: `itemId`, `newName`
- `drive delete` — write, uses `DeleteAsync`, argument: `itemId`

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/FireLakeLabs.MsGraphCli/Commands/DriveCommands.cs
git commit -m "feat: Add Drive CLI commands (ls, search, get, download, upload, mkdir, move, rename, delete)"
```

---

### Task 9: TasksCommands

**Files:**
- Create: `src/FireLakeLabs.MsGraphCli/Commands/TasksCommands.cs`

- [ ] **Step 1: Create TasksCommands.cs**

Same pattern as DriveCommands. The top-level command is `todo`.

Service context uses `ScopeRegistry.GetScopes(["todo"], readOnly: readOnly)`. Same `readOnly` parameter convention as DriveCommands: read commands default `readOnly: true`, write commands pass `readOnly: false` to get `Tasks.ReadWrite` scope.

Commands:
- `todo lists` — read, uses `ListTaskListsAsync`. No arguments. This is a subcommand group.
- `todo lists create` — write (nested under `lists` subcommand), uses `CreateTaskListAsync`, argument: `displayName`
- `todo list` — read, uses `ListTasksAsync`, argument: `listId`, options: `--status`, `--max`
- `todo get` — read, uses `GetTaskAsync`, arguments: `listId`, `taskId`
- `todo add` — write, uses `CreateTaskAsync`, argument: `listId`, options: `--title` (required), `--due`, `--body`, `--importance`
- `todo update` — write, uses `UpdateTaskAsync`, arguments: `listId`, `taskId`, options: `--title`, `--due`, `--body`, `--importance`
- `todo done` — write, uses `SetTaskStatusAsync(completed: true)`, arguments: `listId`, `taskId`
- `todo undo` — write, uses `SetTaskStatusAsync(completed: false)`, arguments: `listId`, `taskId`
- `todo delete` — write, uses `DeleteTaskAsync`, arguments: `listId`, `taskId`

Note: `todo lists` (list all task lists) and `todo lists create` (create a new list) are structured as:
```csharp
var listsCommand = new Command("lists", "Manage task lists");
// Default action on "lists" = list all task lists
SetGuardedAction(listsCommand, global, async (parseResult, ct) => { ... });
// Subcommand "create"
listsCommand.Subcommands.Add(BuildListsCreate(global));
```

**System.CommandLine note:** Setting an action on a command that also has subcommands works in 2.0.5 — the parent action fires when no subcommand is matched. Verify this during implementation by testing both `msgraph todo lists` and `msgraph todo lists create "Test"` in Task 11.

All write commands include `CommandGuard.EnforceReadOnly()` and `--dry-run` checks.

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/FireLakeLabs.MsGraphCli/Commands/TasksCommands.cs
git commit -m "feat: Add Todo CLI commands (lists, list, get, add, update, done, undo, delete)"
```

---

### Task 10: Register commands in Program.cs

**Files:**
- Modify: `src/FireLakeLabs.MsGraphCli/Program.cs`

- [ ] **Step 1: Add drive and todo command registration**

Add after the `CalendarCommands.Build` line:

```csharp
rootCommand.Subcommands.Add(DriveCommands.Build(globalContext));
rootCommand.Subcommands.Add(TasksCommands.Build(globalContext));
```

- [ ] **Step 2: Build and run full test suite**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, all tests pass

- [ ] **Step 3: Commit**

```bash
git add src/FireLakeLabs.MsGraphCli/Program.cs
git commit -m "feat: Register Drive and Todo commands in root command"
```

---

### Task 11: Final verification

- [ ] **Step 1: Full build**

Run: `dotnet build`
Expected: 0 warnings, 0 errors

- [ ] **Step 2: Full test suite**

Run: `dotnet test`
Expected: All tests pass (existing ~107 + new DriveServiceTests + TasksServiceTests + CommandGuardTests extensions)

- [ ] **Step 3: Verify CLI help output**

Run: `dotnet run --project src/FireLakeLabs.MsGraphCli -- --help`
Expected: Shows `drive` and `todo` in command list

Run: `dotnet run --project src/FireLakeLabs.MsGraphCli -- drive --help`
Expected: Shows all drive subcommands (ls, search, get, download, upload, mkdir, move, rename, delete)

Run: `dotnet run --project src/FireLakeLabs.MsGraphCli -- todo --help`
Expected: Shows all todo subcommands (lists, list, get, add, update, done, undo, delete)

- [ ] **Step 4: Verify --dry-run flag**

Run: `dotnet run --project src/FireLakeLabs.MsGraphCli -- --help`
Expected: Shows `--dry-run` in global options
