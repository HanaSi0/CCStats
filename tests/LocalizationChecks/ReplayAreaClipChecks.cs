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
        var red=ReplayMapMesh.LoadEmbedded(1138).ArenaMask!;
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
