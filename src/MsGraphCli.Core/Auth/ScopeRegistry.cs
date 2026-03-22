namespace MsGraphCli.Core.Auth;

public record ServiceScopes(string[] Read, string[] Write);

/// <summary>
/// Maps service names to their required Microsoft Graph scopes.
/// </summary>
public static class ScopeRegistry
{
    public static readonly string[] BaseScopes = ["User.Read", "offline_access"];

    public static readonly Dictionary<string, ServiceScopes> Services = new()
    {
        ["mail"] = new(Read: ["Mail.Read"], Write: ["Mail.Send"]),
        ["calendar"] = new(Read: ["Calendars.Read"], Write: ["Calendars.ReadWrite"]),
        ["drive"] = new(Read: ["Files.Read"], Write: ["Files.ReadWrite"]),
        ["todo"] = new(Read: ["Tasks.Read"], Write: ["Tasks.ReadWrite"]),
        ["excel"] = new(Read: ["Files.Read"], Write: ["Files.ReadWrite"]),
        ["docs"] = new(Read: ["Files.Read"], Write: ["Files.ReadWrite"]),
    };

    /// <summary>
    /// Get the combined scopes for the given services.
    /// Always includes base scopes (User.Read, offline_access).
    /// </summary>
    public static string[] GetScopes(IEnumerable<string> serviceNames, bool readOnly)
    {
        var scopes = new HashSet<string>(BaseScopes);

        foreach (string name in serviceNames)
        {
            string normalizedName = name.Trim().ToLowerInvariant();

            if (!Services.TryGetValue(normalizedName, out ServiceScopes? serviceScopes))
            {
                throw new ArgumentException($"Unknown service: '{name}'. Valid services: {string.Join(", ", Services.Keys)}");
            }

            foreach (string scope in serviceScopes.Read)
            {
                scopes.Add(scope);
            }

            if (!readOnly)
            {
                foreach (string scope in serviceScopes.Write)
                {
                    scopes.Add(scope);
                }
            }
        }

        return [.. scopes];
    }

    /// <summary>
    /// Get scopes for all known services.
    /// </summary>
    public static string[] GetAllScopes(bool readOnly) =>
        GetScopes(Services.Keys, readOnly);

    /// <summary>
    /// Validate that all service names are recognized.
    /// </summary>
    public static void ValidateServiceNames(IEnumerable<string> serviceNames)
    {
        foreach (string name in serviceNames)
        {
            string normalizedName = name.Trim().ToLowerInvariant();
            if (!Services.ContainsKey(normalizedName))
            {
                throw new ArgumentException(
                    $"Unknown service: '{name}'. Valid services: {string.Join(", ", Services.Keys)}");
            }
        }
    }
}
