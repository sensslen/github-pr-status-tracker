namespace PrStatusTracker.Models;

public enum PrBucket { NeedsYou, WaitingOnOthers, ReadyToMerge }

public enum PrStatus
{
    // PRs where you are a requested reviewer
    ReviewerDraft,
    AwaitingYourReview,          // asked for personally, you haven't looked yet
    RequestedViaTeam,            // asked for through a team you belong to
    AwaitingYourRereview,        // you reviewed, the author re-requested you
    NewCommitsSinceYourReview,   // you reviewed, then commits landed; only the delta needs a look
    YouRequestedChanges,
    YouApproved,
    YouCommented,

    // PRs you authored
    Draft,
    MergeConflict,
    ChecksFailing,
    ChangesRequested,
    ReadyToMerge,
    ReadyToMergeConflict,        // approved and green, but the branch no longer merges cleanly
    ApprovedChecksPending,
    AwaitingReview,
    NoReviewers,
}

public static class PrStatusExtensions
{
    /// <summary>Statuses the current user brought about themselves; never worth a toast.</summary>
    public static bool IsSelfInflicted(this PrStatus status) =>
        status is PrStatus.YouApproved or PrStatus.YouRequestedChanges or PrStatus.YouCommented;

    public static bool IsDraft(this PrStatus status) => status is PrStatus.Draft or PrStatus.ReviewerDraft;

    public static PrBucket Bucket(this PrStatus status) => status switch
    {
        PrStatus.AwaitingYourReview or PrStatus.RequestedViaTeam or PrStatus.AwaitingYourRereview
            or PrStatus.NewCommitsSinceYourReview or PrStatus.MergeConflict or PrStatus.ChecksFailing
            or PrStatus.ChangesRequested or PrStatus.NoReviewers => PrBucket.NeedsYou,
        PrStatus.ReadyToMerge or PrStatus.ReadyToMergeConflict => PrBucket.ReadyToMerge,
        _ => PrBucket.WaitingOnOthers,
    };

    public static string Label(this PrStatus status) => status switch
    {
        PrStatus.ReviewerDraft => "Draft",
        PrStatus.AwaitingYourReview => "Review requested",
        PrStatus.RequestedViaTeam => "Requested via team",
        PrStatus.AwaitingYourRereview => "Re-review requested",
        PrStatus.NewCommitsSinceYourReview => "New commits since your review",
        PrStatus.YouRequestedChanges => "You requested changes",
        PrStatus.YouApproved => "You approved",
        PrStatus.YouCommented => "You commented",
        PrStatus.Draft => "Draft",
        PrStatus.MergeConflict => "Merge conflict",
        PrStatus.ChecksFailing => "Checks failing",
        PrStatus.ChangesRequested => "Changes requested",
        PrStatus.ReadyToMerge => "Ready to merge",
        PrStatus.ReadyToMergeConflict => "Approved, merge conflict",
        PrStatus.ApprovedChecksPending => "Approved, checks pending",
        PrStatus.AwaitingReview => "Awaiting review",
        PrStatus.NoReviewers => "No reviewers",
        _ => status.ToString(),
    };

    public static string Label(this PrBucket bucket) => bucket switch
    {
        PrBucket.NeedsYou => "Needs you",
        PrBucket.WaitingOnOthers => "Waiting on others",
        PrBucket.ReadyToMerge => "Ready to merge",
        _ => bucket.ToString(),
    };

    /// <summary>For narrow spaces such as the compact popup's segmented control.</summary>
    public static string ShortLabel(this PrBucket bucket) => bucket switch
    {
        PrBucket.NeedsYou => "Needs you",
        PrBucket.WaitingOnOthers => "Waiting",
        PrBucket.ReadyToMerge => "Ready",
        _ => bucket.ToString(),
    };

    public static string EmptyText(this PrBucket bucket) => bucket switch
    {
        PrBucket.NeedsYou => "Nothing needs you right now.",
        PrBucket.ReadyToMerge => "Nothing is ready to merge.",
        _ => "Nothing is waiting on others.",
    };
}
