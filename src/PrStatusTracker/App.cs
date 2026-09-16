using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using PrStatusTracker.Core;
using PrStatusTracker.GitHub;
using PrStatusTracker.Models;
using PrStatusTracker.Notifications;
using PrStatusTracker.Platform;
using PrStatusTracker.ViewModels;
using PrStatusTracker.Views;

namespace PrStatusTracker;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Name = "PR Status Tracker";
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Composition root — wired by hand, see ARCHITECTURE.md section 2.
        var settingsFile = new JsonFileStore<AppSettings>(AppPaths.Settings);
        var settings = settingsFile.Load(() => new AppSettings());
        var gh = new GhCommandRunner();
        var github = new GitHubClient(gh);
        var store = new PrStore(new JsonFileStore<List<TrackedPr>>(AppPaths.Snapshot));
        var notifications = new NotificationService(store, Notifier.ForCurrentPlatform(),
            new JsonFileStore<List<NotificationRecord>>(AppPaths.Notifications), () => settings);
        var poller = new PollingService(github, store, () => settings);
        var vm = new MainViewModel(store, poller, notifications, settings, settingsFile, AutoStart.ForCurrentPlatform(), gh);

        var appIcon = new WindowIcon(AssetLoader.Open(new Uri("avares://PrStatusTracker/Assets/github.png")));
        var window = new Window()
            .Title("PR Status Tracker")
            .Width(820)
            .Height(600)
            .MinWidth(520)
            .MinHeight(320)
            .Content(new MainView(vm));
        window.Icon = appIcon;
        window.Closing += (_, e) => { e.Cancel = true; window.Hide(); };

        void ShowWindow()
        {
            window.Show();
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
        }

        var compact = new CompactWindow(vm, ShowWindow) { Icon = appIcon };
        var tray = new TrayIconHost(this, vm, compact.Toggle, ShowWindow, () => desktop.Shutdown());

        desktop.Exit += (_, _) =>
        {
            tray.Dispose();
            poller.Dispose();
            notifications.Dispose();
        };

        if (!desktop.Args!.Contains(AutoStart.TrayArgument))
        {
            ShowWindow();
        }

        poller.Start();
    }
}
