using System.CommandLine;
using Microsoft.Graph.Models.ODataErrors;
using FireLakeLabs.MsGraphCli.Core.Exceptions;
using FireLakeLabs.MsGraphCli.Output;

namespace FireLakeLabs.MsGraphCli.Middleware;

/// <summary>
/// Wraps command actions with global exception handling for MsGraphCliException
/// and Graph SDK ODataError.
/// </summary>
public static class ActionRunner
{
    /// <summary>
    /// Wraps a command action to catch MsGraphCliException and ODataError,
    /// write a clean error, and set the correct exit code.
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
            catch (ODataError ex)
            {
                string errorCode = ex.Error?.Code ?? "GraphApiError";
                string message = ex.Error?.Message ?? ex.Message;
                bool isJson = parseResult.GetValue(global.Json);
                IOutputFormatter formatter = OutputFormatResolver.Resolve(isJson, parseResult.GetValue(global.Plain));
                formatter.WriteError(errorCode, message, Console.Error);
                Environment.ExitCode = 1;
            }
        });
    }
}
