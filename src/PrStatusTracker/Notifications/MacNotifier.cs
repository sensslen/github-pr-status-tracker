using PrStatusTracker.Platform;

namespace PrStatusTracker.Notifications;

/// <summary>
/// macOS notification via `osascript`. This is the fallback path described in ARCHITECTURE.md: it works from an
/// unsigned dev build, but the notification is attributed to Script Editor and clicking it does not open the PR.
/// The UNUserNotificationCenter path needs a signed .app bundle and is left for the packaging phase.
/// </summary>
public sealed class MacNotifier : INotifier
{
    public Task ShowAsync(Toast toast, CancellationToken ct = default) =>
        Processes.RunHiddenAsync("/usr/bin/osascript",
            ["-e", $"display notification \"{Escape(toast.Body)}\" with title \"{Escape(toast.Title)}\""], ct);

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
