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
    [InlineData(PrStatus.YouApproved, PrBucket.WaitingOnOthers)]
    [InlineData(PrStatus.YouRequestedChanges, PrBucket.WaitingOnOthers)]
    [InlineData(PrStatus.YouCommented, PrBucket.WaitingOnOthers)]
    [InlineData(PrStatus.ReviewerDraft, PrBucket.WaitingOnOthers)]
    [InlineData(PrStatus.Draft, PrBucket.YourPrs)]
    [InlineData(PrStatus.MergeConflict, PrBucket.YourPrs)]
    [InlineData(PrStatus.ChecksFailing, PrBucket.YourPrs)]
    [InlineData(PrStatus.ChangesRequested, PrBucket.YourPrs)]
    [InlineData(PrStatus.ReadyToMerge, PrBucket.YourPrs)]
    [InlineData(PrStatus.ReadyToMergeConflict, PrBucket.YourPrs)]
    [InlineData(PrStatus.ApprovedChecksPending, PrBucket.YourPrs)]
    [InlineData(PrStatus.AwaitingReview, PrBucket.YourPrs)]
    [InlineData(PrStatus.NoReviewers, PrBucket.YourPrs)]
    public void Bucket_mapping(PrStatus status, PrBucket expected) => Assert.Equal(expected, status.Bucket());

    /// <summary>
    /// The partition the whole tab split rests on, pinned to the evaluator that produces it: whatever
    /// `Evaluate` returns for a PR of yours must read as authored, and nothing it returns for anyone else's
    /// may. Without this, a status added to one side and forgotten on the other misfiles silently.
    /// </summary>
    [Fact]
    public void IsAuthored_agrees_with_the_evaluator()
    {
        PullRequest[] cases =
        [
            Pr(author: Me), Pr(author: Me, draft: true),
            Pr(author: Me, decision: ReviewDecision.Approved), Pr(author: Me, decision: ReviewDecision.ChangesRequested),
            Pr(author: Me, checks: CheckState.Failure), Pr(author: Me, mergeable: Mergeable.Conflicting),
            Pr(author: Me, checks: CheckState.Pending, decision: ReviewDecision.Approved),
            Pr(author: Me, requested: [Other]),
            Pr(author: Other), Pr(author: Other, draft: true),
            Pr(author: Other, requested: [Me]),
            Pr(author: Other, reviews: [new Review(Me, ReviewState.Approved, T0)]),
            Pr(author: Other, reviews: [new Review(Me, ReviewState.ChangesRequested, T0)]),
            Pr(author: Other, reviews: [new Review(Me, ReviewState.Commented, T0)]),
            Pr(author: Other, reviews: [new Review(Me, ReviewState.Approved, T0)], committed: T0.AddHours(1)),
        ];

        foreach (var pr in cases)
        {
            var status = StatusEvaluator.Evaluate(pr, Me);
            Assert.Equal(pr.Author == Me, status.IsAuthored());
            Assert.Equal(pr.Author == Me, status.Bucket() == PrBucket.YourPrs);
        }
    }

    /// <summary>Everything you authored lands in one tab, so the callout is what sorts and colours it there.</summary>
    [Theory]
    [InlineData(PrStatus.ReadyToMerge, PrCallout.ReadyToMerge)]
    [InlineData(PrStatus.ChecksFailing, PrCallout.ChecksFailing)]
    [InlineData(PrStatus.MergeConflict, PrCallout.MergeConflict)]
    [InlineData(PrStatus.ReadyToMergeConflict, PrCallout.MergeConflict)]
    [InlineData(PrStatus.ChangesRequested, PrCallout.ChangesRequested)]
    [InlineData(PrStatus.NoReviewers, PrCallout.NoReviewers)]
    [InlineData(PrStatus.Draft, PrCallout.None)]
    [InlineData(PrStatus.AwaitingReview, PrCallout.None)]
    [InlineData(PrStatus.ApprovedChecksPending, PrCallout.None)]
    public void Callout_mapping(PrStatus status, PrCallout expected) => Assert.Equal(expected, status.Callout());

    /// <summary>
    /// The two named distinctions the tray, the summary line and the notifier all key off. They differ on
    /// purpose: a PR that is simply ready to merge is news worth a toast, but is not work, so it earns the
    /// green tray icon rather than the orange one.
    /// </summary>
    [Theory]
    [InlineData(PrCallout.None, false)]
    [InlineData(PrCallout.ReadyToMerge, false)]
    [InlineData(PrCallout.ChecksFailing, true)]
    [InlineData(PrCallout.MergeConflict, true)]
    [InlineData(PrCallout.ChangesRequested, true)]
    [InlineData(PrCallout.OpenThreads, true)]
    [InlineData(PrCallout.NoReviewers, true)]
    public void IsWork_is_every_callout_but_a_pr_you_can_just_merge(PrCallout callout, bool expected) =>
        Assert.Equal(expected, callout.IsWork());

    [Theory]
    [InlineData(PrStatus.AwaitingYourReview, true)]      // someone wants your review
    [InlineData(PrStatus.RequestedViaTeam, true)]
    [InlineData(PrStatus.ChecksFailing, true)]           // one of yours, and broken
    [InlineData(PrStatus.ReadyToMerge, true)]            // one of yours, and there is a button to press
    [InlineData(PrStatus.Draft, false)]
    [InlineData(PrStatus.AwaitingReview, false)]         // one of yours, ticking along
    [InlineData(PrStatus.ApprovedChecksPending, false)]
    [InlineData(PrStatus.YouApproved, false)]            // you already did your part
    [InlineData(PrStatus.ReviewerDraft, false)]
    public void IsAskingSomething_is_the_test_for_whether_a_pr_turning_up_is_news(PrStatus status, bool expected) =>
        Assert.Equal(expected, status.IsAskingSomething());

    /// <summary>A PR you are reviewing never carries a callout — the Needs you tab is the callout.</summary>
    [Theory]
    [InlineData(PrStatus.AwaitingYourReview)]
    [InlineData(PrStatus.RequestedViaTeam)]
    [InlineData(PrStatus.AwaitingYourRereview)]
    [InlineData(PrStatus.NewCommitsSinceYourReview)]
    [InlineData(PrStatus.YouApproved)]
    [InlineData(PrStatus.YouRequestedChanges)]
    [InlineData(PrStatus.YouCommented)]
    [InlineData(PrStatus.ReviewerDraft)]
    public void Reviewer_statuses_have_no_callout(PrStatus status) => Assert.Equal(PrCallout.None, status.Callout());
}
