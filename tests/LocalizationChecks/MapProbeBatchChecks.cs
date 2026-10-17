using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PvpStats.Analysis;
internal static class MapProbeBatchChecks {
    internal static void Run(Action<bool,string> check) {
        var root=Path.Combine(Path.GetTempPath(),"ccstats-batch-"+Guid.NewGuid().ToString("N"));
        try {
            var store=new MapProbeBatchStore(root);
            var a=store.Begin("角力学校",1032,1);store.Update(a,.4f,"读取模型");
            var docs=new Dictionary<string,string> { ["capture.json"]="{\"Territory\":1032}",["scene.json"]="{\"Models\":3}" };
            store.Complete(a,docs);
            check(store.Snapshot().Single().State=="Complete"&&store.Snapshot().Single().Progress==1,"map completion requires saved and verified probe documents");
            var b=store.Begin("火山之心",1033,2);
            store.Fail(b,"模型读取失败，等待重试");
            var path=Path.Combine(root,"batch.zip");var updates=new List<float>();
            store.Export(path,(progress,_)=>updates.Add(progress));
            using(var zip=ZipFile.OpenRead(path)) {
                check(zip.Entries.Count==3&&zip.GetEntry(a+"/capture.json")!=null&&zip.GetEntry(b+"/capture.json")==null,"batch ZIP includes only complete maps and a manifest, never failed partial captures");
                using var reader=new StreamReader(zip.GetEntry("manifest.json")!.Open());
                using var manifest=JsonDocument.Parse(reader.ReadToEnd());
                check(manifest.RootElement.GetProperty("Transfer").GetString()=="local-export-only"&&manifest.RootElement.GetProperty("Maps").GetArrayLength()==1,"export manifest clearly records local-only transfer and included maps");
            }
            check(updates.SequenceEqual(updates.OrderBy(p=>p))&&updates[^1]==1,"ZIP progress increases with files written and reaches completion after finalizing the archive");
            var reopened=new MapProbeBatchStore(root);
            check(reopened.Snapshot().Count(e=>e.State=="Complete")==1,"completed maps survive reopening or restarting the plugin");
            var interrupted=reopened.Begin("未完成采样",1034,3);
            var recovered=new MapProbeBatchStore(root);
            check(recovered.Snapshot().Single(e=>e.Id==interrupted).State=="Interrupted","interrupted captures resume as retryable records rather than falsely complete maps");
            bool Throws(Action run) { try { run();return false; } catch { return true; } }
            check(Throws(()=>recovered.Complete(a,new Dictionary<string,string> { ["capture.json"]="changed" }))&&File.ReadAllText(Path.Combine(root,a,"capture.json"))==docs["capture.json"],"a completed map cannot be overwritten by a duplicate completion");
            var unsafeEntry=recovered.Begin("不安全路径",1034,3);
            check(Throws(()=>recovered.Complete(unsafeEntry,new Dictionary<string,string> { ["../escape.json"]="{}" }))&&!File.Exists(Path.Combine(root,"escape.json")),"probe documents cannot escape their capture directory");
            recovered.Fail(unsafeEntry,"路径错误");
            File.WriteAllText(Path.Combine(root,a,"capture.json"),"corrupted");
            var badZip=Path.Combine(root,"bad.zip");
            check(Throws(()=>recovered.Export(badZip))&&!File.Exists(badZip)&&!File.Exists(badZip+".partial"),"corrupt capture files fail export without publishing a partial ZIP");
            var active=recovered.Begin("活动采集",1034,3);
            check(Throws(()=>recovered.Export(Path.Combine(root,"active.zip"))),"export waits for an active map capture instead of silently omitting it");
        } finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }
}
