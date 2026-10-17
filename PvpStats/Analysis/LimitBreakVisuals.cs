using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
namespace PvpStats.Analysis;
public enum LbShape { Circle, Line, DoubleLine, Cone, Cross, Single, Self }
public enum LbAnchor { Source, Target, Ground }
public sealed record LbVisualRule(uint ActionId,uint JobId,LbShape Shape,LbAnchor Anchor,float Size,float Width=0,bool Benefit=false,float Duration=1.25f,bool Reference=false,uint StatusId=0);
public sealed class ReplaySkillEvent {
    public DateTime Timestamp { get; set; }
    public uint ActionId { get; set; }
    public uint Sequence { get; set; }
    public int Variation { get; set; }
    public string Actor { get; set; } = "";
    public string TargetPlayer { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Heading { get; set; }
    public float? TargetX { get; set; }
    public float? TargetY { get; set; }
    public float? TargetZ { get; set; }
    public float? GroundX { get; set; }
    public float? GroundY { get; set; }
    public float? GroundZ { get; set; }
    public bool LegacyEstimate { get; set; }
    public bool Resolved { get; set; }
    public byte ClientCastType { get; set; }
    public int ClientEffectRange { get; set; }
    public int ClientWidth { get; set; }
    public List<uint> SelfStatusIds { get; set; } = new();
}
public static class LimitBreakVisuals {
    // Official PvP guide snapshot 2026-10-17 + client Action metadata. Unpublished angles/cross widths are references.
    public static readonly IReadOnlyList<LbVisualRule> Rules = new LbVisualRule[] {
        new(29069,19,LbShape.Circle,LbAnchor.Source,15,Benefit:true),
        new(29069,19,LbShape.Self,LbAnchor.Source,1,Benefit:true,Duration:10,StatusId:1302),
        new(29083,21,LbShape.Cone,LbAnchor.Source,12,90,Reference:true),
        new(29083,21,LbShape.Self,LbAnchor.Source,1,Benefit:true,Duration:15),
        new(29097,32,LbShape.DoubleLine,LbAnchor.Source,30,6,Reference:true),
        new(29097,32,LbShape.Self,LbAnchor.Source,1,Benefit:true,Duration:10,StatusId:3039),
        new(29130,37,LbShape.Circle,LbAnchor.Source,6,Duration:4,StatusId:3052),
        new(29131,37,LbShape.Circle,LbAnchor.Source,6),new(29469,37,LbShape.Circle,LbAnchor.Source,6),
        new(29230,24,LbShape.Line,LbAnchor.Source,40,6),
        new(29230,24,LbShape.Circle,LbAnchor.Source,30,Benefit:true,Duration:15,StatusId:2037),
        new(41502,28,LbShape.Circle,LbAnchor.Source,30,Benefit:true),
        new(41502,28,LbShape.Self,LbAnchor.Source,1,Benefit:true,Duration:20),
        new(29255,33,LbShape.Circle,LbAnchor.Source,15,Benefit:true),
        new(29255,33,LbShape.Self,LbAnchor.Source,1,Benefit:true,Duration:15),
        new(29255,33,LbShape.Circle,LbAnchor.Source,15),
        new(29266,40,LbShape.Circle,LbAnchor.Ground,5,Benefit:true,Duration:15),
        new(29266,40,LbShape.Circle,LbAnchor.Ground,5,Duration:15),
        new(29267,40,LbShape.Circle,LbAnchor.Ground,5,Benefit:true,Duration:15),
        new(29267,40,LbShape.Circle,LbAnchor.Ground,5,Duration:15),
        new(29485,20,LbShape.Single,LbAnchor.Target,1),
        new(29497,22,LbShape.Self,LbAnchor.Source,1,Benefit:true,Duration:4,StatusId:3180),
        new(29498,22,LbShape.Circle,LbAnchor.Source,10),new(29498,22,LbShape.Circle,LbAnchor.Source,5),
        new(29499,22,LbShape.Circle,LbAnchor.Source,10),
        new(29515,30,LbShape.Single,LbAnchor.Target,1),new(29516,30,LbShape.Single,LbAnchor.Target,1),
        new(29537,34,LbShape.Circle,LbAnchor.Target,5),
        new(29553,39,LbShape.Circle,LbAnchor.Source,10),
        new(29553,39,LbShape.Self,LbAnchor.Source,1,Benefit:true,Duration:20,StatusId:2863),
        new(39190,41,LbShape.Circle,LbAnchor.Target,5),
        new(39190,41,LbShape.Self,LbAnchor.Source,1,Benefit:true,Duration:20),
        new(29401,23,LbShape.Circle,LbAnchor.Source,30,Benefit:true,Duration:30,StatusId:3144),
        new(29415,31,LbShape.Single,LbAnchor.Target,1),
        new(29432,38,LbShape.Circle,LbAnchor.Source,15),
        new(29662,25,LbShape.Self,LbAnchor.Source,1,Benefit:true,Duration:30),
        new(29673,27,LbShape.Circle,LbAnchor.Ground,15),new(29675,27,LbShape.Circle,LbAnchor.Source,15),
        new(29678,27,LbShape.Circle,LbAnchor.Ground,15,Benefit:true),new(29680,27,LbShape.Circle,LbAnchor.Source,15,Benefit:true),
        new(41498,35,LbShape.Cross,LbAnchor.Target,15,6,Reference:true),
        new(41498,35,LbShape.Cross,LbAnchor.Target,15,6,Benefit:true,Reference:true),
        new(39215,42,LbShape.Circle,LbAnchor.Source,8),
        new(39215,42,LbShape.Circle,LbAnchor.Ground,8,Benefit:true,Duration:15),
        // LB-granted follow-ups. A rule never creates an event; the action must actually be recorded.
        new(29071,19,LbShape.Circle,LbAnchor.Target,5),new(29072,19,LbShape.Circle,LbAnchor.Target,5),new(29073,19,LbShape.Circle,LbAnchor.Target,5),
        new(29078,21,LbShape.Single,LbAnchor.Target,1),new(41433,21,LbShape.Circle,LbAnchor.Source,10),new(41437,32,LbShape.Line,LbAnchor.Source,10,4),
        new(41434,32,LbShape.Single,LbAnchor.Target,1),new(41435,32,LbShape.Single,LbAnchor.Target,1),new(41436,32,LbShape.Single,LbAnchor.Target,1),
        new(41500,28,LbShape.Circle,LbAnchor.Target,5),new(41501,28,LbShape.Circle,LbAnchor.Source,15,Benefit:true),
        new(41508,33,LbShape.Circle,LbAnchor.Target,5),
        new(29543,39,LbShape.Single,LbAnchor.Target,1),new(29544,39,LbShape.Single,LbAnchor.Target,1),
        new(29548,39,LbShape.Cone,LbAnchor.Source,8,180,Reference:true),new(29554,39,LbShape.Circle,LbAnchor.Target,5),new(41458,39,LbShape.Circle,LbAnchor.Target,5),
        new(39169,41,LbShape.Circle,LbAnchor.Target,5),new(39170,41,LbShape.Circle,LbAnchor.Target,5),new(39171,41,LbShape.Circle,LbAnchor.Target,5),new(39172,41,LbShape.Circle,LbAnchor.Target,5),new(39173,41,LbShape.Circle,LbAnchor.Target,5),
        new(39179,41,LbShape.Circle,LbAnchor.Target,5),new(39180,41,LbShape.Circle,LbAnchor.Target,5),new(39181,41,LbShape.Circle,LbAnchor.Target,5),new(39182,41,LbShape.Circle,LbAnchor.Target,5),
        new(41467,23,LbShape.Circle,LbAnchor.Target,5),
        new(29651,25,LbShape.Circle,LbAnchor.Target,5),new(29655,25,LbShape.Circle,LbAnchor.Target,5),new(41480,25,LbShape.Circle,LbAnchor.Target,5),new(41481,25,LbShape.Circle,LbAnchor.Target,5),
        new(41484,27,LbShape.Circle,LbAnchor.Target,5),new(41485,27,LbShape.Circle,LbAnchor.Target,5),
        new(29665,27,LbShape.Single,LbAnchor.Target,1),new(29666,27,LbShape.Single,LbAnchor.Target,1,Benefit:true),
        new(29676,27,LbShape.Single,LbAnchor.Target,1),new(29681,27,LbShape.Single,LbAnchor.Target,1),
        new(39216,42,LbShape.Circle,LbAnchor.Target,5),new(39217,42,LbShape.Circle,LbAnchor.Source,30,Benefit:true)
    };
    private static readonly HashSet<uint> Ids=Rules.Select(r=>r.ActionId).ToHashSet();
    public static bool Contains(uint id)=>Ids.Contains(id);
    public static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
    public static Vector3 Forward(float heading)=>new(MathF.Sin(heading),0,MathF.Cos(heading));
    public static bool Center(LbVisualRule rule,ReplaySkillEvent e,out Vector3 at) {
        at=new(e.X,e.Y,e.Z);
        if(rule.Anchor==LbAnchor.Target) {
            if(!e.TargetX.HasValue||!e.TargetZ.HasValue)return false;
            at=new(e.TargetX.Value,e.TargetY??e.Y,e.TargetZ.Value);
        } else if(rule.Anchor==LbAnchor.Ground) {
            if(!e.GroundX.HasValue||!e.GroundZ.HasValue)return false;
            at=new(e.GroundX.Value,e.GroundY??e.Y,e.GroundZ.Value);
        }
        return Finite(at)&&float.IsFinite(e.Heading);
    }
    public static IEnumerable<(ReplaySkillEvent Event,LbVisualRule Rule)> Active(IReadOnlyList<ReplaySkillEvent> events,MovementFrame? frame,DateTime now) {
        // Exclude future observations. Use and impact packets for a cast form one visual, not two explosions.
        var recent=events.Where(e=>e.Timestamp<=now&&e.Timestamp>=now.AddSeconds(-30)).ToLookup(e=>e.ActionId);
        foreach(var rule in Rules) {
            foreach(var e in recent[rule.ActionId].Where(e=>e.Timestamp>=now.AddSeconds(-(rule.Shape==LbShape.Single&&!e.Resolved?3:rule.Duration))).GroupBy(e=>e.Actor).Select(g=>g.OrderByDescending(e=>e.Timestamp).First())) {
                if(rule.Duration>1.5f&&rule.Anchor==LbAnchor.Source) {
                    var player=frame?.Players.FirstOrDefault(p=>p.Player==e.Actor);
                    if(player==null||player.StatusTimestamp>now||!player.Statuses.Any(s=>(rule.StatusId!=0?s.Id==rule.StatusId:e.SelfStatusIds.Contains(s.Id))&&s.Remaining>(now-player.StatusTimestamp).TotalSeconds))continue;
                }
                // Mesotes relocation replaces the original location without refreshing its 15-second lifetime.
                if(rule.ActionId is 29266 or 29267) {
                    var sequence=events.Where(x=>x.Actor==e.Actor&&x.ActionId is 29266 or 29267&&x.Timestamp<=now&&x.Timestamp>=now.AddSeconds(-15)).OrderBy(x=>x.Timestamp).ToArray();
                    if(sequence.Length==0||!ReferenceEquals(sequence[^1],e))continue;
                    if(e.ActionId==29267) {
                        var original=events.LastOrDefault(x=>x.Actor==e.Actor&&x.ActionId==29266&&x.Timestamp<=e.Timestamp);
                        if(original==null||now>=original.Timestamp.AddSeconds(15))continue;
                    }
                }
                yield return(e,rule);
            }
        }
    }
    public static LbVisualRule ClientDimensions(LbVisualRule rule,ReplaySkillEvent e) {
        if(e.ClientEffectRange is <=0 or >100)return rule;
        if(rule.Shape is LbShape.Line or LbShape.DoubleLine && e.ClientCastType==4 && e.ClientWidth is >0 and <=50)
            return rule with { Size=e.ClientEffectRange,Width=e.ClientWidth };
        if(rule.Shape==LbShape.Cross&&e.ClientCastType==11&&e.ClientWidth is >0 and <=50)
            return rule with { Size=e.ClientEffectRange,Width=e.ClientWidth };
        if(rule.Shape==LbShape.Cone&&e.ClientCastType==3)return rule with {Size=e.ClientEffectRange};
        // The inner 5m Sky Shatter bonus and textual party auras have their own independent dimensions.
        if(rule.Shape==LbShape.Circle&&e.ClientCastType is 2 or 7&&!(rule.ActionId==29498&&rule.Size==5))
            return rule with {Size=e.ClientEffectRange};
        return rule;
    }
    public static float Reveal(ReplaySkillEvent e,DateTime now) {
        var t=Math.Clamp((float)(now-e.Timestamp).TotalSeconds/.32f,0,1);
        return 1-MathF.Pow(1-t,3);
    }
    public static float Opacity(LbVisualRule rule,ReplaySkillEvent e,DateTime now) {
        var elapsed=(float)(now-e.Timestamp).TotalSeconds;
        if(elapsed<0)return 0;
        if(!e.Resolved)return rule.Shape==LbShape.Single&&elapsed<3?.6f:0;
        if(elapsed>=rule.Duration)return 0;
        return rule.Duration>1.5f ? (elapsed<.5f?1:.4f) : elapsed<.45f?1:Math.Clamp((rule.Duration-elapsed)/(rule.Duration-.45f),0,1);
    }
    public static void Draw(ReplaySceneGeometry geometry,LbVisualRule rule,Vector3 at,float heading,Vector3 source,Func<float,float,float> height,float fade=1,float reveal=1) {
        reveal=Math.Clamp(reveal,0,1);if(reveal<=0)return;
        var alpha=(rule.Reference ? .32f : .42f)*Math.Clamp(fade,0,1);
        var color=rule.Benefit?ReplaySceneGeometry.Rgba(.2f,.65f,1,alpha):ReplaySceneGeometry.Rgba(1,.2f,.25f,alpha);
        var edge=rule.Benefit?ReplaySceneGeometry.Rgba(.25f,.85f,1,Math.Max(.45f,.95f*fade)):ReplaySceneGeometry.Rgba(1,.38f,.18f,Math.Max(.45f,.95f*fade));
        Vector3 Ground(Vector3 p)=>new(p.X,height(p.X,p.Z)+.22f,p.Z);
        void Tri(Vector3 a,Vector3 b,Vector3 c)=>geometry.Triangle(Ground(a),Ground(b),Ground(c),color);
        void Border(Vector3 a,Vector3 b) {
            var side=new Vector3(b.Z-a.Z,0,a.X-b.X);if(side.LengthSquared()<.00001f)return;
            side=Vector3.Normalize(side)*.13f;
            geometry.Triangle(Ground(a-side),Ground(a+side),Ground(b+side),edge);
            geometry.Triangle(Ground(a-side),Ground(b+side),Ground(b-side),edge);
        }
        var forward=Forward(heading);var side=new Vector3(forward.Z,0,-forward.X);
        void Rect(Vector3 center,Vector3 f,float front,float back,float width) {
            var right=new Vector3(f.Z,0,-f.X)*width/2;
            // Subdivide long strips to follow approximate ground height instead of clipping through slopes.
            int pieces=Math.Max(1,(int)MathF.Ceiling((front+back)/2));
            for(int i=0;i<pieces;i++) {
                var a=center+f*(-back+(front+back)*i/pieces);var b=center+f*(-back+(front+back)*(i+1)/pieces);
                Tri(a-right,a+right,b+right);Tri(a-right,b+right,b-right);
                Border(a-right,b-right);Border(a+right,b+right);
                if(i==0)Border(a-right,a+right);if(i==pieces-1)Border(b-right,b+right);
            }
        }
        if(rule.Shape is LbShape.Line or LbShape.DoubleLine) { Rect(at,forward,rule.Size*reveal*(rule.Shape==LbShape.DoubleLine?.5f:1),rule.Shape==LbShape.DoubleLine?rule.Size*reveal*.5f:0,rule.Width);return; }
        if(rule.Shape==LbShape.Cross) { Rect(at,forward,rule.Size*reveal,rule.Size*reveal,rule.Width);Rect(at,side,rule.Size*reveal,rule.Size*reveal,rule.Width);return; }
        if(rule.Shape==LbShape.Single) { Rect((at+source)/2,Vector3.Normalize(new Vector3(at.X-source.X,0,at.Z-source.Z)+new Vector3(.000001f,0,0)),Vector2.Distance(new(at.X,at.Z),new(source.X,source.Z))/2,Vector2.Distance(new(at.X,at.Z),new(source.X,source.Z))/2,.45f); }
        float radius=(rule.Shape is LbShape.Single or LbShape.Self?1:rule.Size)*reveal;
        float arc=rule.Shape==LbShape.Cone?rule.Width*MathF.PI/180:MathF.Tau;
        float angle=rule.Shape==LbShape.Cone?heading-arc/2:0;
        for(int ring=0;ring<Math.Max(1,(int)MathF.Ceiling(radius/2));ring++) {
            int rings=Math.Max(1,(int)MathF.Ceiling(radius/2));float inner=radius*ring/rings,outer=radius*(ring+1)/rings;
            for(int i=0;i<64;i++) {
                var a=Forward(angle+arc*i/64);var b=Forward(angle+arc*(i+1)/64);
                Tri(at+a*inner,at+a*outer,at+b*outer);Tri(at+a*inner,at+b*outer,at+b*inner);
                if(ring==rings-1)Border(at+a*outer,at+b*outer);
                if(rule.Shape==LbShape.Cone&&i==0)Border(at+a*inner,at+a*outer);
                if(rule.Shape==LbShape.Cone&&i==63)Border(at+b*inner,at+b*outer);
            }
        }
    }
}
