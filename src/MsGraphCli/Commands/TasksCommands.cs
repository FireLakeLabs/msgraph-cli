using System.CommandLine;
using System.Globalization;
using MsGraphCli.Core.Auth;
using MsGraphCli.Core.Config;
using MsGraphCli.Core.Graph;
using MsGraphCli.Core.Models;
using MsGraphCli.Core.Services;
using static MsGraphCli.Middleware.ActionRunner;
using MsGraphCli.Middleware;
using MsGraphCli.Output;

namespace MsGraphCli.Commands;

public static class TasksCommands
{
    public static Command Build(GlobalOptions global)
    {
        var todoCommand = new Command("todo", "Microsoft To Do task operations");

        // "todo lists" — has both a default action (list all) and a subcommand (create)
        var listsCommand = new Command("lists", "Manage task lists");
        SetGuardedAction(listsCommand, global, async (parseResult, cancellationToken) =>
        {
            // Default action: list all task lists
            var (service, formatter) = CreateServiceContext(parseResult, global);
            IReadOnlyList<TaskListInfo> lists = await service.ListTaskListsAsync(null, cancellationToken);
            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { lists }, Console.Out);
            }
            else if (formatter is TableOutputFormatter)
            {
                TableOutputFormatter.WriteTaskListTable(lists);
            }
            else
            {
                foreach (TaskListInfo list in lists)
                {
                    Console.WriteLine($"{list.DisplayName}\t{(list.IsDefaultList ? "default" : "")}\t{list.Id}");
                }
            }
        });
        listsCommand.Subcommands.Add(BuildListsCreate(global));

        todoCommand.Subcommands.Add(listsCommand);
        todoCommand.Subcommands.Add(BuildList(global));
        todoCommand.Subcommands.Add(BuildGet(global));
        todoCommand.Subcommands.Add(BuildAdd(global));
        todoCommand.Subcommands.Add(BuildUpdate(global));
        todoCommand.Subcommands.Add(BuildDone(global));
        todoCommand.Subcommands.Add(BuildUndo(global));
        todoCommand.Subcommands.Add(BuildDelete(global));

        return todoCommand;
    }

    private static (ITasksService Service, IOutputFormatter Formatter) CreateServiceContext(
        ParseResult parseResult, GlobalOptions global, bool readOnly = true)
    {
        AppConfig config = ConfigLoader.Load();
        var store = new OnePasswordSecretStore(config.OnePasswordVault);
        var tokenCacheStore = new OnePasswordSecretStore(config.TokenCacheVault);
        var authProvider = new GraphAuthProvider(store, tokenCacheStore);

        string[] scopes = ScopeRegistry.GetScopes(["todo"], readOnly: readOnly);
        HttpClient httpClient = GraphClientFactory.CreateDefaultHttpClient();
        var factory = new GraphClientFactory(authProvider, scopes, httpClient);
        var client = factory.CreateClient();
        var service = new TasksService(client);

        bool isJson = parseResult.GetValue(global.Json);
        bool isPlain = parseResult.GetValue(global.Plain);
        IOutputFormatter formatter = OutputFormatResolver.Resolve(isJson, isPlain);

        return (service, formatter);
    }

    // ── todo lists create ──

    private static Command BuildListsCreate(GlobalOptions global)
    {
        var displayNameArgument = new Argument<string>("displayName") { Description = "Name for the new task list" };

        var command = new Command("create", "Create a new task list");
        command.Arguments.Add(displayNameArgument);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("todo lists create", parseResult.GetValue(global.ReadOnly));

            string displayName = parseResult.GetValue(displayNameArgument)!;

            if (parseResult.GetValue(global.DryRun))
            {
                IOutputFormatter dryFormatter = OutputFormatResolver.Resolve(parseResult.GetValue(global.Json), parseResult.GetValue(global.Plain));
                if (parseResult.GetValue(global.Json))
                {
                    dryFormatter.WriteResult(new { dryRun = true, action = "lists create", details = new { displayName } }, Console.Out);
                }
                else
                {
                    Console.Error.WriteLine($"[DRY RUN] Would create task list: {displayName}");
                }
                return;
            }

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            TaskListInfo created = await service.CreateTaskListAsync(displayName, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "created", list = created }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Created: {created.DisplayName} ({created.Id})");
            }
        });

        return command;
    }

    // ── todo list ──

    private static Command BuildList(GlobalOptions global)
    {
        var listIdArgument = new Argument<string>("listId") { Description = "Task list ID" };
        var statusOption = new Option<string?>("--status") { Description = "Filter by status: incomplete or completed" };
        var maxOption = new Option<int?>("-n", "--max") { Description = "Maximum number of tasks" };

        var command = new Command("list", "List tasks in a task list");
        command.Arguments.Add(listIdArgument);
        command.Options.Add(statusOption);
        command.Options.Add(maxOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            string listId = parseResult.GetValue(listIdArgument)!;
            string? status = parseResult.GetValue(statusOption);
            int? max = parseResult.GetValue(maxOption);

            IReadOnlyList<TodoTaskItem> tasks = await service.ListTasksAsync(listId, status, max, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { tasks }, Console.Out);
            }
            else if (formatter is TableOutputFormatter)
            {
                TableOutputFormatter.WriteTodoTaskTable(tasks);
            }
            else
            {
                foreach (TodoTaskItem task in tasks)
                {
                    string due = task.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
                    Console.WriteLine($"{task.Title}\t{task.Status}\t{due}\t{task.Id}");
                }
            }
        });

        return command;
    }

    // ── todo get ──

    private static Command BuildGet(GlobalOptions global)
    {
        var listIdArgument = new Argument<string>("listId") { Description = "Task list ID" };
        var taskIdArgument = new Argument<string>("taskId") { Description = "Task ID" };

        var command = new Command("get", "Get task details");
        command.Arguments.Add(listIdArgument);
        command.Arguments.Add(taskIdArgument);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            string listId = parseResult.GetValue(listIdArgument)!;
            string taskId = parseResult.GetValue(taskIdArgument)!;

            TodoTaskItem task = await service.GetTaskAsync(listId, taskId, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { task }, Console.Out);
            }
            else
            {
                Console.WriteLine($"Title:      {task.Title}");
                Console.WriteLine($"Status:     {task.Status}");
                Console.WriteLine($"Importance: {task.Importance}");
                if (task.DueDate.HasValue) Console.WriteLine($"Due:        {task.DueDate.Value:yyyy-MM-dd}");
                if (task.CompletedDate.HasValue) Console.WriteLine($"Completed:  {task.CompletedDate.Value:yyyy-MM-dd}");
                if (task.Body is not null)
                {
                    Console.WriteLine();
                    Console.WriteLine("── Body ──");
                    Console.WriteLine(task.Body);
                }
                Console.WriteLine($"ID:         {task.Id}");
            }
        });

        return command;
    }

    // ── todo add ──

    private static Command BuildAdd(GlobalOptions global)
    {
        var listIdArgument = new Argument<string>("listId") { Description = "Task list ID" };
        var titleOption = new Option<string>("--title") { Description = "Task title", Required = true };
        var dueOption = new Option<string?>("--due") { Description = "Due date (yyyy-MM-dd or ISO 8601)" };
        var bodyOption = new Option<string?>("--body") { Description = "Task body text" };
        var importanceOption = new Option<string?>("--importance") { Description = "Importance: low, normal, or high" };

        var command = new Command("add", "Add a new task");
        command.Arguments.Add(listIdArgument);
        command.Options.Add(titleOption);
        command.Options.Add(dueOption);
        command.Options.Add(bodyOption);
        command.Options.Add(importanceOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("todo add", parseResult.GetValue(global.ReadOnly));

            string listId = parseResult.GetValue(listIdArgument)!;
            string title = parseResult.GetValue(titleOption)!;

            if (parseResult.GetValue(global.DryRun))
            {
                IOutputFormatter dryFormatter = OutputFormatResolver.Resolve(parseResult.GetValue(global.Json), parseResult.GetValue(global.Plain));
                if (parseResult.GetValue(global.Json))
                {
                    dryFormatter.WriteResult(new { dryRun = true, action = "add", details = new { listId, title } }, Console.Out);
                }
                else
                {
                    Console.Error.WriteLine($"[DRY RUN] Would add task: {title}");
                }
                return;
            }

            string? dueRaw = parseResult.GetValue(dueOption);
            DateTimeOffset? dueDate = null;
            if (dueRaw is not null)
            {
                if (!DateTimeOffset.TryParse(dueRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsedDue))
                {
                    throw new MsGraphCli.Core.Exceptions.MsGraphCliException(
                        $"Invalid value for --due: '{dueRaw}'. Expected a date like yyyy-MM-dd or ISO 8601.",
                        "InvalidArgument", exitCode: 1);
                }
                dueDate = parsedDue;
            }

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            var request = new TodoTaskCreateRequest(
                Title: title,
                Body: parseResult.GetValue(bodyOption),
                DueDate: dueDate,
                Importance: parseResult.GetValue(importanceOption) ?? "normal"
            );

            TodoTaskItem created = await service.CreateTaskAsync(listId, request, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "created", task = created }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Created: {created.Title} ({created.Id})");
            }
        });

        return command;
    }

    // ── todo update ──

    private static Command BuildUpdate(GlobalOptions global)
    {
        var listIdArgument = new Argument<string>("listId") { Description = "Task list ID" };
        var taskIdArgument = new Argument<string>("taskId") { Description = "Task ID to update" };
        var titleOption = new Option<string?>("--title") { Description = "New title" };
        var dueOption = new Option<string?>("--due") { Description = "New due date" };
        var bodyOption = new Option<string?>("--body") { Description = "New body text" };
        var importanceOption = new Option<string?>("--importance") { Description = "New importance: low, normal, or high" };

        var command = new Command("update", "Update a task");
        command.Arguments.Add(listIdArgument);
        command.Arguments.Add(taskIdArgument);
        command.Options.Add(titleOption);
        command.Options.Add(dueOption);
        command.Options.Add(bodyOption);
        command.Options.Add(importanceOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("todo update", parseResult.GetValue(global.ReadOnly));

            string listId = parseResult.GetValue(listIdArgument)!;
            string taskId = parseResult.GetValue(taskIdArgument)!;

            if (parseResult.GetValue(global.DryRun))
            {
                IOutputFormatter dryFormatter = OutputFormatResolver.Resolve(parseResult.GetValue(global.Json), parseResult.GetValue(global.Plain));
                if (parseResult.GetValue(global.Json))
                {
                    dryFormatter.WriteResult(new { dryRun = true, action = "update", details = new { listId, taskId } }, Console.Out);
                }
                else
                {
                    Console.Error.WriteLine($"[DRY RUN] Would update task: {taskId}");
                }
                return;
            }

            string? dueRaw = parseResult.GetValue(dueOption);
            DateTimeOffset? dueDate = null;
            if (dueRaw is not null)
            {
                if (!DateTimeOffset.TryParse(dueRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsedDue))
                {
                    throw new MsGraphCli.Core.Exceptions.MsGraphCliException(
                        $"Invalid value for --due: '{dueRaw}'. Expected a date like yyyy-MM-dd or ISO 8601.",
                        "InvalidArgument", exitCode: 1);
                }
                dueDate = parsedDue;
            }

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            var request = new TodoTaskUpdateRequest(
                Title: parseResult.GetValue(titleOption),
                Body: parseResult.GetValue(bodyOption),
                DueDate: dueDate,
                Importance: parseResult.GetValue(importanceOption)
            );

            TodoTaskItem updated = await service.UpdateTaskAsync(listId, taskId, request, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "updated", task = updated }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Updated: {updated.Title}");
            }
        });

        return command;
    }

    // ── todo done ──

    private static Command BuildDone(GlobalOptions global)
    {
        var listIdArgument = new Argument<string>("listId") { Description = "Task list ID" };
        var taskIdArgument = new Argument<string>("taskId") { Description = "Task ID to mark complete" };

        var command = new Command("done", "Mark a task as completed");
        command.Arguments.Add(listIdArgument);
        command.Arguments.Add(taskIdArgument);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("todo done", parseResult.GetValue(global.ReadOnly));

            string listId = parseResult.GetValue(listIdArgument)!;
            string taskId = parseResult.GetValue(taskIdArgument)!;

            if (parseResult.GetValue(global.DryRun))
            {
                IOutputFormatter dryFormatter = OutputFormatResolver.Resolve(parseResult.GetValue(global.Json), parseResult.GetValue(global.Plain));
                if (parseResult.GetValue(global.Json))
                {
                    dryFormatter.WriteResult(new { dryRun = true, action = "done", details = new { listId, taskId } }, Console.Out);
                }
                else
                {
                    Console.Error.WriteLine($"[DRY RUN] Would mark task completed: {taskId}");
                }
                return;
            }

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            TodoTaskItem updated = await service.SetTaskStatusAsync(listId, taskId, completed: true, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "completed", task = updated }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Completed: {updated.Title}");
            }
        });

        return command;
    }

    // ── todo undo ──

    private static Command BuildUndo(GlobalOptions global)
    {
        var listIdArgument = new Argument<string>("listId") { Description = "Task list ID" };
        var taskIdArgument = new Argument<string>("taskId") { Description = "Task ID to reopen" };

        var command = new Command("undo", "Mark a completed task as not started");
        command.Arguments.Add(listIdArgument);
        command.Arguments.Add(taskIdArgument);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("todo undo", parseResult.GetValue(global.ReadOnly));

            string listId = parseResult.GetValue(listIdArgument)!;
            string taskId = parseResult.GetValue(taskIdArgument)!;

            if (parseResult.GetValue(global.DryRun))
            {
                IOutputFormatter dryFormatter = OutputFormatResolver.Resolve(parseResult.GetValue(global.Json), parseResult.GetValue(global.Plain));
                if (parseResult.GetValue(global.Json))
                {
                    dryFormatter.WriteResult(new { dryRun = true, action = "undo", details = new { listId, taskId } }, Console.Out);
                }
                else
                {
                    Console.Error.WriteLine($"[DRY RUN] Would reopen task: {taskId}");
                }
                return;
            }

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            TodoTaskItem updated = await service.SetTaskStatusAsync(listId, taskId, completed: false, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "reopened", task = updated }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Reopened: {updated.Title}");
            }
        });

        return command;
    }

    // ── todo delete ──

    private static Command BuildDelete(GlobalOptions global)
    {
        var listIdArgument = new Argument<string>("listId") { Description = "Task list ID" };
        var taskIdArgument = new Argument<string>("taskId") { Description = "Task ID to delete" };

        var command = new Command("delete", "Delete a task");
        command.Arguments.Add(listIdArgument);
        command.Arguments.Add(taskIdArgument);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("todo delete", parseResult.GetValue(global.ReadOnly));

            string listId = parseResult.GetValue(listIdArgument)!;
            string taskId = parseResult.GetValue(taskIdArgument)!;

            if (parseResult.GetValue(global.DryRun))
            {
                IOutputFormatter dryFormatter = OutputFormatResolver.Resolve(parseResult.GetValue(global.Json), parseResult.GetValue(global.Plain));
                if (parseResult.GetValue(global.Json))
                {
                    dryFormatter.WriteResult(new { dryRun = true, action = "delete", details = new { listId, taskId } }, Console.Out);
                }
                else
                {
                    Console.Error.WriteLine($"[DRY RUN] Would delete task: {taskId}");
                }
                return;
            }

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            await service.DeleteTaskAsync(listId, taskId, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "deleted", listId, taskId }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine("Task deleted.");
            }
        });

        return command;
    }
}
