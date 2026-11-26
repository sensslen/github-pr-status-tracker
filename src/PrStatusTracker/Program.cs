namespace PrStatusTracker;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Single instance: a second launch just exits. The first instance is already in the tray.
        using var mutex = new Mutex(initiallyOwned: true, Core.UpdateService.SingleInstanceMutex, out var isFirstInstance);
        if (!isFirstInstance)
        {
            return 0;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
