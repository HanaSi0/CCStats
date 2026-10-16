using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace PvpStats.Helpers;
public static class RefreshBatch {
    public static async Task<int> Run<T>(IEnumerable<T> items,Action<T> process,Action<Exception>? onError=null,Action? completed=null,int concurrency=2) {
        int failures=0;
        await Parallel.ForEachAsync(items,new ParallelOptions { MaxDegreeOfParallelism=Math.Clamp(concurrency,1,4) },(item,token)=> {
            try { process(item); }
            catch(Exception e) { Interlocked.Increment(ref failures); onError?.Invoke(e); }
            finally { completed?.Invoke(); }
            return ValueTask.CompletedTask;
        });
        return failures;
    }
}
