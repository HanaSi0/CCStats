using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
namespace PvpStats.Analysis;
public sealed class ReplaySummonFrame {
    public DateTime Timestamp { get; set; }
    public List<ReplaySummon> Summons { get; set; } = new();
}
public sealed class ReplaySummon {
    public bool Estimated { get; set; }
    public uint EntityId { get; set; }
    public uint NameId { get; set; }
    public string Owner { get; set; } = "";
    public bool Phoenix { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}
public sealed class ReplaySummonTarget {
    public string Player { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public bool Benefit { get; set; }
}
public static class ReplaySummons {
    // Only autonomous actions of LB summons, not ordinary player follow-up spells.
    public static bool IsPetAction(uint id) => id is 29675 or 29676 or 29680 or 29681;
    public static bool IsPhoenixAction(uint id) => id is 29678 or 29680 or 29681;
    public static bool IsSummonAction(uint id) => id is 29673 or 29678;
    private static bool Ground(ReplaySkillEvent e,out Vector3 at) {
        at=new(e.GroundX??float.NaN,e.GroundY??e.Y,e.GroundZ??float.NaN);
        return LimitBreakVisuals.Finite(at);
    }
    public static ReplaySummonFrame Resolve(IReadOnlyList<ReplaySummonFrame> frames,IReadOnlyList<ReplaySkillEvent> events,DateTime now) {
        var result=new ReplaySummonFrame {Timestamp=now};
        var observed=At(frames,now);
        var lastScan=frames.LastOrDefault(f=>f.Timestamp<=now);
        if(observed!=null)result.Summons.AddRange(observed.Summons);
        var past=events.Where(e=>e.Timestamp<=now&&e.Timestamp>=now.AddSeconds(-22)).ToArray();
        foreach(var group in past.Where(e=>IsSummonAction(e.ActionId)&&e.Actor.Length>0).GroupBy(e=>e.Actor)) {
            var last=group.OrderByDescending(e=>e.Timestamp).First();
            // Use/impact packets belong to one summon and must not restart its 20s lifetime.
            var root=group.Where(e=>e.ActionId==last.ActionId&&e.Timestamp>=last.Timestamp.AddSeconds(-2)).OrderBy(e=>e.Timestamp).First();
            if(now>=root.Timestamp.AddSeconds(20))continue;
            var phoenix=IsPhoenixAction(root.ActionId);
            var petEvent=past.Where(e=>IsPetAction(e.ActionId)&&e.Timestamp>=root.Timestamp&&IsPhoenixAction(e.ActionId)==phoenix&&e.Actor==root.Actor)
                .OrderByDescending(e=>e.Timestamp).FirstOrDefault();
            if(!Ground(root,out var at)) {
                if(petEvent==null||!LimitBreakVisuals.Finite(at=new(petEvent.X,petEvent.Y,petEvent.Z)))continue;
            }
            bool Matches(ReplaySummon p)=>p.Phoenix==phoenix&&(p.Owner==root.Actor||(petEvent is {SourceEntityId:>0}&&p.EntityId==petEvent.SourceEntityId)
                ||(p.Owner.Length==0&&Vector2.DistanceSquared(new(p.X,p.Z),new(at.X,at.Z))<16));
            if(result.Summons.Any(Matches))continue;
            // Once an identified object disappears, do not resurrect it when the empty sample becomes stale.
            if(lastScan!=null&&!lastScan.Summons.Any(Matches)&&frames.Any(f=>f.Timestamp>=root.Timestamp&&f.Timestamp<lastScan.Timestamp&&f.Summons.Any(Matches)))continue;
            var recentPhysical=petEvent!=null&&(now-petEvent.Timestamp).TotalSeconds<=1;
            if(recentPhysical)at=new(petEvent!.X,petEvent.Y,petEvent.Z);
            result.Summons.Add(new() {Owner=root.Actor,Phoenix=phoenix,X=at.X,Y=at.Y,Z=at.Z,EntityId=recentPhysical?petEvent!.SourceEntityId:0,Estimated=true});
        }
        // Owner-less autonomous effects still provide real source positions, even if no root LB was recorded.
        foreach(var e in past.Where(e=>IsPetAction(e.ActionId)&&e.Timestamp>=now.AddSeconds(-1)).GroupBy(e=>e.SourceEntityId).Select(g=>g.OrderByDescending(x=>x.Timestamp).First())) {
            if(e.SourceEntityId==0||result.Summons.Any(p=>p.EntityId==e.SourceEntityId||p.Owner.Length>0&&p.Owner==e.Actor&&p.Phoenix==IsPhoenixAction(e.ActionId)))continue;
            if(!LimitBreakVisuals.Finite(new(e.X,e.Y,e.Z)))continue;
            var nearby=result.Summons.Where(p=>p.Estimated&&p.EntityId==0&&p.Phoenix==IsPhoenixAction(e.ActionId)&&Vector2.DistanceSquared(new(p.X,p.Z),new(e.X,e.Z))<16).ToArray();
            if(nearby.Length==1) {
                nearby[0].EntityId=e.SourceEntityId;nearby[0].X=e.X;nearby[0].Y=e.Y;nearby[0].Z=e.Z;continue;
            }
            result.Summons.Add(new() {EntityId=e.SourceEntityId,Owner=e.Actor,Phoenix=IsPhoenixAction(e.ActionId),X=e.X,Y=e.Y,Z=e.Z});
        }
        return result;
    }
    public static void RecoverEffects(DetailedTelemetry data,List<ReplaySkillEvent> events) {
        var recorded=events.ToArray();
        foreach(var group in data.Effects.Where(e=>e.PetSource&&IsPetAction(e.ActionId)).GroupBy(e=>(e.Timestamp,e.Actor,e.ActionId,e.Sequence))) {
            var effect=group.First();
            if(recorded.Any(e=>e.ActionId==effect.ActionId&&e.Actor==effect.Actor&&Math.Abs((e.Timestamp-effect.Timestamp).TotalSeconds)<.4))continue;
            var root=recorded.Where(e=>IsSummonAction(e.ActionId)&&e.Actor==effect.Actor&&e.Timestamp<=effect.Timestamp&&e.Timestamp>effect.Timestamp.AddSeconds(-20))
                .OrderByDescending(e=>e.Timestamp).FirstOrDefault();
            if(root==null||IsPhoenixAction(root.ActionId)!=IsPhoenixAction(effect.ActionId)||!Ground(root,out var at))continue;
            var entry=new ReplaySkillEvent {Timestamp=effect.Timestamp,ActionId=effect.ActionId,Sequence=effect.Sequence,Actor=effect.Actor,PetSource=true,Resolved=true,LegacyEstimate=true,X=at.X,Y=at.Y,Z=at.Z};
            var frame=DetailedAnalysis.FrameAt(data,effect.Timestamp);
            foreach(var target in group.Where(e=>e.Type is 3 or 4 or 5 or 6||e.ActionId==29680&&e.Type is 14 or 15).GroupBy(e=>(e.Target,Benefit:e.Type is 4 or 14 or 15))) {
                var player=frame?.Players.FirstOrDefault(p=>p.Player==target.Key.Target);if(player==null)continue;
                entry.SummonTargets.Add(new() {Player=player.Player,X=player.X,Y=player.Y??at.Y,Z=player.Z,Benefit=target.Key.Benefit});
            }
            if(entry.SummonTargets.Count>0)events.Add(entry);
        }
    }
    public static bool IsPetName(uint id) => id is 6566 or 8228;
    public static ReplaySummonFrame? At(IReadOnlyList<ReplaySummonFrame> frames,DateTime now) {
        int lo=0,hi=frames.Count-1,found=-1;
        while(lo<=hi) { int mid=(lo+hi)/2;if(frames[mid].Timestamp<=now) { found=mid;lo=mid+1; }else hi=mid-1; }
        return found>=0&&(now-frames[found].Timestamp).TotalSeconds<=.45?frames[found]:null;
    }
    private static bool MatchesSource(ReplaySummon pet,ReplaySkillEvent effect) {
        if(pet.Phoenix!=IsPhoenixAction(effect.ActionId))return false;
        if(pet.EntityId!=0&&effect.SourceEntityId!=0)return pet.EntityId==effect.SourceEntityId;
        return pet.Owner.Length>0&&effect.Actor==pet.Owner;
    }
    public static void Draw(ReplaySceneGeometry scene,ReplaySummonFrame? frame,IReadOnlyList<ReplaySkillEvent> events,MovementFrame? players,DateTime now,Func<float,float,float,float> height) {
        if(frame==null)return;
        var impacts=events.Where(e=>IsPetAction(e.ActionId)&&e.Resolved&&e.Timestamp<=now&&e.Timestamp>=now.AddSeconds(-1.2)).ToArray();
        foreach(var pet in frame.Summons.Take(10)) {
            if(scene.Vertices.Length>72000)break;
            var basePoint=new Vector3(pet.X,height(pet.X,pet.Y,pet.Z),pet.Z);
            if(!LimitBreakVisuals.Finite(basePoint))continue;
            // Violet = summon damage; mint = summon healing. Neither uses player red/blue.
            var marker=pet.Phoenix?ReplaySceneGeometry.Rgba(.25f,1,.68f):ReplaySceneGeometry.Rgba(.85f,.38f,1);
            scene.Diamond(basePoint+new Vector3(0,4.5f+MathF.Sin((float)(now.TimeOfDay.TotalSeconds*3))*.25f,0),1.15f,2.2f,marker);
            // Both pets automatically select enemies within 30y. This is a selection range, not AoE damage.
            var phase=(float)(now.TimeOfDay.TotalSeconds%2)/2;
            for(int wave=0;wave<3;wave++) {
                var t=(phase+wave/3f)%1;var radius=30*t;
                if(radius<.3f)continue;
                var color=ReplaySceneGeometry.Rgba(.85f,.38f,1,.85f*(1-t));
                for(int i=0;i<64;i++) {
                    Vector3 P(int j) { var a=j*MathF.Tau/64;var x=pet.X+MathF.Sin(a)*radius;var z=pet.Z+MathF.Cos(a)*radius;return new(x,height(x,pet.Y,z)+.1f,z); }
                    scene.Ribbon(P(i),P(i+1),.3f,color);
                }
            }
            foreach(var e in impacts.Where(e=>MatchesSource(pet,e)).GroupBy(e=>(e.ActionId,e.Sequence)).Select(g=>g.Last()).TakeLast(4)) {
                var fade=1-(float)(now-e.Timestamp).TotalSeconds/1.2f;
                if(e.ActionId is 29675 or 29680) {
                    var radius=15*(1-fade);
                    if(radius>.3f)for(int i=0;i<64;i++) {
                        Vector3 P(int j) { var a=j*MathF.Tau/64;var x=e.X+MathF.Sin(a)*radius;var z=e.Z+MathF.Cos(a)*radius;return new(x,height(x,e.Y,z)+.12f,z); }
                        var color=e.ActionId==29680?ReplaySceneGeometry.Rgba(.25f,1,.68f,.75f*fade):ReplaySceneGeometry.Rgba(.85f,.38f,1,.75f*fade);
                        scene.Ribbon(P(i),P(i+1),.2f,color);
                    }
                }
                foreach(var target in e.SummonTargets.Take(10)) {
                    var observed=players?.Players.FirstOrDefault(p=>p.Player==target.Player);
                    var at=observed==null?new Vector3(target.X,target.Y,target.Z):new Vector3(observed.X,observed.Y??target.Y,observed.Z);
                    at.Y=height(at.X,at.Y,at.Z)+1.4f;
                    if(!LimitBreakVisuals.Finite(at))continue;
                    var color=target.Benefit?ReplaySceneGeometry.Rgba(.25f,1,.68f,.85f*fade):ReplaySceneGeometry.Rgba(.85f,.38f,1,.85f*fade);
                    scene.Beam(basePoint+new Vector3(0,5.3f,0),at,.24f,color);
                }
            }
        }
    }
}
