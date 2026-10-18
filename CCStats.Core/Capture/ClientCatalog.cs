using System.Text.Json;
using PvpStats.Analysis;
namespace CCStats.Core.Capture;
/// <summary>Stable name identities from a pinned sheet snapshot, not a table of inferred trigger dimensions.</summary>
public static class ClientCatalog {
    private sealed class Catalog {
        public string SourceCommit {get;set;}="";
        public Dictionary<uint,string> Worlds {get;set;}=new();
        public Dictionary<uint,string> Npcs {get;set;}=new();
        public Dictionary<uint,string> Objects {get;set;}=new();
    }
    private static readonly Lazy<Catalog> Data=new(()=> {
        using var stream=typeof(ClientCatalog).Assembly.GetManifestResourceStream("CCStats.ClientIdentities.json")??throw new InvalidDataException("缺少客户端名称资料。");
        return JsonSerializer.Deserialize<Catalog>(stream)??throw new InvalidDataException("客户端名称资料损坏。");
    });
    public static string PlayerName(string name,ushort world)=>Data.Value.Worlds.TryGetValue(world,out var server)?$"{name} {server}":$"{name} [世界 {world}]";
    public static ArenaMechanicKind Mechanic(byte objectKind,uint nameId,uint dataId,uint territory) {
        var map=ReplayMapMesh.MapKey(territory);var kind=objectKind==2?ArenaMechanicScene.FromNpc(nameId,map):ArenaMechanicKind.Unknown;
        if(kind!=ArenaMechanicKind.Unknown)return kind;
        var names=objectKind==2?Data.Value.Npcs:Data.Value.Objects;var id=objectKind==2?nameId:dataId;
        return names.TryGetValue(id,out var label)?ArenaMechanicScene.FromObjectName(label,map):ArenaMechanicKind.Unknown;
    }
}
