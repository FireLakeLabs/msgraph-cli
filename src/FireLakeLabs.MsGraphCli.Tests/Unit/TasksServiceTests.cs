using System.Net;
using FluentAssertions;
using FireLakeLabs.MsGraphCli.Core.Exceptions;
using FireLakeLabs.MsGraphCli.Core.Models;
using FireLakeLabs.MsGraphCli.Core.Services;
using FireLakeLabs.MsGraphCli.Tests.Unit.Helpers;
using Xunit;

namespace FireLakeLabs.MsGraphCli.Tests.Unit;

public class TasksServiceTests
{
    // ── ListTaskListsAsync ──

    [Fact]
    public async Task ListTaskLists_ReturnsTaskListInfos()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        {
            "value": [
                { "id": "list-1", "displayName": "Tasks", "wellknownListName": "defaultList" },
                { "id": "list-2", "displayName": "Work", "wellknownListName": "none" }
            ]
        }
        """);
        TasksService svc = CreateService(handler);

        IReadOnlyList<TaskListInfo> result = await svc.ListTaskListsAsync(null, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("list-1");
        result[0].DisplayName.Should().Be("Tasks");
        result[0].IsDefaultList.Should().BeTrue();
        result[1].Id.Should().Be("list-2");
        result[1].DisplayName.Should().Be("Work");
        result[1].IsDefaultList.Should().BeFalse();
    }

    [Fact]
    public async Task ListTaskLists_NullResponse_ReturnsEmptyList()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("{}");
        TasksService svc = CreateService(handler);

        IReadOnlyList<TaskListInfo> result = await svc.ListTaskListsAsync(null, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListTaskLists_DefaultMax_Uses100()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        TasksService svc = CreateService(handler);

        await svc.ListTaskListsAsync(null, CancellationToken.None);

        handler.LastRequest.Uri.Query.Should().Contain("%24top=100");
    }

    // ── CreateTaskListAsync ──

    [Fact]
    public async Task CreateTaskList_PostsToListsEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        { "id": "new-list", "displayName": "Shopping", "wellknownListName": "none" }
        """);
        TasksService svc = CreateService(handler);

        TaskListInfo result = await svc.CreateTaskListAsync("Shopping", CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.Uri.AbsolutePath.Should().Contain("/me/todo/lists");
        handler.LastRequest.Body.Should().Contain("Shopping");
        result.Id.Should().Be("new-list");
        result.DisplayName.Should().Be("Shopping");
    }

    [Fact]
    public async Task CreateTaskList_NullResponse_ThrowsMsGraphCliException()
    {
        var handler = new MockGraphHandler();
        handler.EnqueueEmpty();
        TasksService svc = CreateService(handler);

        Func<Task> act = () => svc.CreateTaskListAsync("Test", CancellationToken.None);

        MsGraphCliException ex = (await act.Should().ThrowAsync<MsGraphCliException>()).Which;
        ex.ErrorCode.Should().Be("CreateFailed");
    }

    // ── ListTasksAsync ──

    [Fact]
    public async Task ListTasks_NoStatusFilter_GetsAllTasks()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        TasksService svc = CreateService(handler);

        await svc.ListTasksAsync("list-1", null, null, CancellationToken.None);

        string query = Uri.UnescapeDataString(handler.LastRequest.Uri.Query);
        query.Should().NotContain("$filter");
    }

    [Fact]
    public async Task ListTasks_StatusCompleted_FiltersCompleted()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        TasksService svc = CreateService(handler);

        await svc.ListTasksAsync("list-1", "completed", null, CancellationToken.None);

        string query = Uri.UnescapeDataString(handler.LastRequest.Uri.Query);
        query.Should().Contain("status eq 'completed'");
    }

    [Fact]
    public async Task ListTasks_StatusIncomplete_FiltersIncomplete()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        TasksService svc = CreateService(handler);

        await svc.ListTasksAsync("list-1", "incomplete", null, CancellationToken.None);

        string query = Uri.UnescapeDataString(handler.LastRequest.Uri.Query);
        query.Should().Contain("status ne 'completed'");
    }

    [Fact]
    public async Task ListTasks_DefaultMax_Uses50()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""{"value": []}""");
        TasksService svc = CreateService(handler);

        await svc.ListTasksAsync("list-1", null, null, CancellationToken.None);

        handler.LastRequest.Uri.Query.Should().Contain("%24top=50");
    }

    [Fact]
    public async Task ListTasks_MapsFieldsCorrectly()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        {
            "value": [{
                "id": "task-1",
                "title": "Buy groceries",
                "body": { "content": "Milk, eggs, bread", "contentType": "text" },
                "status": "notStarted",
                "dueDateTime": { "dateTime": "2025-06-15T00:00:00.0000000", "timeZone": "UTC" },
                "completedDateTime": null,
                "createdDateTime": "2025-01-10T08:00:00Z",
                "lastModifiedDateTime": "2025-01-10T09:00:00Z",
                "importance": "high"
            }]
        }
        """);
        TasksService svc = CreateService(handler);

        IReadOnlyList<TodoTaskItem> result =
            await svc.ListTasksAsync("list-1", null, null, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("task-1");
        result[0].Title.Should().Be("Buy groceries");
        result[0].Body.Should().Be("Milk, eggs, bread");
        result[0].Status.Should().Be("notStarted");
        result[0].DueDate.Should().NotBeNull();
        result[0].Importance.Should().Be("high");
        result[0].Created.Should().NotBeNull();
        result[0].LastModified.Should().NotBeNull();
    }

    // ── GetTaskAsync ──

    [Fact]
    public async Task GetTask_ReturnsTaskDetail()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        {
            "id": "task-1",
            "title": "Review PR",
            "body": { "content": "Check the tests", "contentType": "text" },
            "status": "inProgress",
            "importance": "normal",
            "createdDateTime": "2025-01-10T08:00:00Z",
            "lastModifiedDateTime": "2025-01-10T09:00:00Z"
        }
        """);
        TasksService svc = CreateService(handler);

        TodoTaskItem result = await svc.GetTaskAsync("list-1", "task-1", CancellationToken.None);

        result.Id.Should().Be("task-1");
        result.Title.Should().Be("Review PR");
        result.Body.Should().Be("Check the tests");
    }

    [Fact]
    public async Task GetTask_NullResponse_ThrowsResourceNotFoundException()
    {
        var handler = new MockGraphHandler();
        handler.EnqueueEmpty();
        TasksService svc = CreateService(handler);

        Func<Task> act = () => svc.GetTaskAsync("list-1", "task-1", CancellationToken.None);

        await act.Should().ThrowAsync<ResourceNotFoundException>();
    }

    // ── CreateTaskAsync ──

    [Fact]
    public async Task CreateTask_MinimalRequest_PostsTitle()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        { "id": "new-task", "title": "Simple task", "status": "notStarted", "importance": "normal" }
        """);
        TasksService svc = CreateService(handler);

        var request = new TodoTaskCreateRequest(Title: "Simple task");

        TodoTaskItem result = await svc.CreateTaskAsync("list-1", request, CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.Body.Should().Contain("Simple task");
        result.Id.Should().Be("new-task");
        result.Title.Should().Be("Simple task");
    }

    [Fact]
    public async Task CreateTask_FullRequest_IncludesAllFields()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        {
            "id": "new-task",
            "title": "Full task",
            "body": { "content": "Detailed body", "contentType": "text" },
            "status": "notStarted",
            "dueDateTime": { "dateTime": "2025-06-15T00:00:00.0000000Z", "timeZone": "UTC" },
            "importance": "high"
        }
        """);
        TasksService svc = CreateService(handler);

        var request = new TodoTaskCreateRequest(
            Title: "Full task",
            Body: "Detailed body",
            DueDate: new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero),
            Importance: "high");

        TodoTaskItem result = await svc.CreateTaskAsync("list-1", request, CancellationToken.None);

        handler.LastRequest.Body.Should().Contain("Full task");
        handler.LastRequest.Body.Should().Contain("Detailed body");
        handler.LastRequest.Body.Should().Contain("dueDateTime");
        result.Title.Should().Be("Full task");
        result.Body.Should().Be("Detailed body");
        result.Importance.Should().Be("high");
    }

    // ── UpdateTaskAsync ──

    [Fact]
    public async Task UpdateTask_PartialUpdate_PatchesOnlyProvidedFields()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        { "id": "task-1", "title": "Updated title", "status": "notStarted", "importance": "normal" }
        """);
        TasksService svc = CreateService(handler);

        var request = new TodoTaskUpdateRequest(Title: "Updated title");

        TodoTaskItem result = await svc.UpdateTaskAsync("list-1", "task-1", request, CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Patch);
        handler.LastRequest.Body.Should().Contain("Updated title");
        result.Title.Should().Be("Updated title");
    }

    // ── SetTaskStatusAsync ──

    [Fact]
    public async Task SetTaskStatus_Completed_SetsStatusCompleted()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        { "id": "task-1", "title": "Done task", "status": "completed", "importance": "normal" }
        """);
        TasksService svc = CreateService(handler);

        TodoTaskItem result = await svc.SetTaskStatusAsync("list-1", "task-1", true, CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Patch);
        handler.LastRequest.Body.Should().Contain("completed");
        result.Status.Should().Be("completed");
    }

    [Fact]
    public async Task SetTaskStatus_NotCompleted_SetsStatusNotStarted()
    {
        var handler = new MockGraphHandler();
        handler.Enqueue("""
        { "id": "task-1", "title": "Reopened task", "status": "notStarted", "importance": "normal" }
        """);
        TasksService svc = CreateService(handler);

        TodoTaskItem result = await svc.SetTaskStatusAsync("list-1", "task-1", false, CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Patch);
        handler.LastRequest.Body.Should().Contain("notStarted");
        result.Status.Should().Be("notStarted");
    }

    // ── DeleteTaskAsync ──

    [Fact]
    public async Task DeleteTask_CallsDeleteEndpoint()
    {
        var handler = new MockGraphHandler();
        handler.EnqueueEmpty();
        TasksService svc = CreateService(handler);

        await svc.DeleteTaskAsync("list-1", "task-1", CancellationToken.None);

        handler.LastRequest.Method.Should().Be(HttpMethod.Delete);
        handler.LastRequest.Uri.AbsolutePath.Should().Contain("/todo/lists/list-1/tasks/task-1");
    }

    // ── Helpers ──

    private static TasksService CreateService(MockGraphHandler handler)
    {
        return new TasksService(MockGraphHandler.CreateClient(handler));
    }
}
