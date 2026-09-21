using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PrStatusTracker.Core;

namespace PrStatusTracker.ViewModels;

/// <summary>
/// One filter bar: the free text, the dropdown selections, and the choices those dropdowns offer. A tab either
/// owns one of these or shares it with another tab — nothing here is global, so narrowing one list never
/// silently narrows a list you are not looking at.
/// </summary>
public sealed partial class PrFilterViewModel(bool withAuthor = true) : ObservableObject
{
    public const string AnyAuthor = "Any author";
    public const string AnyLabel = "Any label";
    public const string AnyRepo = "Any repo";

    private bool _rebuildingOptions;

    /// <summary>
    /// Whether an author dropdown is offered. A tab whose PRs are all by one person has nothing to choose
    /// between, and because the dimension is simply absent from <see cref="Current"/> there is no hidden
    /// author filter for the missing control to have left behind.
    /// </summary>
    public bool HasAuthor { get; } = withAuthor;

    /// <summary>Dropdown choices, rebuilt from the rows this bar filters; first entry is the "any" sentinel.</summary>
    public ObservableCollection<string> Authors { get; } = [AnyAuthor];
    public ObservableCollection<string> Labels { get; } = [AnyLabel];
    public ObservableCollection<string> Repos { get; } = [AnyRepo];

    [ObservableProperty] public partial string Text { get; set; } = string.Empty;
    [ObservableProperty] public partial string? Author { get; set; } = AnyAuthor;
    [ObservableProperty] public partial string? Label { get; set; } = AnyLabel;
    [ObservableProperty] public partial string? Repo { get; set; } = AnyRepo;

    /// <summary>Drives the Clear button, so it only appears on a bar that is actually narrowing something.</summary>
    [ObservableProperty] public partial bool IsActive { get; set; }

    /// <summary>Raised when the user changed something; a rebuild of the dropdown choices is not a user change.</summary>
    public event Action? Changed;

    public PrFilter Current => new(
        Text,
        HasAuthor && Author is not (null or AnyAuthor) ? Author : null,
        Label is not (null or AnyLabel) ? Label : null,
        Repo is not (null or AnyRepo) ? Repo : null);

    public void Clear()
    {
        Text = string.Empty;
        Author = AnyAuthor;
        Label = AnyLabel;
        Repo = AnyRepo;
    }

    partial void OnTextChanged(string value) => OnFilterChanged();
    partial void OnAuthorChanged(string? value) => OnFilterChanged();
    partial void OnLabelChanged(string? value) => OnFilterChanged();
    partial void OnRepoChanged(string? value) => OnFilterChanged();

    private void OnFilterChanged()
    {
        if (_rebuildingOptions)
        {
            return; // a ComboBox resetting while its items change is not a user choice
        }

        IsActive = Current.IsActive;
        Changed?.Invoke();
    }

    /// <summary>
    /// Keep each dropdown's choices in step with the rows this bar filters, without disturbing a still-valid
    /// selection. A selection that no longer exists falls back to "any", which can deactivate the bar.
    /// </summary>
    public void RebuildOptions(IEnumerable<PrItemViewModel> items)
    {
        var rows = items as IReadOnlyCollection<PrItemViewModel> ?? [.. items];
        _rebuildingOptions = true;
        try
        {
            Author = Sync(Authors, AnyAuthor, rows.Select(i => i.Pr.Author), Author);
            Label = Sync(Labels, AnyLabel, rows.SelectMany(i => i.Pr.Labels), Label);
            Repo = Sync(Repos, AnyRepo, rows.Select(i => i.Pr.Repo), Repo);
        }
        finally { _rebuildingOptions = false; }

        IsActive = Current.IsActive;
    }

    private static string Sync(ObservableCollection<string> target, string any, IEnumerable<string> values, string? current)
    {
        var wanted = values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).Prepend(any).ToList();
        if (!wanted.SequenceEqual(target))
        {
            target.Clear();
            foreach (var v in wanted)
            {
                target.Add(v);
            }
        }
        return current is not null && wanted.Contains(current) ? current : any;
    }
}
