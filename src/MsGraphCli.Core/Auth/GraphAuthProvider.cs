using Microsoft.Identity.Client;

namespace MsGraphCli.Core.Auth;

public record AuthStatus(
    string? UserEmail,
    string? TenantId,
    DateTimeOffset? TokenExpiry,
    IReadOnlyList<string> GrantedScopes,
    bool IsAuthenticated
);

/// <summary>
/// Manages authentication via MSAL, backed by 1Password for token persistence.
/// </summary>
public sealed class GraphAuthProvider
{
    private const string AppRegistrationItem = "app-registration";

    private readonly ISecretStore _store;
    private readonly TokenCacheHelper _cacheHelper;
    private IPublicClientApplication? _pca;

    public GraphAuthProvider(ISecretStore store)
    {
        _store = store;
        _cacheHelper = new TokenCacheHelper(store);
    }

    /// <summary>
    /// Interactive login flow. Opens browser or uses device code.
    /// </summary>
    public async Task<AuthenticationResult> LoginInteractiveAsync(
        string[] scopes, bool useDeviceCode = false, CancellationToken cancellationToken = default)
    {
        IPublicClientApplication pca = await GetOrCreatePcaAsync(cancellationToken);

        if (useDeviceCode)
        {
            return await pca.AcquireTokenWithDeviceCode(scopes, deviceCodeResult =>
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(deviceCodeResult.Message);
                Console.Error.WriteLine();
                return Task.CompletedTask;
            }).ExecuteAsync(cancellationToken);
        }

        return await pca.AcquireTokenInteractive(scopes)
            .WithUseEmbeddedWebView(false)
            .ExecuteAsync(cancellationToken);
    }

    /// <summary>
    /// Silently acquire a token using cached credentials.
    /// Throws MsalUiRequiredException if interactive login is needed.
    /// </summary>
    public async Task<AuthenticationResult> AcquireTokenSilentAsync(
        string[] scopes, CancellationToken cancellationToken = default)
    {
        IPublicClientApplication pca = await GetOrCreatePcaAsync(cancellationToken);

        var accounts = await pca.GetAccountsAsync();
        IAccount? account = accounts.FirstOrDefault()
            ?? throw new MsalUiRequiredException("no_account", "No cached account found. Interactive login required.");

        return await pca.AcquireTokenSilent(scopes, account).ExecuteAsync(cancellationToken);
    }

    /// <summary>
    /// Remove all cached tokens.
    /// </summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _cacheHelper.ClearAsync(cancellationToken);
    }

    /// <summary>
    /// Get the current authentication status.
    /// </summary>
    public async Task<AuthStatus> GetStatusAsync(
        string[] scopes, CancellationToken cancellationToken = default)
    {
        try
        {
            IPublicClientApplication pca = await GetOrCreatePcaAsync(cancellationToken);
            var accounts = await pca.GetAccountsAsync();
            IAccount? account = accounts.FirstOrDefault();

            if (account is null)
            {
                return new AuthStatus(null, null, null, [], false);
            }

            try
            {
                AuthenticationResult result = await pca.AcquireTokenSilent(scopes, account)
                    .ExecuteAsync(cancellationToken);

                return new AuthStatus(
                    UserEmail: account.Username,
                    TenantId: account.HomeAccountId?.TenantId,
                    TokenExpiry: result.ExpiresOn,
                    GrantedScopes: result.Scopes.ToList(),
                    IsAuthenticated: true
                );
            }
            catch (MsalUiRequiredException)
            {
                return new AuthStatus(
                    UserEmail: account.Username,
                    TenantId: account.HomeAccountId?.TenantId,
                    TokenExpiry: null,
                    GrantedScopes: [],
                    IsAuthenticated: false
                );
            }
        }
        catch
        {
            return new AuthStatus(null, null, null, [], false);
        }
    }

    /// <summary>
    /// Get the underlying MSAL application, building it if needed.
    /// Exposed for constructing Graph client credentials.
    /// </summary>
    public async Task<IPublicClientApplication> GetOrCreatePcaAsync(
        CancellationToken cancellationToken = default)
    {
        if (_pca is not null)
        {
            return _pca;
        }

        string? clientId = await _store.ReadFieldAsync(AppRegistrationItem, "client-id", cancellationToken);
        string? tenantId = await _store.ReadFieldAsync(AppRegistrationItem, "tenant-id", cancellationToken);

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(tenantId))
        {
            throw new InvalidOperationException(
                "App registration not configured. Run: msgraph auth setup");
        }

        _pca = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
            .WithRedirectUri("http://localhost")
            .Build();

        _cacheHelper.Register(_pca.UserTokenCache);
        return _pca;
    }
}
