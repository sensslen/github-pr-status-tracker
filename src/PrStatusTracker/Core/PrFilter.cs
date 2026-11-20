using PrStatusTracker.Models;

namespace PrStatusTracker.Core;

/// <summary>
/// What the main window is currently narrowed to. Free text matches title, repo, author, labels, number and
/// target branch;
/// the three exact fields come from dropdowns. Null means "any".
/// </summary>
public sealed record PrFilter(string Text = "", string? Author = null, string? Label = null, string? Repo = null)
{
    public static readonly PrFilter None = new();

    public bool IsActive => Text.Trim().Length > 0 || Author is not null || Label is not null || Repo is not null;

    public bool Matches(PullRequest pr)
    {
        if (Author is not null && !Same(pr.Author, Author))
        {
            return false;
        }

        if (Repo is not null && !Same(pr.Repo, Repo))
        {
            return false;
        }

        if (Label is not null && !pr.Labels.Any(l => Same(l, Label)))
        {
            return false;
        }

        var text = Text.Trim();
        if (text.Length == 0)
        {
            return true;
        }

        return Has(pr.Title, text) || Has(pr.Repo, text) || Has(pr.Author, text)
            || pr.Labels.Any(l => Has(l, text))
            || Has($"#{pr.Number}", text) || Has(pr.BaseBranch, text);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool Has(string haystack, string needle) => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
