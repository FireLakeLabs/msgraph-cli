using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions.Authentication;
using System.Text.Json;

namespace MsalOnePasswordSpike;

/// <summary>
/// Spike: Prove that MSAL token cache serialization through 1Password works.
///
/// Success criteria:
///   1. First run: interactive login → token cached in 1Password → GET /me succeeds.
///   2. Second run: no browser → token loaded from 1Password → GET /me succeeds.
///
/// Usage:
///   dotnet run                        # Interactive login (loopback browser flow)
///   dotnet run -- --device-code       # Device code flow (for headless/SSH)
///   dotnet run -- --status            # Show auth status without calling Graph
///   dotnet run -- --logout            # Clear tokens from 1Password
///   dotnet run -- --setup             # Store client ID + tenant ID in 1Password
/// </summary>
public static class Program
{
    // Scopes we need for the spike: just read the user's profile.
    private static readonly string[] Scopes = ["User.Read", "offline_access"];

    private const string AppRegistrationItem = "app-registration";
    private const string VaultName = "msgraph-cli";

    public static async Task<int> Main(string[] args)
    {
        var ct = CancellationToken.None;
        var store = new OnePasswordStore(VaultName);

        try
        {
            // ── Preflight: verify op CLI is available ──
            if (!await store.IsAvailableAsync(ct))
            {
                Console.Error.WriteLine("ERROR: 1Password CLI (op) is not available or you are not signed in.");
                Console.Error.WriteLine("  Install: https://developer.1password.com/docs/cli/get-started/");
                Console.Error.WriteLine("  Sign in: eval $(op signin)");
                return 1;
            }

            string command = args.Length > 0 ? args[0].TrimStart('-').ToLowerInvariant() : "run";

            return command switch
            {
                "setup" => await RunSetupAsync(store, ct),
                "logout" or "clear" => await RunLogoutAsync(store, ct),
                "status" => await RunStatusAsync(store, ct),
                "device-code" => await RunAuthAndCallGraphAsync(store, useDeviceCode: true, ct),
                _ => await RunAuthAndCallGraphAsync(store, useDeviceCode: false, ct),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex.Message}");
            if (args.Contains("--verbose"))
                Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    // ════════════════════════════════════════════════════════════
    // Setup: store app registration details in 1Password
    // ════════════════════════════════════════════════════════════

    private static async Task<int> RunSetupAsync(OnePasswordStore store, CancellationToken ct)
    {
        Console.Error.WriteLine("=== msgraph-cli 1Password Setup ===");
        Console.Error.WriteLine();

        await store.EnsureVaultExistsAsync(ct);

        Console.Error.Write("Enter your Entra ID Client ID (Application ID): ");
        string? clientId = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(clientId))
        {
            Console.Error.WriteLine("ERROR: Client ID is required.");
            return 1;
        }

        Console.Error.Write("Enter your Entra ID Tenant ID: ");
        string? tenantId = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(tenantId))
        {
            Console.Error.WriteLine("ERROR: Tenant ID is required.");
            return 1;
        }

        await store.WriteFieldsAsync(AppRegistrationItem, new Dictionary<string, string>
        {
            ["client-id"] = clientId,
            ["tenant-id"] = tenantId,
        }, ct);

        Console.Error.WriteLine();
        Console.Error.WriteLine($"✓ Stored app registration in 1Password vault '{VaultName}'.");
        Console.Error.WriteLine("  You can now run: dotnet run");
        return 0;
    }

    // ════════════════════════════════════════════════════════════
    // Logout: clear cached tokens
    // ════════════════════════════════════════════════════════════

    private static async Task<int> RunLogoutAsync(OnePasswordStore store, CancellationToken ct)
    {
        var cacheHelper = new OnePasswordTokenCacheHelper(store);
        await cacheHelper.ClearCacheAsync(ct);
        Console.Error.WriteLine("✓ Logged out. Tokens cleared from 1Password.");
        return 0;
    }

    // ════════════════════════════════════════════════════════════
    // Status: show auth info without calling Graph
    // ════════════════════════════════════════════════════════════

