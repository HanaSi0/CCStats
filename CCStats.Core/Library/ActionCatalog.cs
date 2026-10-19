using System.Text.Json;
namespace CCStats.Core.Library;
public static class ActionCatalog {
    private sealed class Sheet {public Dictionary<uint,string> Names {get;set;}=new();}
    private static readonly Lazy<Sheet> Data=new(()=>{using var stream=typeof(ActionCatalog).Assembly.GetManifestResourceStream("CCStats.ActionNames.json")??throw new InvalidDataException("缺少技能名称资料。");return JsonSerializer.Deserialize<Sheet>(stream)??new();});
    public static string Name(uint action)=>Data.Value.Names.GetValueOrDefault(action,$"技能 {action}");
}
