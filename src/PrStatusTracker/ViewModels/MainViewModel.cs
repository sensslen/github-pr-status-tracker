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

public sealed class PrItemViewModel(TrackedPr tracked, bool isStarred, bool hasNewComments)
{
    public PullRequest Pr { get; } = tracked.Pr;
    public string Title { get; } = tracked.Pr.Title;
    public string Url { get; } = tracked.Pr.Url;
    public PrStatus Status { get; } = tracked.Status;
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

    /// <summary>Someone has said something here since you last opened it. Only ever true for your own PRs.</summary>
    public bool HasNewComments { get; } = hasNewComments;

    /// <summary>
    /// Everything this PR is calling for, most pressing first. Empty for a PR that needs nothing from you,
    /// and for every PR that is not yours — those are what the other two tabs are for.
    /// </summary>
    public IReadOnlyList<PrCallout> Callouts { get; } = tracked.Status.Callouts(hasNewComments);

    /// <summary>The one the row sorts and colours by. None is the enum's default, so an empty list gives it.</summary>
    public PrCallout Callout => Callouts.FirstOrDefault();

    /// <summary>
    /// What the pill reads. Normally the status, but a quiet status is overruled by unread comments: a PR
    /// whose pill said "Awaiting review" while the tab counted it as wanting something was telling you two
    /// different things at once. Where the status already carries a callout of its own, that still leads —
    /// failing CI outranks a comment — and the 💬 marker carries the rest.
    /// </summary>
    public string StatusLabel { get; } = tracked.Status.Callouts(hasNewComments).FirstOrDefault() == PrCallout.NewComments
        ? "New comments"
        : tracked.Status.Label();

    public bool NeedsAttention => Callouts.Count > 0;

    /// <summary>Projected here like every other row string, so the view never reads the raw PR.</summary>
    public string NewCommentsTip { get; } = hasNewComments
        ? $"New comments since you last opened this — latest from {tracked.Pr.LastCommentBy}"
        : string.Empty;

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
public sealed partial class PrGroupViewModel(PrBucket bucket, PrFilterViewModel filter) : ObservableObject
{
    public PrBucket Bucket { get; } = bucket;

    /// <summary>The bar above this tab's rows. Some tabs share one of these; Your PRs has its own.</summary>
    public PrFilterViewModel Filter { get; } = filter;
    public string EmptyText { get; } = bucket.EmptyText();
    public ObservableCollection<PrItemViewModel> Items { get; } = [];

    /// <summary>Rows in this bucket before filtering; what the tray and summary count.</summary>
    public int TotalCount { get; private set; }

    [ObservableProperty] public partial string Header { get; set; } = bucket.Label();
    [ObservableProperty] public partial string ShortHeader { get; set; } = bucket.ShortLabel();
    [ObservableProperty] public partial bool IsEmpty { get; set; } = true;

    /// <summary>
    /// What the visible rows are calling for — `2 ready to merge · 1 CI failing`. Unlike the summary line, this
    /// describes what you are looking at rather than the whole snapshot, since it sits directly over the rows
    /// it is counting. Empty for the two review tabs, whose rows never carry a callout.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAttention))]
    public partial string Attention { get; set; } = string.Empty;

    public bool HasAttention => Attention.Length > 0;

    public void Replace(IReadOnlyList<PrItemViewModel> all)
    {
        var filter = Filter.Current;
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
        // Rows on the review tabs never carry a callout, so there is nothing there to add up.
        Attention = Bucket == PrBucket.YourPrs ? Summarise(Items) : string.Empty;
    }

    /// <summary>Counts each callout a row carries, so a failing PR with unread comments is named under both.</summary>
    internal static string Summarise(IEnumerable<PrItemViewModel> items) =>
        string.Join(" · ", items.SelectMany(i => i.Callouts)
            .GroupBy(c => c)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Count()} {g.Key.Label()}"));
}

/// <summary>What the tray icon should look like, in priority order.</summary>
public enum TrayState { Idle, Ready, NeedsYou, Error }

public sealed partial class MainViewModel : ObservableObject
{
    private readonly PrStore _store;
    private readonly StarStore _stars;
    private readonly CommentSeenStore _seen;
    private readonly PollingService _poller;
    private readonly NotificationService _notifications;
    private readonly AppSettings _settings;
    private readonly JsonFileStore<AppSettings> _settingsFile;
    private readonly IAutoStart _autoStart;
    private readonly GhCommandRunner _gh;
    private readonly PrGroupViewModel[] _groups;
    private bool _startAtLogin;

    // Your PRs you could merge right now, and those wanting something first — both across the whole snapshot,
    // filter or no filter, because the tray and the summary line speak for all of it. Fields rather than
    // locals because RefreshTrayState is also reached from RefreshPollerState, long after the last poll.
    private int _readyCount;
    private int _attentionCount;
    private string? _localMessage; // shown in the banner when there is no poll error

