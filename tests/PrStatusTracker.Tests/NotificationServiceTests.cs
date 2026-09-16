using PrStatusTracker.Core;
using PrStatusTracker.Models;
using PrStatusTracker.Notifications;
using Xunit;
using static PrStatusTracker.Tests.TestData;

namespace PrStatusTracker.Tests;

public class NotificationServiceTests
{
    [Fact]
    public void New_review_request_notifies_with_pr_title()
    {
        var record = NotificationService.ToRecord(new PrChange(PrChangeKind.Appeared, Tracked(PrStatus.AwaitingYourReview, 9), null), new AppSettings());
        Assert.NotNull(record);
        Assert.Equal("Review requested — PR 9", record.Title);
        Assert.Equal("org/repo#9 · alice", record.Body);
    }

    [Fact]
    public void Your_own_new_pr_is_quiet_unless_it_already_needs_you()
    {
        Assert.Null(NotificationService.ToRecord(new PrChange(PrChangeKind.Appeared, Tracked(PrStatus.AwaitingReview, author: Me), null), new AppSettings()));
        Assert.NotNull(NotificationService.ToRecord(new PrChange(PrChangeKind.Appeared, Tracked(PrStatus.ChecksFailing, author: Me), null), new AppSettings()));
    }

    [Fact]
    public void Status_change_uses_status_label_and_respects_bucket_preference()
    {
        var change = new PrChange(PrChangeKind.StatusChanged, Tracked(PrStatus.ReadyToMerge, author: Me), PrStatus.AwaitingReview);

        Assert.Equal("Ready to merge — PR 1", NotificationService.ToRecord(change, new AppSettings())?.Title);
        Assert.Null(NotificationService.ToRecord(change, new AppSettings { NotifyReadyToMerge = false }));
    }

    [Fact]
    public void Draft_you_are_requested_on_is_quiet_until_ready()
    {
        Assert.Null(NotificationService.ToRecord(new PrChange(PrChangeKind.Appeared, Tracked(PrStatus.ReviewerDraft), null), new AppSettings()));
        Assert.Null(NotificationService.ToRecord(new PrChange(PrChangeKind.StatusChanged, Tracked(PrStatus.ReviewerDraft), PrStatus.AwaitingYourReview), new AppSettings()));
        Assert.Null(NotificationService.ToRecord(new PrChange(PrChangeKind.StatusChanged, Tracked(PrStatus.Draft, author: Me), PrStatus.AwaitingReview), new AppSettings()));
        Assert.StartsWith("Review requested",
            NotificationService.ToRecord(new PrChange(PrChangeKind.StatusChanged, Tracked(PrStatus.AwaitingYourReview), PrStatus.ReviewerDraft), new AppSettings())?.Title);
    }

    [Theory]
    [InlineData(PrStatus.YouApproved)]
    [InlineData(PrStatus.YouRequestedChanges)]
    [InlineData(PrStatus.YouCommented)]
    public void Your_own_review_does_not_toast(PrStatus status) =>
        Assert.Null(NotificationService.ToRecord(new PrChange(PrChangeKind.StatusChanged, Tracked(status), PrStatus.AwaitingYourReview), new AppSettings()));

    [Fact]
    public void Closed_pr_respects_preference()
    {
        var change = new PrChange(PrChangeKind.Disappeared, Tracked(PrStatus.YouApproved), PrStatus.YouApproved);

        Assert.Equal("No longer on your list — PR 1", NotificationService.ToRecord(change, new AppSettings())?.Title);
        Assert.Null(NotificationService.ToRecord(change, new AppSettings { NotifyClosed = false }));
    }

    [Fact]
    public void Single_record_toasts_as_itself()
    {
        var record = new NotificationRecord { Title = "T", Body = "B", Url = "https://github.com/o/r/pull/1" };
        Assert.Equal(new Toast("T", "B", record.Url), NotificationService.Compose([record]));
    }

    [Fact]
    public void Many_records_collapse_into_one_toast()
    {
        var records = Enumerable.Range(1, 6)
            .Select(i => new NotificationRecord { Title = $"Title {i}", Body = "B", Url = $"https://github.com/o/r/pull/{i}" })
            .ToList();

        var toast = NotificationService.Compose(records);

        Assert.Equal("6 pull request updates", toast.Title);
        Assert.Equal("Title 1\nTitle 2\nTitle 3\nTitle 4\n…and 2 more", toast.Body);
        Assert.Equal("https://github.com/pulls", toast.Url);
    }
}
