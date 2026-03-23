using System.CommandLine;
using System.Globalization;
using MsGraphCli.Core.Auth;
using MsGraphCli.Core.Config;
using MsGraphCli.Core.Graph;
using MsGraphCli.Core.Models;
using MsGraphCli.Core.Services;
using static MsGraphCli.Middleware.ActionRunner;
using MsGraphCli.Middleware;
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
        mailCommand.Subcommands.Add(BuildSend(global));
        mailCommand.Subcommands.Add(BuildReply(global));
        mailCommand.Subcommands.Add(BuildForward(global));
        mailCommand.Subcommands.Add(BuildMove(global));
        mailCommand.Subcommands.Add(BuildMarkRead(global));
        mailCommand.Subcommands.Add(BuildMarkUnread(global));
        mailCommand.Subcommands.Add(BuildAttachments(global));

        return mailCommand;
    }

    private static (IMailService Service, IOutputFormatter Formatter) CreateServiceContext(
        ParseResult parseResult, GlobalOptions global, bool readOnly = true)
    {
        AppConfig config = ConfigLoader.Load();
        var store = new OnePasswordSecretStore(config.OnePasswordVault);
        var tokenCacheStore = new OnePasswordSecretStore(config.TokenCacheVault);
        var authProvider = new GraphAuthProvider(store, tokenCacheStore);

        string[] scopes = ScopeRegistry.GetScopes(["mail"], readOnly: readOnly);
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

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
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

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
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

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
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

    // ── msgraph mail send ──

    private static Command BuildSend(GlobalOptions global)
    {
        var toOption = new Option<string>("--to") { Description = "Recipient email address(es), comma-separated", Required = true };
        var subjectOption = new Option<string>("--subject") { Description = "Email subject", Required = true };
        var bodyOption = new Option<string>("--body") { Description = "Email body (plain text)", Required = true };
        var bodyHtmlOption = new Option<string?>("--body-html") { Description = "Email body (HTML, overrides --body)" };
        var ccOption = new Option<string?>("--cc") { Description = "CC recipient(s), comma-separated" };
        var bccOption = new Option<string?>("--bcc") { Description = "BCC recipient(s), comma-separated" };
        var attachOption = new Option<string[]?>("--attach") { Description = "File path(s) to attach" };

        var command = new Command("send", "Send an email message");
        command.Options.Add(toOption);
        command.Options.Add(subjectOption);
        command.Options.Add(bodyOption);
        command.Options.Add(bodyHtmlOption);
        command.Options.Add(ccOption);
        command.Options.Add(bccOption);
        command.Options.Add(attachOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("mail send", parseResult.GetValue(global.ReadOnly));

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            string toRaw = parseResult.GetValue(toOption)!;
            List<string> to = SplitAddresses(toRaw);
            string? ccRaw = parseResult.GetValue(ccOption);
            List<string>? cc = ccRaw is not null ? SplitAddresses(ccRaw) : null;
            string? bccRaw = parseResult.GetValue(bccOption);
            List<string>? bcc = bccRaw is not null ? SplitAddresses(bccRaw) : null;

            string? bodyHtml = parseResult.GetValue(bodyHtmlOption);
            string body = bodyHtml ?? parseResult.GetValue(bodyOption)!;
            string contentType = bodyHtml is not null ? "HTML" : "Text";

            string[]? attachPaths = parseResult.GetValue(attachOption);

            var request = new MailSendRequest(
                To: to,
                Cc: cc,
                Bcc: bcc,
                Subject: parseResult.GetValue(subjectOption)!,
                Body: body,
                BodyContentType: contentType,
                AttachmentPaths: attachPaths
            );

            await service.SendMessageAsync(request, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "sent", to = request.To, subject = request.Subject }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Sent to {string.Join(", ", to)}");
            }
        });

        return command;
    }

    // ── msgraph mail reply ──

    private static Command BuildReply(GlobalOptions global)
    {
        var messageIdArgument = new Argument<string>("messageId") { Description = "Message ID to reply to" };
        var bodyOption = new Option<string>("--body") { Description = "Reply body text", Required = true };
        var allOption = new Option<bool>("--all") { Description = "Reply to all recipients" };

        var command = new Command("reply", "Reply to a message");
        command.Arguments.Add(messageIdArgument);
        command.Options.Add(bodyOption);
        command.Options.Add(allOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("mail reply", parseResult.GetValue(global.ReadOnly));

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            string messageId = parseResult.GetValue(messageIdArgument)!;
            string body = parseResult.GetValue(bodyOption)!;
            bool replyAll = parseResult.GetValue(allOption);

            await service.ReplyAsync(messageId, body, replyAll, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = replyAll ? "replied_all" : "replied", messageId }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine(replyAll ? "Reply-all sent." : "Reply sent.");
            }
        });

        return command;
    }

    // ── msgraph mail forward ──

    private static Command BuildForward(GlobalOptions global)
    {
        var messageIdArgument = new Argument<string>("messageId") { Description = "Message ID to forward" };
        var toOption = new Option<string>("--to") { Description = "Recipient email address(es), comma-separated", Required = true };
        var bodyOption = new Option<string?>("--body") { Description = "Optional comment to include" };

        var command = new Command("forward", "Forward a message");
        command.Arguments.Add(messageIdArgument);
        command.Options.Add(toOption);
        command.Options.Add(bodyOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("mail forward", parseResult.GetValue(global.ReadOnly));

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            string messageId = parseResult.GetValue(messageIdArgument)!;
            string toRaw = parseResult.GetValue(toOption)!;
            List<string> to = SplitAddresses(toRaw);
            string? body = parseResult.GetValue(bodyOption);

            await service.ForwardAsync(messageId, to, body, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "forwarded", messageId, to }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Forwarded to {string.Join(", ", to)}");
            }
        });

        return command;
    }

    // ── msgraph mail move ──

    private static Command BuildMove(GlobalOptions global)
    {
        var messageIdArgument = new Argument<string>("messageId") { Description = "Message ID to move" };
        var folderOption = new Option<string>("--folder") { Description = "Destination folder name or ID", Required = true };

        var command = new Command("move", "Move a message to another folder");
        command.Arguments.Add(messageIdArgument);
        command.Options.Add(folderOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("mail move", parseResult.GetValue(global.ReadOnly));

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            string messageId = parseResult.GetValue(messageIdArgument)!;
            string folder = parseResult.GetValue(folderOption)!;
            string folderId = ResolveFolderName(folder);

            MailMoveResult result = await service.MoveMessageAsync(messageId, folderId, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "moved", result.MessageId, destination = folderId }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine($"Moved to {folder}");
            }
        });

        return command;
    }

    // ── msgraph mail mark-read ──

    private static Command BuildMarkRead(GlobalOptions global)
    {
        var messageIdArgument = new Argument<string>("messageId") { Description = "Message ID" };

        var command = new Command("mark-read", "Mark a message as read");
        command.Arguments.Add(messageIdArgument);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("mail mark-read", parseResult.GetValue(global.ReadOnly));

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            string messageId = parseResult.GetValue(messageIdArgument)!;
            await service.SetReadStatusAsync(messageId, isRead: true, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "marked_read", messageId }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine("Marked as read.");
            }
        });

        return command;
    }

    // ── msgraph mail mark-unread ──

    private static Command BuildMarkUnread(GlobalOptions global)
    {
        var messageIdArgument = new Argument<string>("messageId") { Description = "Message ID" };

        var command = new Command("mark-unread", "Mark a message as unread");
        command.Arguments.Add(messageIdArgument);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            CommandGuard.EnforceReadOnly("mail mark-unread", parseResult.GetValue(global.ReadOnly));

            var (service, formatter) = CreateServiceContext(parseResult, global, readOnly: false);

            string messageId = parseResult.GetValue(messageIdArgument)!;
            await service.SetReadStatusAsync(messageId, isRead: false, cancellationToken);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                formatter.WriteResult(new { status = "marked_unread", messageId }, Console.Out);
            }
            else
            {
                Console.Error.WriteLine("Marked as unread.");
            }
        });

        return command;
    }

    // ── msgraph mail attachments ──

    private static Command BuildAttachments(GlobalOptions global)
    {
        var messageIdArgument = new Argument<string>("messageId") { Description = "Message ID" };
        var downloadOption = new Option<bool>("--download") { Description = "Download all attachments" };
        var outDirOption = new Option<string?>("--out-dir") { Description = "Output directory for downloads (default: current directory)" };

        var command = new Command("attachments", "List or download message attachments");
        command.Arguments.Add(messageIdArgument);
        command.Options.Add(downloadOption);
        command.Options.Add(outDirOption);

        SetGuardedAction(command, global, async (parseResult, cancellationToken) =>
        {
            var (service, formatter) = CreateServiceContext(parseResult, global);

            string messageId = parseResult.GetValue(messageIdArgument)!;
            bool download = parseResult.GetValue(downloadOption);
            string? outDir = parseResult.GetValue(outDirOption);

            IReadOnlyList<MailAttachmentInfo> attachments = await service.ListAttachmentsAsync(messageId, cancellationToken);

            if (download)
            {
                string outputDir = outDir ?? Directory.GetCurrentDirectory();
                Directory.CreateDirectory(outputDir);

                foreach (MailAttachmentInfo att in attachments)
                {
                    (byte[] content, string fileName, _) = await service.DownloadAttachmentAsync(messageId, att.Id, cancellationToken);
                    string filePath = Path.Combine(outputDir, fileName);
                    await File.WriteAllBytesAsync(filePath, content, cancellationToken);
                    Console.Error.WriteLine($"Downloaded: {filePath}");
                }

                bool isJson = parseResult.GetValue(global.Json);
                if (isJson)
                {
                    formatter.WriteResult(new { status = "downloaded", count = attachments.Count, outputDir }, Console.Out);
                }
            }
            else
            {
                bool isJson = parseResult.GetValue(global.Json);
                if (isJson)
                {
                    formatter.WriteResult(new { attachments }, Console.Out);
                }
                else if (formatter is TableOutputFormatter)
                {
                    TableOutputFormatter.WriteAttachmentTable(attachments);
                }
                else
                {
                    foreach (MailAttachmentInfo att in attachments)
                    {
                        Console.WriteLine($"{att.Name}\t{att.ContentType}\t{att.Size.ToString(CultureInfo.InvariantCulture)}");
                    }
                }
            }
        });

        return command;
    }

    private static List<string> SplitAddresses(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