    public MainViewModel(PrStore store, StarStore stars, CommentSeenStore seen, PollingService poller,
        NotificationService notifications, AppSettings settings, JsonFileStore<AppSettings> settingsFile,
        IAutoStart autoStart, GhCommandRunner gh)
    {
        _store = store;
        _stars = stars;
        _seen = seen;
        _poller = poller;
        _notifications = notifications;
        _settings = settings;
        _settingsFile = settingsFile;
        _autoStart = autoStart;
        _gh = gh;
        _startAtLogin = autoStart.IsEnabled();
        NeedsYou = new(PrBucket.NeedsYou, ReviewFilter);
        WaitingOnOthers = new(PrBucket.WaitingOnOthers, ReviewFilter);
        YourPrs = new(PrBucket.YourPrs, YourPrsFilter);
        _groups = [NeedsYou, WaitingOnOthers, YourPrs];

        ReviewFilter.Changed += RefreshGroups;
        YourPrsFilter.Changed += RefreshGroups;
        store.Updated += () => Dispatcher.UIThread.Post(RefreshGroups);
        stars.Changed += () => Dispatcher.UIThread.Post(RefreshGroups);
        seen.Changed += () => Dispatcher.UIThread.Post(RefreshGroups);
        poller.StateChanged += () => Dispatcher.UIThread.Post(RefreshPollerState);
        notifications.HistoryChanged += () => Dispatcher.UIThread.Post(RefreshNotifications);

        RefreshGroups();
        RefreshPollerState();
        RefreshNotifications();
        ResolveGhPath();
    }

    /// <summary>
    /// Other people's PRs are one population, sifted the same way whether or not you have reviewed them yet,
    /// so those two tabs share a bar. Your own PRs are a different population — a handful, all by you — and
    /// narrowing them has nothing to do with narrowing a review queue, so that tab keeps its own.
    /// </summary>
    public PrFilterViewModel ReviewFilter { get; } = new();
    public PrFilterViewModel YourPrsFilter { get; } = new(withAuthor: false);

    public PrGroupViewModel NeedsYou { get; }
    public PrGroupViewModel WaitingOnOthers { get; }
    public PrGroupViewModel YourPrs { get; }
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

    public bool NotifyYourPrs
    {
        get => _settings.NotifyYourPrs;
        set => SetSetting(_settings.NotifyYourPrs, value, (s, v) => s.NotifyYourPrs = v);
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

    /// <summary>Opening a PR is what counts as reading it: its comments stop being new from here on.</summary>
    public void OpenPr(PrItemViewModel item)
    {
        _seen.MarkRead(item.Pr.Key, item.Pr.LastCommentAt);
        Browser.Open(item.Url);
    }

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
        // Starred first — those are the ones being worked on right now — then anything calling for attention,
        // most pressing callout first, then oldest PR first, so the ones that have waited longest come to the
        // top. Only your own PRs carry callouts, so for the two review tabs this is the old star/age order.
        var items = _store.Current
            .Select(t => new PrItemViewModel(t, _stars.IsStarred(t.Key), _seen.HasUnread(t.Key, t.Pr.LastCommentAt)))
            .OrderByDescending(i => i.IsStarred)
            .ThenByDescending(i => i.NeedsAttention)
            .ThenBy(i => i.Callout)
            .ThenBy(i => i.Pr.CreatedAt)
            .ToList();
        // Each bar offers choices drawn from the rows it filters, so the Your PRs repo dropdown lists the
        // repos you have PRs in rather than every repo you have ever been asked to review.
        var byBucket = items.ToLookup(i => i.Status.Bucket());
        ReviewFilter.RebuildOptions(byBucket[PrBucket.NeedsYou].Concat(byBucket[PrBucket.WaitingOnOthers]));
        YourPrsFilter.RebuildOptions(byBucket[PrBucket.YourPrs]);
        foreach (var group in _groups)
        {
            group.Replace([.. byBucket[group.Bucket]]);
        }

        // Summary and tray always describe everything, whatever the window is narrowed to. Your own PRs are one
        // tab now, so the count that mattered — how many of them want something — has to be spelled out here
        // rather than read off the tab sizes.
        _readyCount = items.Count(i => i.Callout == PrCallout.ReadyToMerge);
        _attentionCount = items.Count(i => i.Callout.IsWork());
        var parts = new List<string>();
        if (_readyCount > 0)
        {
            parts.Add($"{_readyCount} ready");
        }

        if (_attentionCount > 0)
        {
            parts.Add($"{_attentionCount} need attention");
        }

        var detail = parts.Count == 0 ? string.Empty : $" ({string.Join(" · ", parts)})";

        Summary = items.Count == 0
            ? "No open pull requests"
            : $"{NeedsYou.TotalCount} need you · {WaitingOnOthers.TotalCount} waiting · {YourPrs.TotalCount} yours{detail}";
        RefreshTrayState();
    }

    private void RefreshPollerState()
    {
        IsRefreshing = _poller.IsPolling;
        Banner = _poller.LastError?.UserMessage ?? _localMessage ?? string.Empty;
        LastChecked = _poller.LastSuccess is { } t ? $"Last checked {t:t}" : string.Empty;
        RefreshTrayState();
    }

    /// <summary>
    /// Driven by callouts rather than tab sizes: a tab full of your own drafts is not a reason to light the
    /// tray up, and a PR of yours with failing checks is — exactly as it was when it lived under "needs you".
    /// </summary>
    private void RefreshTrayState() =>
        TrayState = _poller.LastError is not null ? TrayState.Error
            : NeedsYou.TotalCount > 0 || _attentionCount > 0 ? TrayState.NeedsYou
            : _readyCount > 0 ? TrayState.Ready
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
