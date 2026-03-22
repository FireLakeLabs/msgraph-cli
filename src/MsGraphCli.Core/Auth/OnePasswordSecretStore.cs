using System.Diagnostics;
using System.Text;

namespace MsGraphCli.Core.Auth;

/// <summary>
/// Implements <see cref="ISecretStore"/> using the 1Password CLI (op).
/// All secrets are stored in a dedicated vault.
/// </summary>
public sealed class OnePasswordSecretStore : ISecretStore
{
    private readonly string _vault;
    private readonly TimeSpan _timeout;

    public OnePasswordSecretStore(string vault = "msgraph-cli", TimeSpan? timeout = null)
    {
        _vault = vault;
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var (exitCode, stdout, _) = await RunOpAsync(["whoami"], cancellationToken);
            return exitCode == 0 && !string.IsNullOrWhiteSpace(stdout);
        }
        catch
        {
            return false;
        }
    }

    public async Task EnsureVaultExistsAsync(CancellationToken cancellationToken = default)
    {
        var (exitCode, _, _) = await RunOpAsync(
            ["vault", "get", _vault, "--format", "json"], cancellationToken);

        if (exitCode != 0)
        {
            var (createExit, _, createErr) = await RunOpAsync(
                ["vault", "create", _vault], cancellationToken);

            if (createExit != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to create 1Password vault '{_vault}': {createErr}");
            }
        }
    }

    public async Task<string?> ReadFieldAsync(
        string itemName, string fieldName, CancellationToken cancellationToken = default)
    {
        string reference = $"op://{_vault}/{itemName}/{fieldName}";
        var (exitCode, stdout, stderr) = await RunOpAsync(["read", reference], cancellationToken);

        if (exitCode != 0)
        {
            if (IsNotFoundError(stderr))
            {
                return null;
            }

            throw new InvalidOperationException($"Failed to read '{reference}': {stderr}");
        }

        return stdout.TrimEnd('\n', '\r');
    }

    public async Task WriteFieldsAsync(
        string itemName, Dictionary<string, string> fields, CancellationToken cancellationToken = default)
    {
        bool exists = await ItemExistsAsync(itemName, cancellationToken);
        List<string> fieldArgs = fields.Select(kv => $"{kv.Key}={kv.Value}").ToList();

        if (exists)
        {
            List<string> args = ["item", "edit", itemName, "--vault", _vault, .. fieldArgs];
            var (exitCode, _, stderr) = await RunOpAsync(args, cancellationToken);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"Failed to update item '{itemName}': {stderr}");
            }
        }
        else
        {
            List<string> args = [
                "item", "create", "--category", "Login", "--title", itemName,
                "--vault", _vault, .. fieldArgs
            ];
            var (exitCode, _, stderr) = await RunOpAsync(args, cancellationToken);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"Failed to create item '{itemName}': {stderr}");
            }
        }
    }

    public async Task<string?> ReadNoteAsync(string itemName, CancellationToken cancellationToken = default)
    {
        var (exitCode, stdout, stderr) = await RunOpAsync(
            ["item", "get", itemName, "--vault", _vault, "--fields", "notesPlain", "--reveal"],
            cancellationToken);

        if (exitCode != 0)
        {
            if (IsNotFoundError(stderr))
            {
                return null;
            }

            throw new InvalidOperationException($"Failed to read note '{itemName}': {stderr}");
        }

        string result = stdout.TrimEnd('\n', '\r');
        return string.IsNullOrEmpty(result) ? null : result;
    }

    public async Task WriteNoteAsync(
        string itemName, string content, CancellationToken cancellationToken = default)
    {
        bool exists = await ItemExistsAsync(itemName, cancellationToken);

        if (exists)
        {
            var (exitCode, _, stderr) = await RunOpWithStdinAsync(
                ["item", "edit", itemName, "--vault", _vault, "notesPlain=-"],
                content, cancellationToken);

            if (exitCode != 0)
            {
                throw new InvalidOperationException($"Failed to update note '{itemName}': {stderr}");
            }
        }
        else
        {
            var (exitCode, _, stderr) = await RunOpWithStdinAsync(
                ["item", "create", "--category", "Secure Note", "--title", itemName,
                 "--vault", _vault, "notesPlain=-"],
                content, cancellationToken);

            if (exitCode != 0)
            {
                throw new InvalidOperationException($"Failed to create note '{itemName}': {stderr}");
            }
        }
    }

    public async Task<bool> ItemExistsAsync(string itemName, CancellationToken cancellationToken = default)
    {
        var (exitCode, _, _) = await RunOpAsync(
            ["item", "get", itemName, "--vault", _vault, "--format", "json"], cancellationToken);
        return exitCode == 0;
    }

    public async Task DeleteItemAsync(string itemName, CancellationToken cancellationToken = default)
    {
        await RunOpAsync(["item", "delete", itemName, "--vault", _vault], cancellationToken);
    }

    // ────────────────────────────────────────────────────────────
    // Internals
    // ────────────────────────────────────────────────────────────

    private static bool IsNotFoundError(string stderr) =>
        stderr.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
        stderr.Contains("could not be found", StringComparison.OrdinalIgnoreCase);

    private Task<(int ExitCode, string Stdout, string Stderr)> RunOpAsync(
        IEnumerable<string> arguments, CancellationToken cancellationToken) =>
        RunOpWithStdinAsync(arguments, stdin: null, cancellationToken);

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunOpWithStdinAsync(
        IEnumerable<string> arguments, string? stdin, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "op",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) stdoutBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) stderrBuilder.AppendLine(e.Data);
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start 'op' process.");
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                "The 1Password CLI (op) was not found on PATH. " +
                "Install it from: https://developer.1password.com/docs/cli/get-started/");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"1Password CLI timed out after {_timeout.TotalSeconds}s");
        }

        return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }
}
