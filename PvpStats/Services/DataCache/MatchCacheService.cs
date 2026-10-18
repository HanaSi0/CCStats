using LiteDB;
using PvpStats.Helpers;
using PvpStats.Types.Match;
using PvpStats.Types.Match.Timeline;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace PvpStats.Services.DataCache;
internal abstract class MatchCacheService<T> where T : PvpMatch {
    protected readonly Plugin Plugin;
    //private readonly StorageService _storage;
    private readonly MatchSnapshotStore<T> _matches = new();

    internal bool CachingEnabled { get; private set; }
    internal ReadOnlyCollection<T> Matches {
        get {
            if(CachingEnabled) {
                return _matches.Snapshot();
            } else {
                return GetFromStorage().ToList().AsReadOnly();
            }
        }
    }

    protected abstract IEnumerable<T> GetFromStorage();
    protected abstract Task AddToStorage(T match);
    protected abstract Task UpdateToStorage(T match);
    protected abstract Task UpdateManyToStorage(IEnumerable<T> matches);
    internal abstract PvpMatchTimeline? GetTimeline(T match);

    internal MatchCacheService(Plugin plugin) {
        Plugin = plugin;
    }

    internal void EnableCaching() {
        RebuildCache();
        CachingEnabled = true;
    }

    internal void DisableCaching() {
        ClearCache();
        CachingEnabled = false;
    }

    private void ClearCache() {
        _matches.Reset(System.Array.Empty<T>());
    }

    private void RebuildCache() {
        _matches.Reset(GetFromStorage());
    }

    internal async Task AddMatch(T match) {
        await AddToStorage(match);
        if(CachingEnabled) _matches.Add(match);
        Plugin.Ipc.OnMatchSaved(match.Id.ToString());
    }

    internal async Task UpdateMatch(T match) {
        await UpdateToStorage(match);
        if(CachingEnabled) _matches.Update(new[] { match }, (a,b) => a.Id == b.Id);
    }

    internal async Task UpdateMatches(IEnumerable<T> matches) {
        var updates = matches.ToArray();
        await UpdateManyToStorage(updates);
        if(CachingEnabled) _matches.Update(updates, (a,b) => a.Id == b.Id);
    }
}
