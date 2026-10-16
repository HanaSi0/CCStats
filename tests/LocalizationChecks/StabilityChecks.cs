using PvpStats.Helpers;
using System.Collections.Concurrent;
internal static class StabilityChecks {
    internal static async Task Run(Action<bool,string> check) {
        using var queue=new UiWorkQueue();
        var values=new List<int>(); var threads=new List<int>();
        await Task.Run(()=> { queue.Post(()=> { values.Add(1); threads.Add(Environment.CurrentManagedThreadId); }); queue.Post(()=>values.Add(2)); });
        check(values.Count==0,"background UI posts never execute native work immediately");
        var drawingThread=Environment.CurrentManagedThreadId;
        check(queue.Drain()==2&&values.SequenceEqual(new[]{1,2})&&threads[0]==drawingThread,"UI operations execute in FIFO order on the draining UI thread");
        int errors=0; queue.Post(()=>throw new InvalidOperationException("fixture")); queue.Post(()=>values.Add(3));
        queue.Drain(_=>errors++);
        check(errors==1&&values[^1]==3,"one failed UI operation does not block later UI operations");
        for(int i=0;i<100;i++) queue.Post(()=>values.Add(4));
        check(queue.Drain(budget:3)==3&&queue.Drain()==64,"UI queue enforces a per-frame budget");
        queue.Dispose(); queue.Post(()=>values.Add(99));
        check(queue.Drain()==0&&!values.Contains(99),"plugin unload discards pending and late background UI work");
        var processed=new ConcurrentBag<int>(); int active=0,peak=0,completed=0,failed=0;
        var failures=await RefreshBatch.Run(Enumerable.Range(0,100),n=> {
            var running=Interlocked.Increment(ref active);
            int old;
            do { old=Volatile.Read(ref peak); if(old>=running)break; } while(Interlocked.CompareExchange(ref peak,running,old)!=old);
            try { Thread.SpinWait(100000); if(n==7)throw new FormatException("invalid fixture record"); processed.Add(n); }
            finally { Interlocked.Decrement(ref active); }
        },_=>Interlocked.Increment(ref failed),()=>Interlocked.Increment(ref completed),2);
        check(peak>=1&&peak<=2&&active==0,"statistics refresh bounds simultaneous processing rather than starting a task per historical match");
        check(failures==1&&failed==1&&completed==100&&processed.Count==99&&!processed.Contains(7),"bad historical rows are counted while the rest of the batch still completes");
        check(processed.Distinct().Count()==99,"bounded refresh processes each valid record exactly once");
        var empty=await RefreshBatch.Run(Array.Empty<int>(),_=>throw new Exception());
        check(empty==0,"empty refresh does not invoke processors or produce a failure");
        foreach(var scale in new[]{1f,1.5f,2f}) foreach(var available in new[]{200f,400f,800f}) {
            var height=ViewLayout.FilterHeight(available,500,scale);
            check(height>=0&&height<=available*.35f&&height<=Math.Max(0,available-180*scale),"filter area reserves room for data at scale "+scale+" and height "+available);
        }
        check(ViewLayout.FilterHeight(100,250,1)==0&&ViewLayout.FilterHeight(float.NaN,250,1)==0,"tiny or invalid space cannot become a full-height zero-sized child window");
        check(float.IsFinite(ViewLayout.FilterHeight(600,float.NaN,float.NaN)),"invalid saved layout values are sanitized");
    }
}
