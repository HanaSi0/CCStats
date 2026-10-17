using System.Numerics;
using PvpStats.Analysis;
internal static class ReplayAreaClipChecks {
    private static float Area(ReplaySceneGeometry scene) {
        float area=0;var vertices=scene.Vertices;
        for(int i=0;i<vertices.Length;i+=3)area+=MathF.Abs(Vector3.Cross(vertices[i+1].Position-vertices[i].Position,vertices[i+2].Position-vertices[i].Position).Y)/2;
        return area;
    }
    internal static void Run(Action<bool,string> check) {
        // Two islands separated by an actual gap. A crossing triangle must split instead of bridging the gap.
        var cells=new bool[16];for(int z=0;z<4;z++) { cells[z*4]=true;cells[z*4+3]=true; }
        var mask=new ReplayArenaMask(Vector2.Zero,4,4,cells);var scene=new ReplaySceneGeometry();
        using(scene.ClipTo(mask)) {
            scene.Triangle(new(-1,0,-1),new(3,4,-1),new(3,4,3),0x88ff00ff);
            scene.Triangle(new(-1,0,-1),new(3,4,3),new(-1,0,3),0x88ff00ff);
        }
        check(Math.Abs(Area(scene)-2)<.0001,"large effect clips to both islands without filling the inaccessible gap");
        check(scene.Vertices.ToArray().All(v=>v.Position.X<=.5001f||v.Position.X>=1.4999f),"clipped vertices remain on the two legitimate floor strips");
        check(scene.Vertices.ToArray().All(v=>Math.Abs(v.Position.Y-v.Position.X-1)<.0001&&v.Color==0x88ff00ff),"clipping preserves interpolated height and effect color");
        scene.Clear();using(scene.ClipTo(mask))scene.Triangle(new(10,0,10),new(12,0,10),new(10,0,12),0xffffffff);
        check(scene.Vertices.Length==0,"fully external effect triangles are discarded");
        using(scene.ClipTo(mask)) { using(scene.ClipTo(null))scene.Triangle(new(10,0,10),new(12,0,10),new(10,0,12),0xffffffff); }
        check(scene.Vertices.Length==3,"airborne markers can bypass clipping in a nested scope");
        scene.Triangle(new(10,0,10),new(12,0,10),new(10,0,12),0xffffffff);
        check(scene.Vertices.Length==6,"clip scope restores the previous state and never clips subsequent pawns or shields");
        var complete=new ReplayArenaMask(Vector2.Zero,4,4,Enumerable.Repeat(true,16).ToArray());scene.Clear();
        using(scene.ClipTo(complete))scene.Triangle(new(.6f,0,.6f),new(.9f,0,.6f),new(.6f,0,.9f),0xffffffff);
        check(scene.Vertices.Length==3&&Math.Abs(Area(scene)-.045f)<.0001,"fully internal triangles keep their original topology");
        foreach(uint territory in new uint[]{1032,1033,1034,1116,1138,1293,1357}) {
            var mesh=ReplayMapMesh.LoadEmbedded(territory);var arena=mesh.ArenaMask;
            check(arena!=null&&arena.CoveredCells>1000,"each exported arena builds a usable cached display footprint: "+territory);
            check(arena!=null&&!arena.Contains(mesh.Max.X+5,mesh.Max.Z+5),"effects outside arena bounds are excluded: "+territory);
        }
        foreach(uint territory in new uint[]{1032,1033,1034,1116,1138,1293,1357})
            check(ReplayMapMesh.LoadEmbedded(territory).ArenaMask is {Layered:true},"official-silhouette walk grid drives the footprint: "+territory);
        // Layered floors: the event elevation picks the floor, so a bridge footprint stays on the bridge.
        var layers=new ReplayWalkGrid(3,3,Vector2.Zero,ReplayArenaMask.Cell,Enumerable.Repeat((byte)2,9).ToArray(),Enumerable.Range(0,9).SelectMany(_=>new[]{0f,5f}).ToArray());
        var layered=ReplayArenaMask.FromWalk(layers)!;var bridge=new ReplaySceneGeometry();
        using(bridge.ClipTo(layered,4.6f))bridge.Triangle(new(.2f,9,.2f),new(1.3f,9,.2f),new(.2f,9,1.3f),0x88ffffff);
        check(bridge.Vertices.Length>0&&bridge.Vertices.ToArray().All(v=>Math.Abs(v.Position.Y-5.24f)<.001f),"footprint on a bridge uses the bridge layer, not the ground or a draped height");
        bridge.Clear();using(bridge.ClipTo(layered,.3f))bridge.Triangle(new(.2f,9,.2f),new(1.3f,9,.2f),new(.2f,9,1.3f),0x88ffffff);
        check(bridge.Vertices.ToArray().All(v=>Math.Abs(v.Position.Y-.24f)<.001f),"footprint under a bridge uses the lower floor");
        // A cell whose only floor is far below the effect (sand under a platform edge) must not pull a strip down.
        var edge=new ReplayWalkGrid(2,1,Vector2.Zero,ReplayArenaMask.Cell,new byte[]{1,1},new[]{0f,-5f});
        var edgeMask=ReplayArenaMask.FromWalk(edge)!;var strip=new ReplaySceneGeometry();
        using(strip.ClipTo(edgeMask,0))strip.Triangle(new(.05f,0,.05f),new(.95f,0,.05f),new(.05f,0,.45f),0x88ffffff);
        check(strip.Vertices.Length>0&&strip.Vertices.ToArray().All(v=>v.Position.X<=.5001f&&Math.Abs(v.Position.Y-.24f)<.001f),"floors on another level are dropped instead of stretching the footprint down to them");
        var red=ReplayMapMesh.LoadEmbedded(1138).ArenaMask!;
        // Dunes behind the east plaza wall: inside the old height band, outside the official arena.
        check(!red.Contains(40,-20)&&!red.Contains(42,-20),"Red Sands dunes behind the plaza wall are excluded");
        var bay=ReplayMapMesh.LoadEmbedded(1293).ArenaMask!;
        check(!bay.Contains(70,100)&&bay.Contains(100,100),"Bayside sand under the platforms is excluded while the central platform stays");
        var plaza=new ReplaySceneGeometry();var shield=LimitBreakVisuals.Rules.First(r=>r.ActionId==41502&&r.Shape==LbShape.Circle);
        var plazaAt=new Vector3(60,ReplayMapMesh.LoadEmbedded(1138).EstimatedHeight(60,0),0);
        using(plaza.ClipTo(red,plazaAt.Y))LimitBreakVisuals.Draw(plaza,shield with {Size=22},plazaAt,0,plazaAt,(_,_)=>8);
        check(plaza.Vertices.Length>0&&plaza.Vertices.ToArray().All(v=>v.Position.Y<plazaAt.Y+1.2f),"plaza footprint stays on the plaza floor instead of rising onto its walls");
        check(red.Contains(-60,0)&&red.Contains(60,0)&&red.Contains(0,0),"clipping retains both repaired circular floors and central crystal lane");
        check(!red.Contains(0,80),"Red Sands distant canyon floor does not become a playable effect surface");
        var geometry=new ReplaySceneGeometry();var rule=LimitBreakVisuals.Rules.First(r=>r.ActionId==41502&&r.Shape==LbShape.Circle);
        var at=new Vector3(0,0,0);var clock=System.Diagnostics.Stopwatch.StartNew();
        using(geometry.ClipTo(red))LimitBreakVisuals.Draw(geometry,rule,at,0,at,(_,_)=>0);
        clock.Stop();check(geometry.Vertices.Length>0&&geometry.Vertices.Length<60000,"large thirty-unit LB remains within the dynamic geometry budget after clipping");
        Console.WriteLine($"Arena clipping experiment: {geometry.Vertices.Length:N0} vertices, {clock.Elapsed.TotalMilliseconds:F1} ms (cloud CPU, not game FPS).");
        var export=Environment.GetEnvironmentVariable("CCSTATS_CLIP_FIXTURE");
        if(!string.IsNullOrEmpty(export)) {
            // Real C# output, consumed by the browser visualization experiment.
            var wide=new ReplaySceneGeometry();var wideRule=rule with {Size=80};
            using(wide.ClipTo(red))LimitBreakVisuals.Draw(wide,wideRule,at,0,at,(_,_)=>0);
            File.WriteAllText(export,System.Text.Json.JsonSerializer.Serialize(wide.Vertices.ToArray().Select(v=>new {x=v.Position.X,y=v.Position.Y,z=v.Position.Z,color=v.Color})));
        }
    }
}
