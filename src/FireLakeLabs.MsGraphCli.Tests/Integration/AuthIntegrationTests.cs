using FireLakeLabs.MsGraphCli.Core.Auth;
using FireLakeLabs.MsGraphCli.Core.Config;
using Xunit;

namespace FireLakeLabs.MsGraphCli.Tests.Integration;

/// <summary>
/// Integration tests for authentication.
/// Requires authenticated 1Password session and MSGRAPH_LIVE=1.
/// </summary>
[Trait("Category", "Integration")]
public class AuthIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task AcquireTokenSilent_Succeeds()
    {
        if (!IsLiveTestEnabled) return;

        AppConfig config = ConfigLoader.Load();
        var configStore = new OnePasswordSecretStore(config.OnePasswordVault);
        var tokenCacheStore = new OnePasswordSecretStore(config.TokenCacheVault);
        var authProvider = new GraphAuthProvider(configStore, tokenCacheStore);

        string[] scopes = ScopeRegistry.GetScopes(["mail"], readOnly: true);
        var result = await authProvider.AcquireTokenSilentAsync(scopes, CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEmpty(result.AccessToken);
        Assert.NotEmpty(result.Account.Username);
    }

    [Fact]
    public void ScopeRegistry_ContainsAllServices()
    {
        // This test doesn't need live API but validates the registry
        string[] allServices = ["mail", "calendar", "drive", "todo", "excel", "docs"];
        foreach (string service in allServices)
        {
            string[] scopes = ScopeRegistry.GetScopes([service], readOnly: true);
            Assert.NotEmpty(scopes);
            Assert.Contains("User.Read", scopes);
            Assert.Contains("offline_access", scopes);
        }
    }

    [Fact]
    public async Task OnePasswordStore_IsAvailable()
    {
        if (!IsLiveTestEnabled) return;

        AppConfig config = ConfigLoader.Load();
        var store = new OnePasswordSecretStore(config.OnePasswordVault);
        bool available = await store.IsAvailableAsync(CancellationToken.None);

        Assert.True(available, "1Password CLI should be available and authenticated");
    }
}
