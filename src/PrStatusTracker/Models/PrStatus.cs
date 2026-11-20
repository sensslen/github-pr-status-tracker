namespace PrStatusTracker.Models;

public enum PrBucket { NeedsYou, WaitingOnOthers, YourPrs }

/// <summary>
/// Why a PR of yours is calling for attention, in the order the Your PRs tab sorts them: the merge button
/// first, then the things standing between it and that button. <see cref="None"/> means it is ticking along
/// quietly and needs nothing from you.
/// </summary>
public enum PrCallout
{
    // First because it is the enum's default, which is the opposite of its priority. The Your PRs sort relies
    // on NeedsAttention having already partitioned the quiet rows out before it orders by this.
    None,
    ReadyToMerge,
    ChecksFailing,
    MergeConflict,
    ChangesRequested,
    OpenThreads,
    NoReviewers,
}

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

    /// <summary>
    /// Which tab a PR belongs to. The split is simply whether you wrote it: everything you authored is yours to
    /// deal with and lives together, whatever state it is in, and the other two tabs are other people's PRs
    /// before and after you have reviewed them. Both sides are listed positively, so a status added to the enum
    /// and forgotten here falls through to Needs you — visible, rather than silently filed among your own.
    /// </summary>
    public static PrBucket Bucket(this PrStatus status) => status switch
    {
        PrStatus.Draft or PrStatus.MergeConflict or PrStatus.ChecksFailing or PrStatus.ChangesRequested
            or PrStatus.ReadyToMerge or PrStatus.ReadyToMergeConflict or PrStatus.ApprovedChecksPending
            or PrStatus.AwaitingReview or PrStatus.NoReviewers => PrBucket.YourPrs,
        PrStatus.ReviewerDraft or PrStatus.YouRequestedChanges or PrStatus.YouApproved
            or PrStatus.YouCommented => PrBucket.WaitingOnOthers,
        _ => PrBucket.NeedsYou,
    };

    /// <summary>
    /// Whether <see cref="Core.StatusEvaluator"/> produced this for a PR you wrote. The partition decides the
    /// tab, the callout and which toast preference applies, so it is stated once — as the Your PRs arm of
    /// <see cref="Bucket"/> — and pinned to the evaluator by test.
    /// </summary>
    public static bool IsAuthored(this PrStatus status) => status.Bucket() == PrBucket.YourPrs;

    /// <summary>
    /// Whether anything is being asked of this PR right now: someone wants your review, or it is one of yours
    /// with a callout. The test for whether a PR turning up in your lists is news worth a toast.
    /// </summary>
    public static bool IsAskingSomething(this PrStatus status) =>
        status.Bucket() == PrBucket.NeedsYou || status.Callout() != PrCallout.None;

    /// <summary>
    /// Thread callouts stay separate from status so they do not produce status-change notifications.
    /// A conflicted PR is never "ready", approved and green or not: the merge button will not work until it
    /// has been rebased, so it is called out as the conflict it is.
    /// </summary>
    public static PrCallout Callout(this PrStatus status) => status switch
    {
        PrStatus.ReadyToMerge => PrCallout.ReadyToMerge,
        PrStatus.ChecksFailing => PrCallout.ChecksFailing,
        PrStatus.MergeConflict or PrStatus.ReadyToMergeConflict => PrCallout.MergeConflict,
        PrStatus.ChangesRequested => PrCallout.ChangesRequested,
        PrStatus.NoReviewers => PrCallout.NoReviewers,
        _ => PrCallout.None,
    };

    /// <summary>
    /// The status callout leads so an open thread cannot hide failing CI or a merge conflict.
    /// <paramref name="hasUnresolvedThreads"/> covers threads still waiting on a reviewer too. Those stay quiet,
    /// except on a PR that is otherwise ready: a branch that requires conversation resolution will not merge it,
    /// so it is an open-threads PR, not a ready one.
    /// </summary>
    public static IReadOnlyList<PrCallout> Callouts(this PrStatus status, bool hasOpenThreads, bool hasUnresolvedThreads = false) =>
        (status.Callout(), hasOpenThreads) switch
        {
            (PrCallout.ReadyToMerge, _) when hasOpenThreads || hasUnresolvedThreads => [PrCallout.OpenThreads],
            // Built once per row on every rebuild of a list that does not virtualise, and most rows want
            // nothing, so the quiet case has to cost no allocation: `[]` lowers to Array.Empty.
            (PrCallout.None, false) => [],
            (PrCallout.None, true) => [PrCallout.OpenThreads],
            (var fromStatus, false) => [fromStatus],
            (var fromStatus, _) => [fromStatus, PrCallout.OpenThreads],
        };

    /// <summary>
    /// Something to do, as opposed to something to enjoy: every callout but a PR that is simply ready to merge.
    /// What the tray lights up orange for, and what the summary line counts as needing attention.
    /// </summary>
    public static bool IsWork(this PrCallout callout) => callout is not (PrCallout.None or PrCallout.ReadyToMerge);

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
        PrBucket.YourPrs => "Your PRs",
        _ => bucket.ToString(),
    };

    public static string EmptyText(this PrBucket bucket) => bucket switch
    {
        PrBucket.NeedsYou => "Nothing needs you right now.",
        PrBucket.YourPrs => "You have no open pull requests.",
        _ => "Nothing is waiting on others.",
    };

    /// <summary>Wording for the attention strip on the Your PRs tab; plural-agnostic, so it reads after a count.</summary>
    public static string Label(this PrCallout callout) => callout switch
    {
        PrCallout.ReadyToMerge => "ready to merge",
        PrCallout.ChecksFailing => "CI failing",
        PrCallout.MergeConflict => "conflicted",
        PrCallout.ChangesRequested => "changes requested",
        PrCallout.OpenThreads => "with open threads",
        PrCallout.NoReviewers => "without reviewers",
        _ => callout.ToString(),
    };
}
