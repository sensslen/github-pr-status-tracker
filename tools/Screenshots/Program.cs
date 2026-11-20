using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using PrStatusTracker.Core;
using PrStatusTracker.GitHub;
using PrStatusTracker.Models;
using PrStatusTracker.Notifications;
using PrStatusTracker.Platform;
using PrStatusTracker.ViewModels;
using PrStatusTracker.Views;

// Renders docs/screenshots/*.png from the app's real views over the made-up data below.
// Usage: dotnet run --project tools/Screenshots [-- <output directory>]
var output = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine("docs", "screenshots"));
Directory.CreateDirectory(output);

AppBuilder.Configure<ScreenshotApp>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

var scratch = Directory.CreateTempSubdirectory("prst-screenshots-");
try
{
    var store = new PrStore(new JsonFileStore<List<TrackedPr>>(Path.Combine(scratch.FullName, "snapshot.json")));
    var stars = new StarStore(new JsonFileStore<List<string>>(Path.Combine(scratch.FullName, "stars.json")));
    var settings = new AppSettings();
    var settingsFile = new JsonFileStore<AppSettings>(Path.Combine(scratch.FullName, "settings.json"));
    var notifications = new NotificationService(store, new NullNotifier(),
        new JsonFileStore<List<NotificationRecord>>(Path.Combine(scratch.FullName, "notifications.json")), () => settings);
    using var poller = new PollingService(new OfflineGitHub(), store, () => settings);
    var gh = new GhCommandRunner();
    var vm = new MainViewModel(store, stars, poller, notifications, settings, settingsFile,
        new UnsupportedAutoStart(), gh, new UpdateService(gh, () => { }));

    // The first snapshot is the baseline and the second the news, so the notification history has entries.
    store.Apply(DemoData.Earlier());
    store.Apply(DemoData.Now());
    stars.Toggle("acme/web-app#418");

    var window = new Window { Title = "PR Status Tracker", Width = 1000, Height = 900, Content = new MainView(vm) };
    window.Show();
    var tabs = window.GetLogicalDescendants().OfType<TabControl>().First();

    Capture(window, tabs, 2, 900, Path.Combine(output, "your-prs.png"));
    Capture(window, tabs, 0, 510, Path.Combine(output, "needs-you.png"));
    Capture(window, tabs, 1, 360, Path.Combine(output, "waiting.png"));
    Capture(window, tabs, 3, 300, Path.Combine(output, "notifications.png"));
    window.Close();
}
finally
{
    scratch.Delete(recursive: true);
}

static void Capture(Window window, TabControl tabs, int tab, double height, string path)
{
    tabs.SelectedIndex = tab;
    window.Height = height;
    Dispatcher.UIThread.RunJobs();
    using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was rendered");
    using var file = File.Create(path);
    frame.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    Console.WriteLine(path);
}

internal sealed class ScreenshotApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Light;
    }
}

/// <summary>Never asked: the poller is not started, so the views show only the demo snapshot.</summary>
internal sealed class OfflineGitHub : IGitHubClient
{
    public Task<string> GetViewerLoginAsync(CancellationToken ct = default) => Task.FromResult(DemoData.Me);

    public Task<IReadOnlyList<PullRequest>> FetchPullRequestsAsync(string login, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PullRequest>>([]);
}

internal static class DemoData
{
    public const string Me = "you";
    private static readonly DateTimeOffset s_now = DateTimeOffset.Now;

    public static IEnumerable<TrackedPr> Earlier() =>
        Now().Select(t => t.Pr.Number switch
        {
            412 => With(t.Pr with { ReviewDecision = null, LatestReviews = [] }),
            420 => With(t.Pr with { Checks = CheckState.Pending }),
            431 => null,
            _ => t,
        }).OfType<TrackedPr>();

