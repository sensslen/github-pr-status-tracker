using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PrStatusTracker.Core;
using PrStatusTracker.GitHub;
using PrStatusTracker.Models;
using PrStatusTracker.Notifications;
using PrStatusTracker.Platform;

namespace PrStatusTracker.ViewModels;

public sealed class PrItemViewModel(TrackedPr tracked, bool isStarred)
{
    public PullRequest Pr { get; } = tracked.Pr;
    public string Title { get; } = tracked.Pr.Title;
    public string Url { get; } = tracked.Pr.Url;
    public PrStatus Status { get; } = tracked.Status;
    public string StatusLabel { get; } = tracked.Status.Label();
    public string Age { get; } = FormatAge(tracked.Pr.CreatedAt);

    /// <summary>Open for more than a day: the age gets emphasised so long-waiting PRs stand out.</summary>
    public bool IsOverdue { get; } = DateTimeOffset.Now - tracked.Pr.CreatedAt > TimeSpan.FromHours(24);
    public CheckState? Checks { get; } = tracked.Pr.Checks;

    /// <summary>The branch will not merge as it stands. Worth flagging even where the status pill does not say so.</summary>
    public bool HasConflict { get; } = IsConflicted(tracked.Pr);

    /// <summary>One dense line: repo, author, size, review tallies, labels, last activity.</summary>
    public string Details { get; } = BuildDetails(tracked.Pr);

    /// <summary>Marked by the user as one they are working through; starred rows sort to the top of their bucket.</summary>
    public bool IsStarred { get; } = isStarred;

    private static string BuildDetails(PullRequest pr)
    {
        var approvals = pr.LatestReviews.Count(r => r.State == ReviewState.Approved);
        var changes = pr.LatestReviews.Count(r => r.State == ReviewState.ChangesRequested);

        var parts = new List<string> { $"{pr.Repo}#{pr.Number}", pr.Author, $"+{pr.Additions} −{pr.Deletions}" };
        if (approvals > 0)
        {
            parts.Add($"✓ {approvals}");
        }

        if (changes > 0)
        {
            parts.Add($"✗ {changes}");
        }

        if (pr.RequestedReviewers.Count > 0)
        {
            parts.Add($"{pr.RequestedReviewers.Count} awaiting");
        }

        if (IsConflicted(pr))
        {
            parts.Add("conflicts");
        }

        if (pr.Labels.Count > 0)
        {
            parts.Add("🏷 " + string.Join(", ", pr.Labels));
        }

        parts.Add($"updated {FormatAge(pr.UpdatedAt)} ago");
        return string.Join(" · ", parts);
    }

    private static bool IsConflicted(PullRequest pr) => pr.Mergeable == Mergeable.Conflicting;

    internal static string FormatAge(DateTimeOffset when)
    {
        var span = DateTimeOffset.Now - when;
        return span.TotalMinutes < 60 ? $"{Math.Max(1, (int)span.TotalMinutes)}m"
            : span.TotalHours < 24 ? $"{(int)span.TotalHours}h"
            : $"{(int)span.TotalDays}d";
    }
}

/// <summary>One bucket's rows plus the derived bits the views bind to (tab header, empty state).</summary>
public sealed partial class PrGroupViewModel(PrBucket bucket) : ObservableObject
{
    public PrBucket Bucket { get; } = bucket;
    public string EmptyText { get; } = bucket.EmptyText();
    public ObservableCollection<PrItemViewModel> Items { get; } = [];

    /// <summary>Ready-to-merge is a short list you act on, not one you sift; only the long buckets get the filter bar.</summary>
    public bool Filterable { get; } = bucket != PrBucket.ReadyToMerge;

    /// <summary>Rows in this bucket before filtering; what the tray and summary count.</summary>
    public int TotalCount { get; private set; }

    [ObservableProperty] public partial string Header { get; set; } = bucket.Label();
    [ObservableProperty] public partial string ShortHeader { get; set; } = bucket.ShortLabel();
    [ObservableProperty] public partial bool IsEmpty { get; set; } = true;

