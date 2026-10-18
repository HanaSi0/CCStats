using System.Text.Json;
namespace CCStats.Core.Library;
public sealed record MapCoordinates(int Factor,int OffsetX,int OffsetY);
public static class MapMetadata {
    private static readonly Lazy<Dictionary<uint,MapCoordinates>> Maps=new(()=> {
        using var stream=typeof(MapMetadata).Assembly.GetManifestResourceStream("CCStats.MapMetadata.json")??throw new InvalidDataException("地图元数据缺失");
        using var json=JsonDocument.Parse(stream);var result=new Dictionary<uint,MapCoordinates>();
        foreach(var map in json.RootElement.GetProperty("maps").EnumerateArray())foreach(var id in map.GetProperty("territory_ids").EnumerateArray())result[id.GetUInt32()]=new(map.GetProperty("size_factor").GetInt32(),map.GetProperty("offset_x").GetInt32(),map.GetProperty("offset_y").GetInt32());
        return result;
    });
    public static MapCoordinates? For(uint territory)=>Maps.Value.GetValueOrDefault(territory);
}
