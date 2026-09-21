using PrStatusTracker.Models;
using PrStatusTracker.ViewModels;
using static PrStatusTracker.Views.ViewHelpers;

namespace PrStatusTracker.Views;

internal static class PrListView
{
    /// <summary>One bucket tab: the filter bar, then what the rows are calling for, then the rows.</summary>
    public static Control Build(MainViewModel vm, PrGroupViewModel group) =>
        new DockPanel().Children(
            FilterBar(group.Filter).DockPanel_Dock(Dock.Top),
            TeamOnlyToggle(vm, group).DockPanel_Dock(Dock.Top),
            AttentionStrip(group).DockPanel_Dock(Dock.Top),
            new Panel().Children(
                new ScrollViewer().Content(
                    new ItemsControl().Margin(10, 8).ItemsSource(group.Items).ItemTemplate(Template<PrItemViewModel>(item => BuildRow(vm, item)))),
                EmptyLabel(group).Margin(10, 8).IsVisible(group, g => g.IsEmpty)));

    /// <summary>
    /// The Needs you tab's own line under the bar: drop the PRs you are only being asked for because a team you
    /// belong to was. Gated on the group the same way the author dropdown is gated on the bar, so a tab that
    /// could never hold such a row does not offer the control — and the line stays free for the attention strip
    /// on the one tab that has one.
    /// </summary>
    private static Border TeamOnlyToggle(MainViewModel vm, PrGroupViewModel group) =>
        Strip(new CheckBox().FontSize(11.5).MinHeight(0)
                .ToolTip_Tip("PRs where only a team you belong to was asked, not you personally")
                .IsChecked(vm, x => x.HideTeamOnlyRequests, BindingMode.TwoWay)
                .Content(group, g => g.HideTeamOnlyLabel))
            .IsVisible(group.CanHideTeamOnly);

    /// <summary>
    /// What the rows below want — `2 ready to merge · 1 CI failing · 1 with new comments`. Collapses to nothing
    /// on the review tabs, whose rows never carry a callout, so they look exactly as they did.
    /// </summary>
    private static Border AttentionStrip(PrGroupViewModel group) =>
        Strip(new TextBlock().FontSize(11.5).FontWeight(FontWeight.Medium).Foreground(AccentBrush)
                .TextTrimming(TextTrimming.CharacterEllipsis)
                .Text(group, g => g.Attention))
            .IsVisible(group, g => g.HasAttention);

    /// <summary>
    /// Free-text box plus label / repo dropdowns, and author where there is more than one to choose from.
    /// Bound to the tab's own filter, so what a bar narrows is exactly the list sitting under it.
    /// </summary>
    private static Border FilterBar(PrFilterViewModel filter) =>
        Strip(
            new DockPanel().Children(
                new Button().DockPanel_Dock(Dock.Right).Content("Clear").FontSize(11).Padding(8, 3).Margin(6, 0, 0, 0)
                    .IsVisible(filter, f => f.IsActive)
                    .OnClick(_ => filter.Clear()),
                FilterCombo(filter.Repos).SelectedItem(filter, f => f.Repo, BindingMode.TwoWay).DockPanel_Dock(Dock.Right),
                FilterCombo(filter.Labels).SelectedItem(filter, f => f.Label, BindingMode.TwoWay).DockPanel_Dock(Dock.Right),
                FilterCombo(filter.Authors).SelectedItem(filter, f => f.Author, BindingMode.TwoWay).DockPanel_Dock(Dock.Right)
                    .IsVisible(filter.HasAuthor),
                // The free-text box still matches author everywhere, so the placeholder says so everywhere:
                // it is only the dropdown, which would offer one choice, that Your PRs does without.
                new TextBox().FontSize(12).MinHeight(28).Padding(8, 4)
                    .PlaceholderText("Filter by title, repo, author, label or #number")
                    .Text(filter, f => f.Text, BindingMode.TwoWay)));

    /// <summary>One of the full-width lines stacked above the rows; they share their inset so they line up.</summary>
    private static Border Strip(Control child) => new Border().Padding(10, 8, 10, 0).Child(child);

    private static ComboBox FilterCombo(System.Collections.IEnumerable items) =>
        new ComboBox().ItemsSource(items).FontSize(12).MinWidth(130).MaxWidth(200).MinHeight(28).Padding(8, 2).Margin(6, 0, 0, 0)
            .VerticalAlignment(VerticalAlignment.Center);

    /// <summary>Two dense lines: [star] [CI badge] [status pill] [💬] title … age / details.</summary>
    private static Button BuildRow(MainViewModel vm, PrItemViewModel item) =>
        Row(
            new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,Auto,Auto,*,Auto"), RowDefinitions = RowDefinitions.Parse("Auto,Auto") }
                .Children(
                    StarToggle(vm, item).Grid_Row(0).Grid_RowSpan(2).Grid_Column(0).Margin(0, 0, 6, 0),
                    CheckBadge(item.Checks).Grid_Row(0).Grid_Column(1).Margin(0, 0, 8, 0),
                    Pill(item.StatusLabel, PillBrush(item)).Grid_Row(0).Grid_Column(2).Margin(0, 0, 8, 0),
                    // Only when the pill is saying something else; otherwise it would repeat it.
                    CommentMarker(item, item.HasNewComments && item.Callout != PrCallout.NewComments)
                        .Grid_Row(0).Grid_Column(3).Margin(0, 0, 8, 0),
                    RowTitle(item.Title).Grid_Row(0).Grid_Column(4),
                    AgeLabel(item).Grid_Row(0).Grid_Column(5).Margin(8, 0, 0, 0),
                    Meta(item.Details).Grid_Row(1).Grid_Column(4).Grid_ColumnSpan(2).Margin(0, 1, 0, 0)),
            () => vm.OpenPr(item));
}
