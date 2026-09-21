namespace PrStatusTracker.Core;

/// <summary>
/// How far you have read each of your own PRs: the timestamp of the newest comment you had seen when you last
/// opened it. Purely a local annotation, like <see cref="StarStore"/> — GitHub's own read state is not exposed
/// through the API we use — so it lives in its own file rather than on the snapshot, which every poll overwrites.
/// </summary>
public sealed class CommentSeenStore
{
    private readonly JsonFileStore<Dictionary<string, DateTimeOffset>> _file;
    private readonly Dictionary<string, DateTimeOffset> _seen;

    // Marked from the UI thread, seeded and pruned from the poll thread: the map needs guarding.
    private readonly Lock _gate = new();

    public CommentSeenStore(JsonFileStore<Dictionary<string, DateTimeOffset>> file)
    {
        _file = file;
        _seen = new Dictionary<string, DateTimeOffset>(file.LoadOrNull() ?? [], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Raised when a PR is marked read, so the lists can drop its badge. <see cref="Sync"/> is silent.</summary>
    public event Action? Changed;

    /// <summary>
    /// Whether <paramref name="lastCommentAt"/> is something you have not read. A PR with no comments, or whose
    /// newest comment predates your mark, is quiet — and so is one this store has never heard of, which is what
    /// keeps a fresh install from announcing a backlog you read on GitHub months ago. Stating that here rather
    /// than relying on <see cref="Sync"/> having run first means no caller can see the backlog by asking early.
    /// </summary>
    public bool HasUnread(string key, DateTimeOffset? lastCommentAt)
    {
        if (lastCommentAt is not { } at)
        {
            return false;
        }

        lock (_gate)
        {
            return _seen.TryGetValue(key, out var seen) && at > seen;
        }
    }

    /// <summary>Everything said on this PR so far has now been read. No-op when there was nothing to read.</summary>
    public void MarkRead(string key, DateTimeOffset? lastCommentAt)
    {
        lock (_gate)
        {
            if (lastCommentAt is not { } at || (_seen.TryGetValue(key, out var seen) && seen >= at))
            {
                return;
            }

            _seen[key] = at;
            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Bring the marks in line with a freshly applied snapshot: forget PRs that have left it, and record where
    /// first-seen ones start. <see cref="HasUnread"/> already treats an unknown PR as read, so this is not what
    /// makes a fresh install quiet — it is what lets comments arriving *after* this poll register as new.
    /// </summary>
    public void Sync(IEnumerable<(string Key, DateTimeOffset? LastCommentAt)> snapshot)
    {
        lock (_gate)
        {
            var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirty = false;
            foreach (var (key, lastCommentAt) in snapshot)
            {
                live.Add(key);
                dirty |= _seen.TryAdd(key, lastCommentAt ?? DateTimeOffset.MinValue);
            }

            // Every live key is in _seen now, so the counts match exactly when nothing has gone stale — which
            // is every steady-state poll, and the only case worth keeping off the allocation path.
            if (_seen.Count != live.Count)
            {
                foreach (var gone in _seen.Keys.Where(k => !live.Contains(k)).ToList())
                {
                    _seen.Remove(gone);
                    dirty = true;
                }
            }

            if (dirty)
            {
                Save();
            }
        }
    }

    private void Save() => _file.Save(_seen);
}