    public void Replace(IReadOnlyList<PrItemViewModel> all, PrFilter filter)
    {
        TotalCount = all.Count;
        Items.Clear();
        foreach (var item in all)
        {
            if (filter.Matches(item.Pr))
            {
                Items.Add(item);
            }
        }

        IsEmpty = Items.Count == 0;
        Header = filter.IsActive && Items.Count != TotalCount
            ? $"{Bucket.Label()} ({Items.Count} of {TotalCount})"
            : MainViewModel.WithCount(Bucket.Label(), TotalCount);
        ShortHeader = MainViewModel.WithCount(Bucket.ShortLabel(), TotalCount);
    }
}

/// <summary>What the tray icon should look like, in priority order.</summary>
public enum TrayState { Idle, Ready, NeedsYou, Error }

public sealed partial class MainViewModel : ObservableObject
{
    public const string AnyAuthor = "Any author";
    public const string AnyLabel = "Any label";
    public const string AnyRepo = "Any repo";

    private readonly PrStore _store;
    private readonly StarStore _stars;
    private readonly PollingService _poller;
    private readonly NotificationService _notifications;
    private readonly AppSettings _settings;
    private readonly JsonFileStore<AppSettings> _settingsFile;
    private readonly IAutoStart _autoStart;
    private readonly GhCommandRunner _gh;
    private readonly PrGroupViewModel[] _groups;
    private bool _startAtLogin;
    private bool _rebuildingOptions;
    private string? _localMessage; // shown in the banner when there is no poll error

    public MainViewModel(PrStore store, StarStore stars, PollingService poller, NotificationService notifications,
        AppSettings settings, JsonFileStore<AppSettings> settingsFile, IAutoStart autoStart, GhCommandRunner gh)
    {
        _store = store;
        _stars = stars;
        _poller = poller;
        _notifications = notifications;
        _settings = settings;
        _settingsFile = settingsFile;
        _autoStart = autoStart;
        _gh = gh;
        _startAtLogin = autoStart.IsEnabled();
        _groups = [NeedsYou, WaitingOnOthers, ReadyToMerge];

        store.Updated += () => Dispatcher.UIThread.Post(RefreshGroups);
        stars.Changed += () => Dispatcher.UIThread.Post(RefreshGroups);
        poller.StateChanged += () => Dispatcher.UIThread.Post(RefreshPollerState);
        notifications.HistoryChanged += () => Dispatcher.UIThread.Post(RefreshNotifications);

        RefreshGroups();
        RefreshPollerState();
        RefreshNotifications();
        ResolveGhPath();
    }

    public PrGroupViewModel NeedsYou { get; } = new(PrBucket.NeedsYou);
    public PrGroupViewModel WaitingOnOthers { get; } = new(PrBucket.WaitingOnOthers);
    public PrGroupViewModel ReadyToMerge { get; } = new(PrBucket.ReadyToMerge);
    public ObservableCollection<NotificationRecord> Notifications { get; } = [];
    public static string DataDirectory => AppPaths.DataDirectory;
    public bool AutoStartSupported => _autoStart.IsSupported;

    [ObservableProperty] public partial string Summary { get; set; } = "Loading…";
    [ObservableProperty] public partial TrayState TrayState { get; set; }
    [ObservableProperty] public partial bool IsRefreshing { get; set; }
    [ObservableProperty] public partial string LastChecked { get; set; } = string.Empty;
    [ObservableProperty] public partial bool ShowDismissed { get; set; }
    [ObservableProperty] public partial string NotificationsHeader { get; set; } = "Notifications";
    [ObservableProperty] public partial string GhPath { get; set; } = "checking…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBanner))]
    public partial string Banner { get; set; } = string.Empty;

    public bool HasBanner => Banner.Length > 0;

    // --- filtering -------------------------------------------------------------------------------------------

    /// <summary>Dropdown choices, rebuilt from the snapshot; the first entry is always the "any" sentinel.</summary>
    public ObservableCollection<string> Authors { get; } = [AnyAuthor];
    public ObservableCollection<string> Labels { get; } = [AnyLabel];
    public ObservableCollection<string> Repos { get; } = [AnyRepo];

