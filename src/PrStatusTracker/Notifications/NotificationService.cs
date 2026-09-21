using PrStatusTracker.Core;
using PrStatusTracker.Models;
using PrStatusTracker.Platform;

namespace PrStatusTracker.Notifications;

/// <summary>Turns PR changes into notification records: filters noise, persists history, shows one toast per poll.</summary>
public sealed class NotificationService : IDisposable
{
    private const int MaxHistory = 200;
    private const int MaxToastLines = 4;

    private readonly PrStore _store;
    private readonly INotifier _notifier;
    private readonly JsonFileStore<List<NotificationRecord>> _file;
    private readonly Func<AppSettings> _settings;
    private readonly List<NotificationRecord> _history;
    private readonly Lock _gate = new();

    public NotificationService(PrStore store, INotifier notifier, JsonFileStore<List<NotificationRecord>> file, Func<AppSettings> settings)
    {
        _store = store;
        _notifier = notifier;
        _file = file;
        _settings = settings;
        _history = file.Load(() => []);
        _store.Changed += OnChanged;
    }

    /// <summary>Newest first. The records are the live instances, so <see cref="Dismiss"/> accepts them back.</summary>
    public IReadOnlyList<NotificationRecord> History
    {
        get
        {
            lock (_gate)
            {
                return [.. _history];
            }
        }
    }

    public event Action? HistoryChanged;

    public void Dismiss(NotificationRecord record)
    {
        lock (_gate)
        {
            if (record.Dismissed)
            {
                return;
            }

            record.Dismissed = true;
            _file.Save(_history);
        }
        HistoryChanged?.Invoke();
    }

    public void DismissAll()
    {
        lock (_gate)
        {
            foreach (var r in _history)
            {
                r.Dismissed = true;
            }

            _file.Save(_history);
        }
        HistoryChanged?.Invoke();
    }

    private void OnChanged(IReadOnlyList<PrChange> changes)
    {
        var settings = _settings();
        var added = new List<NotificationRecord>();

        lock (_gate)
        {
            foreach (var change in changes)
            {
                if (ToRecord(change, settings) is not { } record)
                {
                    continue;
                }

                _history.Insert(0, record);
                added.Add(record);
            }
            if (added.Count == 0)
            {
                return;
            }

            if (_history.Count > MaxHistory)
            {
                _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
            }

            _file.Save(_history);
        }

        HistoryChanged?.Invoke();
        _ = ShowSafelyAsync(Compose(added));
    }

    internal static NotificationRecord? ToRecord(PrChange change, AppSettings settings)
    {
        var pr = change.Current.Pr;
        var status = change.Current.Status;
        var wanted = Wanted(status.Bucket(), settings);

        var what = change.Kind switch
        {
            // All we know is the PR left your lists: merged, closed, or a team request satisfied by someone else.
            PrChangeKind.Disappeared => settings.NotifyClosed ? "No longer on your list" : null,
            // Your own action (approving, commenting) is not news to you, and a draft never needs anyone yet.
            _ when status.IsSelfInflicted() || status.IsDraft() => null,
            // A PR showing up is only news if something is already being asked of it: your own new PR is
            // usually you opening it, and a draft you're requested on will announce itself when it's marked ready.
            PrChangeKind.Appeared when !status.IsAskingSomething() => null,
            _ => wanted ? status.Label() : null,
        };
        if (what is null)
        {
            return null;
        }

        return new NotificationRecord
        {
            Title = $"{what} — {pr.Title}",
            Body = $"{pr.Repo}#{pr.Number} · {pr.Author}",
            Url = pr.Url,
        };
    }

    /// <summary>Several changes in one poll become a single toast rather than a stream of them.</summary>
    internal static Toast Compose(IReadOnlyList<NotificationRecord> records)
    {
        if (records.Count == 1)
        {
            return new Toast(records[0].Title, records[0].Body, records[0].Url);
        }

        var lines = records.Take(MaxToastLines).Select(r => r.Title).ToList();
        if (records.Count > MaxToastLines)
        {
            lines.Add($"…and {records.Count - MaxToastLines} more");
        }

        return new Toast($"{records.Count} pull request updates", string.Join("\n", lines), Browser.AllPullRequestsUrl);
    }

    private static bool Wanted(PrBucket bucket, AppSettings s) => bucket switch
    {
        PrBucket.NeedsYou => s.NotifyNeedsYou,
        PrBucket.WaitingOnOthers => s.NotifyWaitingOnOthers,
        PrBucket.YourPrs => s.NotifyYourPrs,
        _ => true,
    };

    private async Task ShowSafelyAsync(Toast toast)
    {
        try { await _notifier.ShowAsync(toast); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Toast failed: {ex.Message}"); }
    }

    public void Dispose() => _store.Changed -= OnChanged;
}
