using System.Text.Json;
using System.Text.Json.Nodes;
using PrStatusTracker.Models;

namespace PrStatusTracker.GitHub;

public interface IGitHubClient
{
    Task<string> GetViewerLoginAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PullRequest>> FetchPullRequestsAsync(string login, CancellationToken ct = default);
}

/// <summary>Fetches PR data through `gh api graphql` and maps it to <see cref="PullRequest"/>.</summary>
public sealed class GitHubClient : IGitHubClient
{
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<GhResult>> _run;

    /// <summary>
    /// How many more polls a "nothing waiting" answer is reused for. Reopening a thread is not guaranteed to move
    /// updatedAt, so the answer has to expire for a reopened thread to come back.
    /// </summary>
    internal const int QuietReuses = 4;

    private Dictionary<string, Quiet> _quiet = [];

    private sealed record Quiet(DateTimeOffset UpdatedAt, int ReusesLeft);

    public GitHubClient(GhCommandRunner gh) : this(gh.RunAsync) { }

    internal GitHubClient(Func<IReadOnlyList<string>, CancellationToken, Task<GhResult>> run) => _run = run;

    public async Task<string> GetViewerLoginAsync(CancellationToken ct = default)
    {
        var result = await _run(["api", "user", "--jq", ".login"], ct);
        var login = result.StdOut.Trim();
        if (string.IsNullOrEmpty(login))
        {
            throw new GhException(GhFailureKind.NotAuthenticated, "gh returned no login");
        }

        return login;
    }

    public async Task<IReadOnlyList<PullRequest>> FetchPullRequestsAsync(string login, CancellationToken ct = default)
    {
        var result = await _run(
        [
            "api", "graphql",
            "-f", $"query={Queries.PullRequests}",
            "-F", $"reviewQ={Queries.ReviewRequested(login)}",
            "-F", $"reviewedQ={Queries.ReviewedBy(login)}",
            "-F", $"authoredQ={Queries.Authored(login)}",
        ], ct);

        var data = ParseData(result.StdOut);
        var updatedAt = (data[Queries.AuthoredAlias]?["nodes"]?.AsArray() ?? [])
            .Where(pr => pr?["id"] is not null)
            .ToDictionary(
                pr => pr!["id"]!.GetValue<string>(),
                pr => pr!["updatedAt"]?.GetValue<DateTimeOffset>() ?? DateTimeOffset.MinValue);

        var quiet = new Dictionary<string, Quiet>();
        var ask = new List<string>();
        foreach (var (id, at) in updatedAt)
        {
            if (_quiet.TryGetValue(id, out var q) && q.UpdatedAt == at && q.ReusesLeft > 0)
            {
                quiet[id] = q with { ReusesLeft = q.ReusesLeft - 1 };
            }
            else
            {
                ask.Add(id);
            }
        }

        var open = new HashSet<string>();
        foreach (var (id, isOpen) in await CheckThreadsAsync(ask, login, ct))
        {
            if (isOpen)
            {
                open.Add(id);
            }
            else
            {
                quiet[id] = new Quiet(updatedAt[id], QuietReuses);
            }
        }

        _quiet = quiet;
        return MapResponse(data, open);
    }

    private async Task<List<(string Id, bool Open)>> CheckThreadsAsync(IReadOnlyList<string> ids, string viewer, CancellationToken ct)
    {
        var results = new List<(string, bool)>();
        if (ids.Count == 0)
        {
            return results;
        }

        foreach (var (id, first) in await ThreadPagesAsync(ids, null, ct))
        {
            var threads = first;
            var isOpen = HasActionable(threads, viewer);
            while (!isOpen && threads["pageInfo"]?["hasNextPage"]?.GetValue<bool>() == true)
            {
                var cursor = threads["pageInfo"]?["endCursor"]?.GetValue<string>()
                    ?? throw new JsonException("Missing review-thread cursor");
                threads = (await ThreadPagesAsync([id], cursor, ct)).Single().Threads;
                isOpen = HasActionable(threads, viewer);
            }

            results.Add((id, isOpen));
        }

        return results;
    }

    private async Task<IEnumerable<(string Id, JsonNode Threads)>> ThreadPagesAsync(
        IReadOnlyList<string> ids, string? cursor, CancellationToken ct)
    {
        List<string> args = ["api", "graphql", "-f", $"query={Queries.ReviewThreads}", .. ids.SelectMany(id => new[] { "-f", $"ids[]={id}" })];
        if (cursor is not null)
        {
            args.AddRange(["-f", $"cursor={cursor}"]);
        }

        var nodes = ParseData((await _run(args, ct)).StdOut)["nodes"]?.AsArray() ?? throw new JsonException("Missing review threads");
        return nodes.Select(pr => (
            pr?["id"]?.GetValue<string>() ?? throw new JsonException("Missing pull request"),
            pr["reviewThreads"] is { } threads && threads["nodes"] is JsonArray ? threads : throw new JsonException("Missing review threads")));
    }

    private static JsonNode ParseData(string json)
    {
        var root = JsonNode.Parse(json) ?? throw new JsonException("empty response");
        if (root["errors"] is JsonArray { Count: > 0 } errors)
        {
            var kind = errors[0]?["type"]?.GetValue<string>() == "RATE_LIMITED" ? GhFailureKind.RateLimited : GhFailureKind.Other;
            throw new GhException(kind, "GraphQL error: " + errors[0]?["message"]?.GetValue<string>());
        }

        return root["data"] ?? throw new JsonException("no data in response");
    }

