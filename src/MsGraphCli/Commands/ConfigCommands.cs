using System.CommandLine;
using MsGraphCli.Core.Config;
using MsGraphCli.Output;

namespace MsGraphCli.Commands;

public static class ConfigCommands
{
    public static Command Build(GlobalOptions global)
    {
        var configCommand = new Command("config", "View and modify configuration");

        configCommand.Subcommands.Add(BuildPath());
        configCommand.Subcommands.Add(BuildList(global));
        configCommand.Subcommands.Add(BuildGet(global));
        configCommand.Subcommands.Add(BuildSet(global));

        return configCommand;
    }

    // ── msgraph config path ──

    private static Command BuildPath()
    {
        var command = new Command("path", "Show the configuration file path");

        command.SetAction(_ =>
        {
            Console.WriteLine(ConfigLoader.GetConfigPath());
        });

        return command;
    }

    // ── msgraph config list ──

    private static Command BuildList(GlobalOptions global)
    {
        var command = new Command("list", "Show all configuration values");

        command.SetAction(parseResult =>
        {
            AppConfig config = ConfigLoader.Load();
            bool isJson = parseResult.GetValue(global.Json);
            bool isPlain = parseResult.GetValue(global.Plain);
            IOutputFormatter formatter = OutputFormatResolver.Resolve(isJson, isPlain);

            IReadOnlyDictionary<string, string> keys = ConfigLoader.GetKnownKeys();
            var entries = new Dictionary<string, string?>();
            foreach (string key in keys.Keys)
            {
                entries[key] = ConfigLoader.GetValue(config, key);
            }

            if (isJson || isPlain)
            {
                formatter.WriteResult(entries, Console.Out);
            }
            else
            {
                foreach (KeyValuePair<string, string?> entry in entries)
                {
                    Console.WriteLine($"{entry.Key}\t{entry.Value ?? "(not set)"}");
                }
            }
        });

        return command;
    }

    // ── msgraph config get <key> ──

    private static Command BuildGet(GlobalOptions global)
    {
        var keyArgument = new Argument<string>("key") { Description = "Configuration key name" };

        var command = new Command("get", "Get a configuration value");
        command.Arguments.Add(keyArgument);

        command.SetAction(parseResult =>
        {
            string key = parseResult.GetValue(keyArgument)!;

            IReadOnlyDictionary<string, string> knownKeys = ConfigLoader.GetKnownKeys();
            if (!knownKeys.ContainsKey(key))
            {
                Console.Error.WriteLine($"Unknown config key: {key}");
                Console.Error.WriteLine($"Valid keys: {string.Join(", ", knownKeys.Keys)}");
                Environment.ExitCode = 1;
                return;
            }

            AppConfig config = ConfigLoader.Load();
            string? value = ConfigLoader.GetValue(config, key);

            bool isJson = parseResult.GetValue(global.Json);
            if (isJson)
            {
                IOutputFormatter formatter = OutputFormatResolver.Resolve(isJson, false);
                formatter.WriteResult(new Dictionary<string, string?> { [key] = value }, Console.Out);
            }
            else
            {
                Console.WriteLine(value ?? "");
            }
        });

        return command;
    }

    // ── msgraph config set <key> <value> ──

    private static Command BuildSet(GlobalOptions global)
    {
        var keyArgument = new Argument<string>("key") { Description = "Configuration key name" };
        var valueArgument = new Argument<string>("value") { Description = "Value to set" };

        var command = new Command("set", "Set a configuration value");
        command.Arguments.Add(keyArgument);
        command.Arguments.Add(valueArgument);

        command.SetAction(parseResult =>
        {
            string key = parseResult.GetValue(keyArgument)!;
            string value = parseResult.GetValue(valueArgument)!;

            IReadOnlyDictionary<string, string> knownKeys = ConfigLoader.GetKnownKeys();
            if (!knownKeys.ContainsKey(key))
            {
                Console.Error.WriteLine($"Unknown config key: {key}");
                Console.Error.WriteLine($"Valid keys: {string.Join(", ", knownKeys.Keys)}");
                Environment.ExitCode = 1;
                return;
            }

            AppConfig config = ConfigLoader.LoadFromDisk();

            try
            {
                ConfigLoader.SetValue(config, key, value);
            }
            catch (FormatException)
            {
                Console.Error.WriteLine($"Invalid value for {key}: {value}");
                Environment.ExitCode = 1;
                return;
            }

            ConfigLoader.Save(config);

            bool isJson = parseResult.GetValue(global.Json);
            if (!isJson)
            {
                Console.Error.WriteLine($"Set {key} = {value}");
            }
        });

        return command;
    }
}
