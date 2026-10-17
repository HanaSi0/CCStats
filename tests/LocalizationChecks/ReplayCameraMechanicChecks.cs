using System.Numerics;
using LiteDB;
using PvpStats.Analysis;

internal static class ReplayCameraMechanicChecks {
    private static Vector2 Project(ReplayOrbitCamera camera,Vector3 point,Vector2 size) {
        var clip=Vector4.Transform(new Vector4(point,1),camera.ViewProjection(size));
        return new((clip.X/clip.W+1)*size.X/2,(1-clip.Y/clip.W)*size.Y/2);
    }
    internal static void Run(Action<bool,string> check) {
        var size=new Vector2(1000,700);var pixel=new Vector2(650,420);var moved=new Vector2(790,490);
        var camera=new ReplayOrbitCamera();camera.Reset(new(100,1,100),190);
        check(camera.GroundPoint(pixel,size,out var anchor),"3D drag picks a world point on the ground");
        camera.BeginDrag(false,pixel,size);camera.Drag(moved,size);
        check(Vector2.Distance(Project(camera,anchor,size),moved)<.03,"3D pan keeps the grabbed world point under the mouse");
        var outside=new Vector2(1200,850);camera.Drag(outside,size);
        check(Vector2.Distance(Project(camera,anchor,size),outside)<.1,"captured map drag remains continuous outside the viewport");camera.EndDrag();
        camera.Reset(new(100,1,100),190);camera.GroundPoint(pixel,size,out anchor);camera.BeginDrag(true,pixel,size);camera.Drag(pixel+new Vector2(95,35),size);
        check(Vector2.Distance(Project(camera,anchor,size),pixel)<.1,"3D orbit preserves the off-center picked pivot without an initial snap");camera.EndDrag();
        for(int i=0;i<8;i++) {
            camera.BeginDrag(true,pixel,size);camera.Drag(pixel+new Vector2(100,0),size);camera.EndDrag();
            check(camera.GroundPoint(pixel,size,out anchor),"ground picking remains valid at rotated camera angles");
            camera.BeginDrag(false,pixel,size);camera.Drag(moved,size);camera.EndDrag();
            check(Vector2.Distance(Project(camera,anchor,size),moved)<.12,"drag follows the cursor across camera rotations");
        }
        camera.Reset(new(100,1,100),190);camera.GroundPoint(pixel,size,out anchor);camera.Zoom(2,pixel,size);
        check(camera.Distance==190&&camera.DesiredDistance<190,"wheel zoom has a smooth target instead of a sudden camera jump");
        for(int i=0;i<120;i++)camera.Update(1f/60,size);
        check(camera.Distance<190&&Vector2.Distance(Project(camera,anchor,size),pixel)<.12,"smooth zoom remains anchored at the mouse position");
        camera.Zoom(1000,pixel,size);for(int i=0;i<120;i++)camera.Update(1f/60,size);
        check(camera.Distance>=12&&float.IsFinite(camera.Distance),"camera zoom clamps safely at the near limit");
        var before=camera.Target;camera.BeginDrag(false,pixel,size);camera.EndDrag();
        check(camera.Target==before&&!camera.Dragging,"press/release and drag cancellation do not move the camera");
        var start=new DateTime(2026,10,17,0,0,0,DateTimeKind.Utc);
        var cast=new ReplayMapEvent { Timestamp=start,Kind=ReplayMapEventKind.CastObserved,ObservedUntil=start.AddSeconds(2),ExpectedEndTime=start.AddSeconds(5),X=1,Y=3,Z=2,Heading=1,TargetX=4,TargetY=5,TargetZ=6 };
        check(ReplayMechanicObservation.Visible(cast,start.AddSeconds(1)),"recorded environmental cast is visible during observation");
        check(!ReplayMechanicObservation.Visible(cast,start.AddSeconds(3)),"unloaded environmental cast does not pretend to persist to its expected end");
        cast.ResolvedAt=start.AddSeconds(2.1);
        check(!ReplayMechanicObservation.Visible(cast,start.AddSeconds(2.2)),"cast warning stops at observed action resolution");
        check(!ReplayMechanicObservation.Visible(cast,start.AddMilliseconds(-1)),"mechanics are not visible before their first observation");
        var timer=new ReplayMapEvent { Timestamp=start,Kind=ReplayMapEventKind.TimerWarning,MapWide=true,ExpectedEndTime=start.AddSeconds(5) };
        check(ReplayMechanicObservation.Caption(timer,start.AddSeconds(3)).Contains("2.0"),"timer observation shows remaining time without assigning an unverified mechanism");
        check(!ReplayMechanicObservation.Visible(timer,start.AddSeconds(6)),"expired timer warning is removed");
        var legacy=BsonMapper.Global.ToObject<ReplayMapEvent>(new BsonDocument { ["Timestamp"]=start,["Label"]="legacy",["X"]=1,["Z"]=2 });
        check(legacy.Kind==ReplayMapEventKind.ActionResolved&&legacy.Y==null&&ReplayMechanicObservation.Visible(legacy,start.AddSeconds(1)),"historical mechanic events keep their prior short observation marker");
        var restored=BsonMapper.Global.ToObject<ReplayMapEvent>(BsonMapper.Global.ToDocument(cast));
        check(restored.Y==3&&restored.TargetY==5&&restored.Heading==1&&restored.ResolvedAt==cast.ResolvedAt,"mechanic source/target heights, heading and phase evidence survive storage");
    }
}
