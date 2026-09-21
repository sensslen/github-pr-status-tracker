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
public sealed class GitHubClient(GhCommandRunner gh) : IGitHubClient
{
    public async Task<string> GetViewerLoginAsync(CancellationToken ct = default)
    {
        var result = await gh.RunAsync(["api", "user", "--jq", ".login"], ct);
        var login = result.StdOut.Trim();
        if (string.IsNullOrEmpty(login))
        {
            throw new GhException(GhFailureKind.NotAuthenticated, "gh returned no login");
        }

        return login;
    }

    public async Task<IReadOnlyList<PullRequest>> FetchPullRequestsAsync(string login, CancellationToken ct = default)
    {
        var result = await gh.RunAsync(
        [
            "api", "graphql",
            "-f", $"query={Queries.PullRequests}",
            "-F", $"reviewQ={Queries.ReviewRequested(login)}",
            "-F", $"reviewedQ={Queries.ReviewedBy(login)}",
            "-F", $"authoredQ={Queries.Authored(login)}",
        ], ct);

        return ParseResponse(result.StdOut, login);
    }

    internal static IReadOnlyList<PullRequest> ParseResponse(string json, string viewer)
    {
        var root = JsonNode.Parse(json) ?? throw new JsonException("empty response");
        if (root["errors"] is JsonArray { Count: > 0 } errors)
        {
            var kind = errors[0]?["type"]?.GetValue<string>() == "RATE_LIMITED" ? GhFailureKind.RateLimited : GhFailureKind.Other;
            throw new GhException(kind, "GraphQL error: " + errors[0]?["message"]?.GetValue<string>());
        }

        var data = root["data"] ?? throw new JsonException("no data in response");

        // Authored takes precedence when a PR appears in more than one list.
        var byKey = new Dictionary<string, PullRequest>();
        foreach (var (alias, tracksComments) in Queries.Aliases)
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

                var pr = MapPullRequest(node, viewer, tracksComments);
                byKey[pr.Key] = pr;
            }
        }
        return [.. byKey.Values];
    }

    /// <summary>
    /// The newest <b>unresolved</b> thing anyone said on this PR, ignoring your own. If the newest of all is
    /// yours, nothing is reported: you were the last to speak, so there is nothing here you have not read.
    /// <para>
    /// Three sources, each for a reason. Issue comments can never be resolved, so they always count. Inline
    /// conversation counts only while its thread is open — a resolved thread is a finished conversation,
    /// whoever spoke in it last. A review's own body counts only when it is standalone commentary: a body
    /// posted alongside inline comments is a summary of them, and they are what carries the resolution, so
    /// counting it would keep resurrecting a discussion you have already settled.
    /// </para>
    /// </summary>
    private static (DateTimeOffset At, string By)? MapLastComment(JsonNode n, string viewer)
    {
        List<(DateTimeOffset At, string By)> said = [];
        void Add(JsonNode? c)
        {
            if (c?["createdAt"]?.GetValue<DateTimeOffset?>() is { } at
                && c["author"]?["login"]?.GetValue<string>() is { } by)
            {
                said.Add((at, by));
            }
        }

        foreach (var c in n["comments"]?["nodes"]?.AsArray() ?? [])
        {
            Add(c);
        }

        foreach (var thread in n["reviewThreads"]?["nodes"]?.AsArray() ?? [])
        {
            if (thread?["isResolved"]?.GetValue<bool>() != false)
            {
                continue; // resolved, or a shape we cannot read: either way, not waiting on you
            }

            foreach (var c in thread["comments"]?["nodes"]?.AsArray() ?? [])
            {
                Add(c);
            }
        }

        foreach (var review in n["reviews"]?["nodes"]?.AsArray() ?? [])
        {
            var standalone = review?["comments"]?["totalCount"]?.GetValue<int>() == 0;
            if (standalone && !string.IsNullOrWhiteSpace(review?["bodyText"]?.GetValue<string>()))
            {
                Add(review);
            }
        }

        if (said.Count == 0)
        {
            return null;
        }

        var last = said.MaxBy(s => s.At);
        return string.Equals(last.By, viewer, StringComparison.OrdinalIgnoreCase) ? null : last;
    }

    private static PullRequest MapPullRequest(JsonNode n, string viewer, bool tracksComments)
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
        var lastComment = tracksComments ? MapLastComment(n, viewer) : null;

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
            LastCommentAt: lastComment?.At,
            LastCommentBy: lastComment?.By);
    }

    private static TEnum ParseEnum<TEnum>(JsonNode? node, TEnum fallback) where TEnum : struct, Enum
    {
        // GitHub uses SCREAMING_SNAKE_CASE; our enums are PascalCase. Dropping underscores and ignoring case lines them up.
        var raw = node?.GetValue<string>()?.Replace("_", string.Empty);
        return raw is not null && Enum.TryParse<TEnum>(raw, ignoreCase: true, out var value) ? value : fallback;
    }
}
