using System.Text.Json;

namespace MsGraphCli.Core.Config;

public sealed class AppConfig
{
    public string OnePasswordVault { get; set; } = "msgraph-cli";
    public string TokenCacheVault { get; set; } = "netclaw-rw";
    public string DefaultOutputFormat { get; set; } = "table";
    public string? DefaultTimezone { get; set; }
    public string[]? EnableCommands { get; set; }
    public bool Verbose { get; set; }
}

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Load config from disk only, without environment variable overrides.
    /// Use this when reading config for editing/saving to avoid persisting env overrides.
    /// </summary>
    public static AppConfig LoadFromDisk()
    {
        string configPath = GetConfigPath();

        if (File.Exists(configPath))
        {
            string json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }

        return new AppConfig();
    }

    /// <summary>
    /// Load config from the default location, with environment variable overrides.
    /// </summary>
    public static AppConfig Load()
    {
        AppConfig config = LoadFromDisk();

        // Environment variable overrides
        string? vault = Environment.GetEnvironmentVariable("MSGRAPH_VAULT");
        if (!string.IsNullOrEmpty(vault))
        {
            config.OnePasswordVault = vault;
        }

        string? tokenCacheVault = Environment.GetEnvironmentVariable("MSGRAPH_TOKEN_CACHE_VAULT");
        if (!string.IsNullOrEmpty(tokenCacheVault))
        {
            config.TokenCacheVault = tokenCacheVault;
        }

        string? enableCommands = Environment.GetEnvironmentVariable("MSGRAPH_ENABLE_COMMANDS");
        if (!string.IsNullOrEmpty(enableCommands))
        {
            config.EnableCommands = enableCommands.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (Environment.GetEnvironmentVariable("MSGRAPH_VERBOSE") == "1")
        {
            config.Verbose = true;
        }

        return config;
    }

    /// <summary>
    /// Save config to the default location, creating the directory if needed.
    /// </summary>
    public static void Save(AppConfig config)
    {
        string configPath = GetConfigPath();
        string? directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(configPath, json);
    }

    /// <summary>
    /// Returns the list of known config keys that can be used with get/set.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetKnownKeys() => new Dictionary<string, string>
    {
        ["onePasswordVault"] = "1Password vault for app config (read-only)",
        ["tokenCacheVault"] = "1Password vault for token cache (read-write)",
        ["defaultOutputFormat"] = "Default output format: table, json, or plain",
        ["defaultTimezone"] = "Default timezone for date display",
        ["enableCommands"] = "Comma-separated command allowlist (null = all)",
        ["verbose"] = "Enable verbose logging (true/false)",
    };

    /// <summary>
    /// Get a config value by key name.
    /// </summary>
    public static string? GetValue(AppConfig config, string key)
    {
        return key switch
        {
            "onePasswordVault" => config.OnePasswordVault,
            "tokenCacheVault" => config.TokenCacheVault,
            "defaultOutputFormat" => config.DefaultOutputFormat,
            "defaultTimezone" => config.DefaultTimezone,
            "enableCommands" => config.EnableCommands is not null ? string.Join(",", config.EnableCommands) : null,
            "verbose" => config.Verbose.ToString().ToLowerInvariant(),
            _ => throw new ArgumentException($"Unknown config key: {key}. Valid keys: {string.Join(", ", GetKnownKeys().Keys)}"),
        };
    }

    /// <summary>
    /// Set a config value by key name.
    /// </summary>
    public static void SetValue(AppConfig config, string key, string value)
    {
        switch (key)
        {
            case "onePasswordVault":
                config.OnePasswordVault = value;
                break;
            case "tokenCacheVault":
                config.TokenCacheVault = value;
                break;
            case "defaultOutputFormat":
                config.DefaultOutputFormat = value;
                break;
            case "defaultTimezone":
                config.DefaultTimezone = value;
                break;
            case "enableCommands":
                config.EnableCommands = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                break;
            case "verbose":
                config.Verbose = bool.Parse(value);
                break;
            default:
                throw new ArgumentException($"Unknown config key: {key}. Valid keys: {string.Join(", ", GetKnownKeys().Keys)}");
        }
    }

    public static string GetConfigPath()
    {
        string? envPath = Environment.GetEnvironmentVariable("MSGRAPH_CONFIG");
        if (!string.IsNullOrEmpty(envPath))
        {
            return envPath;
        }

        string configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        return Path.Combine(configHome, "msgraph-cli", "config.json");
    }
}
