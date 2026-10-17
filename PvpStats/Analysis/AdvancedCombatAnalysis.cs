using System;
using System.Collections.Generic;
using System.Linq;
namespace PvpStats.Analysis;
public sealed class AdvancedPlayerStats {
    public string Player { get; set; } = "";
    public long DamageDealt,DamageReceived,HealingDealt,HealingReceived,SelfReturnHealing,DrainHealing,GuardedDamage,ShieldedDamage;
    public int DamageHits,GuardedHits,ShieldedHits,ControlApplications,ControlResists,ControlResistsUnderGuard,ConfirmedControls,UnknownControlResists;
    public double GuardSeconds,ControlSeconds,EstimatedGuardPrevented;
    public int EstimatedHits,PendingHits;
}
public static class AdvancedCombatAnalysis {
    public static Dictionary<string,AdvancedPlayerStats> Calculate(DetailedTelemetry data,List<MitigationEstimate> estimates) {
        var result=new Dictionary<string,AdvancedPlayerStats>();
        AdvancedPlayerStats Get(string name) { if(!result.TryGetValue(name,out var value))result[name]=value=new() {Player=name};return value; }
        // Global sequence is shared by use/impact packets. Collapse duplicate control results, not different attackers or targets.
        var controlKeys=new HashSet<(uint Seq,string Actor,string Target,uint Status,long Time)>();
        foreach(var e in data.Effects) {
            var actor=e.Actor.Length>0?Get(e.Actor):null;var target=e.Target.Length>0?Get(e.Target):null;
            if(DetailedAnalysis.IsDamage(e.Type)) {
                if(actor!=null) {actor.DamageDealt+=e.Amount;actor.DamageHits++;}
                if(target!=null) {
                    target.DamageReceived+=e.Amount;
                    if(e.GuardObserved) {target.GuardedHits++;target.GuardedDamage+=e.Amount;}
                    if(e.ShieldPercent>0) {target.ShieldedHits++;target.ShieldedDamage+=e.Amount;}
                }
            }
            if(e.Type==4) {
                if(actor!=null)actor.HealingDealt+=e.Amount;
                if(target!=null)target.HealingReceived+=e.Amount;
                if(actor!=null&&e.SelfReturn&&e.Actor==e.Target) {
                    actor.SelfReturnHealing+=e.Amount;
                    if(GuardActionCatalog.Drain.Contains(e.ActionId)||(e.ActorStatuses.Any(s=>s.Id==3039&&s.Remaining>0)&&e.IsWeaponskill))actor.DrainHealing+=e.Amount;
                }
            }
            if(!DetailedAnalysis.IsEnemy(e)||!DetailedAnalysis.Controls.ContainsKey(e.StatusId))continue;
            if(e.Type is not (14 or 20 or 55))continue;
            if(!controlKeys.Add((e.Sequence,e.Actor,e.Target,e.StatusId,e.Sequence==0?e.Timestamp.Ticks:0)))continue;
            if(e.Type==14&&actor!=null) {
                actor.ControlApplications++;
                if(data.Frames.Any(f=>f.Timestamp>=e.Timestamp&&f.Timestamp<=e.Timestamp.AddSeconds(Math.Max(.5,data.FrameGapSeconds))&&f.Players.Any(p=>p.Player==e.Target&&p.Controls.Any(s=>s.StatusId==e.StatusId&&s.Source==e.Actor&&s.Remaining>0))))actor.ConfirmedControls++;
            }
            if(e.Type is 20 or 55) {
                if(actor!=null)actor.ControlResists++;
                if(target!=null) {if(e.GuardObserved)target.ControlResistsUnderGuard++;else target.UnknownControlResists++;}
            }
        }
        for(int i=0;i+1<data.Frames.Count;i++) {
            var frame=data.Frames[i];var next=data.Frames[i+1];var dt=(next.Timestamp-frame.Timestamp).TotalSeconds;
            if(dt<=0||dt>data.FrameGapSeconds)continue;
            foreach(var player in frame.Players) {
                var stat=Get(player.Player);var after=next.Players.FirstOrDefault(p=>p.Player==player.Player);
                if(after==null||player.HP==0||after.HP==0)continue;
                var status=player.Statuses.FirstOrDefault(s=>s.Id is 3054 or 3673);
                if(status!=null&&player.StatusTimestamp<=frame.Timestamp&&after.Statuses.Any(s=>s.Id==status.Id)) {
                    var remaining=status.Remaining-(frame.Timestamp-player.StatusTimestamp).TotalSeconds;
                    if(remaining>0)stat.GuardSeconds+=Math.Min(dt,remaining);
                }
            }
        }
        foreach(var (name,baseStat) in DetailedAnalysis.Summarize(data))Get(name).ControlSeconds=baseStat.ObservedControlSeconds;
        foreach(var estimate in estimates) {
            if(estimate.Effect.Target.Length==0)continue;var target=Get(estimate.Effect.Target);
            if(estimate.Supported) {target.EstimatedHits++;target.EstimatedGuardPrevented+=estimate.GuardPrevented!.Value;}else target.PendingHits++;
        }
        return result;
    }
}
