using Microsoft.Identity.Client;

namespace MsalOnePasswordSpike;

/// <summary>
/// Bridges MSAL.NET's token cache to 1Password storage.
///
/// The MSAL token cache is serialized as a byte[] blob (MSAL V3 format).
/// We Base64-encode it and store it as a 1Password Secure Note.
/// On each token operation, MSAL calls our callbacks to load/save the cache.
/// </summary>
public sealed class OnePasswordTokenCacheHelper : IDisposable
{
    private const string CacheItemName = "msal-token-cache";

    private readonly OnePasswordStore _store;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public OnePasswordTokenCacheHelper(OnePasswordStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Register this helper with an MSAL token cache.
    /// Call this once after building the PublicClientApplication.
    /// </summary>
    public void Register(ITokenCache tokenCache)
    {
        tokenCache.SetBeforeAccessAsync(BeforeAccessAsync);
        tokenCache.SetAfterAccessAsync(AfterAccessAsync);
    }

    /// <summary>
    /// Called by MSAL before it accesses the cache.
    /// We load the serialized blob from 1Password and deserialize it.
    /// </summary>
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
                    Console.Error.WriteLine("[TokenCache] Loaded cache from 1Password.");
                }
                catch (FormatException)
                {
                    // Corrupted cache — start fresh
                    Console.Error.WriteLine("[TokenCache] WARNING: Cache in 1Password was corrupted. Starting fresh.");
                    args.TokenCache.DeserializeMsalV3(null);
                }
            }
            else
            {
                Console.Error.WriteLine("[TokenCache] No cached tokens found in 1Password (first run?).");
                args.TokenCache.DeserializeMsalV3(null);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Called by MSAL after it accesses the cache.
    /// If the cache changed (new token acquired, token refreshed), we persist it.
    /// </summary>
    private async Task AfterAccessAsync(TokenCacheNotificationArgs args)
    {
        if (!args.HasStateChanged)
            return;

        await _lock.WaitAsync();
        try
        {
            byte[] cacheBytes = args.TokenCache.SerializeMsalV3();
            string base64 = Convert.ToBase64String(cacheBytes);

            await _store.WriteNoteAsync(CacheItemName, base64);
            Console.Error.WriteLine("[TokenCache] Saved updated cache to 1Password.");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Delete the cached tokens from 1Password.
    /// </summary>
    public async Task ClearCacheAsync(CancellationToken ct = default)
    {
        await _store.DeleteItemAsync(CacheItemName, ct);
        Console.Error.WriteLine("[TokenCache] Cleared cache from 1Password.");
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
