using PrStatusTracker.Core;
using PrStatusTracker.Models;
using Xunit;
using static PrStatusTracker.Tests.TestData;

namespace PrStatusTracker.Tests;

public class StatusEvaluatorTests
{
    private static Review Mine(ReviewState state, int hoursAfterT0 = 1) => new(Me, state, T0.AddHours(hoursAfterT0));

    // --- as reviewer ---------------------------------------------------------------------------------------

    [Fact]
    public void Reviewer_draft() =>
        Assert.Equal(PrStatus.ReviewerDraft, StatusEvaluator.Evaluate(Pr(draft: true, requested: [Me]), Me));

    [Fact]
    public void Reviewer_not_yet_reviewed() =>
        Assert.Equal(PrStatus.AwaitingYourReview, StatusEvaluator.Evaluate(Pr(requested: [Me]), Me));

    [Fact]
    public void Reviewer_requested_only_through_a_team() =>
        Assert.Equal(PrStatus.RequestedViaTeam, StatusEvaluator.Evaluate(Pr(requested: ["platform-team"]), Me));

    [Fact]
    public void Reviewer_only_commented_while_requested_is_still_awaiting() =>
        Assert.Equal(PrStatus.AwaitingYourReview, StatusEvaluator.Evaluate(Pr(requested: [Me], reviews: [Mine(ReviewState.Commented)]), Me));

    [Fact]
    public void Reviewer_only_commented_without_request_is_you_commented() =>
        Assert.Equal(PrStatus.YouCommented, StatusEvaluator.Evaluate(Pr(reviews: [Mine(ReviewState.Commented)]), Me));

    [Fact]
    public void Reviewer_approved() =>
        Assert.Equal(PrStatus.YouApproved, StatusEvaluator.Evaluate(Pr(reviews: [Mine(ReviewState.Approved)]), Me));

    [Fact]
    public void Reviewer_requested_changes() =>
        Assert.Equal(PrStatus.YouRequestedChanges, StatusEvaluator.Evaluate(Pr(reviews: [Mine(ReviewState.ChangesRequested)]), Me));

    [Fact]
    public void Reviewer_rerequested_after_review() =>
        Assert.Equal(PrStatus.AwaitingYourRereview,
            StatusEvaluator.Evaluate(Pr(requested: [Me], reviews: [Mine(ReviewState.Approved)]), Me));

    [Fact]
    public void Reviewer_new_push_after_approval_needs_another_look() =>
        Assert.Equal(PrStatus.NewCommitsSinceYourReview,
            StatusEvaluator.Evaluate(Pr(reviews: [Mine(ReviewState.Approved, hoursAfterT0: 1)], committed: T0.AddHours(2)), Me));

    [Fact]
    public void Reviewer_login_comparison_is_case_insensitive() =>
        Assert.Equal(PrStatus.YouApproved,
            StatusEvaluator.Evaluate(Pr(reviews: [new Review("OWEN", ReviewState.Approved, T0.AddHours(1))]), Me));

    // --- as author -----------------------------------------------------------------------------------------

    [Fact]
    public void Author_draft() =>
        Assert.Equal(PrStatus.Draft, StatusEvaluator.Evaluate(Pr(author: Me, draft: true), Me));

    /// <summary>A conflict is yours to fix either way; what it changes is which bucket you fix it from.
    /// Only a PR that is otherwise ready to merge is called out in *Ready to merge* rather than *Needs you*.</summary>
    [Theory]
    [InlineData(ReviewDecision.Approved, CheckState.Success, PrStatus.ReadyToMergeConflict)]
    [InlineData(ReviewDecision.Approved, null, PrStatus.ReadyToMergeConflict)]           // repo with no checks
    [InlineData(ReviewDecision.Approved, CheckState.Pending, PrStatus.MergeConflict)]    // not ready until they land
    [InlineData(ReviewDecision.Approved, CheckState.Failure, PrStatus.MergeConflict)]
    [InlineData(ReviewDecision.ChangesRequested, CheckState.Success, PrStatus.MergeConflict)]
    [InlineData(null, CheckState.Success, PrStatus.MergeConflict)]                       // nobody has signed off
    public void Author_conflict(ReviewDecision? decision, CheckState? checks, PrStatus expected) =>
        Assert.Equal(expected, StatusEvaluator.Evaluate(
            Pr(author: Me, mergeable: Mergeable.Conflicting, decision: decision, checks: checks), Me));

