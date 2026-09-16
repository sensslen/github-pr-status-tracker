using PrStatusTracker.Models;
using PrStatusTracker.ViewModels;
using static PrStatusTracker.Views.ViewHelpers;

namespace PrStatusTracker.Views;

internal static class PrListView
{
    /// <summary>One bucket tab: the filter bar (for buckets long enough to need one) over the rows.</summary>
    public static Control Build(MainViewModel vm, PrGroupViewModel group)
    {
        var list = new Panel().Children(
            new ScrollViewer().Content(
                new ItemsControl().Margin(10, 8).ItemsSource(group.Items).ItemTemplate(Template<PrItemViewModel>(item => BuildRow(item)))),
            EmptyLabel(group.EmptyText).Margin(10, 8).IsVisible(group, g => g.IsEmpty));

        return group.Filterable
            ? new DockPanel().Children(FilterBar(vm).DockPanel_Dock(Dock.Top), list)
            : list;
    }

    /// <summary>Free-text box plus author / label / repo dropdowns. All filterable tabs share one filter state.</summary>
    private static Border FilterBar(MainViewModel vm) =>
        new Border().Padding(10, 8, 10, 0).Child(
            new DockPanel().Children(
                new Button().DockPanel_Dock(Dock.Right).Content("Clear").FontSize(11).Padding(8, 3).Margin(6, 0, 0, 0)
                    .IsVisible(vm, x => x.IsFiltering)
                    .OnClick(_ => vm.ClearFilter()),
                FilterCombo(vm.Repos).SelectedItem(vm, x => x.FilterRepo, BindingMode.TwoWay).DockPanel_Dock(Dock.Right),
                FilterCombo(vm.Labels).SelectedItem(vm, x => x.FilterLabel, BindingMode.TwoWay).DockPanel_Dock(Dock.Right),
                FilterCombo(vm.Authors).SelectedItem(vm, x => x.FilterAuthor, BindingMode.TwoWay).DockPanel_Dock(Dock.Right),
                new TextBox().FontSize(12).MinHeight(28).Padding(8, 4)
                    .PlaceholderText("Filter by title, repo, author, label or #number")
                    .Text(vm, x => x.FilterText, BindingMode.TwoWay)));

    private static ComboBox FilterCombo(System.Collections.IEnumerable items) =>
        new ComboBox().ItemsSource(items).FontSize(12).MinWidth(130).MaxWidth(200).MinHeight(28).Padding(8, 2).Margin(6, 0, 0, 0)
            .VerticalAlignment(VerticalAlignment.Center);

    /// <summary>Two dense lines: [CI badge] [status pill] title … age / details.</summary>
    private static Button BuildRow(PrItemViewModel item) =>
        Row(
            new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,*,Auto"), RowDefinitions = RowDefinitions.Parse("Auto,Auto") }
                .Children(
                    CheckBadge(item.Checks).Grid_Row(0).Grid_Column(0).Margin(0, 0, 8, 0),
                    Pill(item.StatusLabel, PillBrush(item.Status)).Grid_Row(0).Grid_Column(1).Margin(0, 0, 8, 0),
                    RowTitle(item.Title).Grid_Row(0).Grid_Column(2),
                    AgeLabel(item).Grid_Row(0).Grid_Column(3).Margin(8, 0, 0, 0),
                    Meta(item.Details).Grid_Row(1).Grid_Column(2).Grid_ColumnSpan(2).Margin(0, 1, 0, 0)),
            () => MainViewModel.OpenPr(item));
}
