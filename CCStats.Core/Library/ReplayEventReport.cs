using PvpStats.Analysis;
namespace CCStats.Core.Library;
public static class ReplayEventReport {
    public static TimelineRow[] Build(ReplayBundle bundle) {
        var rows=new List<TimelineRow>(bundle.Timeline);var resolved=bundle.Data.Effects.Select(e=>(e.Actor,e.ActionId,e.Sequence)).ToHashSet();
        foreach(var group in bundle.Data.Effects.GroupBy(e=>(e.Actor,e.ActionId,e.Sequence,Time:e.Sequence==0?e.Timestamp:DateTime.MinValue))) {
            var first=group.MinBy(e=>e.Timestamp)!;var targets=group.Select(e=>e.Target).Where(t=>!string.IsNullOrWhiteSpace(t)).Distinct().ToArray();
            rows.Add(new(first.Timestamp,first.Actor.StartsWith("场地实体 ",StringComparison.Ordinal)?"地图事件":"技能",SkillReport.Source(first.Actor,first.ActionId),string.Join("、",targets),SkillReport.Name(first.ActionId)));
        }
        foreach(var group in bundle.Data.ReplaySkills.Where(s=>s.Sequence==0||!resolved.Contains((s.Actor,s.ActionId,s.Sequence))).GroupBy(s=>(s.Actor,s.ActionId,s.Sequence,Time:s.Sequence==0?s.Timestamp:DateTime.MinValue))) {
            var first=group.MinBy(e=>e.Timestamp)!;rows.Add(new(first.Timestamp,"技能",SkillReport.Source(first.Actor,first.ActionId),string.Join("、",group.Select(s=>s.TargetPlayer).Where(t=>!string.IsNullOrWhiteSpace(t)).Distinct()),SkillReport.Name(first.ActionId)));
        }
        rows.AddRange(bundle.Data.MapMechanics.Select(e=>new TimelineRow(e.Timestamp,"地图事件","场地机制","",MapMechanicVisuals.Find(e.ActionId)?.Name??e.Label)));
        return rows.Distinct().OrderBy(e=>e.Timestamp).ToArray();
    }
}
