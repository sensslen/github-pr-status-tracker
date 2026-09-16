using System.Diagnostics;

namespace PrStatusTracker.Platform;

public static class Browser
{
    public const string AllPullRequestsUrl = "https://github.com/pulls";

    public static void Open(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        ShellOpen(uri.ToString());
    }

    /// <summary>Reveal a directory in Explorer / Finder, creating it first so the shell has something to show.</summary>
    public static void OpenFolder(string path)
    {
        try { Directory.CreateDirectory(path); }
        catch (Exception ex) { Debug.WriteLine($"Could not create {path}: {ex.Message}"); return; }
        ShellOpen(path);
    }

    private static void ShellOpen(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open {target}: {ex.Message}");
        }
    }
}