    public static IEnumerable<TrackedPr> Now() => new[]
    {
        // Yours
        Pr("acme/web-app", 412, "Add retry with backoff to the upload client", Me, ageHours: 50, adds: 120, dels: 40,
            labels: ["backend"], decision: ReviewDecision.Approved,
            reviews: [Review("maria", ReviewState.Approved), Review("coderabbitai", ReviewState.Approved)]),
        Pr("acme/web-app", 418, "Cache rendered thumbnails on disk", Me, ageHours: 20, adds: 120, dels: 40,
            labels: ["performance"], decision: ReviewDecision.Approved,
            reviews: [Review("devon", ReviewState.Approved), Review("coderabbitai", ReviewState.Commented)]),
        Pr("acme/web-app", 405, "Backport the session timeout fix", Me, ageHours: 26, adds: 18, dels: 6,
            labels: ["backport"], decision: ReviewDecision.Approved, baseBranch: "release/2.4",
            reviews: [Review("maria", ReviewState.Approved)]),
        Pr("acme/web-app", 416, "Validate webhook signatures", Me, ageHours: 30, adds: 210, dels: 15,
            labels: ["security"], decision: ReviewDecision.Approved, unresolvedThreads: true,
            reviews: [Review("priya", ReviewState.Approved), Review("coderabbitai", ReviewState.Approved)]),
        Pr("acme/web-app", 388, "Migrate to the new router", Me, ageHours: 120, adds: 1200, dels: 980,
            mergeable: Mergeable.Conflicting, requested: ["priya"]),
        Pr("acme/web-app", 399, "Rework the settings page layout", Me, ageHours: 72, adds: 640, dels: 310, labels: ["ui"],
            decision: ReviewDecision.ChangesRequested,
            reviews: [Review("maria", ReviewState.ChangesRequested), Review("coderabbitai", ReviewState.ChangesRequested)]),
        Pr("acme/web-app", 424, "Prototype offline mode", Me, ageHours: 190, adds: 2300, dels: 140, draft: true,
            checks: CheckState.Pending),
        Pr("acme/api-server", 420, "Split the billing module", Me, ageHours: 10, adds: 890, dels: 720,
            checks: CheckState.Failure, requested: ["devon"], reviews: [Review("coderabbitai", ReviewState.Commented)]),
        Pr("acme/api-server", 423, "Extract the auth middleware", Me, ageHours: 4, adds: 160, dels: 90,
            requested: ["maria", "coderabbitai"], openThreads: true, unresolvedThreads: true),
        Pr("acme/api-server", 422, "Show upload progress per file", Me, ageHours: 8, adds: 120, dels: 40, labels: ["ui"],
            decision: ReviewDecision.Approved, checks: CheckState.Pending, reviews: [Review("devon", ReviewState.Approved)]),
        Pr("acme/docs", 425, "Document the release process", Me, ageHours: 3, adds: 80, dels: 4),

        // Others'
        Pr("acme/api-server", 431, "Rate-limit the public search endpoint", "maria", ageHours: 2, adds: 340, dels: 25,
            labels: ["security"], requested: [Me]),
        Pr("acme/web-app", 427, "Drop the legacy image proxy", "devon", ageHours: 28, adds: 45, dels: 610,
            requested: [Me], reviews: [Review(Me, ReviewState.Approved, hoursAgo: 20)], committedHoursAgo: 3),
        Pr("acme/mobile", 302, "Support dark mode on Android", "priya", ageHours: 52, adds: 780, dels: 120,
            labels: ["ui"], requested: ["mobile-team"]),
        Pr("acme/api-server", 429, "Bump the Postgres driver", "renovate", ageHours: 6, adds: 4, dels: 4,
            labels: ["dependencies"], requested: [Me]),
        Pr("acme/web-app", 415, "Lazy-load the analytics bundle", "devon", ageHours: 70, adds: 95, dels: 30,
            reviews: [Review(Me, ReviewState.Approved, hoursAgo: 30)], committedHoursAgo: 40),
        Pr("acme/docs", 211, "Fix broken links in the API guide", "maria", ageHours: 40, adds: 12, dels: 12,
            reviews: [Review(Me, ReviewState.ChangesRequested, hoursAgo: 12)], committedHoursAgo: 20),
    };

    private static Review Review(string author, ReviewState state, double hoursAgo = 2) =>
        new(author, state, s_now.AddHours(-hoursAgo));

    private static TrackedPr With(PullRequest pr) => new(pr, StatusEvaluator.Evaluate(pr, Me));

    private static TrackedPr Pr(string repo, int number, string title, string author, double ageHours, int adds, int dels,
        string[]? labels = null, ReviewDecision? decision = null, Mergeable mergeable = Mergeable.Mergeable,
        CheckState? checks = CheckState.Success, string[]? requested = null, Review[]? reviews = null,
        bool draft = false, bool openThreads = false, bool unresolvedThreads = false, string baseBranch = "main",
        double committedHoursAgo = 6)
    {
        var pr = new PullRequest(repo, number, title, $"https://github.com/{repo}/pull/{number}", draft, author,
            CreatedAt: s_now.AddHours(-ageHours), UpdatedAt: s_now.AddHours(-2), adds, dels, labels ?? [],
            decision, mergeable, requested ?? [], reviews ?? [], s_now.AddHours(-committedHoursAgo), checks,
            HasOpenThreads: openThreads, BaseBranch: baseBranch, HeadBranch: $"{author}/{number}", DefaultBranch: "main",
            HasUnresolvedThreads: unresolvedThreads);
        return With(pr);
    }
}
