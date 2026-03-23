using System.CommandLine;
using MsGraphCli.Core.Auth;
using MsGraphCli.Core.Config;
using MsGraphCli.Core.Graph;
using MsGraphCli.Core.Models;
using MsGraphCli.Core.Services;
using MsGraphCli.Output;

namespace MsGraphCli.Commands;

public static class MailCommands
{
    public static Command Build(GlobalOptions global)
    {
        var mailCommand = new Command("mail", "Outlook mail operations");

        mailCommand.Subcommands.Add(BuildList(global));
        mailCommand.Subcommands.Add(BuildSearch(global));
        mailCommand.Subcommands.Add(BuildGet(global));
        mailCommand.Subcommands.Add(BuildFolders(global));

        return mailCommand;
    }

    private static (IMailService Service, IOutputFormatter Formatter) CreateServiceContext(
        ParseResult parseResult, GlobalOptions global)
    {
        AppConfig config = ConfigLoader.Load();
        var store = new OnePasswordSecretStore(config.OnePasswordVault);
        var tokenCacheStore = new OnePasswordSecretStore(config.TokenCacheVault);
        var authProvider = new GraphAuthProvider(store, tokenCacheStore);

        string[] scopes = ScopeRegistry.GetScopes(["mail"], readOnly: true);
        var factory = new GraphClientFactory(authProvider, scopes);
        var client = factory.CreateClient();
        var service = new MailService(client);

        bool isJson = parseResult.GetValue(global.Json);
        bool isPlain = parseResult.GetValue(global.Plain);
        IOutputFormatter formatter = OutputFormatResolver.Resolve(isJson, isPlain);

        return (service, formatter);
    }

    // ── Well-known folder name resolution ──

    private static readonly Dictionary<string, string> WellKnownFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["inbox"] = "inbox",
        ["sent"] = "sentitems",
        ["sentitems"] = "sentitems",
        ["drafts"] = "drafts",
        ["deleted"] = "deleteditems",
        ["deleteditems"] = "deleteditems",
        ["archive"] = "archive",
        ["junk"] = "junkemail",
        ["junkemail"] = "junkemail",
    };

    private static string ResolveFolderName(string? folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return "inbox";
        }

        return WellKnownFolders.GetValueOrDefault(folder, folder);
    }

    // ── msgraph mail list ──

    private static Command BuildList(GlobalOptions global)
    {
        var folderOption = new Option<string?>("-f", "--folder") { Description = "Mail folder name or ID (default: inbox)" };
        var maxOption = new Option<int?>("-n", "--max") { Description = "Maximum number of messages to return" };

        var command = new Command("list", "List messages in a mail folder");
        command.Options.Add(folderOption);
        command.Options.Add(maxOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            string? folder = parseResult.GetValue(folderOption);
            int? max = parseResult.GetValue(maxOption);

            string folderId = ResolveFolderName(folder);
            IReadOnlyList<MailMessageSummary> messages = await service.ListMessagesAsync(folderId, max, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { messages }, Console.Out);
            }
            else if (formatter is TableOutputFormatter)
            {
                TableOutputFormatter.WriteMailTable(messages);
            }
            else
            {
                // Plain mode
                foreach (MailMessageSummary msg in messages)
                {
                    Console.WriteLine($"{msg.From}\t{msg.Subject}\t{msg.ReceivedDateTime:u}\t{msg.IsRead}");
                }
            }
        });

        return command;
    }

    // ── msgraph mail search ──

    private static Command BuildSearch(GlobalOptions global)
    {
        var queryArgument = new Argument<string>("query") { Description = "Search query (KQL syntax)" };
        var maxOption = new Option<int?>("-n", "--max") { Description = "Maximum number of messages to return" };

        var command = new Command("search", "Search messages across all folders");
        command.Arguments.Add(queryArgument);
        command.Options.Add(maxOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            string query = parseResult.GetValue(queryArgument)!;
            int? max = parseResult.GetValue(maxOption);

            IReadOnlyList<MailMessageSummary> messages = await service.SearchMessagesAsync(query, max, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { messages, query }, Console.Out);
            }
            else if (formatter is TableOutputFormatter)
            {
                TableOutputFormatter.WriteMailTable(messages);
            }
            else
            {
                foreach (MailMessageSummary msg in messages)
                {
                    Console.WriteLine($"{msg.From}\t{msg.Subject}\t{msg.ReceivedDateTime:u}\t{msg.IsRead}");
                }
            }
        });

        return command;
    }

    // ── msgraph mail get ──

    private static Command BuildGet(GlobalOptions global)
    {
        var messageIdArgument = new Argument<string>("messageId") { Description = "Message ID" };
        var formatOption = new Option<string>("--format") { Description = "Output detail level: summary or full", DefaultValueFactory = _ => "summary" };

        var command = new Command("get", "Get a specific message");
        command.Arguments.Add(messageIdArgument);
        command.Options.Add(formatOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            string messageId = parseResult.GetValue(messageIdArgument)!;
            string format = parseResult.GetValue(formatOption)!;
            bool includeBody = string.Equals(format, "full", StringComparison.OrdinalIgnoreCase);

            MailMessageDetail message = await service.GetMessageAsync(messageId, includeBody, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { message }, Console.Out);
            }
            else
            {
                Console.WriteLine($"From:    {message.From}");
                Console.WriteLine($"To:      {string.Join(", ", message.ToRecipients)}");

                if (message.CcRecipients.Count > 0)
                {
                    Console.WriteLine($"Cc:      {string.Join(", ", message.CcRecipients)}");
                }

                Console.WriteLine($"Subject: {message.Subject}");
                Console.WriteLine($"Date:    {message.ReceivedDateTime:u}");
                Console.WriteLine($"Read:    {(message.IsRead ? "yes" : "no")}");

                if (includeBody && message.BodyText is not null)
                {
                    Console.WriteLine();
                    Console.WriteLine("── Body ──");
                    Console.WriteLine(message.BodyText);
                }
            }
        });

        return command;
    }

    // ── msgraph mail folders ──

    private static Command BuildFolders(GlobalOptions global)
    {
        var listCommand = new Command("list", "List mail folders");

        listCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            IReadOnlyList<MailFolder> folders = await service.ListFoldersAsync(cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { folders }, Console.Out);
            }
            else if (formatter is TableOutputFormatter)
            {
                TableOutputFormatter.WriteFolderTable(folders);
            }
            else
            {
                foreach (MailFolder folder in folders)
                {
                    Console.WriteLine($"{folder.DisplayName}\t{folder.TotalItemCount}\t{folder.UnreadItemCount}");
                }
            }
        });

        var foldersCommand = new Command("folders", "Mail folder operations");
        foldersCommand.Subcommands.Add(listCommand);
        return foldersCommand;
    }
}
