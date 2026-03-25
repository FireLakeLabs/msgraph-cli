namespace FireLakeLabs.MsGraphCli.Core.Auth;

/// <summary>
/// Abstraction for reading and writing secrets.
/// Primary implementation: 1Password CLI.
/// </summary>
public interface ISecretStore
{
    Task<string?> ReadFieldAsync(string itemName, string fieldName, CancellationToken cancellationToken = default);
    Task WriteFieldsAsync(string itemName, Dictionary<string, string> fields, CancellationToken cancellationToken = default);
    Task<string?> ReadNoteAsync(string itemName, CancellationToken cancellationToken = default);
    Task WriteNoteAsync(string itemName, string content, CancellationToken cancellationToken = default);
    Task<bool> ItemExistsAsync(string itemName, CancellationToken cancellationToken = default);
    Task DeleteItemAsync(string itemName, CancellationToken cancellationToken = default);
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    Task EnsureVaultExistsAsync(CancellationToken cancellationToken = default);
}
