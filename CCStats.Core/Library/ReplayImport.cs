using System.Text.Json;
using PvpStats.Analysis;

namespace CCStats.Core.Library;
public static class ReplayImport {
    public static async Task<ReplayBundle> ReadAsync(string path,CancellationToken token=default) {
        if(new FileInfo(path).Length>Import.ReplayFileReader.MaximumBytes)throw new InvalidDataException("文件超过 128 MiB。");
        await using var stream=File.OpenRead(path);using var json=await JsonDocument.ParseAsync(stream,cancellationToken:token);
        var root=json.RootElement;var summary=Import.ReplayFileReader.Read(root,Path.GetFullPath(path));var data=root.GetProperty("Telemetry").Deserialize<DetailedTelemetry>()??new();
        if(data.Frames.Count>180000||data.Effects.Count>1000000)throw new InvalidDataException("记录超出容量限制。");
        var bundle=new ReplayBundle {Match=new() {Id=summary.MatchId,StartUtc=summary.StartUtc.UtcDateTime,EndUtc=summary.EndUtc?.UtcDateTime,Territory=summary.TerritoryId,Observation=summary.IsObservation,SourcePath=Path.GetFullPath(path)},Data=data};
        if(root.TryGetProperty("Record",out var metadata)&&metadata.ValueKind==JsonValueKind.Object) {var record=metadata.Deserialize<MatchRecord>();if(record!=null&&record.Id==summary.MatchId) {bundle.Match=record;bundle.Match.SourcePath=Path.GetFullPath(path);}}
        if(root.TryGetProperty("Timeline",out var timeline)&&timeline.ValueKind==JsonValueKind.Array)bundle.Timeline=timeline.Deserialize<List<TimelineRow>>()??new();
        if(root.TryGetProperty("Actions",out var actions)&&actions.ValueKind==JsonValueKind.Array)bundle.Actions=actions.Deserialize<List<ActionSummary>>()??new();
        if(root.TryGetProperty("Progress",out var progress)&&progress.ValueKind==JsonValueKind.Array)bundle.Progress=progress.Deserialize<List<ProgressSample>>()??new();
        if(bundle.Match.Players.Count==0)bundle.Match.Players=data.Frames.SelectMany(f=>f.Players).GroupBy(p=>p.Player,StringComparer.Ordinal).Select(g=>new PlayerScore {Name=g.Key,Job=JobNames.Name(g.First().ClassJobId),Team=g.First().Team}).ToList();
        if(root.TryGetProperty("Knockouts",out var kills)&&kills.ValueKind==JsonValueKind.Array)foreach(var row in kills.EnumerateArray()) {
            if(row.TryGetProperty("Timestamp",out var stamp)&&stamp.TryGetDateTime(out var time))bundle.Timeline.Add(new(time,"减员",row.TryGetProperty("Killer",out var killer)?killer.ToString():"",row.TryGetProperty("Victim",out var victim)?victim.ToString():"",""));
        }
        ReplaySummons.RecoverEffects(bundle.Data,bundle.Data.ReplaySkills);
        return bundle;
    }
    public static async Task ExportAsync(ReplayBundle bundle,string path) {
        // Expand movement only in the external interchange document; keep the owned database compressed.
        await using var file=File.Create(path);
        await JsonSerializer.SerializeAsync(file,new {RecordKind=bundle.Match.Observation?"IndependentObservation":"Match",MatchId=bundle.Match.Id,TerritoryId=bundle.Match.Territory,StartUtc=bundle.Match.StartUtc,MatchEndTime=bundle.Match.EndUtc,Record=bundle.Match,Timeline=bundle.Timeline,Actions=bundle.Actions,Progress=bundle.Progress,Telemetry=bundle.Data},new JsonSerializerOptions {WriteIndented=false});
    }
}
