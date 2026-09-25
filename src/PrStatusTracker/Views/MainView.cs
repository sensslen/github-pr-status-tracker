using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media.Immutable;
using PrStatusTracker.Models;
using PrStatusTracker.ViewModels;

namespace PrStatusTracker.Views;

public sealed class MainView(MainViewModel vm) : ViewBase<MainViewModel>(vm)
{
    protected override object Build(MainViewModel vm) =>
        new DockPanel()
            .Children(
                new Border()
                    .DockPanel_Dock(Dock.Top)
                    .Background(Brushes.DarkOrange)
                    .Padding(12, 6)
                    .IsVisible(vm, x => x.HasBanner)
                    .Child(new TextBlock().Foreground(Brushes.White).TextWrapping(TextWrapping.Wrap).FontSize(12).Text(vm, x => x.Banner)),
                new Border()
                    .DockPanel_Dock(Dock.Top)
                    .Padding(12, 6)
                    .Child(new DockPanel().Children(
                        new Button().DockPanel_Dock(Dock.Right).Content("Refresh").FontSize(12).Padding(10, 4)
                            .IsEnabled(vm, x => !x.IsRefreshing)
                            .OnClick(e => { _ = vm.RefreshAsync(); }),
                        // Nobody goes looking in Settings for an update, so the offer is made where the app is read.
                        new Button().DockPanel_Dock(Dock.Right).Content(MainViewModel.UpdateActionLabel).FontSize(12).Padding(10, 4).Margin(0, 0, 8, 0)
                            .IsVisible(vm, x => x.UpdateAvailable)
                            .IsEnabled(vm, x => !x.IsInstallingUpdate)
                            .OnClick(e => { _ = vm.InstallUpdateAsync(); }),
                        new StackPanel().Orientation(Orientation.Horizontal).Spacing(10).VerticalAlignment(VerticalAlignment.Center).Children(
                            new TextBlock().FontSize(14).FontWeight(FontWeight.SemiBold).VerticalAlignment(VerticalAlignment.Center).Text(vm, x => x.Summary),
                            new TextBlock().FontSize(11).Opacity(0.6).VerticalAlignment(VerticalAlignment.Center).Text(vm, x => x.LastChecked)))),
                new TabControl
                {
                    Padding = new Thickness(0),
                    Items =
                {
                    BucketTab(vm, vm.NeedsYou),
                    BucketTab(vm, vm.WaitingOnOthers),
                    BucketTab(vm, vm.YourPrs),
                    new TabItem { Content = NotificationsView.Build(vm) }.Header(vm, x => x.NotificationsHeader),
                    new TabItem { Header = "Settings", Content = SettingsView.Build(vm) },
                }
                }
            );

    private static TabItem BucketTab(MainViewModel vm, PrGroupViewModel group) =>
        new TabItem { Content = PrListView.Build(vm, group) }.Header(group, g => g.Header);
}

/// <summary>Shared building blocks so the PR list and notification list stay visually consistent.</summary>
internal static class ViewHelpers
{
    public static readonly IBrush NeedsYouBrush = new ImmutableSolidColorBrush(Color.Parse("#DB6D28"));
    public static readonly IBrush ReadyBrush = new ImmutableSolidColorBrush(Color.Parse("#2DA44E"));
    public static readonly IBrush FailBrush = new ImmutableSolidColorBrush(Color.Parse("#CF222E"));
    public static readonly IBrush PendingBrush = new ImmutableSolidColorBrush(Color.Parse("#D4A72C"));
    public static readonly IBrush NeutralBrush = new ImmutableSolidColorBrush(Color.Parse("#6E7681"));
    public static readonly IBrush StarBrush = new ImmutableSolidColorBrush(Color.Parse("#E3B341"));
    public static readonly IBrush AccentBrush = new ImmutableSolidColorBrush(Color.Parse("#0969DA"));
    public static readonly IBrush CardBrush = new ImmutableSolidColorBrush(Color.Parse("#14808080"));
    public static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    public static FuncDataTemplate<T> Template<T>(Func<T, Control> build) => new((item, _) => build(item), supportsRecycling: false);

    /// <summary>The fallback colour for a row with no callout of its own. Your PRs hold every state at once,
    /// so that bucket gets a neutral accent and the rows inside it carry the colour.</summary>
    private static IBrush BucketBrush(PrBucket bucket) => bucket switch
    {
        PrBucket.NeedsYou => NeedsYouBrush,
        PrBucket.YourPrs => AccentBrush,
        _ => NeutralBrush,
    };

    /// <summary>
    /// Colour for a row's status pill. Within Your PRs the pill is the callout, so it answers what the PR wants
    /// rather than restating which tab you are on: green only when the merge button will actually work, red when
    /// something is broken, orange when someone is waiting on you, accent when the only thing outstanding is a
    /// conversation. Rows on the two review tabs carry no callout and fall back to their bucket's colour.
    /// </summary>
    public static IBrush PillBrush(PrItemViewModel item) => item.Callout switch
    {
        PrCallout.ReadyToMerge => ReadyBrush,
        PrCallout.ChecksFailing or PrCallout.MergeConflict => FailBrush,
        PrCallout.ChangesRequested or PrCallout.NoReviewers => NeedsYouBrush,
        PrCallout.NewComments => AccentBrush,
        _ => BucketBrush(item.Status.Bucket()),
    };

