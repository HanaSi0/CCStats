using System.Numerics;
using JsonSerializer = System.Text.Json.JsonSerializer;
using LiteDB;
using PvpStats.Analysis;
internal static class ArenaMechanicSceneChecks {
    internal static void Run(Action<bool,string> check) {
        var now=new DateTime(2026,10,18,0,0,0,DateTimeKind.Utc);
        var geometry=new ReplaySceneGeometry();var signatures=new HashSet<string>();
        foreach(var kind in ArenaMechanicScene.GalleryKinds) {
            geometry.Clear();var item=new ArenaMechanicInstance(kind,Vector3.Zero,Destination:kind is ArenaMechanicKind.AetherBridge or ArenaMechanicKind.TrickDoor?new Vector3(0,0,10):null,Progress:.6f,Reference:true);
            ArenaMechanicRenderer.Draw(geometry,item,.35f);var vertices=geometry.Vertices.ToArray();
            check(vertices.Length>=3&&vertices.Length<5000&&vertices.All(v=>LimitBreakVisuals.Finite(v.Position)),"mechanic renderer is finite and bounded: "+kind);
            signatures.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan()))));
            check(!ArenaMechanicGuide.Detail(kind).Contains("未知机制"),"every mechanic has a Chinese behavior description: "+kind);
        }
        check(signatures.Count==ArenaMechanicScene.GalleryKinds.Length,"every mechanic has a distinct placeholder, not just one shared generic marker");
        geometry.Clear();ArenaMechanicRenderer.Draw(geometry,new(ArenaMechanicKind.Onmyoji,Vector3.Zero),0);
        check(geometry.Vertices.ToArray().Min(v=>v.Position.Y)>4,"onmyoji is only a floating eye, without a pawn or staff");
        check(ArenaMechanicScene.FromNpc(12279,"castletown")==ArenaMechanicKind.Onmyoji&&ArenaMechanicScene.FromNpc(12279,"cloud-nine")==ArenaMechanicKind.Unknown,"pneumatic parade identity is map-scoped");
        check(ArenaMechanicScene.FromAction(32367)==ArenaMechanicKind.Unknown,"a Well Paid effect does not create a coin pile at Kageyama or the recipient");
        check(ArenaMechanicScene.FromObjectName("door","castletown")==ArenaMechanicKind.Unknown,"generic doors never become a verified trick door");
        check(ArenaMechanicGuide.CloudJumpPads(.5f,.5f)==(1,1)&&ArenaMechanicGuide.CloudJumpPads(.501f,.4f)==(2,0)&&ArenaMechanicGuide.CloudJumpPads(.4f,.6f)==(0,2),"Cloud Nine pad reference policy changes only after halfway");
        check(ArenaMechanicGuide.CloudJumpPads(.7f,.7f)==null&&ArenaMechanicGuide.CloudJumpPads(float.NaN,0)==null,"undefined or invalid pad states are not guessed");
        check(ArenaMechanicGuide.ResetAetherCharge(.7f,true,true)==0&&ArenaMechanicGuide.ResetAetherCharge(.7f,false,false)==0&&ArenaMechanicGuide.ResetAetherCharge(.7f,true,false)==.7f,"aether charge resets on an enemy or no advancing player");
        check(float.IsNaN(ArenaMechanicGuide.ResetAetherCharge(null,true,false)),"missing charge readings do not fabricate a progress bar");
        var data=new DetailedTelemetry();var actor=new ReplayArenaObject { ObjectId=0x100000001,NameId=12279,Kind=ArenaMechanicKind.Onmyoji,Timestamp=now,ObservedUntil=now.AddSeconds(1),X=10,Y=2,Z=20 };
        data.ArenaObjects.Add(actor);
        check(ArenaMechanicScene.Objects(data,1116,now).Count()==1&&!ArenaMechanicScene.Objects(data,1034,now).Any(),"recorded objects appear only in their own arena");
        check(!ArenaMechanicScene.Objects(data,1116,now.AddTicks(-1)).Any()&&!ArenaMechanicScene.Objects(data,1116,now.AddSeconds(1.3)).Any(),"objects neither leak from the future nor persist beyond observed lifetime and sampling grace");
        var json=JsonSerializer.Serialize(data);var restored=JsonSerializer.Deserialize<DetailedTelemetry>(json)!;
        check(restored.ArenaObjects.Single().ObjectId==actor.ObjectId,"new object tracks retain 64-bit game object identities in JSON exports");
        var eyeCast=new ReplayMapEvent {ActionId=32366,SourceEntityId=42,Kind=ReplayMapEventKind.CastObserved,Timestamp=now.AddSeconds(1),ObservedUntil=now.AddSeconds(3),ExpectedEndTime=now.AddSeconds(3)};
        check(ArenaMechanicScene.EyeOpenness(new[]{eyeCast},now,42)==0,"moving/waiting onmyoji keeps eye closed before a recorded cast");
        check(ArenaMechanicScene.EyeOpenness(new[]{eyeCast},now.AddSeconds(1.1),42) is >0 and <1,"gaze eye smoothly opens after cast observation");
        check(ArenaMechanicScene.EyeOpenness(new[]{eyeCast},now.AddSeconds(1.5),42)==1,"gaze eye is open while the actual cast continues");
        check(ArenaMechanicScene.EyeOpenness(new[]{new ReplayMapEvent {ActionId=32366,SourceEntityId=0xE0000000,Timestamp=now.AddSeconds(1.2),Kind=ReplayMapEventKind.ActionResolved}},now.AddSeconds(1.5),0xE0000000)==0,"non-networked entity sentinel cannot identify a particular casting onmyoji");
        check(ArenaMechanicScene.EyeOpenness(new[]{eyeCast},now.AddSeconds(1.5),43)==0,"another NPC's gaze cannot open this NPC's eye");
        check(ArenaMechanicScene.EyeOpenness(new[]{eyeCast},now.AddSeconds(3.3),42)==0,"eye closes after the observed cast ends");
        eyeCast.ObservedUntil=now.AddSeconds(1.4);eyeCast.ExpectedEndTime=now.AddSeconds(5);
        check(ArenaMechanicScene.EyeOpenness(new[]{eyeCast},now.AddSeconds(1.8),42)==0,"an interrupted cast closes the eye without waiting for its formerly expected end");
        var eyeResolve=new ReplayMapEvent {ActionId=32366,SourceEntityId=42,Timestamp=now.AddSeconds(2),Kind=ReplayMapEventKind.ActionResolved};
        check(ArenaMechanicScene.EyeForEvent(eyeResolve,now.AddSeconds(2.2))==1&&ArenaMechanicScene.EyeForEvent(eyeResolve,now.AddSeconds(3.2))==0,"resolved gaze briefly emphasises an open eye then closes");
        var movingEye=new ReplayArenaObject {ObjectId=actor.ObjectId,EntityId=42,Kind=ArenaMechanicKind.Onmyoji,Timestamp=now.AddSeconds(1.5),ObservedUntil=now.AddSeconds(1.5),X=12,Y=2,Z=20};
        actor.EntityId=42;actor.ObservedUntil=now.AddSeconds(1.4);data.ArenaObjects.Add(movingEye);data.MapMechanics.Add(eyeResolve);
        check(ArenaMechanicScene.Moving(data.ArenaObjects,movingEye,now.AddSeconds(1.6)),"recorded movement is detected from past poses of the same NPC");
        data.MapMechanics.Clear();data.MapMechanics.Add(new(){ActionId=32366,SourceEntityId=42,Timestamp=now.AddSeconds(1.5),Kind=ReplayMapEventKind.ActionResolved});
        check(ArenaMechanicScene.Objects(data,1116,now.AddSeconds(1.6)).Single().EyeOpen==0,"a moving onmyoji closes its eye even while a previous impact is fading");
        data.ArenaObjects.Remove(movingEye);actor.ObservedUntil=now.AddSeconds(1);data.MapMechanics.Clear();
        var mapper=new BsonMapper();var doc=mapper.ToDocument(data);var fromDb=mapper.ToObject<DetailedTelemetry>(doc);
        check(fromDb.ArenaObjects.Single().Kind==actor.Kind,"mechanic tracks survive LiteDB storage and reload");
        var legacy=JsonSerializer.Deserialize<DetailedTelemetry>("{\"Version\":3}")!;
        check(!ArenaMechanicScene.Objects(legacy,1116,now).Any(),"legacy matches without object data do not invent pickups, doors or bridges");
        data.MapMechanics.Add(new() { ActionId=32366,Timestamp=now,X=10,Y=2,Z=20,SourceEntityId=4 });
        check(!ArenaMechanicScene.Actions(data,1116,now).Any(),"one observed actor suppresses its duplicated action placeholder");
        data.ArenaObjects.Clear();check(ArenaMechanicScene.Actions(data,1116,now).Single().Kind==ArenaMechanicKind.Onmyoji,"older matches can show an eye from a recorded onmyoji action");
        data.MapMechanics.Clear();data.MapMechanics.Add(new() { Kind=ReplayMapEventKind.TimerTriggered,ActionId=28727,Timestamp=now,X=0,Y=0,Z=0 });
        check(!ArenaMechanicScene.Actions(data,1034,now).Any(),"a timer cannot manufacture a global turbulence event");
        check(ArenaMechanicScene.FromObjectName("sprint zone","cloud-nine")==ArenaMechanicKind.SprintZone&&ArenaMechanicScene.FromObjectName("sprint zone",null)==ArenaMechanicKind.Unknown,"an actually identified sprint zone is supported in any known CC map, without inventing its existence");
        var frame=new MovementFrame {Timestamp=now,Players=new(){new(){Player="Example World",HP=100,X=3,Y=0,Z=5,StatusTimestamp=now,Statuses=new(){new(){Id=3754,Remaining=2}}}}};
        check(ArenaMechanicScene.Statuses(frame,1138,now).Single().Kind==ArenaMechanicKind.HeatWave&&!ArenaMechanicScene.Statuses(frame,1034,now).Any(),"heat wave rendering requires an actual arena-specific active status");
        frame.Players[0].Statuses[0].Remaining=0;
        check(ArenaMechanicScene.Statuses(frame,1138,now).Any(),"a zero-duration environmental status remains visible while confirmed by a fresh frame");
        check(!ArenaMechanicScene.Statuses(frame,1138,now.AddSeconds(3)).Any(),"stale player snapshots cannot prolong full-map heat effects");
        var small=new PlayerPosition {HP=100,StatusTimestamp=now,Statuses=new(){new(){Id=3518,Remaining=3}}};
        check(ArenaMechanicScene.Mini(small,now)&&!ArenaMechanicScene.Mini(small,now.AddSeconds(4)),"only an actual unexpired Mini status shrinks a pawn");
        geometry.Clear();geometry.Pawn(Vector3.Zero,0,false,false,1,.4f);float miniHeight=geometry.Vertices.ToArray().Max(v=>v.Position.Y);
        check(miniHeight<1.2f&&miniHeight>1,"Mini scales the actual replay pawn instead of inferring a gaze hit");
        geometry.Clear();ArenaMechanicRenderer.Draw(geometry,new(ArenaMechanicKind.AetherBridge,Vector3.Zero),0);var marker=geometry.Vertices.Length;
        geometry.Clear();ArenaMechanicRenderer.Draw(geometry,new(ArenaMechanicKind.AetherBridge,Vector3.Zero,Destination:new(0,0,10)),0);
        check(geometry.Vertices.Length!=marker,"a bridge path is drawn only when endpoints are supplied");
    }
    internal static void Export(string path) {
        var frames=new List<object>();
        foreach(var kind in ArenaMechanicScene.GalleryKinds) {
            var samples=new List<object>();
            int count=kind==ArenaMechanicKind.Onmyoji?32:8;
            for(int i=0;i<count;i++) {
                var g=new ReplaySceneGeometry();ArenaMechanicRenderer.Draw(g,new(kind,Vector3.Zero,Destination:kind is ArenaMechanicKind.AetherBridge or ArenaMechanicKind.TrickDoor?new(0,0,10):null,Progress:.65f,Reference:true),kind==ArenaMechanicKind.Onmyoji?i*.125f:i/8f);
                samples.Add(g.Vertices.ToArray().Select(v=>new object[]{v.Position.X,v.Position.Y,v.Position.Z,v.Color}).ToArray());
            }
            frames.Add(new {kind=kind.ToString(),name=ArenaMechanicScene.Name(kind),detail=ArenaMechanicGuide.Detail(kind),samples});
        }
        File.WriteAllText(path,JsonSerializer.Serialize(frames));
    }
}
