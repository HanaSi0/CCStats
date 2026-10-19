using System.Text.Json;

namespace CCStats.Core.Import;

/// <summary>Read a replay export without editing its original data.</summary>
public static class ReplayFileReader {
    public const long MaximumBytes = 128L * 1024 * 1024;
    public static async Task<ReplaySummary> ReadAsync(string path, CancellationToken token = default) {
        var fullPath = Path.GetFullPath(path);
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if(stream.Length == 0 || stream.Length > MaximumBytes) throw new InvalidDataException("请选择不超过 128 MiB 的 CCStats 回放 JSON 文件。");
        using var document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 64 }, token);
        return Read(document.RootElement, fullPath);
    }
    internal static ReplaySummary Read(JsonElement root, string path) {
        if(root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Telemetry", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("不是有效的 CCStats 回放文件，请选择包含记录数据的 JSON。");
        if(!root.TryGetProperty("MatchId",out var idElement) || idElement.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("回放缺少比赛编号。");
        var id = idElement.GetString() ?? "";
        if(id.Length != 24 || !id.All(Uri.IsHexDigit)) throw new InvalidDataException("回放比赛编号无效。");
        if(!root.TryGetProperty("TerritoryId",out var territoryElement) || territoryElement.ValueKind != JsonValueKind.Number || !territoryElement.TryGetUInt32(out var territory))
            throw new InvalidDataException("回放缺少有效区域编号。");
        if(!root.TryGetProperty("StartUtc",out var startElement) || startElement.ValueKind != JsonValueKind.String || !UtcStamp(startElement,out var start))
            throw new InvalidDataException("回放缺少有效开始时间。");
        DateTimeOffset? end = null;
        if(root.TryGetProperty("MatchEndTime",out var endElement) && endElement.ValueKind != JsonValueKind.Null) {
            if(endElement.ValueKind != JsonValueKind.String || !UtcStamp(endElement,out var parsed) || parsed < start) throw new InvalidDataException("回放结束时间无效。");
            end = parsed;
        }
        var frames = Array(data,"Frames");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach(var frame in frames.EnumerateArray()) {
            if(frame.ValueKind != JsonValueKind.Object) throw new InvalidDataException("回放帧格式错误。");
            if(!frame.TryGetProperty("Players",out var players)) continue;
            if(players.ValueKind != JsonValueKind.Array) throw new InvalidDataException("玩家数据格式错误。");
            foreach(var player in players.EnumerateArray()) {
                if(player.ValueKind == JsonValueKind.Object && player.TryGetProperty("Player",out var name) && name.ValueKind == JsonValueKind.String) {
                    var text = name.GetString();
                    if(!string.IsNullOrWhiteSpace(text)) names.Add(text);
                    if(names.Count > 100 || text?.Length > 256) throw new InvalidDataException("玩家标识超出回放容量限制。");
                }
            }
        }
        return new(id,territory,MapName(territory),start,end,frames.GetArrayLength(),Array(data,"Effects").GetArrayLength(),
            Array(data,"ReplaySkills").GetArrayLength(),Array(data,"MapMechanics").GetArrayLength(),names.Order(StringComparer.Ordinal).ToArray(),path,root.TryGetProperty("RecordKind",out var kind) && kind.ValueKind==JsonValueKind.String && kind.GetString()=="IndependentObservation");
    }
    private static bool UtcStamp(JsonElement element,out DateTimeOffset value) {
        value=default;if(!element.TryGetDateTime(out var parsed))return false;
        if(parsed.Kind==DateTimeKind.Unspecified){value=new DateTimeOffset(Library.RecordTime.Utc(parsed));return true;}
        if(!element.TryGetDateTimeOffset(out value))return false;value=value.ToUniversalTime();return true;
    }
    private static readonly JsonElement EmptyArray = JsonSerializer.SerializeToElement(System.Array.Empty<object>());
    private static JsonElement Array(JsonElement parent, string key) {
        if(!parent.TryGetProperty(key,out var value)) return EmptyArray;
        if(value.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"回放字段 {key} 应为数组。");
        return value;
    }
    public static string MapName(uint territory) => territory switch {
        1032 or 1058 => "角力学校", 1033 or 1059 => "火山之心", 1034 or 1060 => "九霄云上",
        1116 or 1117 => "机关大殿", 1138 or 1139 => "赤土红沙", 1293 or 1294 => "海岸斗场",
        1357 or 1358 => "休兵书库", _ => $"未知地图（{territory}）"
    };
}
