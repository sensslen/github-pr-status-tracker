using PrStatusTracker.Core;
using PrStatusTracker.Models;

namespace PrStatusTracker.Tests;

internal static class TestData
{
    public const string Me = "owen";
    public const string Other = "alice";
    public static readonly DateTimeOffset T0 = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    public static PullRequest Pr(
        int number = 1,
        string author = Other,
        bool draft = false,
        ReviewDecision? decision = null,
        Mergeable mergeable = Mergeable.Mergeable,
        CheckState? checks = CheckState.Success,
        IReadOnlyList<string>? requested = null,
        IReadOnlyList<Review>? reviews = null,
        DateTimeOffset? committed = null,
        IReadOnlyList<string>? labels = null,
        string repo = "org/repo",
        string? title = null) =>
        new(repo, number, title ?? $"PR {number}", $"https://github.com/{repo}/pull/{number}", draft, author,
            CreatedAt: T0.AddDays(-1), UpdatedAt: T0, Additions: 10, Deletions: 2, Labels: labels ?? [],
            decision, mergeable, requested ?? [], reviews ?? [], committed ?? T0, checks);

    public static TrackedPr Tracked(PrStatus status, int number = 1, string author = Other) =>
        new(Pr(number: number, author: author), status);

    /// <summary>A fresh path in the tests' scratch directory. Nothing is created until something writes there.</summary>
    public static string TempPath() => Path.Combine(Path.GetTempPath(), "prst-tests", Guid.NewGuid() + ".json");

    /// <summary>A store backed by a fresh temp file; the returned path lets the test reopen or delete it.</summary>
    public static PrStore TempStore(out string path)
    {
        path = TempPath();
        return new PrStore(new JsonFileStore<List<TrackedPr>>(path));
    }
}
