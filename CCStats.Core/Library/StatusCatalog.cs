using System.Text.Json;
using PvpStats.Localization;
namespace CCStats.Core.Library;
public sealed record StatusEntry(string Name,uint Icon,int MaxStacks);
public static class StatusCatalog {
    private static readonly Lazy<Dictionary<uint,StatusEntry>> Entries=new(()=>{
        using var stream=typeof(StatusCatalog).Assembly.GetManifestResourceStream("CCStats.StatusCatalog.json")!;using var json=JsonDocument.Parse(stream);
        return json.RootElement.GetProperty("Statuses").EnumerateObject().ToDictionary(p=>uint.Parse(p.Name,System.Globalization.CultureInfo.InvariantCulture),p=>p.Value.Deserialize<StatusEntry>()!);
    });
    public static StatusEntry? Find(uint id)=>Entries.Value.GetValueOrDefault(id);
    public static string Name(uint id)=>ChineseText.StatusName(id,Find(id)?.Name??$"未收录的状态（{id}）");
}
