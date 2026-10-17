using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace PvpStats.Analysis;
public static class LBCaptureArchive {
    // Only completed immediate-child sessions; unrelated files and earlier ZIPs are excluded.
    public static int Export(string root,string destination,Action<int,int>? progress=null) {
        var sessions=Directory.GetDirectories(root).Where(d=>File.Exists(Path.Combine(d,"summary.json"))).OrderBy(d=>d).ToArray();
        if(sessions.Length==0)throw new InvalidOperationException("没有已完成的采集记录");
        bool created=false;
        try {
            using var stream=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            created=true;
            using(var archive=new ZipArchive(stream,ZipArchiveMode.Create)) {
                for(var i=0;i<sessions.Length;i++) {
                    var dir=sessions[i];
                    foreach(var name in new[]{"summary.json","observations.ndjson"}) {
                        var file=Path.Combine(dir,name);
                        // An incomplete session must fail rather than produce a misleading successful export.
                        archive.CreateEntryFromFile(file,Path.GetFileName(dir)+"/"+name,CompressionLevel.Fastest);
                    }
                    progress?.Invoke(i+1,sessions.Length);
                }
            }
            return sessions.Length;
        } catch { if(created&&File.Exists(destination))File.Delete(destination);throw; }
    }
}
