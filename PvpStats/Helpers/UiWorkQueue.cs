using System;
using System.Collections.Concurrent;
using System.Threading;
namespace PvpStats.Helpers;
// Background work may post results; only the Draw callback drains UI operations.
public sealed class UiWorkQueue : IDisposable {
    private readonly ConcurrentQueue<Action> _items=new();
    private int _disposed;
    public void Post(Action action) {
        if(Volatile.Read(ref _disposed)!=0)return;
        _items.Enqueue(action);
        if(Volatile.Read(ref _disposed)!=0)_items.Clear();
    }
    public int Drain(Action<Exception>? onError=null,int budget=64) {
        int count=0;
        while(count<budget && Volatile.Read(ref _disposed)==0 && _items.TryDequeue(out var action)) {
            try { action(); } catch(Exception e) { onError?.Invoke(e); }
            count++;
        }
        return count;
    }
    public void Dispose() { Interlocked.Exchange(ref _disposed,1); _items.Clear(); }
}
