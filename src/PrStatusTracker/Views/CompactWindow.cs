using PrStatusTracker.Models;
using PrStatusTracker.ViewModels;
using static PrStatusTracker.Views.ViewHelpers;

namespace PrStatusTracker.Views;

/// <summary>
/// The at-a-glance popup shown from the tray icon: fixed size, no chrome, pinned next to the tray, and it
/// disappears as soon as it loses focus. A segmented control switches between the three buckets.
/// </summary>
public sealed class CompactWindow : Window
{
    private const double PopupWidth = 380;
    private const double PopupHeight = 440;
    private const long ToggleDebounceMs = 300;
    private long _hiddenAt;

    private readonly Dictionary<PrBucket, (Button Segment, Control List)> _buckets = [];
    private PrBucket _selected = PrBucket.NeedsYou;

    public CompactWindow(MainViewModel vm, Action openMainWindow)
    {
        Width = PopupWidth;
        Height = PopupHeight;
        CanResize = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        Title = "PR Status Tracker";

        var groups = new[] { vm.NeedsYou, vm.WaitingOnOthers, vm.YourPrs };
        var lists = new Panel();
        var segments = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,*,*") };
        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            var list = BucketList(vm, group);
            var segment = new Button()
                .HorizontalAlignment(HorizontalAlignment.Stretch).HorizontalContentAlignment(HorizontalAlignment.Center)
                .FontSize(11).Padding(4, 3).CornerRadius(4).BorderThickness(0).Cursor(HandCursor)
                .Content(new TextBlock().Text(group, g => g.ShortHeader).TextTrimming(TextTrimming.CharacterEllipsis))
                .Grid_Column(i)
                .OnClick(_ => Select(group.Bucket));
            _buckets[group.Bucket] = (segment, list);
            lists.Children.Add(list);
            segments.Children.Add(segment);
        }

        Content = new Border()
            .BorderBrush(NeutralBrush)
            .BorderThickness(1)
            .Child(new DockPanel().Children(
                new Border().DockPanel_Dock(Dock.Top).Padding(12, 10, 12, 6).Child(
                    new StackPanel().Spacing(6).Children(
                        new TextBlock().FontSize(11).Opacity(0.6).Text(vm, x => x.LastChecked),
                        new Border().Background(CardBrush).CornerRadius(6).Padding(3).Child(segments))),
                new Border().DockPanel_Dock(Dock.Bottom).Padding(12, 8).Child(
                    new DockPanel().Children(
                        SmallButton("Refresh", () => _ = vm.RefreshAsync()).DockPanel_Dock(Dock.Right),
                        new Button().Content("Open main window").FontSize(12).Padding(10, 3)
                            .OnClick(_ => { Hide(); openMainWindow(); }))),
                lists));

        Select(_selected);
        Deactivated += (_, _) => { _hiddenAt = Environment.TickCount64; Hide(); };
    }

    private void Select(PrBucket bucket)
    {
        _selected = bucket;
        foreach (var (b, (segment, list)) in _buckets)
        {
            var on = b == bucket;
            list.IsVisible = on;
            segment.Background = on ? BucketBrush(b) : Brushes.Transparent;
            // A local null would override the theme's foreground and hide the text; clear the value instead.
            if (on)
            {
                segment.Foreground = Brushes.White;
            }
            else
            {
                segment.ClearValue(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty);
            }

            segment.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
        }
    }

    private static Panel BucketList(MainViewModel vm, PrGroupViewModel group) =>
        new Panel().Children(
            new ScrollViewer().Padding(12, 0).Content(
                new ItemsControl().Margin(0, 2, 0, 6).ItemsSource(group.Items).ItemTemplate(Template<PrItemViewModel>(item => CompactRow(vm, item)))),
            EmptyLabel(group.EmptyText).Margin(12, 8).IsVisible(group, g => g.IsEmpty));

    /// <summary>Single line: [CI badge] [★ if starred] [⚠ if conflicted] [💬 if unread] title … age.</summary>
    private static Button CompactRow(MainViewModel vm, PrItemViewModel item) =>
        Row(
            new DockPanel().Children(
                CheckBadge(item.Checks).DockPanel_Dock(Dock.Left).Margin(0, 0, 8, 0),
                StarMarker(item).DockPanel_Dock(Dock.Left).Margin(0, 0, 6, 0),
                ConflictMarker(item).DockPanel_Dock(Dock.Left).Margin(0, 0, 6, 0),
                // The popup has no room for a pill, so the glyph is the only place comments can show.
                CommentMarker(item, item.HasNewComments).DockPanel_Dock(Dock.Left).Margin(0, 0, 6, 0),
                AgeLabel(item).DockPanel_Dock(Dock.Right).Margin(8, 0, 0, 0),
                new TextBlock().Text(item.Title).FontSize(12).VerticalAlignment(VerticalAlignment.Center)
                    .TextTrimming(TextTrimming.CharacterEllipsis)),
            () => vm.OpenPr(item));

    /// <summary>
    /// Clicking the tray icon deactivates the popup (which hides it) before the click itself arrives, so a click
    /// meant to close it would otherwise re-open it. A Toggle right after a focus-loss hide is treated as that close.
    /// </summary>
    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
        }
        else if (Environment.TickCount64 - _hiddenAt > ToggleDebounceMs)
        {
            ShowNearTray();
        }
    }

    /// <summary>Show pinned to the tray corner: bottom-right on Windows, top-right on macOS.</summary>
    private void ShowNearTray()
    {
        var screen = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
        if (screen is not null)
        {
            var area = screen.WorkingArea;
            var scale = screen.Scaling;
            int w = (int)(PopupWidth * scale), h = (int)(PopupHeight * scale), margin = (int)(12 * scale);
            Position = OperatingSystem.IsMacOS()
                ? new PixelPoint(area.Right - w - margin, area.Y + margin)
                : new PixelPoint(area.Right - w - margin, area.Bottom - h - margin);
        }
        Show();
        Activate();
    }
}
