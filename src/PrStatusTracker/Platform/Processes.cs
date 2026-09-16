using System.Diagnostics;

namespace PrStatusTracker.Platform;

public static class Processes
{
    /// <summary>Run a helper executable with no window and no shell, and wait for it to exit.</summary>
    public static async Task RunHiddenAsync(string exe, IEnumerable<string> args, CancellationToken ct = default)
    {
        using var process = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true });
        if (process is not null)
        {
            await process.WaitForExitAsync(ct);
        }
    }
}
