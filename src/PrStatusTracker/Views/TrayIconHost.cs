using Avalonia.Platform;
using PrStatusTracker.Platform;
using PrStatusTracker.ViewModels;

namespace PrStatusTracker.Views;

/// <summary>
/// Owns the tray icon: tooltip summary, icon variant, context menu. Left-click opens the main window;
/// the right-click menu is Avalonia's native menu (the TrayIcon API exposes no right-click event).
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private readonly TrayIcon _icon;
    private readonly MainViewModel _vm;
    private readonly Dictionary<TrayState, WindowIcon> _icons = [];
    private TrayState? _shown;

    public TrayIconHost(Application app, MainViewModel vm, Action showMainWindow, Action quit)
    {
        _vm = vm;

        var menu = new NativeMenu();
        menu.Items.Add(MenuItem("Open main window", showMainWindow));
        menu.Items.Add(MenuItem("Refresh now", () => _ = vm.RefreshAsync()));
        menu.Items.Add(MenuItem("Open GitHub pull requests", () => Browser.Open(Browser.AllPullRequestsUrl)));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(MenuItem("Quit", quit));

        _icon = new TrayIcon { Menu = menu };
        _icon.Clicked += (_, _) => showMainWindow();
        TrayIcon.SetIcons(app, [_icon]);

        // The view model only raises these on the UI thread.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Summary) or nameof(MainViewModel.Banner) or nameof(MainViewModel.TrayState))
            {
                Refresh();
            }
        };
        Refresh();
    }

    private void Refresh()
    {
        var tooltip = $"PR Status Tracker — {(_vm.HasBanner ? _vm.BannerSummary : _vm.Summary)}";
        if (_icon.ToolTipText != tooltip)
        {
            _icon.ToolTipText = tooltip;
        }

        if (_vm.TrayState == _shown)
        {
            return;
        }

        _shown = _vm.TrayState;
        if (!_icons.TryGetValue(_vm.TrayState, out var icon))
        {
            var name = _vm.TrayState switch
            {
                TrayState.Error => "error",
                TrayState.NeedsYou => "needs-you",
                TrayState.Ready => "ready",
                _ => "idle",
            };
            _icons[_vm.TrayState] = icon = new WindowIcon(AssetLoader.Open(new Uri($"avares://PrStatusTracker/Assets/tray-{name}.png")));
        }
        _icon.Icon = icon;
    }

    private static NativeMenuItem MenuItem(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    public void Dispose() => _icon.Dispose();
}