    [ObservableProperty] public partial string FilterText { get; set; } = string.Empty;
    [ObservableProperty] public partial string? FilterAuthor { get; set; } = AnyAuthor;
    [ObservableProperty] public partial string? FilterLabel { get; set; } = AnyLabel;
    [ObservableProperty] public partial string? FilterRepo { get; set; } = AnyRepo;
    [ObservableProperty] public partial bool IsFiltering { get; set; }

    public PrFilter Filter => new(
        FilterText,
        FilterAuthor is null or AnyAuthor ? null : FilterAuthor,
        FilterLabel is null or AnyLabel ? null : FilterLabel,
        FilterRepo is null or AnyRepo ? null : FilterRepo);

    public void ClearFilter()
    {
        FilterText = string.Empty;
        FilterAuthor = AnyAuthor;
        FilterLabel = AnyLabel;
        FilterRepo = AnyRepo;
    }

    partial void OnFilterTextChanged(string value) => OnFilterChanged();
    partial void OnFilterAuthorChanged(string? value) => OnFilterChanged();
    partial void OnFilterLabelChanged(string? value) => OnFilterChanged();
    partial void OnFilterRepoChanged(string? value) => OnFilterChanged();

    private void OnFilterChanged()
    {
        if (_rebuildingOptions)
        {
            return; // a ComboBox resetting while its items change is not a user choice
        }

        RefreshGroups();
    }

