using System.Diagnostics;
using System.Text;

namespace MsalOnePasswordSpike;

/// <summary>
/// Wraps the 1Password CLI (op) for reading and writing secrets.
/// All secrets are stored in a dedicated vault.
/// </summary>
public sealed class OnePasswordStore
{
    private readonly string _vault;
    private readonly TimeSpan _timeout;

    public OnePasswordStore(string vault = "msgraph-cli", TimeSpan? timeout = null)
    {
        _vault = vault;
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
    }

    /// <summary>
    /// Verify that the op CLI is available and the user is signed in.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var (exitCode, stdout, _) = await RunOpAsync(["whoami"], ct);
            return exitCode == 0 && !string.IsNullOrWhiteSpace(stdout);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ensure the vault exists, creating it if necessary.
    /// </summary>
    public async Task EnsureVaultExistsAsync(CancellationToken ct = default)
    {
        var (exitCode, _, _) = await RunOpAsync(["vault", "get", _vault, "--format", "json"], ct);
        if (exitCode != 0)
        {
            Console.Error.WriteLine($"[1Password] Creating vault '{_vault}'...");
            var (createExit, _, createErr) = await RunOpAsync(["vault", "create", _vault], ct);
            if (createExit != 0)
                throw new InvalidOperationException($"Failed to create 1Password vault '{_vault}': {createErr}");
        }
    }

    /// <summary>
    /// Read a field from a 1Password item using op:// reference syntax.
    /// Returns null if the item or field doesn't exist.
    /// </summary>
    public async Task<string?> ReadFieldAsync(string itemName, string fieldName, CancellationToken ct = default)
    {
        string reference = $"op://{_vault}/{itemName}/{fieldName}";
        var (exitCode, stdout, stderr) = await RunOpAsync(["read", reference], ct);

        if (exitCode != 0)
        {
            // "not found" is expected on first run
            if (stderr.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                stderr.Contains("could not be found", StringComparison.OrdinalIgnoreCase))
                return null;

            throw new InvalidOperationException($"Failed to read '{reference}': {stderr}");
        }

        return stdout.TrimEnd('\n', '\r');
    }

    /// <summary>
    /// Read the notesPlain field from a Secure Note item.
    /// Returns null if the item doesn't exist.
    /// </summary>
    public async Task<string?> ReadNoteAsync(string itemName, CancellationToken ct = default)
    {
        var (exitCode, stdout, stderr) = await RunOpAsync(
            ["item", "get", itemName, "--vault", _vault, "--fields", "notesPlain", "--reveal"], ct);

        if (exitCode != 0)
        {
            if (stderr.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                stderr.Contains("could not be found", StringComparison.OrdinalIgnoreCase))
                return null;

            throw new InvalidOperationException($"Failed to read note '{itemName}': {stderr}");
        }

        string result = stdout.TrimEnd('\n', '\r');
        return string.IsNullOrEmpty(result) ? null : result;
    }

    /// <summary>
    /// Check if an item exists in the vault.
    /// </summary>
    public async Task<bool> ItemExistsAsync(string itemName, CancellationToken ct = default)
    {
        var (exitCode, _, _) = await RunOpAsync(
            ["item", "get", itemName, "--vault", _vault, "--format", "json"], ct);
        return exitCode == 0;
    }

    /// <summary>
    /// Create or update a Secure Note with content in the notesPlain field.
    /// </summary>
    public async Task WriteNoteAsync(string itemName, string content, CancellationToken ct = default)
    {
        bool exists = await ItemExistsAsync(itemName, ct);

        if (exists)
        {
            // Update existing item. We use stdin to avoid shell escaping issues with large blobs.
            var (exitCode, _, stderr) = await RunOpWithStdinAsync(
                ["item", "edit", itemName, "--vault", _vault, $"notesPlain=-"],
                content, ct);

            if (exitCode != 0)
                throw new InvalidOperationException($"Failed to update note '{itemName}': {stderr}");
        }
        else
        {
            // Create new Secure Note. Use stdin for the content.
            var (exitCode, _, stderr) = await RunOpWithStdinAsync(
                ["item", "create", "--category", "Secure Note", "--title", itemName,
                 "--vault", _vault, $"notesPlain=-"],
                content, ct);

            if (exitCode != 0)
                throw new InvalidOperationException($"Failed to create note '{itemName}': {stderr}");
        }
    }

    /// <summary>
    /// Create or update a Login item with specific fields.
    /// </summary>
    public async Task WriteFieldsAsync(string itemName, Dictionary<string, string> fields, CancellationToken ct = default)
    {
        bool exists = await ItemExistsAsync(itemName, ct);

        // Build field assignment args: "fieldName=value"
        List<string> fieldArgs = fields.Select(kv => $"{kv.Key}={kv.Value}").ToList();

        if (exists)
        {
            List<string> args = ["item", "edit", itemName, "--vault", _vault, .. fieldArgs];
            var (exitCode, _, stderr) = await RunOpAsync(args, ct);
            if (exitCode != 0)
                throw new InvalidOperationException($"Failed to update item '{itemName}': {stderr}");
        }
        else
        {
            List<string> args = ["item", "create", "--category", "Login", "--title", itemName,
                                  "--vault", _vault, .. fieldArgs];
            var (exitCode, _, stderr) = await RunOpAsync(args, ct);
            if (exitCode != 0)
                throw new InvalidOperationException($"Failed to create item '{itemName}': {stderr}");
        }
    }

    /// <summary>
    /// Delete an item from the vault. No-op if it doesn't exist.
    /// </summary>
    public async Task DeleteItemAsync(string itemName, CancellationToken ct = default)
    {
        var (exitCode, _, _) = await RunOpAsync(
            ["item", "delete", itemName, "--vault", _vault], ct);
        // Ignore errors (item may not exist)
    }

    // ────────────────────────────────────────────────────────────
    // Process helpers
    // ────────────────────────────────────────────────────────────

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunOpAsync(
        IEnumerable<string> arguments, CancellationToken ct)
    {
        return await RunOpWithStdinAsync(arguments, stdin: null, ct);
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunOpWithStdinAsync(
        IEnumerable<string> arguments, string? stdin, CancellationToken ct)
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
            psi.ArgumentList.Add(arg);

        // Ensure op doesn't prompt for interactive input
        psi.Environment["OP_FORMAT"] = "json";

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
                throw new InvalidOperationException("Failed to start 'op' process. Is 1Password CLI installed?");
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

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"1Password CLI timed out after {_timeout.TotalSeconds}s");
        }

        return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }
}
