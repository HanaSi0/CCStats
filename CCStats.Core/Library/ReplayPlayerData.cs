using PvpStats.Analysis;
namespace CCStats.Core.Library;
public sealed record ReplayPlayerRow(string Name,string Job,int Team,int PositionSamples,uint? HP,uint? MaxHP,float? X,float? Y,float? Z,DateTime? LastSeen);
public static class ReplayPlayerData {
    public static List<ReplayPlayerRow> Build(ReplayBundle bundle) {
        var rows=new Dictionary<string,ReplayPlayerRow>(StringComparer.Ordinal);
        foreach(var p in bundle.Match.Players)rows[p.Name]=new(p.Name,p.Job,p.Team,0,null,null,null,null,null,null);
        foreach(var frame in bundle.Data.Frames)foreach(var p in frame.Players) {
            if(!float.IsFinite(p.X)||!float.IsFinite(p.Z)||p.Y.HasValue&&!float.IsFinite(p.Y.Value))continue;
            rows.TryGetValue(p.Player,out var previous);
            rows[p.Player]=new(p.Player,JobNames.Name(p.ClassJobId),p.Team,(previous?.PositionSamples??0)+1,p.HP,p.MaxHP,p.X,p.Y,p.Z,frame.Timestamp);
        }
        return rows.Values.OrderBy(p=>p.Team).ThenBy(p=>p.Name,StringComparer.Ordinal).ToList();
    }
}
