namespace PrStatusTracker.Models;

public sealed class AppSettings
{
    public const int MinPollIntervalMinutes = 1;

    public int PollIntervalMinutes
    {
        get;
        set => field = Math.Max(MinPollIntervalMinutes, value);
    } = 2;

    public bool NotifyNeedsYou { get; set; } = true;
    public bool NotifyWaitingOnOthers { get; set; } = true;

    /// <summary>Renamed from NotifyReadyToMerge when that bucket grew into every PR you authored; a settings
    /// file written by an older build has no such key and falls back to the default.</summary>
    public bool NotifyYourPrs { get; set; } = true;
    public bool NotifyClosed { get; set; } = true;

    /// <summary>
    /// Whether the Needs you tab hides the PRs you were only asked for because a team you belong to was.
    /// A standing preference rather than part of the filter bar, so it survives a restart; a settings file
    /// written before it existed has no such key and falls back to showing them.
    /// </summary>
    public bool HideTeamOnlyRequests { get; set; }

    /// <summary>
    /// The repository sections collapsed on each PR tab, keyed by bucket name, so the tabs reopen the way they
    /// were left. A settings file written before it existed has no such key and opens every section.
    /// </summary>
    public Dictionary<string, List<string>> CollapsedRepositories { get; set; } = [];

    public TimeSpan PollInterval => TimeSpan.FromMinutes(PollIntervalMinutes);
}