    private static async Task<int> RunStatusAsync(OnePasswordStore store, CancellationToken ct)
    {
        var (clientId, tenantId) = await ReadAppRegistrationAsync(store, ct);
        Console.Error.WriteLine($"  Client ID:  {clientId}");
        Console.Error.WriteLine($"  Tenant ID:  {tenantId}");

        var pca = BuildPublicClientApp(clientId, tenantId);
        var cacheHelper = new OnePasswordTokenCacheHelper(store);
        cacheHelper.Register(pca.UserTokenCache);

        var accounts = await pca.GetAccountsAsync();
        var account = accounts.FirstOrDefault();

        if (account is null)
        {
            Console.Error.WriteLine("  Status:     Not authenticated (no cached accounts).");
            Console.Error.WriteLine("  Run 'dotnet run' to log in.");
            return 0;
        }

        Console.Error.WriteLine($"  Account:    {account.Username}");
        Console.Error.WriteLine($"  Home Tenant:{account.HomeAccountId?.TenantId}");

        // Try to silently get a token to check expiry
        try
        {
            var result = await pca.AcquireTokenSilent(Scopes, account).ExecuteAsync(ct);
            Console.Error.WriteLine($"  Token:      Valid (expires {result.ExpiresOn:u})");
            Console.Error.WriteLine($"  Scopes:     {string.Join(", ", result.Scopes)}");
        }
        catch (MsalUiRequiredException)
        {
            Console.Error.WriteLine("  Token:      Expired (interactive login required).");
        }

        return 0;
    }

    // ════════════════════════════════════════════════════════════
    // Main flow: authenticate → call GET /me
    // ════════════════════════════════════════════════════════════

    private static async Task<int> RunAuthAndCallGraphAsync(
        OnePasswordStore store, bool useDeviceCode, CancellationToken ct)
    {
        Console.Error.WriteLine("=== MSAL + 1Password Spike ===");
        Console.Error.WriteLine();

        // Step 1: Read app registration from 1Password
        var (clientId, tenantId) = await ReadAppRegistrationAsync(store, ct);
        Console.Error.WriteLine($"[Config] Client ID: {clientId[..8]}...  Tenant ID: {tenantId[..8]}...");

        // Step 2: Build MSAL PublicClientApplication
        var pca = BuildPublicClientApp(clientId, tenantId);

        // Step 3: Register 1Password-backed token cache
        var cacheHelper = new OnePasswordTokenCacheHelper(store);
        cacheHelper.Register(pca.UserTokenCache);
        Console.Error.WriteLine("[MSAL] Token cache registered with 1Password backend.");

        // Step 4: Try to acquire token silently (proves cache round-trip on 2nd run)
        AuthenticationResult authResult;

        var accounts = await pca.GetAccountsAsync();
        var account = accounts.FirstOrDefault();

        if (account is not null)
        {
            Console.Error.WriteLine($"[MSAL] Found cached account: {account.Username}");
            try
            {
                authResult = await pca.AcquireTokenSilent(Scopes, account).ExecuteAsync(ct);
                Console.Error.WriteLine("[MSAL] ✓ Token acquired SILENTLY from cache (no browser needed).");
                Console.Error.WriteLine($"[MSAL]   Expires: {authResult.ExpiresOn:u}");
                Console.Error.WriteLine($"[MSAL]   Scopes:  {string.Join(", ", authResult.Scopes)}");
            }
            catch (MsalUiRequiredException ex)
            {
                Console.Error.WriteLine($"[MSAL] Silent acquisition failed ({ex.ErrorCode}). Falling back to interactive...");
                authResult = await AcquireTokenInteractiveAsync(pca, useDeviceCode, ct);
            }
        }
        else
        {
            Console.Error.WriteLine("[MSAL] No cached accounts found. Starting interactive login...");
            authResult = await AcquireTokenInteractiveAsync(pca, useDeviceCode, ct);
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine($"[Auth] Access token acquired for: {authResult.Account.Username}");
        Console.Error.WriteLine($"[Auth] Token type: {authResult.TokenType}");
        Console.Error.WriteLine($"[Auth] Expires: {authResult.ExpiresOn:u}");

        // Step 5: Call Microsoft Graph GET /me
        Console.Error.WriteLine();
        Console.Error.WriteLine("[Graph] Calling GET /me ...");

        var credential = new MsalTokenCredential(pca, Scopes);
        var graphClient = new GraphServiceClient(
            new BaseBearerTokenAuthenticationProvider(credential));

        var me = await graphClient.Me.GetAsync(cancellationToken: ct);

        if (me is null)
        {
            Console.Error.WriteLine("[Graph] ERROR: GET /me returned null.");
            return 1;
        }

        // Step 6: Print results as JSON to stdout
        var result = new
        {
            spike = "MSAL + 1Password → Microsoft Graph",
            status = "SUCCESS",
            user = new
            {
                displayName = me.DisplayName,
                email = me.Mail ?? me.UserPrincipalName,
                id = me.Id,
                jobTitle = me.JobTitle,
            },
            auth = new
            {
                tokenSource = account is not null ? "cache (silent)" : "interactive",
                expiresOn = authResult.ExpiresOn.ToString("u"),
                scopes = authResult.Scopes.ToArray(),
            }
        };

        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        Console.WriteLine(json);

        Console.Error.WriteLine();
        Console.Error.WriteLine("=== Spike complete. ===");
        Console.Error.WriteLine();
        Console.Error.WriteLine("What was proven:");
        Console.Error.WriteLine("  1. MSAL token cache serialized to 1Password Secure Note.");
        Console.Error.WriteLine("  2. Cache deserialized on subsequent run (silent token acquisition).");
        Console.Error.WriteLine("  3. Access token used to call Microsoft Graph successfully.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Run again to verify silent token acquisition works (no browser should open).");

        return 0;
    }

    // ════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════

    private static async Task<(string ClientId, string TenantId)> ReadAppRegistrationAsync(
        OnePasswordStore store, CancellationToken ct)
    {
        string? clientId = await store.ReadFieldAsync(AppRegistrationItem, "client-id", ct);
        string? tenantId = await store.ReadFieldAsync(AppRegistrationItem, "tenant-id", ct);

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(tenantId))
        {
            throw new InvalidOperationException(
                "App registration not found in 1Password. Run setup first:\n" +
                "  dotnet run -- --setup");
        }

        return (clientId, tenantId);
    }