    /// <summary>Keep each dropdown's choices in step with the snapshot without disturbing a still-valid selection.</summary>
    private void RebuildOptions(IReadOnlyList<PrItemViewModel> items)
    {
        _rebuildingOptions = true;
        try
        {
            FilterAuthor = Sync(Authors, AnyAuthor, items.Select(i => i.Pr.Author), FilterAuthor);
            FilterLabel = Sync(Labels, AnyLabel, items.SelectMany(i => i.Pr.Labels), FilterLabel);
            FilterRepo = Sync(Repos, AnyRepo, items.Select(i => i.Pr.Repo), FilterRepo);
        }
        finally { _rebuildingOptions = false; }
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

    // --- settings: the view model is a thin pass-through over AppSettings ------------------------------------

    public int PollIntervalMinutes
    {
        get => _settings.PollIntervalMinutes;
        set => SetSetting(_settings.PollIntervalMinutes, value, (s, v) => s.PollIntervalMinutes = v);
    }

    public bool NotifyNeedsYou
    {
        get => _settings.NotifyNeedsYou;
        set => SetSetting(_settings.NotifyNeedsYou, value, (s, v) => s.NotifyNeedsYou = v);
    }

    public bool NotifyWaitingOnOthers
    {
        get => _settings.NotifyWaitingOnOthers;
        set => SetSetting(_settings.NotifyWaitingOnOthers, value, (s, v) => s.NotifyWaitingOnOthers = v);
    }

    public bool NotifyReadyToMerge
    {
        get => _settings.NotifyReadyToMerge;
        set => SetSetting(_settings.NotifyReadyToMerge, value, (s, v) => s.NotifyReadyToMerge = v);
    }

    public bool NotifyClosed
    {
        get => _settings.NotifyClosed;
        set => SetSetting(_settings.NotifyClosed, value, (s, v) => s.NotifyClosed = v);
    }

    /// <summary>The OS autostart entry is the source of truth; nothing is stored in settings.</summary>
    public bool StartAtLogin
    {
        get => _startAtLogin;
        set
        {
            if (!SetProperty(ref _startAtLogin, value))
            {
                return;
            }

            _localMessage = null;
            RefreshPollerState();
            _ = ApplyStartAtLoginAsync(value);
        }
    }

    private async Task ApplyStartAtLoginAsync(bool enable)
    {
        try
        {
            await (enable ? _autoStart.EnableAsync() : _autoStart.DisableAsync());
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _localMessage = $"Could not update start-at-login: {ex.Message}";
                SetProperty(ref _startAtLogin, !enable, nameof(StartAtLogin));
                RefreshPollerState();
            });
        }
    }

    private void SetSetting<T>(T current, T value, Action<AppSettings, T> assign, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(current, value, _settings, assign, propertyName))
        {
            _settingsFile.Save(_settings);
        }
    }

    // --- actions -------------------------------------------------------------------------------------------

    public Task RefreshAsync() => _poller.RefreshNowAsync();
    public static void OpenPr(PrItemViewModel item) => Browser.Open(item.Url);

    /// <summary>The store raises its own change event, which re-sorts and rebuilds the rows.</summary>
    public void ToggleStar(PrItemViewModel item) => _stars.Toggle(item.Pr.Key);

    public void Dismiss(NotificationRecord record) => _notifications.Dismiss(record);
    public void DismissAll() => _notifications.DismissAll();
    public static void OpenNotification(NotificationRecord record) => Browser.Open(record.Url);
    public static void OpenDataFolder() => Browser.OpenFolder(DataDirectory);

    public void RecheckGh()
    {
        _gh.Invalidate();
        ResolveGhPath();
        _ = RefreshAsync();
    }

    // --- state refresh ---------------------------------------------------------------------------------------

    /// <summary>Locating gh walks every PATH entry; keep that off the UI thread. Resumes on the UI thread after the await.</summary>
    private async void ResolveGhPath()
    {
        GhPath = "checking…";
        try { GhPath = await Task.Run(() => _gh.ResolvedPath) ?? "not found"; }
        catch (Exception ex) { GhPath = $"error: {ex.Message}"; }
    }

    private void RefreshGroups()
    {
        // Starred first — those are the ones being worked on right now — then oldest PR first regardless of
        // status, so the ones that have waited longest come to the top.
        var items = _store.Current
            .Select(t => new PrItemViewModel(t, _stars.IsStarred(t.Key)))
            .OrderByDescending(i => i.IsStarred)
            .ThenBy(i => i.Pr.CreatedAt)
            .ToList();
        RebuildOptions(items);

        var filter = Filter;
        IsFiltering = filter.IsActive;
        foreach (var group in _groups)
        {
            group.Replace([.. items.Where(i => i.Status.Bucket() == group.Bucket)], group.Filterable ? filter : PrFilter.None);
        }

        // A conflicted PR is still counted as ready — it is one rebase from mergeable — but the count has to say
        // so, or the summary and the tray tooltip promise something to merge that will not merge.
        var conflicted = items.Count(i => i.Status == PrStatus.ReadyToMergeConflict);
        var ready = conflicted == 0
            ? $"{ReadyToMerge.TotalCount} ready to merge"
            : $"{ReadyToMerge.TotalCount} ready to merge ({conflicted} conflicted)";

        // Summary and tray always describe everything, whatever the window is narrowed to.
        Summary = items.Count == 0
            ? "No open pull requests"
            : $"{NeedsYou.TotalCount} need you · {WaitingOnOthers.TotalCount} waiting · {ready}";
        RefreshTrayState();
    }

    private void RefreshPollerState()
    {
        IsRefreshing = _poller.IsPolling;
        Banner = _poller.LastError?.UserMessage ?? _localMessage ?? string.Empty;
        LastChecked = _poller.LastSuccess is { } t ? $"Last checked {t:t}" : string.Empty;
        RefreshTrayState();
    }

    private void RefreshTrayState() =>
        TrayState = _poller.LastError is not null ? TrayState.Error
            : NeedsYou.TotalCount > 0 ? TrayState.NeedsYou
            : ReadyToMerge.TotalCount > 0 ? TrayState.Ready
            : TrayState.Idle;

    private void RefreshNotifications()
    {
        var history = _notifications.History;
        Notifications.Clear();
        foreach (var r in history.Where(r => ShowDismissed || !r.Dismissed))
        {
            Notifications.Add(r);
        }

        NotificationsHeader = WithCount("Notifications", history.Count(r => !r.Dismissed));
    }

    partial void OnShowDismissedChanged(bool value) => RefreshNotifications();

    internal static string WithCount(string title, int count) => count == 0 ? title : $"{title} ({count})";
}
