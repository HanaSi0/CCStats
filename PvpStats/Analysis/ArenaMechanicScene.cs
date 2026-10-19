using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace PvpStats.Analysis;

public enum ArenaMechanicKind {
    Unknown, SprintZone, Bomb, BombCore, JumpGlyph, Turbulence, Feather, Twister, GaleSphere,
    TrickFloor, TrickDoor, Yojimbo, Onmyoji, Gold, ShiftingSands, Antlion, Sabotender, Acutender,
    Oasis, HeatWave, Geyser, Sandpearl, Aetherometer, AetherBridge, HealingGlyph
}
// Objects are observations, not proof that an invisible trigger is active. EventState stays raw.
public sealed class ReplayArenaObject {
    public DateTime Timestamp { get; set; }
    public DateTime ObservedUntil { get; set; }
    public ulong ObjectId { get; set; }
    public uint NameId { get; set; }
    public uint EntityId { get; set; }
    public uint DataId { get; set; }
    public byte ObjectKind { get; set; }
    public byte EventState { get; set; }
    public ulong RenderFlags { get; set; }
    public ArenaMechanicKind Kind { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Heading { get; set; }
    public float? TargetX { get; set; }
    public float? TargetY { get; set; }
    public float? TargetZ { get; set; }
    public float? Progress { get; set; }
    public Vector3 Position()=>new(X,Y,Z);
}
public sealed record ArenaMechanicInstance(ArenaMechanicKind Kind,Vector3 Position,float Heading=0,
    float Radius=2,Vector3? Destination=null,float? Progress=null,bool Reference=false,ulong ObjectId=0,float EyeOpen=0,float FloorAngle=0);

public static class ArenaMechanicScene {
    public static readonly ArenaMechanicKind[] GalleryKinds=Enum.GetValues<ArenaMechanicKind>().Where(k=>k!=ArenaMechanicKind.Unknown).ToArray();
    public static string Name(ArenaMechanicKind k)=>k switch {
        ArenaMechanicKind.SprintZone=>"冲刺带",ArenaMechanicKind.Bomb=>"爆弹怪",ArenaMechanicKind.BombCore=>"爆弹怪之魂",
        ArenaMechanicKind.JumpGlyph=>"跳跃台",ArenaMechanicKind.Turbulence=>"湍流",ArenaMechanicKind.Feather=>"黑陆行鸟的羽毛",
        ArenaMechanicKind.Twister=>"龙卷",ArenaMechanicKind.GaleSphere=>"风球",ArenaMechanicKind.TrickFloor=>"机关榻榻米",
        ArenaMechanicKind.TrickDoor=>"机关门",ArenaMechanicKind.Yojimbo=>"机关保镖",ArenaMechanicKind.Onmyoji=>"机关阴阳师",
        ArenaMechanicKind.Gold=>"金币",ArenaMechanicKind.ShiftingSands=>"流沙",ArenaMechanicKind.Antlion=>"红沙蚁狮",
        ArenaMechanicKind.Sabotender=>"仙人刺",ArenaMechanicKind.Acutender=>"仙人盾",ArenaMechanicKind.Oasis=>"绿洲",
        ArenaMechanicKind.HeatWave=>"热浪",ArenaMechanicKind.Geyser=>"间歇泉",ArenaMechanicKind.Sandpearl=>"沙漠之光",
        ArenaMechanicKind.Aetherometer=>"魔力检测点",ArenaMechanicKind.AetherBridge=>"魔力桥",ArenaMechanicKind.HealingGlyph=>"治疗区",_=>"未知机关"
    };
    public static bool InMap(ArenaMechanicKind k,string? map)=>k switch {
        ArenaMechanicKind.SprintZone=>map is "palaistra" or "volcanic-heart" or "cloud-nine" or "castletown" or "red-sands" or "bayside" or "harmonias",
        ArenaMechanicKind.Bomb or ArenaMechanicKind.BombCore=>map=="volcanic-heart",
        ArenaMechanicKind.JumpGlyph=>map is "cloud-nine" or "bayside" or "harmonias",
        ArenaMechanicKind.Turbulence or ArenaMechanicKind.Feather or ArenaMechanicKind.Twister or ArenaMechanicKind.GaleSphere=>map=="cloud-nine",
        ArenaMechanicKind.TrickFloor or ArenaMechanicKind.TrickDoor or ArenaMechanicKind.Yojimbo or ArenaMechanicKind.Onmyoji or ArenaMechanicKind.Gold=>map=="castletown",
        ArenaMechanicKind.ShiftingSands or ArenaMechanicKind.Antlion or ArenaMechanicKind.Sabotender or ArenaMechanicKind.Acutender or ArenaMechanicKind.Oasis or ArenaMechanicKind.HeatWave or ArenaMechanicKind.Geyser or ArenaMechanicKind.Sandpearl=>map=="red-sands",
        ArenaMechanicKind.Aetherometer or ArenaMechanicKind.AetherBridge or ArenaMechanicKind.HealingGlyph=>map=="harmonias",_=>false
    };
    // BNpcName IDs, not BNpcBase IDs. Scoped by map to exclude unrelated PvE namesakes.
    public static ArenaMechanicKind FromNpc(uint name,string? map) {
        var kind=name switch {11351=>ArenaMechanicKind.BombCore,11352=>ArenaMechanicKind.Turbulence,11353=>ArenaMechanicKind.Feather,
            12368=>ArenaMechanicKind.GaleSphere,12278=>ArenaMechanicKind.Yojimbo,12279=>ArenaMechanicKind.Onmyoji,
            12522=>ArenaMechanicKind.Antlion,12521=>ArenaMechanicKind.Sabotender,12523=>ArenaMechanicKind.Acutender,_=>ArenaMechanicKind.Unknown};
        return InMap(kind,map)?kind:ArenaMechanicKind.Unknown;
    }
    // Exact English client sheet labels only; never classify generic 'door' or 'aether' substrings.
    public static ArenaMechanicKind FromObjectName(string name,string? map) {
        var kind=name.Trim().ToLowerInvariant() switch {
            "bomb" when map=="volcanic-heart"=>ArenaMechanicKind.Bomb,
            "bomb core"=>ArenaMechanicKind.BombCore,"black chocobo feather"=>ArenaMechanicKind.Feather,
            "gale sphere"=>ArenaMechanicKind.GaleSphere,"turbulence"=>ArenaMechanicKind.Turbulence,
            "clockwork yojimbo"=>ArenaMechanicKind.Yojimbo,"clockwork onmyoji"=>ArenaMechanicKind.Onmyoji,
            "red sands antlion"=>ArenaMechanicKind.Antlion,"sabotender"=>ArenaMechanicKind.Sabotender,"acutender"=>ArenaMechanicKind.Acutender,
            "sprint zone"=>ArenaMechanicKind.SprintZone,"jump glyph"=>ArenaMechanicKind.JumpGlyph,
            "trick floor"=>ArenaMechanicKind.TrickFloor,"trick door"=>ArenaMechanicKind.TrickDoor,
            "pile of gold" or "gold pile"=>ArenaMechanicKind.Gold,"shifting sands"=>ArenaMechanicKind.ShiftingSands,
            "oasis"=>ArenaMechanicKind.Oasis,"geyser"=>ArenaMechanicKind.Geyser,"twister"=>ArenaMechanicKind.Twister,
            "aetherometer"=>ArenaMechanicKind.Aetherometer,"aetherial bridge"=>ArenaMechanicKind.AetherBridge,
            "healing glyph"=>ArenaMechanicKind.HealingGlyph,_=>ArenaMechanicKind.Unknown};
        return InMap(kind,map)?kind:ArenaMechanicKind.Unknown;
    }
    public static IEnumerable<ArenaMechanicInstance> Objects(DetailedTelemetry data,uint territory,DateTime now) {
        var map=ReplayMapMesh.MapKey(territory);
        return data.ArenaObjects.Where(o=>o.ObjectId!=0&&o.ObservedUntil>=o.Timestamp&&o.Timestamp<=now&&now<=o.ObservedUntil.AddSeconds(.25)&&InMap(o.Kind,map)&&LimitBreakVisuals.Finite(o.Position())&&float.IsFinite(o.Heading))
            .GroupBy(o=>o.EntityId is not (0 or 0xE0000000)?(ulong)o.EntityId:o.ObjectId).Select(g=>g.OrderByDescending(o=>o.Timestamp).First())
            .OrderBy(o=>o.ObjectId).Take(64).Select(o=>new ArenaMechanicInstance(o.Kind,o.Position(),o.Heading,
                Destination:o.TargetX.HasValue&&o.TargetY.HasValue&&o.TargetZ.HasValue&&LimitBreakVisuals.Finite(new(o.TargetX.Value,o.TargetY.Value,o.TargetZ.Value))?new(o.TargetX.Value,o.TargetY.Value,o.TargetZ.Value):null,
                Progress:o.Progress,ObjectId:o.ObjectId,EyeOpen:o.Kind==ArenaMechanicKind.Onmyoji&&!Moving(data.ArenaObjects,o,now)?EyeOpenness(data.MapMechanics,now,o.EntityId):0,FloorAngle:o.Kind==ArenaMechanicKind.TrickFloor?FloorAngle(data.MapMechanics,now,o.EntityId,o.Position()):0));
    }
    public static ArenaMechanicKind FromAction(uint action)=>action switch {
        28725 or 28726=>ArenaMechanicKind.Bomb,28727 or 28728=>ArenaMechanicKind.Turbulence,28731=>ArenaMechanicKind.Twister,
        32364=>ArenaMechanicKind.TrickFloor,32365=>ArenaMechanicKind.Yojimbo,32366=>ArenaMechanicKind.Onmyoji,
        35751 or 35754=>ArenaMechanicKind.Antlion,35752=>ArenaMechanicKind.Sabotender,35753=>ArenaMechanicKind.Acutender,
        35755=>ArenaMechanicKind.Sandpearl,35756=>ArenaMechanicKind.Geyser,_=>ArenaMechanicKind.Unknown
    };
    public static IEnumerable<ArenaMechanicInstance> Actions(DetailedTelemetry data,uint territory,DateTime now) {
        var map=ReplayMapMesh.MapKey(territory);
        var emitted=new HashSet<(ArenaMechanicKind Kind,uint Entity)>();
        foreach(var e in data.MapMechanics.Where(e=>e.Kind is ReplayMapEventKind.CastObserved or ReplayMapEventKind.ActionResolved).OrderByDescending(e=>e.Timestamp)) {
            var kind=FromAction(e.ActionId);
            if(!InMap(kind,map)||(!ReplayMechanicObservation.Visible(e,now)&&!(kind==ArenaMechanicKind.Onmyoji&&EyeForEvent(e,now)>0)))continue;
            if(e.Kind==ReplayMapEventKind.ActionResolved&&(now-e.Timestamp).TotalSeconds>1.1)continue;
            bool ground=e.ActionId==35755;
            float? x=ground?e.TargetX:e.X,z=ground?e.TargetZ:e.Z,y=ground?e.TargetY:e.Y;
            if(!x.HasValue||!z.HasValue||!y.HasValue)continue;
            var p=new Vector3(x.Value,y.Value,z.Value);if(!LimitBreakVisuals.Finite(p))continue;
            // Prefer observed object pose to duplicate action decorations. Existing AoE footprints stay separate.
            if(data.ArenaObjects.Any(o=>o.Timestamp<=now&&o.ObservedUntil.AddSeconds(.25)>=now&&o.Kind==kind&&
                ((e.SourceEntityId is not (0 or 0xE0000000)&&o.EntityId==e.SourceEntityId)||Vector3.DistanceSquared(o.Position(),p)<9)))continue;
            // Cast-start and resolve snapshots can have different positions as the NPC moves.
            // Prefer its latest active event, rather than drawing another actor at the old position.
            if(e.SourceEntityId is not (0 or 0xE0000000)&&!emitted.Add((kind,e.SourceEntityId)))continue;
            yield return new(kind,p,float.IsFinite(e.Heading??0)?e.Heading??0:0,ObjectId:e.SourceEntityId,EyeOpen:kind==ArenaMechanicKind.Onmyoji?EyeForEvent(e,now):0,FloorAngle:kind==ArenaMechanicKind.TrickFloor?FloorAngle(data.MapMechanics,now,e.SourceEntityId,p):0);
        }
    }
    public static float FloorAngle(IEnumerable<ReplayMapEvent> events,DateTime now,uint entity,Vector3 position) {
        var matching=events.Where(e=>e.ActionId==32364&&e.Timestamp<=now&&
            ((entity is not (0 or 0xE0000000)&&e.SourceEntityId==entity)||
             (e.X.HasValue&&e.Z.HasValue&&Vector2.DistanceSquared(new(e.X.Value,e.Z.Value),new(position.X,position.Z))<=4)));
        var impact=matching.Where(e=>e.Kind==ReplayMapEventKind.ActionResolved).OrderByDescending(e=>e.Timestamp).FirstOrDefault();
        if(impact!=null) {float age=(float)(now-impact.Timestamp).TotalSeconds;if(age<=.95f)return 2.65f*MathF.Sin(MathF.PI*age/.95f);}
        var cast=matching.Where(e=>e.Kind==ReplayMapEventKind.CastObserved&&ReplayMechanicObservation.Visible(e,now)).OrderByDescending(e=>e.Timestamp).FirstOrDefault();
        return cast==null||(impact!=null&&impact.Timestamp>=cast.Timestamp)?0:.035f*MathF.Abs(MathF.Sin((float)(now-cast.Timestamp).TotalSeconds*18));
    }
    public static bool Moving(IEnumerable<ReplayArenaObject> tracks,ReplayArenaObject current,DateTime now) {
        if(current.Timestamp>now||(now-current.Timestamp).TotalSeconds>.25)return false;
        var previous=tracks.Where(o=>o.ObjectId==current.ObjectId&&o.Timestamp<current.Timestamp&&o.Kind==current.Kind)
            .OrderByDescending(o=>o.Timestamp).FirstOrDefault();
        return previous!=null&&(current.Timestamp-previous.ObservedUntil).TotalSeconds<.5&&Vector3.DistanceSquared(previous.Position(),current.Position())>.04f;
    }
    // Openness follows this NPC's observed cast/resolve, never its speed or merely reaching a waypoint.
    public static float EyeOpenness(System.Collections.Generic.IEnumerable<ReplayMapEvent> events,DateTime now,uint source) =>
        source is 0 or 0xE0000000?0:events.Where(e=>e.SourceEntityId==source).Select(e=>EyeForEvent(e,now)).DefaultIfEmpty(0).Max();
    public static float EyeForEvent(ReplayMapEvent e,DateTime now) {
        if(e.ActionId!=32366||now<e.Timestamp)return 0;
        float Smooth(float v) {v=Math.Clamp(v,0,1);return v*v*(3-2*v);}
        if(e.Kind==ReplayMapEventKind.ActionResolved)return Smooth((1.1f-(float)(now-e.Timestamp).TotalSeconds)/.5f);
        if(e.Kind!=ReplayMapEventKind.CastObserved)return 0;
        var end=e.ResolvedAt??e.ObservedUntil?.AddSeconds(.15)??e.Timestamp;
        if(e.ExpectedEndTime.HasValue&&e.ExpectedEndTime.Value<end)end=e.ExpectedEndTime.Value;
        float opening=Smooth((float)(now-e.Timestamp).TotalSeconds/.2f);
        return opening*Smooth(1-(float)Math.Max(0,(now-end).TotalSeconds)/.2f);
    }
    public static float ReferenceEye(float time) {
        var phase=(time%4+4)%4;
        if(phase<1||phase>=3)return 0;
        return Math.Clamp(Math.Min((phase-1)/.3f,(3-phase)/.4f),0,1);
    }
    public static bool Mini(PlayerPosition p,DateTime now)=>p.HP>0&&p.StatusTimestamp<=now&&
        p.Statuses.Any(s=>s.Id==3518&&float.IsFinite(s.Remaining)&&s.Remaining>(now-p.StatusTimestamp).TotalSeconds);
    public static IEnumerable<(ArenaMechanicKind Kind,PlayerPosition Player)> Statuses(MovementFrame? frame,uint territory,DateTime now) {
        if(frame==null||frame.Timestamp>now||(now-frame.Timestamp).TotalSeconds>1)return [];
        var map=ReplayMapMesh.MapKey(territory);var list=new List<(ArenaMechanicKind,PlayerPosition)>();
        foreach(var p in frame.Players.Where(p=>p.HP>0&&p.StatusTimestamp<=now))foreach(var s in p.Statuses) {
            if(!float.IsFinite(s.Remaining)||s.Remaining<0||(s.Remaining>0&&s.Remaining<=(now-p.StatusTimestamp).TotalSeconds))continue;
            var kind=s.Id switch {2994=>ArenaMechanicKind.BombCore,2995=>ArenaMechanicKind.Feather,
                2996 or 2997 or 3125 or 4385 or 4709=>ArenaMechanicKind.SprintZone,3752 or 3753=>ArenaMechanicKind.Oasis,
                3754=>ArenaMechanicKind.HeatWave,5355=>ArenaMechanicKind.HealingGlyph,_=>ArenaMechanicKind.Unknown};
            if(InMap(kind,map))list.Add((kind,p));
        }
        return list.DistinctBy(x=>(x.Item1,x.Item2.Player));
    }
}
