using System.CommandLine;

namespace FireLakeLabs.MsGraphCli.Commands;

public static class CompletionsCommands
{
    public static Command Build()
    {
        var completionsCommand = new Command("completions", "Generate shell completion scripts");

        completionsCommand.Subcommands.Add(BuildBash());
        completionsCommand.Subcommands.Add(BuildZsh());
        completionsCommand.Subcommands.Add(BuildFish());

        return completionsCommand;
    }

    private static Command BuildBash()
    {
        var command = new Command("bash", "Generate bash completion script");

        command.SetAction(_ =>
        {
            Console.Write(BashCompletionScript);
        });

        return command;
    }

    private static Command BuildZsh()
    {
        var command = new Command("zsh", "Generate zsh completion script");

        command.SetAction(_ =>
        {
            Console.Write(ZshCompletionScript);
        });

        return command;
    }

    private static Command BuildFish()
    {
        var command = new Command("fish", "Generate fish completion script");

        command.SetAction(_ =>
        {
            Console.Write(FishCompletionScript);
        });

        return command;
    }

    private const string BashCompletionScript = """
        # bash completion for msgraph
        # Usage: eval "$(msgraph completions bash)" or add to ~/.bashrc

        _msgraph_completions() {
            local cur="${COMP_WORDS[*]}"
            local suggestions
            suggestions=$(msgraph "[suggest]" "$COMP_POINT" -- $cur 2>/dev/null)
            COMPREPLY=($(compgen -W "$suggestions" -- "${COMP_WORDS[$COMP_CWORD]}"))
        }

        complete -F _msgraph_completions msgraph
        """;

    private const string ZshCompletionScript = """
        # zsh completion for msgraph
        # Usage: eval "$(msgraph completions zsh)" or add to ~/.zshrc

        _msgraph_completions() {
            local -a completions
            local cur="${words[*]}"
            local pos=$((CURSOR + 1))
            completions=(${(f)"$(msgraph "[suggest]" "$pos" -- $cur 2>/dev/null)"})
            compadd -a completions
        }

        compdef _msgraph_completions msgraph
        """;

    private const string FishCompletionScript = """
        # fish completion for msgraph
        # Usage: msgraph completions fish | source
        # Or save to ~/.config/fish/completions/msgraph.fish

        complete -c msgraph -f -a '(commandline -cp | string join " " | xargs -I{} msgraph "[suggest]" (commandline -C) -- {} 2>/dev/null)'
        """;
}
