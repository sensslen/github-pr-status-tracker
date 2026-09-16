namespace PrStatusTracker.Notifications;

/// <summary>What actually gets shown on screen. One poll produces at most one toast, however many PRs changed.</summary>
public sealed record Toast(string Title, string Body, string Url);

public interface INotifier
{
    Task ShowAsync(Toast toast, CancellationToken ct = default);
}

public static class Notifier
{
    public static INotifier ForCurrentPlatform() =>
        OperatingSystem.IsWindows() ? new WindowsNotifier()
        : OperatingSystem.IsMacOS() ? new MacNotifier()
        : new NullNotifier();
}

/// <summary>Used on platforms without a toast implementation; history is still recorded.</summary>
public sealed class NullNotifier : INotifier
{
    public Task ShowAsync(Toast toast, CancellationToken ct = default) => Task.CompletedTask;
}
