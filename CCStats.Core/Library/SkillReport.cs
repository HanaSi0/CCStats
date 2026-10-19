using PvpStats.Analysis;
using PvpStats.Localization;
namespace CCStats.Core.Library;
public sealed record SkillReportRow(string Player,string Kind,uint ActionId,string Name,int? Casts,int Targets,int? Hits,long Damage,long Healing,double? AverageDamage,double? AverageHealing,int StatusHits,int StatusMisses);
public static class SkillReport {
    public static string Name(uint id)=>ChineseText.ActionName(id,MapMechanicVisuals.Find(id)?.Name??ActionCatalog.Name(id));
    public static string Source(string actor,uint action){if(!actor.StartsWith("场地实体 ",StringComparison.Ordinal))return actor;var kind=ArenaMechanicScene.FromAction(action);return kind==ArenaMechanicKind.Unknown?"场地机制":ArenaMechanicScene.Name(kind);}
    public static List<SkillReportRow> Build(ReplayBundle bundle) {
        var rows=new Dictionary<(string,uint,string),SkillReportRow>();
        foreach(var a in bundle.Actions){string kind=a.Kind=="状态"?"状态":"玩家";var key=(a.Player,a.ActionId,kind);rows[key]=new(a.Player,kind,a.ActionId,a.Kind=="状态"?ChineseText.StatusName(a.ActionId,$"状态 {a.ActionId}"):Name(a.ActionId),a.Casts,a.Targets,null,a.Damage,a.Healing,a.Casts>0?(double)a.Damage/a.Casts:null,a.Casts>0?(double)a.Healing/a.Casts:null,a.StatusHits,a.StatusMisses);}
        foreach(var group in bundle.Data.Effects.GroupBy(e=>(Actor:Source(e.Actor,e.ActionId),e.ActionId,Kind:e.Actor.StartsWith("场地实体 ",StringComparison.Ordinal)?"场地机制":e.PetSource?"召唤兽":"玩家"))) {
            var key=(group.Key.Actor,group.Key.ActionId,group.Key.Kind);if(rows.ContainsKey(key))continue;
            var damage=group.Where(e=>DetailedAnalysis.IsDamage(e.Type)).ToArray();var healing=group.Where(e=>e.Type==4).ToArray();long d=damage.Sum(e=>(long)e.Amount),h=healing.Sum(e=>(long)e.Amount);
            int? casts=bundle.Data.ReplaySkills.Where(s=>s.Actor==group.Key.Actor&&s.ActionId==group.Key.ActionId).Select(s=>s.Sequence>0?$"{s.Sequence}:{s.SourceEntityId}":s.Timestamp.ToString("O")).Distinct().Count() is var n&&n>0?n:null;
            rows[key]=new(group.Key.Actor,group.Key.Kind,group.Key.ActionId,Name(group.Key.ActionId),casts,group.Select(e=>e.Target).Distinct().Count(),group.Count(),d,h,casts>0?(double)d/casts:null,casts>0?(double)h/casts:null,group.Count(e=>e.Type is 14 or 15),group.Count(e=>e.Type is 20 or 55));
        }
        return rows.Values.OrderByDescending(r=>r.Damage).ThenByDescending(r=>r.Healing).ToList();
    }
}
