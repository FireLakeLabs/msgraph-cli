using System.CommandLine;
using FireLakeLabs.MsGraphCli.Core.Auth;
using FireLakeLabs.MsGraphCli.Core.Config;
using FireLakeLabs.MsGraphCli.Output;

namespace FireLakeLabs.MsGraphCli.Commands;

public static class AuthCommands
{
    public static Command Build(GlobalOptions global)
    {
        var authCommand = new Command("auth", "Authentication and credential management");

        authCommand.Subcommands.Add(BuildSetup(global));
        authCommand.Subcommands.Add(BuildLogin(global));
        authCommand.Subcommands.Add(BuildStatus(global));
        authCommand.Subcommands.Add(BuildLogout(global));
        authCommand.Subcommands.Add(BuildScopes(global));

        return authCommand;
    }

    // ── msgraph auth setup ──

    private static Command BuildSetup(GlobalOptions global)
    {
        var clientIdOption = new Option<string?>("--client-id") { Description = "Entra ID Application (Client) ID" };
        var tenantIdOption = new Option<string?>("--tenant-id") { Description = "Entra ID Directory (Tenant) ID" };

        var command = new Command("setup", "Configure 1Password vault and store app registration");
        command.Options.Add(clientIdOption);
        command.Options.Add(tenantIdOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            AppConfig config = ConfigLoader.Load();
            var store = new OnePasswordSecretStore(config.OnePasswordVault);
            var tokenCacheStore = new OnePasswordSecretStore(config.TokenCacheVault);

            if (!await store.IsAvailableAsync(cancellationToken))
            {
                Console.Error.WriteLine("ERROR: 1Password CLI (op) is not available or you are not signed in.");
                Console.Error.WriteLine("  Install: https://developer.1password.com/docs/cli/get-started/");
                Console.Error.WriteLine("  Sign in: eval $(op signin)");
                // Early return — error already written to stderr
                return;
            }

            await store.EnsureVaultExistsAsync(cancellationToken);
            await tokenCacheStore.EnsureVaultExistsAsync(cancellationToken);

            string? clientId = parseResult.GetValue(clientIdOption);
            string? tenantId = parseResult.GetValue(tenantIdOption);

            if (string.IsNullOrEmpty(clientId))
            {
                Console.Error.Write("Enter your Entra ID Client ID (Application ID): ");
                clientId = Console.ReadLine()?.Trim();
            }

            if (string.IsNullOrEmpty(tenantId))
            {
                Console.Error.Write("Enter your Entra ID Tenant ID: ");
                tenantId = Console.ReadLine()?.Trim();
            }

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(tenantId))
            {
                Console.Error.WriteLine("ERROR: Both Client ID and Tenant ID are required.");
                return;
            }

            await store.WriteFieldsAsync("ms-graph-app-registration", new Dictionary<string, string>
            {
                ["client-id"] = clientId,
                ["tenant-id"] = tenantId,
            }, cancellationToken);

            Console.Error.WriteLine($"✓ App registration stored in 1Password vault '{config.OnePasswordVault}'.");
        });

        return command;
    }

    // ── msgraph auth login ──

    private static Command BuildLogin(GlobalOptions global)
    {
        var servicesOption = new Option<string?>("--services") { Description = "Comma-separated list of services (mail,calendar,drive,todo,excel,docs)" };
        var readonlyOption = new Option<bool>("--readonly") { Description = "Request only read scopes" };
        var deviceCodeOption = new Option<bool>("--device-code") { Description = "Use device code flow (for headless/SSH)" };

        var command = new Command("login", "Authenticate with Microsoft Entra ID");
        command.Options.Add(servicesOption);
        command.Options.Add(readonlyOption);
        command.Options.Add(deviceCodeOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            AppConfig config = ConfigLoader.Load();
            var store = new OnePasswordSecretStore(config.OnePasswordVault);
            var tokenCacheStore = new OnePasswordSecretStore(config.TokenCacheVault);
            var authProvider = new GraphAuthProvider(store, tokenCacheStore);

            string? servicesRaw = parseResult.GetValue(servicesOption);
            bool readOnly = parseResult.GetValue(readonlyOption);
            bool useDeviceCode = parseResult.GetValue(deviceCodeOption);

            string[] scopes;
            if (!string.IsNullOrEmpty(servicesRaw))
            {
                string[] serviceNames = servicesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                ScopeRegistry.ValidateServiceNames(serviceNames);
                scopes = ScopeRegistry.GetScopes(serviceNames, readOnly);
            }
            else
            {
                scopes = ScopeRegistry.GetAllScopes(readOnly);
            }

            Console.Error.WriteLine($"Requesting scopes: {string.Join(", ", scopes)}");

            var result = await authProvider.LoginInteractiveAsync(scopes, useDeviceCode, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            IOutputFormatter formatter = OutputFormatResolver.Resolve(isJson, parseResult.GetValue(global.Plain));

            var output = new
            {
                status = "authenticated",
                account = result.Account.Username,
                expiresOn = result.ExpiresOn.ToString("u"),
                scopes = result.Scopes.ToArray(),
            };

            if (isJson)
            {
                formatter.WriteResult(output, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"✓ Authenticated as {result.Account.Username}");
                Console.Error.WriteLine($"  Token expires: {result.ExpiresOn:u}");
                Console.Error.WriteLine($"  Scopes: {string.Join(", ", result.Scopes)}");
            }
        });

        return command;
    }

    // ── msgraph auth status ──

    private static Command BuildStatus(GlobalOptions global)
    {
        var command = new Command("status", "Show current authentication state");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            AppConfig config = ConfigLoader.Load();
            var store = new OnePasswordSecretStore(config.OnePasswordVault);
            var tokenCacheStore = new OnePasswordSecretStore(config.TokenCacheVault);
            var authProvider = new GraphAuthProvider(store, tokenCacheStore);

            string[] scopes = ScopeRegistry.GetAllScopes(readOnly: false);
            AuthStatus status = await authProvider.GetStatusAsync(scopes, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            IOutputFormatter formatter = OutputFormatResolver.Resolve(isJson, parseResult.GetValue(global.Plain));

            if (isJson)
            {
                formatter.WriteResult(status, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"  Authenticated: {(status.IsAuthenticated ? "yes" : "no")}");

                if (status.UserEmail is not null)
                {
                    Console.Error.WriteLine($"  Account:       {status.UserEmail}");
                }

                if (status.TenantId is not null)
                {
                    Console.Error.WriteLine($"  Tenant:        {status.TenantId}");
                }

                if (status.TokenExpiry is not null)
                {
                    Console.Error.WriteLine($"  Token expires: {status.TokenExpiry:u}");
                }

                if (status.GrantedScopes.Count > 0)
                {
                    Console.Error.WriteLine($"  Scopes:        {string.Join(", ", status.GrantedScopes)}");
                }
                else if (status.IsAuthenticated is false && status.UserEmail is not null)
                {
                    Console.Error.WriteLine("  Token expired. Run: msgraph auth login");
                }
                else if (status.UserEmail is null)
                {
                    Console.Error.WriteLine("  Not authenticated. Run: msgraph auth login");
                }
            }
        });

        return command;
    }

    // ── msgraph auth logout ──

    private static Command BuildLogout(GlobalOptions global)
    {
        var command = new Command("logout", "Clear cached tokens from 1Password");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            AppConfig config = ConfigLoader.Load();
            var store = new OnePasswordSecretStore(config.OnePasswordVault);
            var tokenCacheStore = new OnePasswordSecretStore(config.TokenCacheVault);
            var authProvider = new GraphAuthProvider(store, tokenCacheStore);

            await authProvider.LogoutAsync(cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                IOutputFormatter formatter = OutputFormatResolver.Resolve(isJson, false);
                formatter.WriteResult(new { status = "logged_out" }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine("✓ Logged out. Tokens cleared from 1Password.");
            }
        });

        return command;
    }

    // ── msgraph auth scopes ──

    private static Command BuildScopes(GlobalOptions global)
    {
        var command = new Command("scopes", "Display the scope registry for all services");

        command.SetAction(parseResult =>
        {
            bool isJson = parseResult.GetValue(global.Json);

            if (isJson)
            {
                IOutputFormatter formatter = OutputFormatResolver.Resolve(isJson, false);
                formatter.WriteResult(ScopeRegistry.Services, Console.Out);
            }
            else
            {
                Console.WriteLine($"{"SERVICE",-12} {"READ SCOPES",-30} {"WRITE SCOPES",-30}");
                Console.WriteLine(new string('─', 72));

                foreach (var (name, scopes) in ScopeRegistry.Services)
                {
                    Console.WriteLine($"{name,-12} {string.Join(", ", scopes.Read),-30} {string.Join(", ", scopes.Write),-30}");
                }

                Console.WriteLine();
                Console.WriteLine($"Base scopes (always included): {string.Join(", ", ScopeRegistry.BaseScopes)}");
            }
        });

        return command;
    }
}
