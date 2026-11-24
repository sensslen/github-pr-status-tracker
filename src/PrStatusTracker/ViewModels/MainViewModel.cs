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
    public string Age { get; } = FormatAge(tracked.Pr.CreatedAt);

    /// <summary>Open for more than a day: the age gets emphasised so long-waiting PRs stand out.</summary>
    public bool IsOverdue { get; } = DateTimeOffset.Now - tracked.Pr.CreatedAt > TimeSpan.FromHours(24);
    public CheckState? Checks { get; } = tracked.Pr.Checks;

    public string Number { get; } = $"#{tracked.Pr.Number}";
    public string Author { get; } = tracked.Pr.Author;
    public string Size { get; } = $"+{tracked.Pr.Additions} −{tracked.Pr.Deletions}";
    public string Updated { get; } = $"updated {FormatAge(tracked.Pr.UpdatedAt)} ago";
    public IReadOnlyList<string> Labels { get; } = tracked.Pr.Labels;

    /// <summary>The branch the PR merges into. Empty for a snapshot written before branches were fetched.</summary>
    public string TargetBranch { get; } = tracked.Pr.BaseBranch;
    public string SourceBranch { get; } = tracked.Pr.HeadBranch;
    public bool HasTargetBranch => TargetBranch.Length > 0;

    /// <summary>Merging into a release branch or another PR's branch, which the row calls out.</summary>
    public bool TargetsNonDefaultBranch { get; } = tracked.Pr.TargetsNonDefaultBranch;

    public string BranchTip => (SourceBranch, TargetsNonDefaultBranch) switch
    {
        ("", _) => $"Merges into {TargetBranch}",
        (_, true) => $"Merges {SourceBranch} into {TargetBranch}, not the default branch {tracked.Pr.DefaultBranch}",
        _ => $"Merges {SourceBranch} into {TargetBranch}",
    };

    private readonly IReadOnlyList<Review> _humanVerdicts = [.. tracked.Pr.Verdicts().Where(r => !IsCodeRabbit(r.Author))];
    private readonly IReadOnlyList<string> _humansAwaited = [.. tracked.Pr.RequestedReviewers.Where(r => !IsCodeRabbit(r))];

    /// <summary>
    /// Human review tallies: approvals, who requested changes, reviewers still asked. Empty when there are none.
    /// Change requests are named because they are the ones you have to go and answer.
    /// CodeRabbit is left out and reported by <see cref="Rabbit"/>, so a bot approval never reads as a person's.
    /// </summary>
    public string Reviews => BuildReviews(_humanVerdicts, _humansAwaited);

    /// <summary>
    /// Where the people reviewing stand, as one symbol: any standing change request outweighs approvals, and an
    /// approval only counts as done once GitHub no longer asks for more reviews.
    /// </summary>
    public ReviewVerdict Human => _humanVerdicts.Any(r => r.State == ReviewState.ChangesRequested) ? ReviewVerdict.ChangesRequested
        : HumanApprovals > 0 && tracked.Pr.ReviewDecision != ReviewDecision.ReviewRequired ? ReviewVerdict.Approved
        : HumanApprovals > 0 || _humansAwaited.Count > 0 ? ReviewVerdict.Pending
        : tracked.Pr.LatestReviews.Any(r => !IsCodeRabbit(r.Author)) ? ReviewVerdict.Commented
        : ReviewVerdict.None;

    /// <summary>Who said what, one person per line, for the review chip's tooltip.</summary>
    public string HumanTip
    {
        get
        {
            var lines = _humanVerdicts
                .Select(r => r.State == ReviewState.Approved ? $"{r.Author} approved" : $"{r.Author} requested changes")
                .ToList();
            var asked = _humansAwaited.Where(a => !_humanVerdicts.Any(r => r.Author == a)).ToList();
            var reasked = _humansAwaited.Where(a => _humanVerdicts.Any(r => r.Author == a)).ToList();
            if (reasked.Count > 0)
            {
                lines.Add($"Asked again: {string.Join(", ", reasked)}");
            }

            if (asked.Count > 0)
            {
                lines.Add($"Waiting on: {string.Join(", ", asked)}");
            }

            return lines.Count > 0 ? string.Join("\n", lines) : "No reviewers yet";
        }
    }

    /// <summary>Where CodeRabbit stands on this PR. <see cref="ReviewVerdict.None"/> when it has not been asked
    /// and has not reviewed.</summary>
    public ReviewVerdict Rabbit { get; } = RabbitVerdictOf(tracked.Pr);

    public bool HasRabbit => Rabbit != ReviewVerdict.None;

    /// <summary>Approvals from people, leaving CodeRabbit out.</summary>
    public int HumanApprovals => _humanVerdicts.Count(r => r.State == ReviewState.Approved);

    /// <summary>
    /// A change request, from a person or CodeRabbit, that is still yours to act on: its reviewer has not been
    /// asked to look again. Once you re-request the review the ball is in their court, even though GitHub keeps
    /// the request blocking until they answer.
    /// </summary>
    public bool HasActionableChangeRequest { get; } = tracked.Pr.Verdicts().Any(r => r.State == ReviewState.ChangesRequested
        && !tracked.Pr.RequestedReviewers.Contains(r.Author, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// The card's tint. Attention wins over good: a PR with threads awaiting a reply or a change request not yet
    /// answered with a re-request needs looking at before anything else. Good means safe to merge without a second thought: ready, approved by a
    /// person rather than only CodeRabbit, and merging into the default branch. A ready PR that misses either of
    /// the last two stays untinted, as one to merge with care.
    /// </summary>
    public RowTone Tone => HasOpenThreads || HasActionableChangeRequest ? RowTone.Attention
        : Status == PrStatus.ReadyToMerge && HumanApprovals > 0 && HasTargetBranch && !TargetsNonDefaultBranch ? RowTone.Good
        : RowTone.Neutral;

    public string RabbitTip => Rabbit switch
    {
        ReviewVerdict.Approved => "CodeRabbit approved",
        ReviewVerdict.ChangesRequested => "CodeRabbit requested changes",
        ReviewVerdict.Commented => "CodeRabbit commented",
        ReviewVerdict.Pending => "CodeRabbit has been asked and not reviewed yet",
        _ => string.Empty,
    };

    /// <summary>The CodeRabbit app reviews as `coderabbitai`; the REST API adds `[bot]`, GraphQL does not.</summary>
    internal static bool IsCodeRabbit(string login) => login.StartsWith("coderabbit", StringComparison.OrdinalIgnoreCase);

    public bool HasConflicts { get; } = tracked.Pr.Mergeable == Mergeable.Conflicting;

    public bool IsStarred { get; } = isStarred;

    public bool HasOpenThreads => Callouts.Contains(PrCallout.OpenThreads);

    /// <summary>
    /// Everything this PR is calling for, most pressing first. Empty for a PR that needs nothing from you,
    /// and for every PR that is not yours — those are what the other two tabs are for.
    /// </summary>
    public IReadOnlyList<PrCallout> Callouts { get; } = tracked.Status.Callouts(tracked.Pr.HasOpenThreads, tracked.Pr.HasUnresolvedThreads);

    /// <summary>The one the row sorts and colours by. None is the enum's default, so an empty list gives it.</summary>
    public PrCallout Callout => Callouts.FirstOrDefault();

    /// <summary>
    /// Whether the pill speaks for the threads. Only when they are all the PR is calling for and it is not a
    /// draft: a draft keeps its Draft pill and shows the threads as the 💬 marker, like every other status.
    /// </summary>
    public bool PillShowsThreads => Callout == PrCallout.OpenThreads && !Status.IsDraft();

    public string StatusLabel => PillShowsThreads ? "In discussion" : Status.Label();

    public bool NeedsAttention => Callouts.Count > 0;

    public string OpenThreadsTip => !HasOpenThreads ? string.Empty
        : tracked.Pr.HasOpenThreads ? "Unresolved review threads awaiting your reply"
        : "Approved, but unresolved review threads block the merge";

    private static ReviewVerdict RabbitVerdictOf(PullRequest pr)
    {
        var review = pr.LatestReviews.Where(r => IsCodeRabbit(r.Author)).MaxBy(r => r.SubmittedAt);
        return review?.State switch
        {
            ReviewState.Approved => ReviewVerdict.Approved,
            ReviewState.ChangesRequested => ReviewVerdict.ChangesRequested,
            ReviewState.Commented or ReviewState.Dismissed => ReviewVerdict.Commented,
            _ => pr.RequestedReviewers.Any(IsCodeRabbit) ? ReviewVerdict.Pending : ReviewVerdict.None,
        };
    }

    private static string BuildReviews(IReadOnlyList<Review> verdicts, IReadOnlyList<string> awaited)
    {
        var approvals = verdicts.Count(r => r.State == ReviewState.Approved);
        var changes = verdicts.Where(r => r.State == ReviewState.ChangesRequested).Select(r => r.Author).ToList();
        var awaiting = awaited.Count;

        var parts = new List<string>();
        if (approvals > 0)
        {
            parts.Add($"{approvals} approved");
        }

        if (changes.Count > 0)
        {
            parts.Add($"changes requested by {string.Join(", ", changes)}");
        }

        if (awaiting > 0)
        {
            parts.Add($"{awaiting} awaiting");
        }

        return string.Join(", ", parts);
    }

    internal static string FormatAge(DateTimeOffset when)
    {
        var span = DateTimeOffset.Now - when;
        return span.TotalMinutes < 60 ? $"{Math.Max(1, (int)span.TotalMinutes)}m"
            : span.TotalHours < 24 ? $"{(int)span.TotalHours}h"
            : $"{(int)span.TotalDays}d";
    }
}

/// <summary>How a PR card is tinted; see <see cref="PrItemViewModel.Tone"/>.</summary>
public enum RowTone { Neutral, Good, Attention }

/// <summary>Where a set of reviewers stands on a PR: the people together, or CodeRabbit on its own.</summary>
public enum ReviewVerdict { None, Pending, Commented, ChangesRequested, Approved }

public sealed partial class PrRepositoryViewModel(string name) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] public partial bool IsExpanded { get; set; } = true;
    [ObservableProperty] public partial IReadOnlyList<PrItemViewModel> Items { get; set; } = [];
}

