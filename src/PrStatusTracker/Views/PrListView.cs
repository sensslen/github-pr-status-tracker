using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Styling;
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
                    new ItemsControl().Margin(10, 8).ItemsSource(group.Repositories)
                        .ItemTemplate(Template<PrRepositoryViewModel>(repository => RepositorySection(vm, repository)))),
                EmptyLabel(group).Margin(10, 8).IsVisible(group, g => g.IsEmpty)));

    private static Expander RepositorySection(MainViewModel vm, PrRepositoryViewModel repository) =>
        new Expander
        {
            Resources =
            {
                ["ExpanderHeaderPadding"] = new Thickness(0),
                ["ExpanderHeaderBorderThickness"] = new Thickness(0),
                ["ExpanderHeaderBackground"] = Brushes.Transparent,
                ["ExpanderContentDownBorderThickness"] = new Thickness(0),
            },
            Styles =
            {
                new Style(s => s.OfType<Expander>().Template().OfType<ToggleButton>().Template().OfType<Border>().Name("ExpandCollapseChevronBorder"))
                {
                    Setters = { new Setter(Visual.IsVisibleProperty, false) },
                },
            },
        }
        .HorizontalAlignment(HorizontalAlignment.Stretch).HorizontalContentAlignment(HorizontalAlignment.Stretch)
        .MinHeight(26).Padding(0).BorderThickness(0).Background(Brushes.Transparent).Margin(0, 0, 0, 4)
        .IsExpanded(repository, r => r.IsExpanded, BindingMode.TwoWay)
        .Header(repository.Name)
        .HeaderTemplate(Template<string>(name =>
            new StackPanel().Orientation(Orientation.Horizontal).Spacing(6).Children(
                new PathIcon().Width(10).Height(10).VerticalAlignment(VerticalAlignment.Center)
                    .Data(repository, r => r.IsExpanded, converter: new FuncValueConverter<bool, Geometry>(expanded =>
                        Geometry.Parse(expanded
                            ? "M0,2 L5,7 L10,2 L9,1 L5,5 L1,1 Z"
                            : "M2,0 L7,5 L2,10 L1,9 L5,5 L1,1 Z"))),
                new TextBlock().Text(name).FontSize(13).FontWeight(FontWeight.SemiBold)
                    .VerticalAlignment(VerticalAlignment.Center),
                new TextBlock().FontSize(12).Opacity(0.6).VerticalAlignment(VerticalAlignment.Center)
                    .Text(repository, r => r.Items, converter: new FuncValueConverter<IReadOnlyList<PrItemViewModel>, string>(
                        items => items is null ? string.Empty : items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))))))
        .Content(new ItemsControl().Margin(0, 2, 0, 0).ItemsSource(repository, r => r.Items)
            .ItemTemplate(Template<PrItemViewModel>(item => BuildRow(vm, item))));

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
                    .PlaceholderText("Filter by title, repo, author, label, branch or #number")
                    .Text(filter, f => f.Text, BindingMode.TwoWay)));

    /// <summary>One of the full-width lines stacked above the rows; they share their inset so they line up.</summary>
    private static Border Strip(Control child) => new Border().Padding(10, 8, 10, 0).Child(child);

    private static ComboBox FilterCombo(System.Collections.IEnumerable items) =>
        new ComboBox().ItemsSource(items).FontSize(12).MinWidth(130).MaxWidth(200).MinHeight(28).Padding(8, 2).Margin(6, 0, 0, 0)
            .VerticalAlignment(VerticalAlignment.Center);

    /// <summary>
    /// A card in two lines. The first says what the PR is and where it stands: star, status pill, title, age. The
    /// second carries the facts you scan for: number, author, the branch it merges into, CI, reviews, size, last
    /// activity, then labels. Labels share that line rather than taking one of their own, so a card stays two
    /// lines tall unless the window is too narrow for its facts.
    /// </summary>
    private static Button BuildRow(MainViewModel vm, PrItemViewModel item) =>
        Row(
            new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto") }
                .Children(
                    StarToggle(vm, item).Grid_Column(0).VerticalAlignment(VerticalAlignment.Top).Margin(0, 0, 8, 0),
                    new StackPanel().Grid_Column(1).Spacing(3).Children(
                        new DockPanel().Children(
                            Pill(item.StatusLabel, PillBrush(item))
                                .ToolTip_Tip(item.Callout == PrCallout.OpenThreads ? item.OpenThreadsTip : null)
                                .DockPanel_Dock(Dock.Left).Margin(0, 0, 8, 0),
                            // Only when the pill is saying something else; otherwise it would repeat it.
                            CommentMarker(item, item.HasOpenThreads && item.Callout != PrCallout.OpenThreads)
                                .DockPanel_Dock(Dock.Left).Margin(0, 0, 8, 0),
                            RowTitle(item.Title)),
                        FactsLine(item)),
                    AgeLabel(item).Grid_Column(2).VerticalAlignment(VerticalAlignment.Top).Margin(12, 2, 0, 0)),
            () => MainViewModel.OpenPr(item),
            item.Tone switch
            {
                RowTone.Good => GoodCardBrush,
                RowTone.Attention => AttentionCardBrush,
                _ => null,
            },
            // A thin edge in the full colour, so the tint still reads where the background is hard to tell apart.
            item.Tone switch
            {
                RowTone.Good => ReadyBrush,
                RowTone.Attention => NeedsYouBrush,
                _ => null,
            });

    private static WrapPanel FactsLine(PrItemViewModel item)
    {
        var line = new WrapPanel().Orientation(Orientation.Horizontal);
        void Add(Control fact) => line.Children.Add(fact.Margin(0, 1, 12, 1));

        Add(new TextBlock().Text(item.Number).FontSize(12).FontWeight(FontWeight.SemiBold).Opacity(0.85)
            .VerticalAlignment(VerticalAlignment.Center));
        Add(Meta(item.Author));
        if (item.HasTargetBranch)
        {
            Add(BranchChip(item));
        }

        Add(CheckBadge(item.Checks));
        Add(ReviewChip("Reviews", item.Human, item.Reviews, item.HumanTip));
        if (item.HasRabbit)
        {
            Add(ReviewChip("CodeRabbit", item.Rabbit, string.Empty, item.RabbitTip));
        }

        if (item.HasConflicts)
        {
            Add(new TextBlock().Text("conflicts").FontSize(12).FontWeight(FontWeight.SemiBold).Foreground(FailBrush)
                .VerticalAlignment(VerticalAlignment.Center));
        }

        Add(Meta(item.Size));
        Add(Meta(item.Updated));
        foreach (var label in item.Labels)
        {
            line.Children.Add(new Border().Background(ChipBrush).CornerRadius(10).Padding(8, 1).Margin(0, 1, 6, 1)
                .VerticalAlignment(VerticalAlignment.Center)
                .Child(new TextBlock().Text(label).FontSize(11.5).Opacity(0.85)));
        }

        return line;
    }

    /// <summary>
    /// A verdict chip: the people reviewing, or CodeRabbit on its own so the human tally only ever counts people.
    /// The glyph carries the meaning, as on the CI badge; the colour is a second cue.
    /// </summary>
    private static Border ReviewChip(string who, ReviewVerdict verdict, string tally, string tip)
    {
        var (glyph, brush) = verdict switch
        {
            ReviewVerdict.Approved => ("✓", ReadyBrush),
            ReviewVerdict.ChangesRequested => ("✗", NeedsYouBrush),
            ReviewVerdict.Pending => ("…", PendingBrush),
            ReviewVerdict.Commented => ("💬", NeutralBrush),
            _ => ("–", NeutralBrush),
        };
        var content = new StackPanel().Orientation(Orientation.Horizontal).Spacing(4).Children(
            new TextBlock().Text(glyph).FontSize(12).FontWeight(FontWeight.Bold).Foreground(brush)
                .VerticalAlignment(VerticalAlignment.Center),
            new TextBlock().Text(who).FontSize(12).FontWeight(FontWeight.SemiBold).Opacity(0.9)
                .VerticalAlignment(VerticalAlignment.Center));
        if (tally.Length > 0)
        {
            content.Children.Add(new TextBlock().Text(tally).FontSize(12).Opacity(0.7).VerticalAlignment(VerticalAlignment.Center));
        }

        var chip = new Border().Background(ChipBrush).CornerRadius(4).Padding(6, 1).VerticalAlignment(VerticalAlignment.Center)
            .Opacity(verdict == ReviewVerdict.None ? 0.6 : 1)
            .Child(content);
        ToolTip.SetTip(chip, tip);
        return chip;
    }

    /// <summary>
    /// `→ main`: the branch the PR merges into. Tinted when that is not the repository's default branch, since a
    /// PR into a release branch or onto another PR is the one you do not want to merge by reflex.
    /// </summary>
    private static Border BranchChip(PrItemViewModel item)
    {
        var text = new TextBlock().Text($"→ {item.TargetBranch}").FontSize(12).FontFamily(MonoFont)
            .TextTrimming(TextTrimming.CharacterEllipsis).MaxWidth(280).VerticalAlignment(VerticalAlignment.Center);
        if (item.TargetsNonDefaultBranch)
        {
            text.FontWeight(FontWeight.SemiBold).Foreground(NeedsYouBrush);
        }
        else
        {
            text.Opacity(0.85);
        }

        var chip = new Border()
            .Background(item.TargetsNonDefaultBranch ? WarnChipBrush : ChipBrush)
            .CornerRadius(4).Padding(6, 1).VerticalAlignment(VerticalAlignment.Center)
            .Child(text);
        ToolTip.SetTip(chip, item.BranchTip);
        return chip;
    }
}
