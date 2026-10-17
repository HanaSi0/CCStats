using System.IO.Compression;
using PvpStats.Analysis;
internal static class LBCaptureChecks {
    public static void Run(Action<bool,string> check) {
        var root=Path.Combine(Path.GetTempPath(),"ccstats-lb-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var a=Path.Combine(root,"job24");Directory.CreateDirectory(a);
            File.WriteAllText(Path.Combine(a,"summary.json"),"{\"job\":24}");
            var observation="{\"kind\":\"action\",\"name\":\"狂喜之心\",\"targets\":[{\"x\":1.5}]}\n";
            File.WriteAllText(Path.Combine(a,"observations.ndjson"),observation);
            File.WriteAllText(Path.Combine(a,"unrelated.txt"),"exclude");
            Directory.CreateDirectory(Path.Combine(root,"active-session"));
            File.WriteAllText(Path.Combine(root,"active-session","observations.ndjson"),"incomplete");
            var zip=Path.Combine(root,"export.zip");var progress=0;
            check(LBCaptureArchive.Export(root,zip,(done,total)=>progress=done)==1&&progress==1,"LB export includes completed sessions with progress");
            using(var archive=ZipFile.OpenRead(zip)) {
                check(archive.Entries.Count==2,"LB export excludes active sessions and unrelated files");
                using var read=new StreamReader(archive.GetEntry("job24/observations.ndjson")!.Open());
                check(read.ReadToEnd()==observation,"LB archive preserves Unicode names and raw observation values");
            }
            try {LBCaptureArchive.Export(root,zip);check(false,"duplicate LB export must fail");}catch(IOException) {check(File.Exists(zip),"failed export never deletes a pre-existing archive");}
            File.Delete(Path.Combine(a,"observations.ndjson"));
            var broken=Path.Combine(root,"broken.zip");
            try {LBCaptureArchive.Export(root,broken);check(false,"incomplete completed session must fail");}catch(FileNotFoundException) {check(!File.Exists(broken),"failed LB export removes only its own partial archive");}
        } finally {Directory.Delete(root,true);}
    }
}