/// <summary>One bucket's rows plus the derived bits the views bind to (tab header, empty state).</summary>
public sealed partial class PrGroupViewModel(PrBucket bucket, PrFilterViewModel filter) : ObservableObject
{
    private const string HideTeamOnlyText = "Hide team-only requests";
    private readonly Dictionary<string, PrRepositoryViewModel> _repositories = new(StringComparer.Ordinal);

    /// <summary>
    /// Collapsed sections by repository name, including repositories with no rows on this tab right now, so a
    /// section collapsed yesterday is still collapsed when its next PR arrives.
    /// </summary>
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);

    /// <summary>Raised when the user collapses or expands a section, for the owner to persist <see cref="Collapsed"/>.</summary>
    public event Action? CollapseChanged;

    public IReadOnlyCollection<string> Collapsed => _collapsed;

    /// <summary>Seeds the collapsed sections from a previous run. Call before the first <see cref="Replace"/>.</summary>
    public void RestoreCollapsed(IEnumerable<string> names) => _collapsed.UnionWith(names);

    /// <summary>
    /// Stands in for the bucket's own wording once a tab is narrowed. "Nothing needs you right now" is a claim
    /// about the whole bucket, and it is false of a tab that is merely not showing what it has — the header
    /// directly above would be reading `(0 of 8)` while this said there was nothing there. It needs no count
    /// of its own, because that header carries it.
    /// </summary>
    private const string NarrowedEmptyText = "No pull requests match.";

    public PrBucket Bucket { get; } = bucket;

    /// <summary>The bar above this tab's rows. Some tabs share one of these; Your PRs has its own.</summary>
    public PrFilterViewModel Filter { get; } = filter;
    [ObservableProperty] public partial string EmptyText { get; set; } = bucket.EmptyText();
    public ObservableCollection<PrRepositoryViewModel> Repositories { get; } = [];

    /// <summary>Rows in this bucket before filtering; what the tray and summary count.</summary>
    public int TotalCount { get; private set; }

    /// <summary>
    /// Whether this tab offers the team-only toggle: the one whose bucket those rows actually land in. Asked of
    /// <see cref="PrStatusExtensions.Bucket"/> rather than hard-coded to Needs you, so the partition stays
    /// stated once — re-file <see cref="PrStatus.RequestedViaTeam"/> and the control follows it, instead of
    /// sitting on a tab where it hides nothing with nothing failing.
    /// </summary>
    public bool CanHideTeamOnly { get; } = PrStatus.RequestedViaTeam.Bucket() == bucket;

    [ObservableProperty] public partial string Header { get; set; } = bucket.Label();
    [ObservableProperty] public partial bool IsEmpty { get; set; } = true;

    /// <summary>
    /// Drop the rows you are only being asked for because a team you belong to was. It is narrowing rather than
    /// a dimension of <see cref="Filter"/> because Needs you shares its bar with Waiting on others, where
    /// narrowing one tab must not narrow the other. View-only, like the bar: the summary line and the tray go
    /// on counting what it hides. <see cref="MainViewModel"/> owns the value and assigns it here.
    /// </summary>
    public bool HideTeamOnly { get; set; }

    /// <summary>
    /// The checkbox's own text, which names the count while it is hiding something — with no bar active, that
    /// and the header are the only things on screen accounting for rows that are not there.
    /// </summary>
    [ObservableProperty] public partial string HideTeamOnlyLabel { get; set; } = HideTeamOnlyText;

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
        // The flag is only ever set on the tab that offers it; asking both keeps that true of the class, not
        // just of its caller, the way Current drops an Author a bar has no dropdown for.
        var hidingTeamOnly = CanHideTeamOnly && HideTeamOnly;
        var teamOnlyHidden = 0;
        TotalCount = all.Count;
        var items = new List<PrItemViewModel>();
        // Sifted by the bar first, so the count below is what unchecking the box would bring back rather than
        // rows the bar was excluding anyway.
        foreach (var item in all.Where(i => filter.Matches(i.Pr))
            .OrderBy(i => i.Pr.Repo, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Pr.Repo, StringComparer.Ordinal)
            .ThenByDescending(i => i.IsStarred)
            .ThenByDescending(i => i.NeedsAttention)
            .ThenBy(i => i.Callout)
            .ThenBy(i => i.Status.IsDraft())
            .ThenBy(i => i.Status)
            .ThenBy(i => i.Pr.CreatedAt)
            .ThenBy(i => i.Pr.Number))
        {
            if (hidingTeamOnly && item.Status == PrStatus.RequestedViaTeam)
            {
                teamOnlyHidden++;
                continue;
            }

            items.Add(item);
        }

        Repositories.Clear();
        foreach (var rows in items.GroupBy(i => i.Pr.Repo, StringComparer.Ordinal))
        {
            if (!_repositories.TryGetValue(rows.Key, out var repository))
            {
                repository = new PrRepositoryViewModel(rows.Key) { IsExpanded = !_collapsed.Contains(rows.Key) };
                repository.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(PrRepositoryViewModel.IsExpanded) && Remember(repository))
                    {
                        CollapseChanged?.Invoke();
                    }
                };
                _repositories.Add(rows.Key, repository);
            }

            repository.Items = rows.ToArray();
            Repositories.Add(repository);
        }

        IsEmpty = items.Count == 0;
        // Empty and narrowed-to-nothing are different states, and only the first is the bucket's to describe.
        // Conditioned on what will actually be shown, so the property does not read as a claim about a tab
        // that has rows — and, like the header, without naming which narrowing emptied it.
        EmptyText = IsEmpty && TotalCount > 0 ? NarrowedEmptyText : Bucket.EmptyText();
        HideTeamOnlyLabel = teamOnlyHidden > 0 ? $"{HideTeamOnlyText} ({teamOnlyHidden} hidden)" : HideTeamOnlyText;
        // Missing rows are the whole test for whether this tab is narrowed, whatever did the narrowing: nothing
        // here adds rows, and a bar that is not active matches everything. So no future narrowing has to be
        // named on this line — which is what the toggle would otherwise have had to be.
        Header = items.Count != TotalCount
            ? $"{Bucket.Label()} ({items.Count} of {TotalCount})"
            : MainViewModel.WithCount(Bucket.Label(), TotalCount);
        // Rows on the review tabs never carry a callout, so there is nothing there to add up.
        Attention = Bucket == PrBucket.YourPrs ? Summarise(items) : string.Empty;
    }

    private bool Remember(PrRepositoryViewModel repository) =>
        repository.IsExpanded ? _collapsed.Remove(repository.Name) : _collapsed.Add(repository.Name);

    /// <summary>A failing PR with open threads must be counted under both callouts.</summary>
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
    private readonly PollingService _poller;
    private readonly NotificationService _notifications;
    private readonly AppSettings _settings;
    private readonly JsonFileStore<AppSettings> _settingsFile;
    private readonly IAutoStart _autoStart;
    private readonly GhCommandRunner _gh;
    private readonly UpdateService _updates;
    private readonly PrGroupViewModel[] _groups;
    private bool _startAtLogin;

    // Your PRs you could merge right now, and those wanting something first — both across the whole snapshot,
    // filter or no filter, because the tray and the summary line speak for all of it. Fields rather than
    // locals because RefreshTrayState is also reached from RefreshPollerState, long after the last poll.
    private int _readyCount;
    private int _attentionCount;
    private string? _localMessage; // shown in the banner when there is no poll error

    public MainViewModel(PrStore store, StarStore stars, PollingService poller,
        NotificationService notifications, AppSettings settings, JsonFileStore<AppSettings> settingsFile,
        IAutoStart autoStart, GhCommandRunner gh, UpdateService updates)
    {
        _store = store;
        _stars = stars;
        _poller = poller;
        _notifications = notifications;
        _settings = settings;
        _settingsFile = settingsFile;
        _autoStart = autoStart;
        _gh = gh;
        _updates = updates;
        _startAtLogin = autoStart.IsEnabled();
        NeedsYou = new(PrBucket.NeedsYou, ReviewFilter);
        WaitingOnOthers = new(PrBucket.WaitingOnOthers, ReviewFilter);
        YourPrs = new(PrBucket.YourPrs, YourPrsFilter);
        _groups = [NeedsYou, WaitingOnOthers, YourPrs];

        ReviewFilter.Changed += RefreshGroups;
        YourPrsFilter.Changed += RefreshGroups;
        NeedsYou.HideTeamOnly = settings.HideTeamOnlyRequests;
        foreach (var group in _groups)
        {
            var key = group.Bucket.ToString();
            group.RestoreCollapsed(settings.CollapsedRepositories.GetValueOrDefault(key) ?? []);
            group.CollapseChanged += () =>
            {
                _settings.CollapsedRepositories[key] = [.. group.Collapsed.Order(StringComparer.Ordinal)];
                _settingsFile.Save(_settings);
            };
        }
        store.Updated += () => Dispatcher.UIThread.Post(RefreshGroups);
        stars.Changed += () => Dispatcher.UIThread.Post(RefreshGroups);
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
    public static string CurrentVersion => UpdateService.CurrentDisplay;

    /// <summary>Windows installs in place and comes back; macOS can only put the disk image on screen.</summary>
    public static string UpdateActionLabel => OperatingSystem.IsWindows() ? "Install and restart" : "Download update";

    [ObservableProperty] public partial string Summary { get; set; } = "Loading…";
    [ObservableProperty] public partial TrayState TrayState { get; set; }
    [ObservableProperty] public partial bool IsRefreshing { get; set; }
    [ObservableProperty] public partial string LastChecked { get; set; } = string.Empty;
    [ObservableProperty] public partial bool ShowDismissed { get; set; }
    [ObservableProperty] public partial string NotificationsHeader { get; set; } = "Notifications";
    [ObservableProperty] public partial string GhPath { get; set; } = "checking…";
    [ObservableProperty] public partial string UpdateStatus { get; set; } = string.Empty;
    [ObservableProperty] public partial bool UpdateAvailable { get; set; }
    [ObservableProperty] public partial bool IsInstallingUpdate { get; set; }

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

    /// <summary>
    /// The Needs you tab's own narrowing. A pass-through like the rest, so the setting has one authority and
    /// one save path; the tab is handed the value and rebuilt from it. Only the rows move — the summary line
    /// and the tray recompute from the unfiltered snapshot exactly as they did.
    /// </summary>
    public bool HideTeamOnlyRequests
    {
        get => _settings.HideTeamOnlyRequests;
        set
        {
            if (SetSetting(_settings.HideTeamOnlyRequests, value, (s, v) => s.HideTeamOnlyRequests = v))
            {
                NeedsYou.HideTeamOnly = value;
                RefreshGroups();
            }
        }
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

    private bool SetSetting<T>(T current, T value, Action<AppSettings, T> assign, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(current, value, _settings, assign, propertyName))
        {
            return false;
        }

        _settingsFile.Save(_settings);
        return true;
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

    /// <summary>The unforced call comes from the poll loop; the service is what keeps it to once a day.</summary>
    public async Task CheckForUpdatesAsync(bool force)
    {
        if (force)
        {
            UpdateStatus = "Checking…";
        }

        try
        {
            UpdateAvailable = await Task.Run(() => _updates.CheckAsync(force));
            UpdateStatus = UpdateAvailable ? $"Version {_updates.AvailableTag} is available" : "Up to date";
        }
        catch (Exception ex)
        {
            UpdateStatus = ex is GhException gh ? gh.UserMessage : ex.Message;
        }
    }

    /// <summary>On Windows this is the last thing the application does: the installer needs it gone.</summary>
    public async Task InstallUpdateAsync()
    {
        // Two buttons offer this, and a second run would download over the first one's installer.
        if (IsInstallingUpdate)
        {
            return;
        }

        IsInstallingUpdate = true;
        UpdateStatus = "Downloading…";
        try
        {
            await Task.Run(() => _updates.InstallAsync());
            UpdateStatus = OperatingSystem.IsWindows() ? "Installing…" : "Drag the new version to Applications to finish";
        }
        catch (Exception ex)
        {
            UpdateStatus = ex is GhException gh ? gh.UserMessage : ex.Message;
        }
        finally
        {
            IsInstallingUpdate = false;
        }
    }

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
        var items = _store.Current
            .Select(t => new PrItemViewModel(t, _stars.IsStarred(t.Key)))
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
        _attentionCount = items.Count(i => i.Callouts.Any(c => c.IsWork()));
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

        if (_poller.LastSuccess is not null)
        {
            _ = CheckForUpdatesAsync(force: false);
        }
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
