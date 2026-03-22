using Microsoft.Identity.Client;

namespace MsGraphCli.Core.Auth;

/// <summary>
/// Bridges MSAL.NET's token cache to a secret store.
/// The cache is serialized as a Base64-encoded MSAL V3 blob.
/// </summary>
public sealed class TokenCacheHelper
{
    private const string CacheItemName = "msal-token-cache";

    private readonly ISecretStore _store;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public TokenCacheHelper(ISecretStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Register this helper with an MSAL token cache.
    /// </summary>
    public void Register(ITokenCache tokenCache)
    {
        tokenCache.SetBeforeAccessAsync(BeforeAccessAsync);
        tokenCache.SetAfterAccessAsync(AfterAccessAsync);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _store.DeleteItemAsync(CacheItemName, cancellationToken);
    }

    private async Task BeforeAccessAsync(TokenCacheNotificationArgs args)
    {
        await _lock.WaitAsync();
        try
        {
            string? base64 = await _store.ReadNoteAsync(CacheItemName);

            if (base64 is not null)
            {
                try
                {
                    byte[] cacheBytes = Convert.FromBase64String(base64);
                    args.TokenCache.DeserializeMsalV3(cacheBytes);
                }
                catch (FormatException)
                {
                    // Corrupted cache — start fresh
                    args.TokenCache.DeserializeMsalV3(null);
                }
            }
            else
            {
                args.TokenCache.DeserializeMsalV3(null);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task AfterAccessAsync(TokenCacheNotificationArgs args)
    {
        if (!args.HasStateChanged)
        {
            return;
        }

        await _lock.WaitAsync();
        try
        {
            byte[] cacheBytes = args.TokenCache.SerializeMsalV3();
            string base64 = Convert.ToBase64String(cacheBytes);
            await _store.WriteNoteAsync(CacheItemName, base64);
        }
        finally
        {
            _lock.Release();
        }
    }
}
