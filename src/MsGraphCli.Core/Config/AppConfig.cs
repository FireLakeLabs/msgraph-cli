using System.Text.Json;

namespace MsGraphCli.Core.Config;

public sealed class AppConfig
{
    public string OnePasswordVault { get; set; } = "msgraph-cli";
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
    /// Load config from the default location, with environment variable overrides.
    /// </summary>
    public static AppConfig Load()
    {
        string configPath = GetConfigPath();
        AppConfig config;

        if (File.Exists(configPath))
        {
            string json = File.ReadAllText(configPath);
            config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        else
        {
            config = new AppConfig();
        }

        // Environment variable overrides
        string? vault = Environment.GetEnvironmentVariable("MSGRAPH_VAULT");
        if (!string.IsNullOrEmpty(vault))
        {
            config.OnePasswordVault = vault;
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
