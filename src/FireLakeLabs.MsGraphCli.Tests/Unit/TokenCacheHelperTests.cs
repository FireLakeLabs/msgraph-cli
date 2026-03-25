using FluentAssertions;
using FireLakeLabs.MsGraphCli.Core.Auth;
using Xunit;

namespace FireLakeLabs.MsGraphCli.Tests.Unit;

/// <summary>
/// In-memory implementation of ISecretStore for testing.
/// </summary>
internal sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, Dictionary<string, string>> _fields = new();
    private readonly Dictionary<string, string> _notes = new();

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task EnsureVaultExistsAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<string?> ReadFieldAsync(string itemName, string fieldName, CancellationToken cancellationToken = default)
    {
        if (_fields.TryGetValue(itemName, out var fields) && fields.TryGetValue(fieldName, out string? value))
        {
            return Task.FromResult<string?>(value);
        }

        return Task.FromResult<string?>(null);
    }

    public Task WriteFieldsAsync(string itemName, Dictionary<string, string> fields, CancellationToken cancellationToken = default)
    {
        if (!_fields.TryGetValue(itemName, out Dictionary<string, string>? itemFields))
        {
            itemFields = new Dictionary<string, string>();
            _fields[itemName] = itemFields;
        }

        foreach (var (key, value) in fields)
        {
            itemFields[key] = value;
        }

        return Task.CompletedTask;
    }

    public Task<string?> ReadNoteAsync(string itemName, CancellationToken cancellationToken = default)
    {
        _notes.TryGetValue(itemName, out string? value);
        return Task.FromResult(value);
    }

    public Task WriteNoteAsync(string itemName, string content, CancellationToken cancellationToken = default)
    {
        _notes[itemName] = content;
        return Task.CompletedTask;
    }

    public Task<bool> ItemExistsAsync(string itemName, CancellationToken cancellationToken = default)
    {
        bool exists = _fields.ContainsKey(itemName) || _notes.ContainsKey(itemName);
        return Task.FromResult(exists);
    }

    public Task DeleteItemAsync(string itemName, CancellationToken cancellationToken = default)
    {
        _fields.Remove(itemName);
        _notes.Remove(itemName);
        return Task.CompletedTask;
    }

    // Test helpers
    public bool HasNote(string itemName) => _notes.ContainsKey(itemName);
    public string? GetNote(string itemName) => _notes.GetValueOrDefault(itemName);
}

public class TokenCacheHelperTests
{
    [Fact]
    public async Task ClearAsync_RemovesCacheItem()
    {
        var store = new InMemorySecretStore();
        await store.WriteNoteAsync("msal-token-cache", "some-data");

        var helper = new TokenCacheHelper(store);
        await helper.ClearAsync();

        store.HasNote("msal-token-cache").Should().BeFalse();
    }
}

public class InMemorySecretStoreTests
{
    [Fact]
    public async Task WriteAndReadFields_RoundTrip()
    {
        var store = new InMemorySecretStore();

        await store.WriteFieldsAsync("test-item", new Dictionary<string, string>
        {
            ["field1"] = "value1",
            ["field2"] = "value2",
        });

        string? result1 = await store.ReadFieldAsync("test-item", "field1");
        string? result2 = await store.ReadFieldAsync("test-item", "field2");
        string? result3 = await store.ReadFieldAsync("test-item", "nonexistent");

        result1.Should().Be("value1");
        result2.Should().Be("value2");
        result3.Should().BeNull();
    }

    [Fact]
    public async Task WriteAndReadNote_RoundTrip()
    {
        var store = new InMemorySecretStore();

        await store.WriteNoteAsync("test-note", "hello world");

        string? result = await store.ReadNoteAsync("test-note");
        result.Should().Be("hello world");
    }

    [Fact]
    public async Task ReadField_NonexistentItem_ReturnsNull()
    {
        var store = new InMemorySecretStore();

        string? result = await store.ReadFieldAsync("nonexistent", "field");
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteItem_RemovesBothFieldsAndNotes()
    {
        var store = new InMemorySecretStore();

        await store.WriteFieldsAsync("item", new Dictionary<string, string> { ["f"] = "v" });
        await store.WriteNoteAsync("item", "note");

        await store.DeleteItemAsync("item");

        (await store.ItemExistsAsync("item")).Should().BeFalse();
        (await store.ReadFieldAsync("item", "f")).Should().BeNull();
        (await store.ReadNoteAsync("item")).Should().BeNull();
    }
}