    internal static IReadOnlyList<PullRequest> ParseResponse(string json) => MapResponse(ParseData(json), new HashSet<string>());

    private static IReadOnlyList<PullRequest> MapResponse(JsonNode data, IReadOnlySet<string> withOpenThreads)
    {
        // Authored takes precedence when a PR appears in more than one list.
        var byKey = new Dictionary<string, PullRequest>();
        foreach (var alias in Queries.Aliases)
        {
            if (data[alias]?["nodes"] is not JsonArray nodes)
            {
                continue;
            }

            foreach (var node in nodes)
            {
                if (node is null || node["number"] is null)
                {
                    continue; // non-PR search hits have no fields
                }

                var pr = MapPullRequest(node, withOpenThreads);
                byKey[pr.Key] = pr;
            }
        }
        return [.. byKey.Values];
    }

    private static bool HasActionable(JsonNode threads, string viewer) =>
        threads["nodes"] is JsonArray nodes && nodes.Any(thread => IsActionable(thread, viewer));

    /// <summary>A pending comment is yours and unsent, so the thread is still waiting on whoever wrote before it.</summary>
    private static bool IsActionable(JsonNode? thread, string viewer) =>
        thread?["isResolved"]?.GetValue<bool>() == false
        && thread["comments"]?["nodes"]?.AsArray()
            .LastOrDefault(c => c?["state"]?.GetValue<string>() != "PENDING")?["author"]?["login"]?.GetValue<string>() is { } by
        && !string.Equals(by, viewer, StringComparison.OrdinalIgnoreCase);

    private static PullRequest MapPullRequest(JsonNode n, IReadOnlySet<string> withOpenThreads)
    {
        var reviewers = new List<string>();
        foreach (var r in n["reviewRequests"]?["nodes"]?.AsArray() ?? [])
        {
            var rr = r?["requestedReviewer"];
            var name = rr?["login"]?.GetValue<string>() ?? rr?["name"]?.GetValue<string>();
            if (name is not null)
            {
                reviewers.Add(name);
            }
        }

        var reviews = new List<Review>();
        foreach (var r in n["latestReviews"]?["nodes"]?.AsArray() ?? [])
        {
            if (r is null)
            {
                continue;
            }

            var author = r["author"]?["login"]?.GetValue<string>();
            var submitted = r["submittedAt"]?.GetValue<DateTimeOffset?>();
            if (author is null || submitted is null)
            {
                continue;
            }

            reviews.Add(new Review(author, ParseEnum(r["state"], ReviewState.Commented), submitted.Value));
        }

        var commit = n["commits"]?["nodes"]?.AsArray().FirstOrDefault()?["commit"];
        var rollup = commit?["statusCheckRollup"]?["state"]?.GetValue<string>();

        return new PullRequest(
            Repo: n["repository"]?["nameWithOwner"]?.GetValue<string>() ?? "?",
            Number: n["number"]!.GetValue<int>(),
            Title: n["title"]?.GetValue<string>() ?? string.Empty,
            Url: n["url"]?.GetValue<string>() ?? string.Empty,
            IsDraft: n["isDraft"]?.GetValue<bool>() ?? false,
            Author: n["author"]?["login"]?.GetValue<string>() ?? "?",
            CreatedAt: n["createdAt"]?.GetValue<DateTimeOffset>() ?? DateTimeOffset.MinValue,
            UpdatedAt: n["updatedAt"]?.GetValue<DateTimeOffset>() ?? DateTimeOffset.MinValue,
            Additions: n["additions"]?.GetValue<int>() ?? 0,
            Deletions: n["deletions"]?.GetValue<int>() ?? 0,
            Labels: (n["labels"]?["nodes"]?.AsArray() ?? [])
                .Select(l => l?["name"]?.GetValue<string>())
                .Where(name => name is not null)
                .ToList()!,
            ReviewDecision: n["reviewDecision"] is { } rd ? ParseEnum(rd, Models.ReviewDecision.ReviewRequired) : null,
            Mergeable: ParseEnum(n["mergeable"], Mergeable.Unknown),
            RequestedReviewers: reviewers,
            LatestReviews: reviews,
            HeadCommittedAt: commit?["committedDate"]?.GetValue<DateTimeOffset?>(),
            Checks: rollup switch
            {
                null => null,
                "SUCCESS" => CheckState.Success,
                "FAILURE" or "ERROR" => CheckState.Failure,
                _ => CheckState.Pending, // PENDING, EXPECTED
            },
            HasOpenThreads: n["id"]?.GetValue<string>() is { } id && withOpenThreads.Contains(id));
    }

    private static TEnum ParseEnum<TEnum>(JsonNode? node, TEnum fallback) where TEnum : struct, Enum
    {
        // GitHub uses SCREAMING_SNAKE_CASE; our enums are PascalCase. Dropping underscores and ignoring case lines them up.
        var raw = node?.GetValue<string>()?.Replace("_", string.Empty);
        return raw is not null && Enum.TryParse<TEnum>(raw, ignoreCase: true, out var value) ? value : fallback;
    }
}
