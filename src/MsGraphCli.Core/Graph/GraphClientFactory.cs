using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using MsGraphCli.Core.Auth;

namespace MsGraphCli.Core.Graph;

/// <summary>
/// Creates authenticated GraphServiceClient instances.
/// </summary>
public sealed class GraphClientFactory
{
    private readonly GraphAuthProvider _authProvider;
    private readonly string[] _scopes;
    private readonly Lazy<HttpClient> _httpClient;

    public GraphClientFactory(GraphAuthProvider authProvider, string[] scopes)
    {
        _authProvider = authProvider;
        _scopes = scopes;
        _httpClient = new Lazy<HttpClient>(() =>
            new HttpClient(new RetryDelegatingHandler(new HttpClientHandler())));
    }

    public GraphServiceClient CreateClient()
    {
        var credential = new MsalAccessTokenProvider(_authProvider, _scopes);
        var authProvider = new BaseBearerTokenAuthenticationProvider(credential);

        return new GraphServiceClient(_httpClient.Value, authProvider);
    }
}

/// <summary>
/// Adapts <see cref="GraphAuthProvider"/> into <see cref="IAccessTokenProvider"/>
/// for the Microsoft Graph SDK (Kiota-based v5+).
/// </summary>
internal sealed class MsalAccessTokenProvider : IAccessTokenProvider
{
    private readonly GraphAuthProvider _authProvider;
    private readonly string[] _scopes;

    public MsalAccessTokenProvider(GraphAuthProvider authProvider, string[] scopes)
    {
        _authProvider = authProvider;
        _scopes = scopes;
        AllowedHostsValidator = new AllowedHostsValidator(["graph.microsoft.com"]);
    }

    public AllowedHostsValidator AllowedHostsValidator { get; }

    public async Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _authProvider.AcquireTokenSilentAsync(_scopes, cancellationToken);
        return result.AccessToken;
    }
}
