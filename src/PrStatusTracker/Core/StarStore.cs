namespace PrStatusTracker.Core;

/// <summary>
/// The PRs the user has starred to mark "I am working on this one". Purely a local annotation — nothing is
/// pushed to GitHub — so it is kept in its own file rather than on the snapshot, which is overwritten by
/// every poll.
/// </summary>
public sealed class StarStore
{
    private readonly JsonFileStore<List<string>> _file;
    private readonly HashSet<string> _keys;

    // Toggled from the UI thread, pruned from the poll thread: the set needs guarding.
    private readonly Lock _gate = new();

    public StarStore(JsonFileStore<List<string>> file)
    {
        _file = file;
        _keys = new HashSet<string>(file.LoadOrNull() ?? [], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Raised when a star is added or removed, so the lists can re-sort. <see cref="Retain"/> is silent.</summary>
    public event Action? Changed;

    public bool IsStarred(string key)
    {
        lock (_gate)
        {
            return _keys.Contains(key);
        }
    }

    public bool Toggle(string key)
    {
        bool starred;
        lock (_gate)
        {
            starred = !_keys.Remove(key);
            if (starred)
            {
                _keys.Add(key);
            }

            Save();
        }

        Changed?.Invoke();
        return starred;
    }

    /// <summary>
    /// Drop stars for PRs that are no longer in the snapshot — merged, closed, or no longer ours to see — so the
    /// file does not accumulate keys forever. Only ever called off a snapshot the poller successfully applied,
    /// so an empty one genuinely means there is nothing left to star.
    /// </summary>
    public void Retain(IEnumerable<string> keys)
    {
        lock (_gate)
        {
            if (_keys.Count == 0)
            {
                return; // nothing starred: skip building the live set on every poll
            }

            var live = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            if (_keys.RemoveWhere(k => !live.Contains(k)) > 0)
            {
                Save();
            }
        }
    }

    private void Save() => _file.Save([.. _keys]);
}
