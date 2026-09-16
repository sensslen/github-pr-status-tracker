using System.Diagnostics;
using System.Text;

namespace PrStatusTracker.GitHub;

public enum GhFailureKind { NotInstalled, NotAuthenticated, RateLimited, Network, Other }

public sealed class GhException(GhFailureKind kind, string message) : Exception(message)
{
    public GhFailureKind Kind { get; } = kind;

    public string UserMessage => Kind switch
    {
        GhFailureKind.NotInstalled => "GitHub CLI (gh) was not found. Install it from https://cli.github.com and restart.",
        GhFailureKind.NotAuthenticated => "GitHub CLI is not signed in. Run `gh auth login` in a terminal.",
        GhFailureKind.RateLimited => "GitHub API rate limit reached. Polling will resume automatically.",
        GhFailureKind.Network => "Could not reach GitHub. Check your network connection.",
        _ => Message,
    };
}

public sealed record GhResult(int ExitCode, string StdOut, string StdErr);

/// <summary>Thin wrapper around the `gh` executable. Never uses a shell; arguments are passed verbatim.</summary>
public sealed class GhCommandRunner
{
    private static readonly TimeSpan s_defaultTimeout = TimeSpan.FromSeconds(60);

    public string? ResolvedPath { get => field ??= ResolveExecutable(); private set; }

    /// <summary>Forget the cached location so the next <see cref="ResolvedPath"/> read looks again, e.g. after the user installs gh.</summary>
    public void Invalidate() => ResolvedPath = null;

    public async Task<GhResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        var exe = ResolvedPath ?? throw new GhException(GhFailureKind.NotInstalled, "gh executable not found");

        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // Keep gh from ever prompting or paging.
        psi.Environment["GH_PROMPT_DISABLED"] = "1";
        psi.Environment["GH_PAGER"] = "cat";
        psi.Environment["NO_COLOR"] = "1";

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            ResolvedPath = null;
            throw new GhException(GhFailureKind.NotInstalled, $"Failed to start gh: {ex.Message}");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(s_defaultTimeout);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            try { await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false); } catch { /* pipes closed by Kill */ }
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            throw new GhException(GhFailureKind.Network, "gh timed out");
        }

        var result = new GhResult(process.ExitCode, await stdoutTask, await stderrTask);
        if (result.ExitCode != 0)
        {
            throw Classify(result);
        }

        return result;
    }

    private static GhException Classify(GhResult result)
    {
        var text = (result.StdErr + "\n" + result.StdOut);
        var lower = text.ToLowerInvariant();
        // gh exits 4 when authentication is required; everything else has to be read from the message.
        var kind =
            result.ExitCode == 4 || lower.Contains("gh auth login") || lower.Contains("not logged in") || lower.Contains("authentication") || lower.Contains("http 401")
                ? GhFailureKind.NotAuthenticated
            : lower.Contains("rate limit") || lower.Contains("rate_limited") || lower.Contains("http 403") || lower.Contains("http 429")
                ? GhFailureKind.RateLimited
            : lower.Contains("dial tcp") || lower.Contains("no such host") || lower.Contains("timeout") || lower.Contains("connection refused") || lower.Contains("certificate")
                ? GhFailureKind.Network
            : GhFailureKind.Other;
        return new GhException(kind, $"gh exited with {result.ExitCode}: {text.Trim()}");
    }

    private static string? ResolveExecutable()
    {
        var isWindows = OperatingSystem.IsWindows();
        var name = isWindows ? "gh.exe" : "gh";

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir.Trim('"'), name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string[] wellKnown = isWindows
            ? [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "GitHub CLI", "gh.exe"),
              ]
            : ["/opt/homebrew/bin/gh", "/usr/local/bin/gh", "/usr/bin/gh"];
        return wellKnown.FirstOrDefault(File.Exists);
    }
}
