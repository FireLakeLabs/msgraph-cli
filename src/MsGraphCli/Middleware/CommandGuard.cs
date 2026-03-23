using MsGraphCli.Core.Exceptions;

namespace MsGraphCli.Middleware;

/// <summary>
/// Enforces command restrictions such as read-only mode and allowlists.
/// </summary>
public static class CommandGuard
{
    private static readonly HashSet<string> WriteCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "mail send",
        "mail reply",
        "mail forward",
        "mail move",
        "mail mark-read",
        "mail mark-unread",
        "calendar create",
        "calendar update",
        "calendar delete",
        "calendar respond",
    };

    /// <summary>
    /// Throws if the command is a write operation and read-only mode is enabled.
    /// </summary>
    public static void EnforceReadOnly(string commandPath, bool readOnlyFlag)
    {
        if (readOnlyFlag && WriteCommands.Contains(commandPath))
        {
            throw new ReadOnlyViolationException(commandPath);
        }
    }

    /// <summary>
    /// Throws if the command is not in the allowed commands list.
    /// </summary>
    public static void EnforceAllowList(string commandPath, IReadOnlySet<string>? allowedCommands)
    {
        if (allowedCommands is not null && !allowedCommands.Contains(commandPath))
        {
            throw new CommandNotAllowedException(commandPath);
        }
    }
}
