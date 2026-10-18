using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace PvpStats.Analysis;

public enum MapMechanicEffect { Damage, SharedDamage, Barrier, Healing, Pull, Knockback, Gaze }
public enum MapMechanicShape { Circle, Cross, Square }
public sealed record MapMechanicRule(uint ActionId,string Map,string NativeName,string Name,MapMechanicEffect Effect,
    float Radius,MapMechanicShape Shape=MapMechanicShape.Circle,float Width=0,bool Ground=false);
public sealed record MapMechanicVisual(ReplayMapEvent Event,MapMechanicRule Rule,Vector3 Center,float Size,float Width);

// Action IDs/ranges: 2026-10-13 client Action sheet. Effect semantics: official CC arena guide.
// Chinese names are descriptive translations; names and countdowns are never painted over the arena.
public static class MapMechanicVisuals {
    public static readonly IReadOnlyList<MapMechanicRule> Rules=new MapMechanicRule[] {
        new(28725,"volcanic-heart","Elemental Impact","元素冲击",MapMechanicEffect.Damage,5),
        new(28726,"volcanic-heart","Self-destruct","自爆",MapMechanicEffect.Damage,70,MapMechanicShape.Cross,10),
        new(28729,"cloud-nine","Biting Wind","凛风",MapMechanicEffect.Damage,5),
        new(28731,"cloud-nine","Typhoon","台风",MapMechanicEffect.Knockback,3),
        new(32364,"castletown","Tatami Twist","榻榻米翻转",MapMechanicEffect.Knockback,10,MapMechanicShape.Square,10),
        new(32365,"castletown","Tsujigiri","辻斩",MapMechanicEffect.Damage,15),
        new(32366,"castletown","Onmyojutsu","阴阳术",MapMechanicEffect.Gaze,30),
        new(35751,"red-sands","Devour","吞噬",MapMechanicEffect.Damage,6),
        new(35752,"red-sands","100,000 Needles","十万针刺",MapMechanicEffect.SharedDamage,22),
        new(35753,"red-sands","Therapeutic Needles","十万针灸",MapMechanicEffect.Barrier,22),
        new(35754,"red-sands","Landslip","流沙吸引",MapMechanicEffect.Pull,22),
        new(35755,"red-sands","Sandpearl Shroud","沙漠之光",MapMechanicEffect.Healing,2,Ground:true),
        new(35756,"red-sands","Geyser","间歇泉",MapMechanicEffect.Knockback,2),
    };
    private static readonly Dictionary<uint,MapMechanicRule> ById=Rules.ToDictionary(r=>r.ActionId);
    public static MapMechanicRule? Find(uint action)=>ById.GetValueOrDefault(action);
    public static Vector3 Color(MapMechanicEffect effect)=>effect switch {
        MapMechanicEffect.SharedDamage=>new(1,.65f,.12f),MapMechanicEffect.Barrier=>new(.15f,.8f,1),
        MapMechanicEffect.Healing=>new(.2f,1,.48f),MapMechanicEffect.Pull=>new(1,.78f,.2f),
        MapMechanicEffect.Knockback=>new(.7f,.42f,1),MapMechanicEffect.Gaze=>new(1,.3f,.8f),_=>new(1,.25f,.12f)
    };
    public static IEnumerable<MapMechanicVisual> Active(IEnumerable<ReplayMapEvent> events,uint territory,DateTime now) {
        var map=ReplayMapMesh.MapKey(territory);
        var candidates=new List<MapMechanicVisual>();
        foreach(var e in events) {
            if(e.Kind is not (ReplayMapEventKind.CastObserved or ReplayMapEventKind.ActionResolved)||e.MapWide||!ReplayMechanicObservation.Visible(e,now))continue;
            if(e.Kind==ReplayMapEventKind.ActionResolved&&(now-e.Timestamp).TotalSeconds>1.1)continue;
            var rule=Find(e.ActionId);if(rule==null||rule.Map!=map)continue;
            // A ground-target action needs a recorded landing position. The NPC position is not its landing point.
            float? x=rule.Ground?e.TargetX:e.X,z=rule.Ground?e.TargetZ:e.Z,y=rule.Ground?e.TargetY:e.Y;
            if(!x.HasValue||!z.HasValue)continue;
            var center=new Vector3(x.Value,y??0,z.Value);
            if(!LimitBreakVisuals.Finite(center)||center.LengthSquared()>100_000_000)continue;
            var size=e.ClientEffectRange>0?e.ClientEffectRange:rule.Radius;
            var width=e.ClientXAxisModifier>0?e.ClientXAxisModifier:rule.Width;
            if(size<=0||size>100||width<0||width>100)continue;
            if(rule.Shape!=MapMechanicShape.Circle&&(!e.Heading.HasValue||!float.IsFinite(e.Heading.Value)||width<=0))continue;
            candidates.Add(new(e,rule,center,size,width));
        }
        // One footprint for a cast/impact at the same source and landing point; prefer actual resolution.
        return candidates.OrderByDescending(v=>v.Event.Kind==ReplayMapEventKind.ActionResolved).ThenByDescending(v=>v.Event.Timestamp)
            .GroupBy(v=>(v.Event.SourceEntityId,v.Event.ActionId,X:(int)MathF.Round(v.Center.X*2),Z:(int)MathF.Round(v.Center.Z*2)))
            .Select(g=>g.First()).Take(32);
    }
    public static void Draw(ReplaySceneGeometry geometry,MapMechanicVisual visual,DateTime now,Func<float,float,float> height) {
        var e=visual.Event;var rule=visual.Rule;var center=visual.Center;var rgb=Color(rule.Effect);
        bool casting=e.Kind==ReplayMapEventKind.CastObserved;
        float age=Math.Max(0,(float)(now-e.Timestamp).TotalSeconds);
        float opacity=casting?1:Math.Clamp((1.1f-age)/.5f,0,1);
        uint fill=ReplaySceneGeometry.Rgba(rgb.X,rgb.Y,rgb.Z,(casting?.12f:.25f)*opacity);
        uint edge=ReplaySceneGeometry.Rgba(rgb.X,rgb.Y,rgb.Z,.85f*opacity);
        Vector3 Ground(Vector3 p)=>new(p.X,height(p.X,p.Z)+.28f,p.Z);
        void Tri(Vector3 a,Vector3 b,Vector3 c,uint color)=>geometry.Triangle(Ground(a),Ground(b),Ground(c),color);
        void Line(Vector3 a,Vector3 b,float thickness=.18f) {
            var side=new Vector3(b.Z-a.Z,0,a.X-b.X);if(side.LengthSquared()<.00001f)return;
            side=Vector3.Normalize(side)*thickness/2;
            Tri(a-side,a+side,b+side,edge);Tri(a-side,b+side,b-side,edge);
        }
        void Ring(float radius) {
            for(int i=0;i<64;i++)Line(center+LimitBreakVisuals.Forward(i*MathF.Tau/64)*radius,center+LimitBreakVisuals.Forward((i+1)*MathF.Tau/64)*radius);
        }
        var forward=LimitBreakVisuals.Forward(float.IsFinite(e.Heading??0)?e.Heading??0:0);var right=new Vector3(forward.Z,0,-forward.X);
        void Rect(Vector3 f,float halfLength,float width) {
            var side=new Vector3(f.Z,0,-f.X)*width/2;int slices=Math.Max(1,(int)MathF.Ceiling(halfLength));
            for(int i=0;i<slices;i++) {
                var a=center+f*(-halfLength+2*halfLength*i/slices);var b=center+f*(-halfLength+2*halfLength*(i+1)/slices);
                Tri(a-side,a+side,b+side,fill);Tri(a-side,b+side,b-side,fill);Line(a-side,b-side);Line(a+side,b+side);
            }
            Line(center-f*halfLength-side,center-f*halfLength+side);Line(center+f*halfLength-side,center+f*halfLength+side);
        }
        if(rule.Shape==MapMechanicShape.Cross) { Rect(forward,visual.Size,visual.Width);Rect(right,visual.Size,visual.Width);return; }
        if(rule.Shape==MapMechanicShape.Square) { Rect(forward,visual.Size/2,visual.Width);return; }
        // Gaze is directional/facing-dependent: outline the reach with an eye, never an all-hit filled disk.
        if(rule.Effect!=MapMechanicEffect.Gaze) {
            int rings=Math.Max(1,(int)MathF.Ceiling(visual.Size/4));
            for(int j=0;j<rings;j++)for(int i=0;i<48;i++) {
                var a=LimitBreakVisuals.Forward(i*MathF.Tau/48);var b=LimitBreakVisuals.Forward((i+1)*MathF.Tau/48);
                float inner=visual.Size*j/rings,outer=visual.Size*(j+1)/rings;
                Tri(center+a*inner,center+a*outer,center+b*outer,fill);Tri(center+a*inner,center+b*outer,center+b*inner,fill);
            }
        }
        Ring(visual.Size);
        if(rule.Effect is MapMechanicEffect.Pull or MapMechanicEffect.Knockback) {
            float phase=(age%1);
            float r=visual.Size*(rule.Effect==MapMechanicEffect.Pull?1-phase:phase);Ring(Math.Max(.2f,r));
            for(int i=0;i<8;i++) {
                var axis=LimitBreakVisuals.Forward(i*MathF.Tau/8);var side=new Vector3(axis.Z,0,-axis.X);
                var tip=center+axis*(visual.Size*.65f);var back=tip+axis*(rule.Effect==MapMechanicEffect.Pull?1.5f:-1.5f);
                Line(tip,back+side*.9f,.25f);Line(tip,back-side*.9f,.25f);
            }
        } else if(rule.Effect is MapMechanicEffect.Barrier or MapMechanicEffect.Healing) {
            Ring(visual.Size*(.25f+.75f*(age%1)));
        } else if(rule.Effect==MapMechanicEffect.SharedDamage) {
            Ring(visual.Size*.8f);
        }
    }
}
