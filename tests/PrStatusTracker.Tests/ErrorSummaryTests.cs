using PrStatusTracker.ViewModels;
using Xunit;

namespace PrStatusTracker.Tests;

public class ErrorSummaryTests
{
    [Fact]
    public void HtmlPageIsSummarizedByItsTitle()
    {
        const string page = "<!DOCTYPE html>\n<html>\n<head>\n  <title>\n    Unicorn! · GitHub\n  </title>\n</head>\n<body>…</body>\n</html>";

        Assert.Equal("Unicorn! · GitHub", MainViewModel.SummarizeError(page));
    }

    [Fact]
    public void PlainMessageKeepsItsFirstNonBlankLine()
    {
        Assert.Equal("gh: Bad credentials (HTTP 401)", MainViewModel.SummarizeError("\n  gh: Bad credentials (HTTP 401)\nmore detail"));
    }

    [Fact]
    public void LongLineIsCutWithAnEllipsis()
    {
        var summary = MainViewModel.SummarizeError(new string('x', 500));

        Assert.Equal(160, summary.Length);
        Assert.EndsWith("…", summary);
    }

    [Fact]
    public void EmptyMessageStaysEmpty()
    {
        Assert.Equal(string.Empty, MainViewModel.SummarizeError(string.Empty));
    }
}
