using System.Globalization;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using MsGraphCli.Core.Models;

namespace MsGraphCli.Core.Services;

public interface ITasksService
{
    Task<IReadOnlyList<TaskListInfo>> ListTaskListsAsync(int? max, CancellationToken cancellationToken);

    Task<TaskListInfo> CreateTaskListAsync(string displayName, CancellationToken cancellationToken);

    Task<IReadOnlyList<TodoTaskItem>> ListTasksAsync(
        string listId, string? status, int? max, CancellationToken cancellationToken);

    Task<TodoTaskItem> GetTaskAsync(string listId, string taskId, CancellationToken cancellationToken);

    Task<TodoTaskItem> CreateTaskAsync(
        string listId, TodoTaskCreateRequest request, CancellationToken cancellationToken);

    Task<TodoTaskItem> UpdateTaskAsync(
        string listId, string taskId, TodoTaskUpdateRequest request, CancellationToken cancellationToken);

    Task<TodoTaskItem> SetTaskStatusAsync(
        string listId, string taskId, bool completed, CancellationToken cancellationToken);

    Task DeleteTaskAsync(string listId, string taskId, CancellationToken cancellationToken);
}

public sealed class TasksService : ITasksService
{
    private readonly GraphServiceClient _client;

    public TasksService(GraphServiceClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<TaskListInfo>> ListTaskListsAsync(int? max, CancellationToken cancellationToken)
    {
        int top = max ?? 100;

        var response = await _client.Me.Todo.Lists
            .GetAsync(config =>
            {
                config.QueryParameters.Top = top;
            }, cancellationToken);

        if (response?.Value is null)
        {
            return [];
        }

        return response.Value.Select(MapTaskList).ToList();
    }

    public async Task<TaskListInfo> CreateTaskListAsync(string displayName, CancellationToken cancellationToken)
    {
        var newList = new TodoTaskList
        {
            DisplayName = displayName,
        };

        TodoTaskList? created = await _client.Me.Todo.Lists
            .PostAsync(newList, cancellationToken: cancellationToken);

        if (created is null)
        {
            throw new Exceptions.MsGraphCliException(
                "Failed to create task list.", "CreateFailed", exitCode: 1);
        }

        return MapTaskList(created);
    }

    public async Task<IReadOnlyList<TodoTaskItem>> ListTasksAsync(
        string listId, string? status, int? max, CancellationToken cancellationToken)
    {
        int top = max ?? 50;

        var response = await _client.Me.Todo.Lists[listId].Tasks
            .GetAsync(config =>
            {
                config.QueryParameters.Top = top;

                if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    config.QueryParameters.Filter = "status eq 'completed'";
                }
                else if (string.Equals(status, "incomplete", StringComparison.OrdinalIgnoreCase))
                {
                    config.QueryParameters.Filter = "status ne 'completed'";
                }
            }, cancellationToken);

        if (response?.Value is null)
        {
            return [];
        }

        return response.Value.Select(MapTask).ToList();
    }

    public async Task<TodoTaskItem> GetTaskAsync(string listId, string taskId, CancellationToken cancellationToken)
    {
        TodoTask? task = await _client.Me.Todo.Lists[listId].Tasks[taskId]
            .GetAsync(cancellationToken: cancellationToken);

        if (task is null)
        {
            throw new Exceptions.ResourceNotFoundException($"Task '{taskId}' not found.");
        }

        return MapTask(task);
    }

    public async Task<TodoTaskItem> CreateTaskAsync(
        string listId, TodoTaskCreateRequest request, CancellationToken cancellationToken)
    {
        var newTask = new TodoTask
        {
            Title = request.Title,
        };

        if (request.Body is not null)
        {
            newTask.Body = new ItemBody
            {
                Content = request.Body,
                ContentType = BodyType.Text,
            };
        }

        if (request.DueDate.HasValue)
        {
            newTask.DueDateTime = new DateTimeTimeZone
            {
                DateTime = request.DueDate.Value.UtcDateTime.ToString("o"),
                TimeZone = "UTC",
            };
        }

        newTask.Importance = ParseImportance(request.Importance);

        TodoTask? created = await _client.Me.Todo.Lists[listId].Tasks
            .PostAsync(newTask, cancellationToken: cancellationToken);

        if (created is null)
        {
            throw new Exceptions.MsGraphCliException(
                "Failed to create task.", "CreateFailed", exitCode: 1);
        }

        return MapTask(created);
    }

