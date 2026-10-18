using System.Text.Json;

namespace CCStats.Core;

public sealed class DesktopPreferences {
    public List<string> RecentFiles { get; set; } = new();
    public static DesktopPreferences Load(string path) {
        try {
            var result = JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(path)) ?? new();
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
