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
    public bool NotifyReadyToMerge { get; set; } = true;
    public bool NotifyClosed { get; set; } = true;

    public TimeSpan PollInterval => TimeSpan.FromMinutes(PollIntervalMinutes);
}
