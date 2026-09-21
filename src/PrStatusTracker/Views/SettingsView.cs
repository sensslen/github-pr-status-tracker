using PrStatusTracker.Models;
using PrStatusTracker.ViewModels;

namespace PrStatusTracker.Views;

internal static class SettingsView
{
    public static Control Build(MainViewModel vm) =>
        new ScrollViewer().Content(
            new StackPanel()
                .Margin(16)
                .Spacing(14)
                .MaxWidth(520)
                .HorizontalAlignment(HorizontalAlignment.Left)
                .Children(
                    Section("Polling",
                        new StackPanel().Orientation(Orientation.Horizontal).Spacing(8).Children(
                            new TextBlock().Text("Check GitHub every").VerticalAlignment(VerticalAlignment.Center),
                            new NumericUpDown().Minimum(AppSettings.MinPollIntervalMinutes).Maximum(60).Increment(1).FormatString("0").Width(110)
                                .Value(vm, x => x.PollIntervalMinutes, BindingMode.TwoWay),
                            new TextBlock().Text("minutes").VerticalAlignment(VerticalAlignment.Center))),
                    Section("Startup",
                        new CheckBox().Content("Start at login (hidden in tray)")
                            .IsEnabled(vm.AutoStartSupported)
                            .IsChecked(vm, x => x.StartAtLogin, BindingMode.TwoWay)),
                    Section("Notify me when a PR…",
                        new StackPanel().Spacing(4).Children(
                            new CheckBox().Content("needs my review (requested personally, via a team, or again)")
                                .IsChecked(vm, x => x.NotifyNeedsYou, BindingMode.TwoWay),
                            new CheckBox().Content("I have already reviewed and it changed")
                                .IsChecked(vm, x => x.NotifyWaitingOnOthers, BindingMode.TwoWay),
                            new CheckBox().Content("is mine and changed (ready to merge, CI failing, changes requested…)")
                                .IsChecked(vm, x => x.NotifyYourPrs, BindingMode.TwoWay),
                            new CheckBox().Content("is merged or closed")
                                .IsChecked(vm, x => x.NotifyClosed, BindingMode.TwoWay))),
                    Section("GitHub CLI",
                        new StackPanel().Spacing(6).Children(
                            new TextBlock().TextWrapping(TextWrapping.Wrap).FontFamily(new FontFamily("Consolas, Menlo, monospace")).FontSize(12)
                                .Text(vm, x => x.GhPath),
                            new Button().Content("Re-check").HorizontalAlignment(HorizontalAlignment.Left).OnClick(_ => vm.RecheckGh()))),
                    Section("Data",
                        new StackPanel().Spacing(2).Children(
                            new TextBlock().FontSize(12).Opacity(0.7).Text("Settings, snapshot and notification history live in"),
                            ViewHelpers.LinkText(MainViewModel.DataDirectory, MainViewModel.OpenDataFolder)))));

    private static StackPanel Section(string title, Control body) =>
        new StackPanel().Spacing(6).Children(
            new TextBlock().Text(title).FontWeight(FontWeight.SemiBold),
            body);
}
