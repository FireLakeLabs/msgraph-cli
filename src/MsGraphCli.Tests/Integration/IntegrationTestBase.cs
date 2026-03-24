using Microsoft.Graph;
using MsGraphCli.Core.Auth;
using MsGraphCli.Core.Config;
using MsGraphCli.Core.Services;
using GraphClientFactory = MsGraphCli.Core.Graph.GraphClientFactory;
using Xunit;

namespace MsGraphCli.Tests.Integration;

/// <summary>
/// Base class for integration tests that hit the live Microsoft Graph API.
/// All tests are skipped unless MSGRAPH_LIVE=1 is set and 1Password is authenticated.
/// </summary>
[Trait("Category", "Integration")]
public abstract class IntegrationTestBase
{
    protected static bool IsLiveTestEnabled =>
        Environment.GetEnvironmentVariable("MSGRAPH_LIVE") == "1";

    private static GraphServiceClient? _sharedClient;
    private static readonly Lock ClientLock = new();

    /// <summary>
    /// Gets a shared authenticated GraphServiceClient for integration tests.
    /// Reuses the same client across all tests to avoid repeated auth.
    /// </summary>
    protected static GraphServiceClient GetClient(string[] serviceNames, bool readOnly = false)
    {
        lock (ClientLock)
        {
            if (_sharedClient is not null)
            {
                return _sharedClient;
            }

            AppConfig config = ConfigLoader.Load();
            var configStore = new OnePasswordSecretStore(config.OnePasswordVault);
            var tokenCacheStore = new OnePasswordSecretStore(config.TokenCacheVault);
            var authProvider = new GraphAuthProvider(configStore, tokenCacheStore);

            string[] scopes = ScopeRegistry.GetScopes(serviceNames, readOnly);
            HttpClient httpClient = GraphClientFactory.CreateDefaultHttpClient();
            var factory = new GraphClientFactory(authProvider, scopes, httpClient);
            _sharedClient = factory.CreateClient();

            return _sharedClient;
        }
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
