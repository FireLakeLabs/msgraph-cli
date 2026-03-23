using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using MsGraphCli.Core.Auth;

namespace MsGraphCli.Core.Graph;

/// <summary>
/// Creates authenticated GraphServiceClient instances.
/// Accepts an HttpClient from the caller to support proper lifecycle management.
/// </summary>
public sealed class GraphClientFactory
{
    private readonly GraphAuthProvider _authProvider;
    private readonly string[] _scopes;
    private readonly HttpClient _httpClient;

    public GraphClientFactory(GraphAuthProvider authProvider, string[] scopes, HttpClient httpClient)
    {
        _authProvider = authProvider;
        _scopes = scopes;
        _httpClient = httpClient;
    }

    public GraphServiceClient CreateClient()
    {
        var credential = new MsalAccessTokenProvider(_authProvider, _scopes);
        var authProvider = new BaseBearerTokenAuthenticationProvider(credential);

        return new GraphServiceClient(_httpClient, authProvider);
    }

    /// <summary>
    /// Creates a default HttpClient with retry handling for use when no
    /// IHttpClientFactory or DI container is available.
    /// </summary>
    public static HttpClient CreateDefaultHttpClient() =>
        new(new RetryDelegatingHandler(new HttpClientHandler()));
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
