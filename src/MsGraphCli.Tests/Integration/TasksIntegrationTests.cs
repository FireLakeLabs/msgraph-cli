using MsGraphCli.Core.Models;
using MsGraphCli.Core.Services;
using Xunit;

namespace MsGraphCli.Tests.Integration;

/// <summary>
/// Integration tests for To Do task operations.
/// Requires authenticated 1Password session and MSGRAPH_LIVE=1.
/// </summary>
[Trait("Category", "Integration")]
public class TasksIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task ListTaskLists_ReturnsAtLeastDefault()
    {
        if (!IsLiveTestEnabled) return;

        TasksService service = CreateTasksService(readOnly: true);
        IReadOnlyList<TaskListInfo> lists = await service.ListTaskListsAsync(null, CancellationToken.None);

        Assert.NotEmpty(lists);
    }

    [Fact]
    public async Task CreateTaskAndComplete_RoundTrip()
    {
        if (!IsLiveTestEnabled) return;

        TasksService service = CreateTasksService();

        // Get the default task list
        IReadOnlyList<TaskListInfo> lists = await service.ListTaskListsAsync(null, CancellationToken.None);
        TaskListInfo defaultList = lists.First(l => l.IsDefaultList);

        // Create a task
        var request = new TodoTaskCreateRequest(
            Title: $"Integration Test {Guid.NewGuid():N}",
            Body: "Created by integration test");

        TodoTaskItem created = await service.CreateTaskAsync(defaultList.Id, request, CancellationToken.None);
        Assert.NotNull(created.Id);
        Assert.Equal(request.Title, created.Title);

        try
        {
            // Read it back
            TodoTaskItem fetched = await service.GetTaskAsync(defaultList.Id, created.Id, CancellationToken.None);
            Assert.Equal(created.Id, fetched.Id);

            // Mark as done
            TodoTaskItem completed = await service.SetTaskStatusAsync(defaultList.Id, created.Id, true, CancellationToken.None);
            Assert.Equal("completed", completed.Status);

            // Undo
            TodoTaskItem undone = await service.SetTaskStatusAsync(defaultList.Id, created.Id, false, CancellationToken.None);
            Assert.NotEqual("completed", undone.Status);
        }
        finally
        {
            // Delete (cleanup)
            await service.DeleteTaskAsync(defaultList.Id, created.Id, CancellationToken.None);
        }
    }
}
