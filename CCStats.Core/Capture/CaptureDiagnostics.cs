using System.IO.Compression;
using System.Text.Json;
namespace CCStats.Core.Capture;
public static class CaptureDiagnostics {
    public static void SavePanels(string recordingDirectory,uint territory,ObservationBatch batch) {
        if(batch.SettlementPanels.Count==0)return;
        var directory=Path.Combine(recordingDirectory,"diagnostics");Directory.CreateDirectory(directory);
        var name="Settlement-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")[..8]+".json";
        var path=Path.Combine(directory,name);var temp=path+".partial";
        try {
            File.WriteAllText(temp,JsonSerializer.Serialize(new {ClientLayout.Profile,batch.GameVersion,Territory=territory,batch.LocalPlayer,batch.DirectorAvailable,batch.EffectsPolled,batch.ChannelStatus,Panels=batch.SettlementPanels}));File.Move(temp,path);
            // Keep diagnostics bounded independently of user-owned recordings.
            foreach(var old in Directory.EnumerateFiles(directory,"Settlement-*.json").OrderByDescending(File.GetLastWriteTimeUtc).Skip(64))File.Delete(old);
        }finally {if(File.Exists(temp))File.Delete(temp);}
    }
    public static void SaveObservationSummary(string recordingDirectory,string id,object summary) {
        if(id.Length!=24||!id.All(Uri.IsHexDigit))throw new InvalidDataException("观测编号无效。");
        var directory=Path.Combine(recordingDirectory,"diagnostics");Directory.CreateDirectory(directory);var path=Path.Combine(directory,"Observation-"+id+".json");
        var temp=path+".partial";
        try {File.WriteAllText(temp,JsonSerializer.Serialize(summary));File.Move(temp,path,true);foreach(var old in Directory.EnumerateFiles(directory,"Observation-*.json").OrderByDescending(File.GetLastWriteTimeUtc).Skip(12))File.Delete(old);}
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
    public static async Task ExportAsync(string dataDirectory,string destination,object status) {
        var directory=Path.Combine(dataDirectory,"observations","diagnostics");
        await using var file=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        using var zip=new ZipArchive(file,ZipArchiveMode.Create,true);
        await using(var entry=zip.CreateEntry("status.json").Open())await JsonSerializer.SerializeAsync(entry,status);
        long total=0;
        foreach(var path in Directory.Exists(directory)?Directory.EnumerateFiles(directory,"*.json").Where(p=>Path.GetFileName(p).StartsWith("Settlement-",StringComparison.Ordinal)||Path.GetFileName(p).StartsWith("Observation-",StringComparison.Ordinal)).OrderByDescending(File.GetLastWriteTimeUtc).Take(76):Enumerable.Empty<string>()) {
            try {
                long size=new FileInfo(path).Length;if(size>2*1024*1024||total+size>32*1024*1024)continue;
                await using var input=File.OpenRead(path);await using var output=zip.CreateEntry((Path.GetFileName(path).StartsWith("Observation-",StringComparison.Ordinal)?"observations/":"settlement/")+Path.GetFileName(path)).Open();await input.CopyToAsync(output);total+=size;
            }catch(FileNotFoundException) { }catch(DirectoryNotFoundException) { }
        }
    }
}