    /// <summary>
    /// Flags unresolved comments left since you last opened the PR; clicking the row clears it. Whether it is
    /// wanted is the caller's to say, because a row whose pill already reads "New comments" does not need the
    /// same thing said twice. Always built, and collapsed out of the layout when it is not wanted, so the row
    /// stays a single declarative expression like every other row here.
    /// </summary>
    public static TextBlock CommentMarker(PrItemViewModel item, bool visible)
    {
        var label = new TextBlock().Text("💬").FontSize(11).FontWeight(FontWeight.SemiBold)
            .Foreground(AccentBrush).VerticalAlignment(VerticalAlignment.Center)
            .IsVisible(visible);
        if (visible)
        {
            ToolTip.SetTip(label, item.NewCommentsTip);
        }

        return label;
    }

    /// <summary>
    /// The star toggle on a main-window row. Filled and gold when starred, a dim outline otherwise — always
    /// present, so the rows keep a straight left edge whether or not anything is starred.
    /// </summary>
    public static Button StarToggle(MainViewModel vm, PrItemViewModel item)
    {
        var button = new Button()
            .Content(new TextBlock().Text(item.IsStarred ? "★" : "☆").FontSize(14)
                .Foreground(item.IsStarred ? StarBrush : NeutralBrush)
                .Opacity(item.IsStarred ? 1 : 0.55))
            .Background(Brushes.Transparent).BorderThickness(0).Padding(2, 0).Cursor(HandCursor)
            .VerticalAlignment(VerticalAlignment.Center)
            .OnClick(e => { e.Handled = true; vm.ToggleStar(item); });
        ToolTip.SetTip(button, item.IsStarred ? "Starred — click to unstar" : "Star this PR to keep it at the top");
        return button;
    }

    public static Border Pill(string text, IBrush background) =>
        new Border()
            .Background(background)
            .CornerRadius(9)
            .Padding(7, 1)
            .VerticalAlignment(VerticalAlignment.Center)
            .Child(new TextBlock().Text(text).FontSize(10.5).Foreground(Brushes.White));

    /// <summary>
    /// Small CI badge. The glyph carries the meaning (✓ passing, ✗ failing, … running, – none) so it reads
    /// without colour; the colour is only a secondary cue.
    /// </summary>
    public static Control CheckBadge(CheckState? checks)
    {
        var (glyph, brush, tip) = checks switch
        {
            CheckState.Success => ("✓ CI", ReadyBrush, "Checks passing"),
            CheckState.Failure => ("✗ CI", FailBrush, "Checks failing"),
            CheckState.Pending => ("… CI", PendingBrush, "Checks running"),
            _ => ("– CI", NeutralBrush, "No checks"),
        };
        var badge = new TextBlock().Text(glyph).FontSize(10.5).FontWeight(FontWeight.SemiBold).Foreground(brush)
            .VerticalAlignment(VerticalAlignment.Center).MinWidth(30)
            .Opacity(checks is null ? 0.5 : 1);
        ToolTip.SetTip(badge, tip);
        return badge;
    }

    /// <summary>
    /// A whole-row clickable card. Button.Click bubbles, so a nested control's click (e.g. Dismiss) also arrives
    /// here; only react when the row itself was the source.
    /// </summary>
    public static Button Row(Control content, Action open)
    {
        var row = new Button()
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .HorizontalContentAlignment(HorizontalAlignment.Stretch)
            .Background(CardBrush)
            .BorderThickness(0)
            .CornerRadius(6)
            .Padding(8, 5)
            .Margin(0, 0, 0, 3)
            .Cursor(HandCursor)
            .Content(content);
        row.Click += (_, e) => { if (ReferenceEquals(e.Source, row)) { open(); } };
        return row;
    }

    /// <summary>
    /// The placeholder shown in place of rows. Bound rather than fixed, because the wording turns on whether the
    /// tab is empty or merely narrowed to nothing.
    /// </summary>
    public static TextBlock EmptyLabel(PrGroupViewModel group) =>
        new TextBlock().Text(group, g => g.EmptyText).FontSize(12).Opacity(0.5).FontStyle(FontStyle.Italic);

    /// <summary>The PR's age; bold, coloured and flagged once it has been open for more than a day.</summary>
    public static TextBlock AgeLabel(PrItemViewModel item)
    {
        if (!item.IsOverdue)
        {
            return Meta(item.Age);
        }

        var label = new TextBlock().Text($"⏳ {item.Age}").FontSize(11).FontWeight(FontWeight.Bold).Foreground(NeedsYouBrush)
            .VerticalAlignment(VerticalAlignment.Center);
        ToolTip.SetTip(label, "Open for more than a day");
        return label;
    }

    public static TextBlock Meta(string text) =>
        new TextBlock().Text(text).FontSize(11).Opacity(0.7).VerticalAlignment(VerticalAlignment.Center)
            .TextTrimming(TextTrimming.CharacterEllipsis);

    public static TextBlock RowTitle(string text) =>
        new TextBlock().Text(text).FontSize(13).FontWeight(FontWeight.Medium).VerticalAlignment(VerticalAlignment.Center)
            .TextTrimming(TextTrimming.CharacterEllipsis);

    public static Button SmallButton(string text, Action onClick) =>
        new Button().Content(text).FontSize(11).Padding(6, 1).OnClick(e => { e.Handled = true; onClick(); });

    /// <summary>Text that looks like a link and acts like a button.</summary>
    public static Button LinkText(string text, Action onClick) =>
        new Button().Content(new TextBlock().Text(text).TextDecorations(TextDecorations.Underline).TextWrapping(TextWrapping.Wrap).FontSize(12))
            .Background(Brushes.Transparent).BorderThickness(0).Padding(0).Cursor(HandCursor)
            .HorizontalAlignment(HorizontalAlignment.Left)
            .OnClick(_ => onClick());
}
