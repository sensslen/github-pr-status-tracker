namespace PrStatusTracker.Models;

public sealed class NotificationRecord
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required string Url { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public bool Dismissed { get; set; }
}
