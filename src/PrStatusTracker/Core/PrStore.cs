using PrStatusTracker.Models;

namespace PrStatusTracker.Core;

public sealed record TrackedPr(PullRequest Pr, PrStatus Status)
{
    public string Key => Pr.Key;
}

public enum PrChangeKind { Appeared, StatusChanged, Disappeared }

public sealed record PrChange(PrChangeKind Kind, TrackedPr Current, PrStatus? PreviousStatus);

/// <summary>Holds the current PR snapshot and diffs each new one against it.</summary>
public sealed class PrStore
{
    private readonly JsonFileStore<List<TrackedPr>> _file;
    private Dictionary<string, TrackedPr> _current;
    private bool _seeded;

    public PrStore(JsonFileStore<List<TrackedPr>> file)
    {
        _file = file;
        // A persisted empty list is still a baseline; only a missing/unreadable file means "first run".
        var persisted = file.LoadOrNull();
        _seeded = persisted is not null;
        _current = (persisted ?? []).ToDictionary(t => t.Key);
    }

    public IReadOnlyCollection<TrackedPr> Current => _current.Values;

    public event Action<IReadOnlyList<PrChange>>? Changed;
    public event Action? Updated;

    /// <summary>
    /// Announce the snapshot restored from disk, once every subscriber is attached. The constructor cannot do
    /// it — nobody is listening yet — and without it each consumer has to prime itself, which is an ordering
    /// contract between subscribers that nothing states and nothing checks.
    /// <para>
    /// Silent when there was nothing to restore. Subscribers prune themselves against the snapshot, and
    /// <see cref="StarStore.Retain"/> takes an empty one to mean every starred PR is gone — true of a snapshot
    /// the poller just applied, but not of a missing or corrupt file, where announcing it would wipe the
    /// user's stars before the first poll ever ran.
    /// </para>
    /// </summary>
    public void Start()
    {
        if (_seeded)
        {
            Updated?.Invoke();
        }
    }

    /// <summary>Replace the snapshot. Returns the diff against the previous snapshot; on the very first
    /// snapshot ever seen (no persisted state), no changes are reported so a fresh install stays quiet.</summary>
    public IReadOnlyList<PrChange> Apply(IEnumerable<TrackedPr> items)
    {
        var next = items.ToDictionary(t => t.Key);
        var changes = _seeded ? Diff(_current, next) : [];

        // Nothing moved and nothing was touched upstream: skip the disk write and the UI rebuild. What counts
        // as touched is PullRequest's to say, and it documents the rule for extending it.
        var untouched = _seeded && changes.Count == 0
            && next.Values.All(t => _current[t.Key].Pr.SameStateAs(t.Pr));
        if (untouched)
        {
            return changes;
        }

        _current = next;
        _seeded = true;
        _file.Save([.. next.Values]);

        if (changes.Count > 0)
        {
            Changed?.Invoke(changes);
        }

        Updated?.Invoke();
        return changes;
    }

    internal static List<PrChange> Diff(Dictionary<string, TrackedPr> previous, Dictionary<string, TrackedPr> next)
    {
        var changes = new List<PrChange>();
        foreach (var (key, item) in next)
        {
            if (!previous.TryGetValue(key, out var old))
            {
                changes.Add(new PrChange(PrChangeKind.Appeared, item, null));
            }
            else if (old.Status != item.Status)
            {
                changes.Add(new PrChange(PrChangeKind.StatusChanged, item, old.Status));
            }
        }
        foreach (var (key, old) in previous)
        {
            if (!next.ContainsKey(key))
            {
                changes.Add(new PrChange(PrChangeKind.Disappeared, old, old.Status));
            }
        }
        return changes;
    }
}
