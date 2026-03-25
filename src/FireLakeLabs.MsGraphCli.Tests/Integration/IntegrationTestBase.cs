using System.Collections.Concurrent;
using Microsoft.Graph;
using FireLakeLabs.MsGraphCli.Core.Auth;
using FireLakeLabs.MsGraphCli.Core.Config;
using FireLakeLabs.MsGraphCli.Core.Services;
using GraphClientFactory = FireLakeLabs.MsGraphCli.Core.Graph.GraphClientFactory;
using Xunit;

namespace FireLakeLabs.MsGraphCli.Tests.Integration;

/// <summary>
/// Base class for integration tests that hit the live Microsoft Graph API.
/// Tests return early (silent pass) when MSGRAPH_LIVE=1 is not set.
/// </summary>
[Trait("Category", "Integration")]
public abstract class IntegrationTestBase
{
    protected static bool IsLiveTestEnabled =>
        Environment.GetEnvironmentVariable("MSGRAPH_LIVE") == "1";

    private static readonly ConcurrentDictionary<string, GraphServiceClient> ClientCache = new();

    /// <summary>
    /// Gets an authenticated GraphServiceClient for the given service/scope combination.
    /// Clients are cached by scope key to avoid repeated auth while ensuring
    /// different scope sets get distinct clients.
    /// </summary>
    protected static GraphServiceClient GetClient(string[] serviceNames, bool readOnly = false)
    {
        string[] scopes = ScopeRegistry.GetScopes(serviceNames, readOnly);
        string cacheKey = string.Join(",", scopes.Order());

        return ClientCache.GetOrAdd(cacheKey, _ =>
        {
            AppConfig config = ConfigLoader.Load();
            var configStore = new OnePasswordSecretStore(config.OnePasswordVault);
            var tokenCacheStore = new OnePasswordSecretStore(config.TokenCacheVault);
            var authProvider = new GraphAuthProvider(configStore, tokenCacheStore);

            HttpClient httpClient = GraphClientFactory.CreateDefaultHttpClient();
            var factory = new GraphClientFactory(authProvider, scopes, httpClient);
            return factory.CreateClient();
        });
    }

    protected static MailService CreateMailService(bool readOnly = false)
    {
        GraphServiceClient client = GetClient(["mail"], readOnly);
        return new MailService(client);
    }

    protected static CalendarService CreateCalendarService(bool readOnly = false)
    {
        GraphServiceClient client = GetClient(["calendar"], readOnly);
        return new CalendarService(client);
    }

    protected static DriveService CreateDriveService(bool readOnly = false)
    {
        GraphServiceClient client = GetClient(["drive"], readOnly);
        return new DriveService(client);
    }

    protected static TasksService CreateTasksService(bool readOnly = false)
    {
        GraphServiceClient client = GetClient(["todo"], readOnly);
        return new TasksService(client);
    }

    protected static ExcelService CreateExcelService(bool readOnly = false)
    {
        GraphServiceClient client = GetClient(["excel"], readOnly);
        return new ExcelService(client);
    }

    protected static DocumentService CreateDocumentService(bool readOnly = false)
    {
        GraphServiceClient client = GetClient(["docs"], readOnly);
        return new DocumentService(client);
    }
}
