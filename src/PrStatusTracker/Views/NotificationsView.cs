using PrStatusTracker.Models;
using PrStatusTracker.ViewModels;
using static PrStatusTracker.Views.ViewHelpers;

namespace PrStatusTracker.Views;

internal static class NotificationsView
{
    public static Control Build(MainViewModel vm) =>
        new DockPanel()
            .Margin(10, 8)
            .Children(
                new DockPanel().DockPanel_Dock(Dock.Top).Margin(0, 0, 0, 6).Children(
                    SmallButton("Dismiss all", vm.DismissAll).DockPanel_Dock(Dock.Right),
                    new CheckBox().Content("Show dismissed").FontSize(12).IsChecked(vm, x => x.ShowDismissed, BindingMode.TwoWay)),
                new ScrollViewer().Content(
                    new ItemsControl()
                        .ItemsSource(vm.Notifications)
                        .ItemTemplate(Template<NotificationRecord>(r => BuildRow(vm, r)))));

    private static Button BuildRow(MainViewModel vm, NotificationRecord record) =>
        Row(
            new DockPanel().Opacity(record.Dismissed ? 0.5 : 1).Children(
                new StackPanel().DockPanel_Dock(Dock.Right).Orientation(Orientation.Horizontal).Spacing(8)
                    .VerticalAlignment(VerticalAlignment.Center).Margin(8, 0, 0, 0).Children(
                        Meta(record.Timestamp.LocalDateTime.ToString("g")),
                        SmallButton("Dismiss", () => vm.Dismiss(record)).IsVisible(!record.Dismissed)),
                new StackPanel().Spacing(1).Children(
                    RowTitle(record.Title),
                    Meta(record.Body))),
            () => MainViewModel.OpenNotification(record));
}
