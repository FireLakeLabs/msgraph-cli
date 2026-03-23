namespace MsGraphCli.Core.Exceptions;

/// <summary>
/// Base exception for msgraph-cli errors with structured exit codes.
/// </summary>
public class MsGraphCliException : Exception
{
    public int ExitCode { get; }
    public string ErrorCode { get; }

    public MsGraphCliException(string message, string errorCode, int exitCode, Exception? inner = null)
        : base(message, inner)
    {
        ErrorCode = errorCode;
        ExitCode = exitCode;
    }
}

public class AuthenticationRequiredException : MsGraphCliException
{
    public string[] MissingScopes { get; }

    public AuthenticationRequiredException(string message, string[]? missingScopes = null, Exception? inner = null)
        : base(message, "AuthenticationRequired", exitCode: 2, inner)
    {
        MissingScopes = missingScopes ?? [];
    }
}

public class InsufficientScopesException : MsGraphCliException
{
    public string[] RequiredScopes { get; }

    public InsufficientScopesException(string message, string[] requiredScopes, Exception? inner = null)
        : base(message, "InsufficientScopes", exitCode: 2, inner)
    {
        RequiredScopes = requiredScopes;
    }
}

public class ResourceNotFoundException : MsGraphCliException
{
    public ResourceNotFoundException(string message, Exception? inner = null)
        : base(message, "ResourceNotFound", exitCode: 3, inner) { }
}

public class PermissionDeniedException : MsGraphCliException
{
    public PermissionDeniedException(string message, Exception? inner = null)
        : base(message, "PermissionDenied", exitCode: 4, inner) { }
}

public class RateLimitedException : MsGraphCliException
{
    public TimeSpan? RetryAfter { get; }

    public RateLimitedException(string message, TimeSpan? retryAfter = null, Exception? inner = null)
        : base(message, "RateLimited", exitCode: 5, inner)
    {
        RetryAfter = retryAfter;
    }
}

public class CommandNotAllowedException : MsGraphCliException
{
    public string CommandName { get; }

    public CommandNotAllowedException(string commandName)
        : base($"Command '{commandName}' is not in the enabled command list.", "CommandNotAllowed", exitCode: 10)
    {
        CommandName = commandName;
    }
}

public class ReadOnlyViolationException : MsGraphCliException
{
    public string CommandName { get; }

    public ReadOnlyViolationException(string commandName)
        : base($"Command '{commandName}' is blocked because --readonly mode is enabled.", "ReadOnlyViolation", exitCode: 10)
    {
        CommandName = commandName;
    }
}