    public async Task<TodoTaskItem> UpdateTaskAsync(
        string listId, string taskId, TodoTaskUpdateRequest request, CancellationToken cancellationToken)
    {
        var update = new TodoTask();

        if (request.Title is not null) update.Title = request.Title;

        if (request.Body is not null)
        {
            update.Body = new ItemBody
            {
                Content = request.Body,
                ContentType = BodyType.Text,
            };
        }

        if (request.DueDate.HasValue)
        {
            update.DueDateTime = new DateTimeTimeZone
            {
                DateTime = request.DueDate.Value.UtcDateTime.ToString("o"),
                TimeZone = "UTC",
            };
        }

        if (request.Importance is not null)
        {
            update.Importance = ParseImportance(request.Importance);
        }

        TodoTask? updated = await _client.Me.Todo.Lists[listId].Tasks[taskId]
            .PatchAsync(update, cancellationToken: cancellationToken);

        if (updated is null)
        {
            throw new Exceptions.MsGraphCliException(
                $"Failed to update task '{taskId}'.", "UpdateFailed", exitCode: 1);
        }

        return MapTask(updated);
    }

    public async Task<TodoTaskItem> SetTaskStatusAsync(
        string listId, string taskId, bool completed, CancellationToken cancellationToken)
    {
        var update = new TodoTask
        {
            Status = completed
                ? Microsoft.Graph.Models.TaskStatus.Completed
                : Microsoft.Graph.Models.TaskStatus.NotStarted,
        };

        TodoTask? updated = await _client.Me.Todo.Lists[listId].Tasks[taskId]
            .PatchAsync(update, cancellationToken: cancellationToken);

        if (updated is null)
        {
            throw new Exceptions.MsGraphCliException(
                $"Failed to update task '{taskId}'.", "UpdateFailed", exitCode: 1);
        }

        return MapTask(updated);
    }

    public async Task DeleteTaskAsync(string listId, string taskId, CancellationToken cancellationToken)
    {
        await _client.Me.Todo.Lists[listId].Tasks[taskId]
            .DeleteAsync(cancellationToken: cancellationToken);
    }

    // ── Mapping helpers ──

    private static TaskListInfo MapTaskList(TodoTaskList list) => new(
        Id: list.Id ?? "",
        DisplayName: list.DisplayName ?? "",
        IsDefaultList: list.WellknownListName == WellknownListName.DefaultList
    );

    private static TodoTaskItem MapTask(TodoTask task) => new(
        Id: task.Id ?? "",
        Title: task.Title ?? "",
        Body: task.Body?.Content,
        Status: NormalizeStatus(task.Status),
        DueDate: ParseGraphDateTime(task.DueDateTime),
        CompletedDate: ParseGraphDateTime(task.CompletedDateTime),
        Created: task.CreatedDateTime,
        LastModified: task.LastModifiedDateTime,
        Importance: task.Importance?.ToString()?.ToLowerInvariant() ?? "normal"
    );

    private static DateTimeOffset? ParseGraphDateTime(DateTimeTimeZone? dtz)
    {
        if (dtz?.DateTime is null)
        {
            return null;
        }

        if (DateTimeOffset.TryParse(dtz.DateTime, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTimeOffset result))
        {
            return result;
        }

        return null;
    }

    private static string NormalizeStatus(Microsoft.Graph.Models.TaskStatus? status)
    {
        return status switch
        {
            Microsoft.Graph.Models.TaskStatus.Completed => "completed",
            Microsoft.Graph.Models.TaskStatus.InProgress => "inProgress",
            Microsoft.Graph.Models.TaskStatus.NotStarted => "notStarted",
            Microsoft.Graph.Models.TaskStatus.WaitingOnOthers => "waitingOnOthers",
            Microsoft.Graph.Models.TaskStatus.Deferred => "deferred",
            _ => "notStarted",
        };
    }

    private static Importance ParseImportance(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "low" => Importance.Low,
            "high" => Importance.High,
            _ => Importance.Normal,
        };
    }
}
