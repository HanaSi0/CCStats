using System;
using System.Threading;
using System.Threading.Tasks;
namespace PvpStats.Helpers;
// Fetching the diff and applying it to all tabs are one operation. A later refresh must not overtake tab publication.
public sealed class SerializedRefresh {
    private readonly SemaphoreSlim _gate=new(1,1);
    public async Task Run(Func<Task> refresh) {
        await _gate.WaitAsync();
        try { await refresh(); }
        finally { _gate.Release(); }
    }
}
