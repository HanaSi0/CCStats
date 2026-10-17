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
    public static bool IsPetName(uint id) => id is 6566 or 8228;
    public static ReplaySummonFrame? At(IReadOnlyList<ReplaySummonFrame> frames,DateTime now) {
        int lo=0,hi=frames.Count-1,found=-1;
        while(lo<=hi) { int mid=(lo+hi)/2;if(frames[mid].Timestamp<=now) { found=mid;lo=mid+1; }else hi=mid-1; }
        return found>=0&&(now-frames[found].Timestamp).TotalSeconds<=.45?frames[found]:null;
    }
    public static void Draw(ReplaySceneGeometry scene,ReplaySummonFrame? frame,IReadOnlyList<ReplaySkillEvent> events,MovementFrame? players,DateTime now,Func<float,float,float,float> height) {
        if(frame==null)return;
        var impacts=events.Where(e=>e.PetSource&&e.Resolved&&e.Timestamp<=now&&e.Timestamp>=now.AddSeconds(-.75)).ToLookup(e=>e.SourceEntityId);
        foreach(var pet in frame.Summons.Take(10)) {
            var basePoint=new Vector3(pet.X,height(pet.X,pet.Y,pet.Z),pet.Z);
            if(!LimitBreakVisuals.Finite(basePoint))continue;
            // Violet = summon damage; mint = summon healing. Neither uses player red/blue.
            var marker=pet.Phoenix?ReplaySceneGeometry.Rgba(.25f,1,.68f):ReplaySceneGeometry.Rgba(.85f,.38f,1);
            scene.Diamond(basePoint+new Vector3(0,4.5f+MathF.Sin((float)(now.TimeOfDay.TotalSeconds*3))*.25f,0),.85f,1.8f,marker);
            // Both pets automatically select enemies within 30y. This is a selection range, not AoE damage.
            var phase=(float)(now.TimeOfDay.TotalSeconds%2)/2;
            for(int wave=0;wave<3;wave++) {
                var t=(phase+wave/3f)%1;var radius=30*t;
                if(radius<.3f)continue;
                var color=ReplaySceneGeometry.Rgba(.85f,.38f,1,.55f*(1-t));
                for(int i=0;i<64;i++) {
                    Vector3 P(int j) { var a=j*MathF.Tau/64;var x=pet.X+MathF.Sin(a)*radius;var z=pet.Z+MathF.Cos(a)*radius;return new(x,height(x,pet.Y,z)+.1f,z); }
                    scene.Ribbon(P(i),P(i+1),.16f,color);
                }
            }
            foreach(var e in impacts[pet.EntityId].GroupBy(e=>(e.ActionId,e.Sequence)).Select(g=>g.Last()).TakeLast(4)) {
                var fade=1-(float)(now-e.Timestamp).TotalSeconds/.75f;
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
                    scene.Ribbon(basePoint+new Vector3(0,5.3f,0),at,.14f,color);
                }
            }
        }
    }
}
