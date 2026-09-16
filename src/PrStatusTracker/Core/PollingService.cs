using PrStatusTracker.GitHub;
using PrStatusTracker.Models;

namespace PrStatusTracker.Core;

/// <summary>Drives the fetch → evaluate → store loop on a timer, with exponential backoff on failure.</summary>
public sealed class PollingService(IGitHubClient github, PrStore store, Func<AppSettings> settings) : IDisposable
{
    private static readonly TimeSpan s_maxBackoff = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private int _consecutiveFailures;
    private string? _login;

    public GhException? LastError { get; private set; }
    public DateTimeOffset? LastSuccess { get; private set; }
    public bool IsPolling { get; private set; }

    public event Action? StateChanged;

    // Both entry points are called from the UI thread; hop off it so process spawning, JSON parsing and
    // file writes never run on the dispatcher. Consumers marshal back to the UI thread themselves.
    public void Start() => _ = Task.Run(RunLoopAsync);

    public Task RefreshNowAsync() => Task.Run(() => PollOnceAsync(_cts.Token));

    private async Task RunLoopAsync()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            await PollOnceAsync(ct);
            // Healthy: the configured interval. Failing: double it per failure, so we never poll faster than normal.
            var interval = settings().PollInterval;
            var delay = TimeSpan.FromSeconds(Math.Min(interval.TotalSeconds * Math.Pow(2, _consecutiveFailures), s_maxBackoff.TotalSeconds));
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        if (!await _pollGate.WaitAsync(0, ct))
        {
            return; // a poll is already in flight
        }

        try
        {
            IsPolling = true;
            StateChanged?.Invoke();

            _login ??= await github.GetViewerLoginAsync(ct);
            var prs = await github.FetchPullRequestsAsync(_login, ct);
            store.Apply(prs.Select(pr => new TrackedPr(pr, StatusEvaluator.Evaluate(pr, _login))));

            _consecutiveFailures = 0;
            LastError = null;
            LastSuccess = DateTimeOffset.Now;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _consecutiveFailures++;
            LastError = ex as GhException ?? new GhException(GhFailureKind.Other, ex.Message);
            if (LastError.Kind == GhFailureKind.NotAuthenticated)
            {
                _login = null;
            }
        }
        finally
        {
            IsPolling = false;
            _pollGate.Release();
            StateChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
