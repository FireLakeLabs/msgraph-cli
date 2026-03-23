using System.CommandLine;
using MsGraphCli.Core.Exceptions;
using MsGraphCli.Output;

namespace MsGraphCli.Middleware;

/// <summary>
/// Wraps command actions with global exception handling for MsGraphCliException.
/// </summary>
public static class ActionRunner
{
    /// <summary>
    /// Wraps a command action to catch MsGraphCliException, write a clean error,
    /// and set the correct exit code.
    /// </summary>
    public static void SetGuardedAction(
        Command command,
        GlobalOptions global,
        Func<ParseResult, CancellationToken, Task> action)
    {
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                await action(parseResult, cancellationToken);
            }
            catch (MsGraphCliException ex)
            {
                bool isJson = parseResult.GetValue(global.Json);
                IOutputFormatter formatter = OutputFormatResolver.Resolve(isJson, parseResult.GetValue(global.Plain));
                formatter.WriteError(ex.ErrorCode, ex.Message, Console.Error);
                Environment.ExitCode = ex.ExitCode;
            }
        });
    }
}
