using PvpStats.Helpers;
internal static class StartupRefreshChecks {
    public static async Task Run(Action<bool,string> check) {
        check(MatchVersionPolicy.NeedsLegacyValidation(0), "old configuration without a repair revision runs historical repairs");
        check(!MatchVersionPolicy.NeedsLegacyValidation(MatchVersionPolicy.LegacyValidationRevision), "successful repairs do not repeat because CCStats uses lower version numbers than upstream");
        check(!MatchVersionPolicy.NeedsLegacyValidation(99), "newer repair revisions survive a plugin downgrade");
        var store = new MatchSnapshotStore<int>();
        store.Reset(Enumerable.Range(0,100));
        var held = store.Snapshot();
        store.Update(store.Snapshot().Where(n=>n%2==0), (a,b)=>a==b);
        check(held.SequenceEqual(Enumerable.Range(0,100)) && store.Snapshot().Count==100, "bulk repairs can consume deferred matches without mutating published lists");
        await Task.WhenAll(Task.Run(()=> { for(int i=100;i<1100;i++)store.Add(i); }), Task.Run(()=> {
            for(int i=0;i<100;i++) foreach(var value in store.Snapshot()) _=value;
        }));
        check(store.Snapshot().Distinct().Count()==1100 && held.Count==100, "recording and historical readers keep independent stable collections");
        store.Reset(Array.Empty<int>());
        check(store.Snapshot().Count==0 && held.Count==100, "cache rebuilds cannot clear a list retained by an open view");
        var gate=new SerializedRefresh();
        var firstFetched=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTabs=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events=new List<string>();
        var first=gate.Run(async()=> {
            events.Add("fetch1");firstFetched.SetResult();
            await releaseTabs.Task;events.Add("publish1");
        });
        await firstFetched.Task;
        var second=gate.Run(()=> {events.Add("fetch2");events.Add("publish2");return Task.CompletedTask;});
        check(!second.IsCompleted&&events.SequenceEqual(new[]{"fetch1"}),"login refresh cannot overtake initial tab publication and leave stale startup data");
        releaseTabs.SetResult();await Task.WhenAll(first,second).WaitAsync(TimeSpan.FromSeconds(3));
        check(events.SequenceEqual(new[]{"fetch1","publish1","fetch2","publish2"}),"initialization and reopen refresh publish full pipelines in order");
        try {await gate.Run(()=>throw new InvalidOperationException("bad legacy data"));}catch(InvalidOperationException){}
        var recovered=false;
        await gate.Run(()=> {recovered=true;return Task.CompletedTask;}).WaitAsync(TimeSpan.FromSeconds(3));
        check(recovered,"failed startup refresh releases gate so reopening can recover without changing filters");
    }
}
