using System.CommandLine;
using MsGraphCli.Commands;
using MsGraphCli.Core.Exceptions;
using MsGraphCli.Output;

namespace MsGraphCli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("msgraph — Microsoft 365 CLI via Microsoft Graph API");

        // ── Global options ──
        var jsonOption = new Option<bool>("--json") { Description = "Output JSON to stdout" };
        var plainOption = new Option<bool>("--plain") { Description = "Output tab-separated values to stdout" };
        var verboseOption = new Option<bool>("--verbose") { Description = "Verbose logging to stderr" };
        var betaOption = new Option<bool>("--beta") { Description = "Use Microsoft Graph beta endpoint" };
        var readOnlyOption = new Option<bool>("--readonly") { Description = "Block write operations" };

        rootCommand.Options.Add(jsonOption);
        rootCommand.Options.Add(plainOption);
        rootCommand.Options.Add(verboseOption);
        rootCommand.Options.Add(betaOption);
        rootCommand.Options.Add(readOnlyOption);

        // ── Global context ──
        // These are passed through to command handlers via ParseResult
        var globalContext = new GlobalOptions(jsonOption, plainOption, verboseOption, betaOption, readOnlyOption);

        // ── Register command groups ──
        rootCommand.Subcommands.Add(AuthCommands.Build(globalContext));
        rootCommand.Subcommands.Add(MailCommands.Build(globalContext));
        rootCommand.Subcommands.Add(CalendarCommands.Build(globalContext));

        // ── Version ──
        var versionCommand = new Command("version", "Show version information");
        versionCommand.SetAction(_ =>
        {
            Console.WriteLine($"msgraph-cli {GetVersion()}");
        });
        rootCommand.Subcommands.Add(versionCommand);

        int result = await rootCommand.Parse(args).InvokeAsync();

        // If a command handler caught a MsGraphCliException via ActionRunner,
        // Environment.ExitCode will be set to the correct exit code.
        if (Environment.ExitCode != 0)
        {
            return Environment.ExitCode;
        }

        return result;
    }

    private static string GetVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version?.ToString(3) ?? "0.0.0";
    }
}
