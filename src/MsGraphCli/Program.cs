using System.CommandLine;
using MsGraphCli.Commands;

namespace MsGraphCli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("msgraph — Microsoft 365 CLI via Microsoft Graph API");

        // ── Global options ──
        var jsonOption = new Option<bool>("--json", "Output JSON to stdout");
        var plainOption = new Option<bool>("--plain", "Output tab-separated values to stdout");
        var verboseOption = new Option<bool>("--verbose", "Verbose logging to stderr");
        var betaOption = new Option<bool>("--beta", "Use Microsoft Graph beta endpoint");

        rootCommand.Options.Add(jsonOption);
        rootCommand.Options.Add(plainOption);
        rootCommand.Options.Add(verboseOption);
        rootCommand.Options.Add(betaOption);

        // ── Global context ──
        // These are passed through to command handlers via ParseResult
        var globalContext = new GlobalOptions(jsonOption, plainOption, verboseOption, betaOption);

        // ── Register command groups ──
        rootCommand.Subcommands.Add(AuthCommands.Build(globalContext));
        rootCommand.Subcommands.Add(MailCommands.Build(globalContext));

        // ── Version ──
        var versionCommand = new Command("version", "Show version information");
        versionCommand.SetAction(_ =>
        {
            Console.WriteLine($"msgraph-cli {GetVersion()}");
        });
        rootCommand.Subcommands.Add(versionCommand);

        return await rootCommand.Parse(args).InvokeAsync();
    }

    private static string GetVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version?.ToString(3) ?? "0.0.0";
    }
}
