using System.Numerics;
using LiteDB;
using PvpStats.Analysis;
internal static class ReplaySummonChecks {
    public static void Run(Action<bool,string> check) {
        var now=new DateTime(2026,10,17,0,0,0,DateTimeKind.Utc);
        var sample=new ReplaySummonFrame {Timestamp=now,Summons=new(){new(){EntityId=10,NameId=6566,Owner="Owner World",X=1,Y=0,Z=2}}};
        var frames=new[]{sample,new ReplaySummonFrame {Timestamp=now.AddSeconds(.2)}};
        check(ReplaySummons.At(frames,now.AddSeconds(-.01))==null,"summons never appear before their first observed sample");
        check(ReplaySummons.At(frames,now.AddSeconds(.1))==sample,"summons use the latest past loaded-object observation");
        check(ReplaySummons.At(frames,now.AddSeconds(.3))!.Summons.Count==0,"an observed empty sample stops a disappeared summon");
        check(ReplaySummons.At(new[]{sample},now.AddSeconds(.46))==null,"recording gaps never fabricate a full nominal summon lifetime");
        check(ReplaySummons.IsPetAction(29675)&&ReplaySummons.IsPetAction(29681)&&!ReplaySummons.IsPetAction(29665)&&!ReplaySummons.IsPetAction(29666),"only autonomous LB pet actions added, ordinary summoner follow-ups stay excluded");
        var scene=new ReplaySceneGeometry();ReplaySummons.Draw(scene,sample,Array.Empty<ReplaySkillEvent>(),null,now,(_,y,_)=>y);
        var baseline=scene.Vertices.Length;
        check(baseline>0&&scene.Vertices.ToArray().Any(v=>v.Position.Y>6),"observed pet has an airborne placeholder");
        check(scene.Vertices.ToArray().Where(v=>v.Position.Y<1).All(v=>Vector2.Distance(new(v.Position.X,v.Position.Z),new(1,2))<30.2),"selection waves stay within the official 30y radius");
        var effect=new ReplaySkillEvent {Timestamp=now,ActionId=29676,SourceEntityId=10,PetSource=true,Resolved=true,SummonTargets=new(){new(){Player="Hit World",X=8,Y=0,Z=9}}};
        scene.Clear();ReplaySummons.Draw(scene,sample,new[]{effect},null,now.AddSeconds(.1),(_,y,_)=>y);var linked=scene.Vertices.Length;
        scene.Clear();ReplaySummons.Draw(scene,sample,Array.Empty<ReplaySkillEvent>(),null,now.AddSeconds(.1),(_,y,_)=>y);
        check(linked==scene.Vertices.Length+6,"only recorded summon hits create target links");
        effect.SourceEntityId=11;scene.Clear();ReplaySummons.Draw(scene,sample,new[]{effect},null,now.AddSeconds(.1),(_,y,_)=>y);
        check(scene.Vertices.Length==linked-6,"different summon entities never inherit one another's targets");
        effect.SourceEntityId=10;effect.Timestamp=now.AddSeconds(1);scene.Clear();ReplaySummons.Draw(scene,sample,new[]{effect},null,now.AddSeconds(.1),(_,y,_)=>y);
        check(scene.Vertices.Length==linked-6,"future hit packets cannot leak into replay links");
        effect.Timestamp=now;effect.SummonTargets[0].Benefit=true;
        scene.Clear();ReplaySummons.Draw(scene,sample,new[]{effect},null,now.AddSeconds(.1),(_,y,_)=>y);
        var linkColor=scene.Vertices.ToArray()[^1].Color;
        check(((linkColor>>8)&255)>(linkColor&255),"recorded healing links are mint green rather than player blue/red");
        scene.Clear();ReplaySummons.Draw(scene,sample,new[]{effect},null,now.AddSeconds(.8),(_,y,_)=>y);var expired=scene.Vertices.Length;
        scene.Clear();ReplaySummons.Draw(scene,sample,Array.Empty<ReplaySkillEvent>(),null,now.AddSeconds(.8),(_,y,_)=>y);
        check(scene.Vertices.Length==expired,"summon effect links fade out rather than accumulating throughout the match");
        var doc=BsonMapper.Global.ToDocument(sample);var restored=BsonMapper.Global.ToObject<ReplaySummonFrame>(doc);
        check(restored.Summons[0].EntityId==10&&restored.Summons[0].Owner=="Owner World","summon position and owner round-trip storage");
        check(BsonMapper.Global.ToObject<DetailedTelemetry>(new BsonDocument()).SummonFrames.Count==0,"old matches without summon observations remain readable");
    }
}
