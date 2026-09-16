using PrStatusTracker.Models;

namespace PrStatusTracker.Core;

/// <summary>Reduces a raw <see cref="PullRequest"/> to a single status relative to the current user.
/// The rules mirror the tables in ARCHITECTURE.md section 4.</summary>
public static class StatusEvaluator
{
    public static PrStatus Evaluate(PullRequest pr, string viewer)
        => IsViewer(pr.Author, viewer) ? EvaluateAuthored(pr, viewer) : EvaluateAsReviewer(pr, viewer);

    private static bool IsViewer(string login, string viewer) => string.Equals(login, viewer, StringComparison.OrdinalIgnoreCase);

    private static PrStatus EvaluateAsReviewer(PullRequest pr, string viewer)
    {
        if (pr.IsDraft)
        {
            return PrStatus.ReviewerDraft;
        }

        var mine = pr.LatestReviews
            .Where(r => IsViewer(r.Author, viewer))
            .OrderByDescending(r => r.SubmittedAt)
            .FirstOrDefault();

        var directlyRequested = pr.RequestedReviewers.Any(r => IsViewer(r, viewer));

        // Never looked at it. If your login isn't among the requests, you're here because a team you're on was asked.
        if (mine is null)
        {
            return directlyRequested ? PrStatus.AwaitingYourReview : PrStatus.RequestedViaTeam;
        }

        // A comment is not a decision. If you're still requested it is on you; otherwise you've merely weighed in.
        if (mine.State is ReviewState.Commented or ReviewState.Dismissed or ReviewState.Pending)
        {
            return directlyRequested ? PrStatus.AwaitingYourReview : PrStatus.YouCommented;
        }

        if (directlyRequested)
        {
            return PrStatus.AwaitingYourRereview;
        }

        if (pr.HeadCommittedAt is { } committed && committed > mine.SubmittedAt)
        {
            return PrStatus.NewCommitsSinceYourReview;
        }

        return mine.State == ReviewState.ChangesRequested ? PrStatus.YouRequestedChanges : PrStatus.YouApproved;
    }

    private static PrStatus EvaluateAuthored(PullRequest pr, string viewer)
    {
        if (pr.IsDraft)
        {
            return PrStatus.Draft;
        }

        if (pr.Mergeable == Mergeable.Conflicting)
        {
            return PrStatus.MergeConflict;
        }

        if (pr.Checks == CheckState.Failure)
        {
            return PrStatus.ChecksFailing;
        }

        if (pr.ReviewDecision == ReviewDecision.ChangesRequested)
        {
            return PrStatus.ChangesRequested;
        }

        if (pr.ReviewDecision == ReviewDecision.Approved)
        {
            return pr.Checks == CheckState.Pending ? PrStatus.ApprovedChecksPending : PrStatus.ReadyToMerge;
        }

        var othersInvolved = pr.RequestedReviewers.Count > 0
            || pr.LatestReviews.Any(r => !IsViewer(r.Author, viewer));
        return othersInvolved ? PrStatus.AwaitingReview : PrStatus.NoReviewers;
    }
}
