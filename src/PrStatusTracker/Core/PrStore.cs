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

    /// <summary>Replace the snapshot. Returns the diff against the previous snapshot; on the very first
    /// snapshot ever seen (no persisted state), no changes are reported so a fresh install stays quiet.</summary>
    public IReadOnlyList<PrChange> Apply(IEnumerable<TrackedPr> items)
    {
        var next = items.ToDictionary(t => t.Key);
        var changes = _seeded ? Diff(_current, next) : [];

        // Nothing moved and nothing was touched upstream: skip the disk write and the UI rebuild. GitHub does not
        // bump updatedAt for check runs or mergeability, so those are compared explicitly.
        var untouched = _seeded && changes.Count == 0
            && next.Values.All(t => _current[t.Key].Pr is var old
                && old.UpdatedAt == t.Pr.UpdatedAt && old.Checks == t.Pr.Checks && old.Mergeable == t.Pr.Mergeable);
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
