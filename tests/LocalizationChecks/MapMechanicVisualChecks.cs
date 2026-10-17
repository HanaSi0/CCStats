using System.Numerics;
using PvpStats.Analysis;
internal static class MapMechanicVisualChecks {
    internal static void Run(Action<bool,string> check) {
        var now=new DateTime(2026,10,18,0,0,0,DateTimeKind.Utc);
        var heal=new ReplayMapEvent { ActionId=35755,Timestamp=now,X=80,Y=2,Z=70,Kind=ReplayMapEventKind.ActionResolved };
        check(!MapMechanicVisuals.Active(new[]{heal},1138,now).Any(),"ground healing mechanic never substitutes NPC position for a missing landing point");
        heal.TargetX=10;heal.TargetY=1;heal.TargetZ=20;
        var visual=MapMechanicVisuals.Active(new[]{heal},1138,now).Single();
        check(visual.Center==new Vector3(10,1,20)&&visual.Size==2,"sandpearl uses actual recorded target and client-table two-unit radius");
        check(!MapMechanicVisuals.Active(new[]{heal},1033,now).Any(),"mechanic IDs cannot leak into another arena");
        check(!MapMechanicVisuals.Active(new[]{heal},1138,now.AddTicks(-1)).Any()&&!MapMechanicVisuals.Active(new[]{heal},1138,now.AddSeconds(2)).Any(),"mechanic footprint is neither a future event nor a fabricated persistent field");
        var timer=new ReplayMapEvent { ActionId=35755,Kind=ReplayMapEventKind.TimerTriggered,Timestamp=now,X=1,Z=1 };
        check(!MapMechanicVisuals.Active(new[]{timer},1138,now).Any(),"timer trigger cannot manufacture an observed arena mechanic");
        var shield=new ReplayMapEvent { ActionId=35753,Timestamp=now,X=0,Y=0,Z=0 };
        var barrier=MapMechanicVisuals.Active(new[]{shield},1139,now).Single();
        check(barrier.Rule.Effect==MapMechanicEffect.Barrier&&barrier.Size==22,"therapeutic needles is the official shared barrier, not healing");
        check(MapMechanicVisuals.Color(barrier.Rule.Effect)!=MapMechanicVisuals.Color(visual.Rule.Effect)&&MapMechanicVisuals.Color(MapMechanicEffect.Damage)!=MapMechanicVisuals.Color(MapMechanicEffect.Knockback),"healing, barriers, damage and displacement use distinct colors");
        var cast=new ReplayMapEvent { ActionId=35753,SourceEntityId=22,Timestamp=now,Kind=ReplayMapEventKind.CastObserved,ObservedUntil=now.AddSeconds(2),ExpectedEndTime=now.AddSeconds(5),X=0,Y=0,Z=0 };
        shield.SourceEntityId=22;shield.Timestamp=now.AddSeconds(1);cast.ResolvedAt=shield.Timestamp;
        var combined=MapMechanicVisuals.Active(new[]{cast,shield},1138,shield.Timestamp).ToArray();
        check(combined.Length==1&&combined[0].Event==shield,"cast and action resolution produce one footprint at the transition");
        var geometry=new ReplaySceneGeometry();MapMechanicVisuals.Draw(geometry,barrier,now,(_,_)=>3);
        check(geometry.Vertices.Length>0&&geometry.Vertices.ToArray().All(v=>v.Position.Y>3&&new Vector2(v.Position.X,v.Position.Z).Length()<22.2f),"mechanic footprint follows surface elevation and native range");
        var unknown=new ReplayMapEvent { ActionId=99999,Timestamp=now,X=0,Z=0,ClientEffectRange=50 };
        check(!MapMechanicVisuals.Active(new[]{unknown},1138,now).Any(),"unknown NPC actions are retained as observations, never guessed as mechanics");
        heal.TargetX=float.NaN;check(!MapMechanicVisuals.Active(new[]{heal},1138,now).Any(),"invalid recorded coordinates cannot enter rendering");
        shield.ClientEffectRange=24;check(MapMechanicVisuals.Active(new[]{shield},1138,shield.Timestamp).Single().Size==24,"recorded client range overrides the reference snapshot");
        check(MapMechanicVisuals.Find(28726) is { Shape:MapMechanicShape.Cross,Width:10 }&&MapMechanicVisuals.Find(32364) is { Shape:MapMechanicShape.Square }&&MapMechanicVisuals.Find(32366) is { Effect:MapMechanicEffect.Gaze },"bomb crosses, trick floors and facing-dependent gaze keep separate mechanic rules");
        var bomb=new ReplayMapEvent { ActionId=28726,Timestamp=now,X=0,Y=0,Z=0 };
        check(!MapMechanicVisuals.Active(new[]{bomb},1033,now).Any(),"directional footprint is omitted when legacy records lack orientation");
        bomb.Heading=MathF.PI/4;
        var cross=MapMechanicVisuals.Active(new[]{bomb},1033,now).Single();geometry.Clear();MapMechanicVisuals.Draw(geometry,cross,now,(_,_)=>0);
        check(geometry.Vertices.Length>0&&geometry.Vertices.ToArray().All(v=>LimitBreakVisuals.Finite(v.Position))&&geometry.Vertices.ToArray().Max(v=>v.Position.X)>49,"oriented bomb cross emits finite geometry at the client-table extent");
        check(ReplayMechanicObservation.Caption(shield,shield.Timestamp)=="治疗针刺","historical events resolve mechanic names by action ID independent of Japanese NPC labels");
    }
}
