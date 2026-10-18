using System.Text.Json;

namespace CCStats.Core;

public sealed class DesktopPreferences {
    public Dictionary<string,string> CustomColors {get;set;}=new();
    public string CloseBehavior {get;set;}="Ask";
    public bool AutoRecord {get;set;}=true;
    public string Theme {get;set;}="深海蓝";
    public bool AutoDetectGame {get;set;}=true;
    public string LocalPlayer {get;set;}="";
    public Library.PlayerLinks Links {get;set;}=new();
    public List<string> RecentFiles { get; set; } = new();
    public static DesktopPreferences Load(string path) {
        try {
            var result = JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(path)) ?? new();
            result.CustomColors??=new();
            result.Links??=new();
            var aliases=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var pair in result.Links.Aliases??new())if(!string.IsNullOrWhiteSpace(pair.Key)&&!string.IsNullOrWhiteSpace(pair.Value))aliases[pair.Key]=pair.Value;
            result.Links.Aliases=aliases;
            if(result.CloseBehavior is not ("Ask" or "Exit" or "Tray"))result.CloseBehavior="Ask";
            result.RecentFiles = (result.RecentFiles ?? new()).Where(p=>!string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
            return result;
        } catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Remember(string path) {
        var full = Path.GetFullPath(path);
        RecentFiles.RemoveAll(p=>string.Equals(p,full,StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0,full);
        if(RecentFiles.Count>10) RecentFiles.RemoveRange(10,RecentFiles.Count-10);
    }
    public void Save(string path) {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions {WriteIndented=true}));
            File.Move(temp,full,true);
        } finally { if(File.Exists(temp)) File.Delete(temp); }
    }
}
