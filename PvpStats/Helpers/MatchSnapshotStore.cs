using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PvpStats.Helpers;

// Readers keep a stable list while recording and background repairs replace entries.
internal sealed class MatchSnapshotStore<T> {
    private readonly object _gate = new();
    private List<T> _items = new();
    public ReadOnlyCollection<T> Snapshot() { lock(_gate) return _items.ToList().AsReadOnly(); }
    public void Reset(IEnumerable<T> items) {
        var next = items.ToList();
        lock(_gate) _items = next;
    }
    public void Add(T item) { lock(_gate) _items.Add(item); }
    public void Update(IEnumerable<T> items, Func<T,T,bool> sameKey) {
        // Materialize before mutating: input may be a deferred query over a snapshot.
        var updates = items.ToArray();
        lock(_gate) foreach(var item in updates) {
            if(_items.RemoveAll(existing => sameKey(existing,item)) > 0) _items.Add(item);
        }
    }
}
