using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace PvpStats.Analysis;
public sealed record MapProbeFile(string Name,long Length,string Sha256);
public sealed record MapProbeBatchEntry {
    public string Id { get; init; }="";
    public string Label { get; init; }="";
    public uint Territory { get; init; }
    public uint Map { get; init; }
    public DateTime StartedUtc { get; init; }
    public string State { get; init; }="Collecting";
    public float Progress { get; init; }
    public string Status { get; init; }="";
    public List<MapProbeFile> Files { get; init; }=new();
}
public sealed class MapProbeBatchStore {
    private sealed class Manifest {
        public int Format { get; set; }=1;
        public string Transfer { get; set; }="local-export-only";
        public List<MapProbeBatchEntry> Maps { get; set; }=new();
    }
    private readonly object _gate=new();
    private readonly string _root;
    private readonly Manifest _manifest;
    private static readonly JsonSerializerOptions Json=new() { WriteIndented=true };
    public string Root=>_root;
    public MapProbeBatchStore(string root) {
        _root=Path.GetFullPath(root);Directory.CreateDirectory(_root);
        var path=Path.Combine(_root,"manifest.json");
        _manifest=File.Exists(path)?JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))??throw new InvalidDataException("Invalid batch manifest."):new();
        if(_manifest.Format!=1||_manifest.Maps.Count>100)throw new InvalidDataException("Unsupported batch manifest.");
        foreach(var entry in _manifest.Maps) {
            ValidateId(entry.Id);
            foreach(var file in entry.Files)ValidateName(file.Name);
        }
        for(int i=0;i<_manifest.Maps.Count;i++)if(_manifest.Maps[i].State=="Collecting")_manifest.Maps[i]=_manifest.Maps[i] with { State="Interrupted",Status="上次采集中断，可重新采集",Progress=0 };
        SaveManifest();
    }
    public MapProbeBatchEntry[] Snapshot() { lock(_gate)return _manifest.Maps.ToArray(); }
    public string Begin(string label,uint territory,uint map) {
        lock(_gate) {
            if(_manifest.Maps.Count>=100)throw new InvalidOperationException("本批次达到 100 次采集上限，请导出后使用新批次。");
            var id=Guid.NewGuid().ToString("N");
            _manifest.Maps.Add(new() { Id=id,Label=(label??"")[..Math.Min(label?.Length??0,160)],Territory=territory,Map=map,StartedUtc=DateTime.UtcNow,Status="已抓取对象快照",Progress=.1f });
            SaveManifest();return id;
        }
    }
    public void Update(string id,float progress,string status) {
        lock(_gate) {
            var index=Index(id);var item=_manifest.Maps[index];
            if(item.State!="Collecting")return;
            _manifest.Maps[index]=item with { Progress=float.IsFinite(progress)?Math.Clamp(progress,0,.99f):0,Status=status };
            SaveManifest();
        }
    }
    public void Fail(string id,string status) {
        lock(_gate) { var index=Index(id);if(_manifest.Maps[index].State=="Complete")return;_manifest.Maps[index]=_manifest.Maps[index] with { State="Failed",Status=status,Progress=0 };SaveManifest(); }
    }
    public void Complete(string id,IReadOnlyDictionary<string,string> documents) {
        ValidateId(id);
        lock(_gate) { if(_manifest.Maps[Index(id)].State!="Collecting")throw new InvalidOperationException("Capture is no longer active."); }
        if(documents.Count==0||documents.Count>8)throw new InvalidDataException("Invalid document count.");
        var files=new List<MapProbeFile>();var folder=Path.Combine(_root,id);Directory.CreateDirectory(folder);
        foreach(var pair in documents) {
            ValidateName(pair.Key);var bytes=Encoding.UTF8.GetBytes(pair.Value);
            if(bytes.Length>8*1024*1024)throw new InvalidDataException("Probe document exceeds 8 MiB.");
            var path=Path.Combine(folder,pair.Key);AtomicWrite(path,bytes);
            var saved=File.ReadAllBytes(path);var hash=Convert.ToHexString(SHA256.HashData(bytes));
            if(Convert.ToHexString(SHA256.HashData(saved))!=hash)throw new IOException("Probe file verification failed.");
            files.Add(new(pair.Key,saved.Length,hash));
        }
        lock(_gate) {
            var index=Index(id);if(_manifest.Maps[index].State!="Collecting")throw new InvalidOperationException("Capture is no longer active.");
            _manifest.Maps[index]=_manifest.Maps[index] with { State="Complete",Progress=1,Status="本地图已保存并校验，可切换下一张",Files=files };
            SaveManifest();
        }
    }
    public string Export(string destination,Action<float,string>? progress=null) {
        Manifest snapshot;
        lock(_gate) {
            if(_manifest.Maps.Any(m=>m.State=="Collecting"))throw new InvalidOperationException("请等待当前地图采集完成。");
            snapshot=new() { Maps=_manifest.Maps.Where(m=>m.State=="Complete").ToList() };
        }
        if(snapshot.Maps.Count==0)throw new InvalidOperationException("没有已完成的地图。");
        var total=snapshot.Maps.Sum(m=>m.Files.Count)+1;int done=0;
        var temporary=destination+".partial";
        try {
            using(var zip=ZipFile.Open(temporary,ZipArchiveMode.Create)) {
                foreach(var map in snapshot.Maps)foreach(var file in map.Files) {
                    ValidateId(map.Id);ValidateName(file.Name);
                    var path=Path.Combine(_root,map.Id,file.Name);var info=new FileInfo(path);
                    if(info.Length!=file.Length||info.Length>8*1024*1024)throw new InvalidDataException("采集文件大小不符，请重新采集该地图。");
                    var bytes=File.ReadAllBytes(path);
                    if(Convert.ToHexString(SHA256.HashData(bytes))!=file.Sha256)throw new InvalidDataException("采集文件校验不符，请重新采集该地图。");
                    using(var stream=zip.CreateEntry(map.Id+"/"+file.Name,CompressionLevel.Optimal).Open())stream.Write(bytes);
                    progress?.Invoke((float)++done/total,"正在打包："+map.Label);
                }
                using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open(),Encoding.UTF8))writer.Write(JsonSerializer.Serialize(snapshot,Json));
            }
            File.Move(temporary,destination,false);progress?.Invoke(1,"ZIP 已保存，可发送到聊天");return destination;
        } catch { if(File.Exists(temporary))File.Delete(temporary);throw; }
    }
    private int Index(string id) { var index=_manifest.Maps.FindIndex(m=>m.Id==id);return index>=0?index:throw new KeyNotFoundException("Unknown capture."); }
    private static void ValidateId(string id) { if(!Guid.TryParseExact(id,"N",out _))throw new InvalidDataException("Invalid capture identity."); }
    private static void ValidateName(string name) {
        if(string.IsNullOrEmpty(name)||name!=Path.GetFileName(name)||name.Contains('\\')||name.Contains('/')||!name.EndsWith(".json",StringComparison.Ordinal)||name.Length>80)throw new InvalidDataException("Invalid probe document name.");
    }
    private void SaveManifest()=>AtomicWrite(Path.Combine(_root,"manifest.json"),Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_manifest,Json)));
    private static void AtomicWrite(string path,byte[] bytes) { var temp=path+".tmp";File.WriteAllBytes(temp,bytes);File.Move(temp,path,true); }
}