    private static IPublicClientApplication BuildPublicClientApp(string clientId, string tenantId)
    {
        return PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
            .WithRedirectUri("http://localhost")
            .Build();
    }

    private static async Task<AuthenticationResult> AcquireTokenInteractiveAsync(
        IPublicClientApplication pca, bool useDeviceCode, CancellationToken ct)
    {
        if (useDeviceCode)
        {
            return await pca.AcquireTokenWithDeviceCode(Scopes, deviceCodeResult =>
            {
                // Display the device code instructions to stderr so they don't pollute JSON stdout
                Console.Error.WriteLine();
                Console.Error.WriteLine("╔════════════════════════════════════════════════════════╗");
                Console.Error.WriteLine("║  Device Code Authentication                            ║");
                Console.Error.WriteLine("╚════════════════════════════════════════════════════════╝");
                Console.Error.WriteLine();
                Console.Error.WriteLine(deviceCodeResult.Message);
                Console.Error.WriteLine();
                return Task.CompletedTask;
            }).ExecuteAsync(ct);
        }
        else
        {
            // Interactive loopback flow: MSAL starts a local HTTP listener
            // and opens the system browser for login.
            Console.Error.WriteLine("[MSAL] Opening browser for authentication...");
            Console.Error.WriteLine("[MSAL] (If no browser opens, re-run with --device-code)");
            return await pca.AcquireTokenInteractive(Scopes)
                .WithUseEmbeddedWebView(false)
                .ExecuteAsync(ct);
        }
    }
}

// ════════════════════════════════════════════════════════════
// TokenCredential adapter: bridges MSAL to Microsoft.Graph SDK
// ════════════════════════════════════════════════════════════

/// <summary>
/// Adapts MSAL's IPublicClientApplication into an IAccessTokenProvider
/// that the Microsoft.Graph SDK can use for authentication.
/// </summary>
internal sealed class MsalTokenCredential : IAccessTokenProvider
{
    private readonly IPublicClientApplication _pca;
    private readonly string[] _scopes;

    public MsalTokenCredential(IPublicClientApplication pca, string[] scopes)
    {
        _pca = pca;
        _scopes = scopes;
        AllowedHostsValidator = new AllowedHostsValidator(["graph.microsoft.com"]);
    }

    public AllowedHostsValidator AllowedHostsValidator { get; }

    public async Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        var accounts = await _pca.GetAccountsAsync();
        var account = accounts.FirstOrDefault();

        if (account is null)
            throw new InvalidOperationException("No cached account. Run interactive login first.");

        var result = await _pca.AcquireTokenSilent(_scopes, account).ExecuteAsync(cancellationToken);
        return result.AccessToken;
    }
}