    [Fact]
    public void Author_checks_failing_beats_review_state() =>
        Assert.Equal(PrStatus.ChecksFailing,
            StatusEvaluator.Evaluate(Pr(author: Me, decision: ReviewDecision.Approved, checks: CheckState.Failure), Me));

    [Fact]
    public void Author_changes_requested() =>
        Assert.Equal(PrStatus.ChangesRequested, StatusEvaluator.Evaluate(Pr(author: Me, decision: ReviewDecision.ChangesRequested), Me));

    [Fact]
    public void Author_approved_and_green_is_ready() =>
        Assert.Equal(PrStatus.ReadyToMerge, StatusEvaluator.Evaluate(Pr(author: Me, decision: ReviewDecision.Approved), Me));

    [Fact]
    public void Author_approved_with_no_checks_is_ready() =>
        Assert.Equal(PrStatus.ReadyToMerge, StatusEvaluator.Evaluate(Pr(author: Me, decision: ReviewDecision.Approved, checks: null), Me));

    [Fact]
    public void Author_approved_checks_pending() =>
        Assert.Equal(PrStatus.ApprovedChecksPending,
            StatusEvaluator.Evaluate(Pr(author: Me, decision: ReviewDecision.Approved, checks: CheckState.Pending), Me));

    [Fact]
    public void Author_awaiting_review_when_reviewers_requested() =>
        Assert.Equal(PrStatus.AwaitingReview, StatusEvaluator.Evaluate(Pr(author: Me, requested: [Other]), Me));

    [Fact]
    public void Author_awaiting_review_when_someone_commented() =>
        Assert.Equal(PrStatus.AwaitingReview,
            StatusEvaluator.Evaluate(Pr(author: Me, reviews: [new Review(Other, ReviewState.Commented, T0)]), Me));

    [Fact]
    public void Author_no_reviewers() =>
        Assert.Equal(PrStatus.NoReviewers, StatusEvaluator.Evaluate(Pr(author: Me), Me));

    [Fact]
    public void Author_null_checks_do_not_read_as_failing() =>
        Assert.Equal(PrStatus.NoReviewers, StatusEvaluator.Evaluate(Pr(author: Me, checks: null), Me));

    // --- buckets --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(PrStatus.AwaitingYourReview, PrBucket.NeedsYou)]
    [InlineData(PrStatus.RequestedViaTeam, PrBucket.NeedsYou)]
    [InlineData(PrStatus.AwaitingYourRereview, PrBucket.NeedsYou)]
    [InlineData(PrStatus.NewCommitsSinceYourReview, PrBucket.NeedsYou)]
    [InlineData(PrStatus.MergeConflict, PrBucket.NeedsYou)]
    [InlineData(PrStatus.ChecksFailing, PrBucket.NeedsYou)]
    [InlineData(PrStatus.ChangesRequested, PrBucket.NeedsYou)]
    [InlineData(PrStatus.NoReviewers, PrBucket.NeedsYou)]
    [InlineData(PrStatus.ReadyToMerge, PrBucket.ReadyToMerge)]
    [InlineData(PrStatus.ReadyToMergeConflict, PrBucket.ReadyToMerge)]
    [InlineData(PrStatus.YouApproved, PrBucket.WaitingOnOthers)]
    [InlineData(PrStatus.YouRequestedChanges, PrBucket.WaitingOnOthers)]
    [InlineData(PrStatus.YouCommented, PrBucket.WaitingOnOthers)]
    [InlineData(PrStatus.Draft, PrBucket.WaitingOnOthers)]
    [InlineData(PrStatus.ReviewerDraft, PrBucket.WaitingOnOthers)]
    [InlineData(PrStatus.ApprovedChecksPending, PrBucket.WaitingOnOthers)]
    [InlineData(PrStatus.AwaitingReview, PrBucket.WaitingOnOthers)]
    public void Bucket_mapping(PrStatus status, PrBucket expected) => Assert.Equal(expected, status.Bucket());
}
