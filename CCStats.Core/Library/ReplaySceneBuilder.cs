using System.Numerics;
using PvpStats.Analysis;
namespace CCStats.Core.Library;
public static class ReplaySceneBuilder {
    public static ReplayVertex[] Build(ReplayBundle bundle,ReplayMapMesh? mesh,DateTime time,bool effects=true,bool trail=true,bool clip=true) {
        var data=bundle.Data;var scene=new ReplaySceneGeometry();
        var frame=ReplayInterpolation.Sample(data,time,(player,a,b)=>bundle.Timeline.Any(e=>e.Kind=="减员"&&e.Target==player&&e.Timestamp>a&&e.Timestamp<=b));
        float Height(float x,float? y,float z)=>y.HasValue&&float.IsFinite(y.Value)?y.Value:mesh?.EstimatedHeight(x,z)??0;
        Vector3 At(PlayerPosition p)=>new(p.X,Height(p.X,p.Y,p.Z),p.Z);
        foreach(var p in frame?.Players??Enumerable.Empty<PlayerPosition>()) {
            if(!float.IsFinite(p.X)||!float.IsFinite(p.Z))continue;
            if(p.Team==0) {
                var at=At(p);float size=ArenaMechanicScene.Mini(p,time)?.4f:1;
                var top=at+new Vector3(0,2.7f*size,0);var points=new Vector3[3];
                for(int i=0;i<3;i++) {float angle=p.Heading+i*MathF.Tau/3;points[i]=at+new Vector3(MathF.Sin(angle)*size,.2f*size,MathF.Cos(angle)*size);}
                for(int i=0;i<3;i++)scene.Triangle(points[i],points[(i+1)%3],top,ReplaySceneGeometry.Rgba(.65f,.68f,.72f));
                scene.Ribbon(at,at+new Vector3(MathF.Sin(p.Heading)*3,0,MathF.Cos(p.Heading)*3),.18f,ReplaySceneGeometry.Rgba(.8f,.82f,.85f));
            } else scene.Pawn(At(p),p.Heading,p.HP==0,false,p.Team,ArenaMechanicScene.Mini(p,time)?.4f:1);
            // Unknown affiliations use a neutral marker, avoiding fabricated opponent identities.
            if(p.Team==0)scene.Diamond(At(p)+Vector3.UnitY*3,.2f,.4f,ReplaySceneGeometry.Rgba(.8f,.8f,.8f));
            if(ReplaySceneGeometry.GuardVisible(p,time))scene.GuardBubble(At(p));
        }
        if(frame?.Crystal is {} crystal) {
            var at=new Vector3(crystal.X,Height(crystal.X,crystal.Y,crystal.Z),crystal.Z);
            scene.Diamond(at+Vector3.UnitY*.5f,1.4f,5.5f,ReplaySceneGeometry.Rgba(.25f,.75f,1));
            scene.Ring(at+Vector3.UnitY*.1f,5,.15f,ReplaySceneGeometry.Rgba(.3f,.85f,1));
        }
        if(trail) {
            MovementFrame? previous=null;int count=0;
            foreach(var sample in data.Frames.Where(f=>f.Timestamp<=time&&f.Crystal!=null)) {
                if(count++%6!=0)continue;
                if(previous?.Crystal is {} a && sample.Crystal is {} b &&(sample.Timestamp-previous.Timestamp).TotalSeconds<2)
                    scene.Ribbon(new(a.X,Height(a.X,a.Y,a.Z),a.Z),new(b.X,Height(b.X,b.Y,b.Z),b.Z),.15f,ReplaySceneGeometry.Rgba(1,.75f,.1f));
                previous=sample;if(count>50000)break;
            }
        }
        if(!effects)return scene.Vertices.ToArray();
        var origin=bundle.Match.StartUtc;
        foreach(var actor in ArenaMechanicScene.Objects(data,bundle.Match.Territory,time).Concat(ArenaMechanicScene.Actions(data,bundle.Match.Territory,time)).DistinctBy(a=>(a.Kind,a.ObjectId,a.Position)).Take(48)) {
            if(scene.Vertices.Length>100000)break;ArenaMechanicRenderer.Draw(scene,actor,(float)(time-origin).TotalSeconds);
        }
        foreach(var visual in MapMechanicVisuals.Active(data.MapMechanics,bundle.Match.Territory,time)) {
            using(scene.ClipTo(clip?mesh?.ArenaMask:null,visual.Center.Y))MapMechanicVisuals.Draw(scene,visual,time,(x,z)=>mesh?.SurfaceHeight(x,z,visual.Center.Y)??visual.Center.Y);
            if(scene.Vertices.Length>140000)break;
        }
        foreach(var (e,rule) in LimitBreakVisuals.Active(data.ReplaySkills,frame,time).OrderByDescending(x=>x.Rule.Benefit)) {
            if(!LimitBreakVisuals.Center(rule,e,out var at))continue;
            if(rule.Anchor==LbAnchor.Source&&rule.Duration>1.5f) {
                var moving=frame?.Players.FirstOrDefault(p=>p.Player==e.Actor);if(moving==null)continue;at=At(moving);
            }
            float opacity=LimitBreakVisuals.Opacity(rule,e,time);if(opacity<=0)continue;
            var terrainOffset=at.Y-(mesh?.EstimatedHeight(at.X,at.Z)??0);
            using(scene.ClipTo(clip?mesh?.ArenaMask:null,at.Y))LimitBreakVisuals.Draw(scene,LimitBreakVisuals.ClientDimensions(rule,e),at,e.Heading,new(e.X,e.Y,e.Z),(x,z)=>(mesh?.EstimatedHeight(x,z)??0)+terrainOffset,opacity,LimitBreakVisuals.Reveal(e,time));
            if(scene.Vertices.Length>200000)break;
        }
        ReplaySummons.Draw(scene,ReplaySummons.Resolve(data.SummonFrames,data.ReplaySkills,time),data.ReplaySkills,frame,time,(x,y,z)=>mesh?.SurfaceHeight(x,z,y)??y,clip?mesh?.ArenaMask:null);
        var vertices=scene.Vertices;var cap=Math.Min(vertices.Length,240000);cap-=cap%3;return vertices[..cap].ToArray();
    }
}
