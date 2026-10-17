using System.IO.Compression;
using System.Text;
using System.Numerics;
using LiteDB;
using PvpStats.Analysis;
internal static class Replay3DChecks {
    internal static void Run(Action<bool,string> check) {
        var camera=new ReplayCamera3D();camera.Fit(new[]{new Vector3(-20,2,-20),new Vector3(20,4,20)});
        var size=new Vector2(1000,700);var matrix=camera.Matrix(size.X/size.Y);
        check(ReplayCamera3D.Project(camera.Target,matrix,size,out var center,out _)&&Vector2.Distance(center,size/2)<.001,"3D camera centers the observed arena rather than the world origin");
        check(!ReplayCamera3D.Project(camera.Eye+(camera.Eye-camera.Target),matrix,size,out _,out _),"3D near clipping rejects points behind the camera");
        check(!ReplayCamera3D.Project(new(float.NaN,0,0),matrix,size,out _,out _),"invalid world positions cannot reach native drawing calls");
        camera.Orbit(new(50,500));check(camera.Pitch<=1.48f&&camera.Pitch>=.12f,"camera orbit avoids pole singularities");
        camera.Zoom(.000001f);check(camera.Distance==8,"3D dolly has a safe near limit");
        camera.Zoom(100000);check(camera.Distance==1200,"3D dolly has a bounded far limit");
        var oldTarget=camera.Target;camera.Pan(new(50,20),700);check(camera.Target!=oldTarget,"3D camera pans in the ground plane");
        camera.Reset();check(Vector3.Distance(camera.Target,new(0,3,0))<.001&&camera.Distance<1200,"camera reset restores the fitted arena framing");
        var geometry=new List<SceneTriangle>();ReplayPrimitives.Pawn(geometry,Vector3.Zero,Vector4.One);
        check(geometry.Count==120&&geometry.All(t=>ReplayScene3D.Finite(t.A)&&ReplayScene3D.Finite(t.B)&&ReplayScene3D.Finite(t.C)),"uniform chess-piece geometry is finite and bounded");
        geometry.Clear();ReplayPrimitives.Crystal(geometry,new(1,2,3),Vector4.One);
        check(geometry.Count==8&&geometry.Any(t=>t.A.Y>7),"tactical crystal has eight actual 3D diamond faces");
        var transform=ReplayScene3D.Transform(new(10,2,0),new(0,MathF.PI/2,0),new(2));
        check(Vector3.Distance(Vector3.Transform(Vector3.UnitX,transform),new(10,2,-2))<.001,"map instance scale, rotation and translation use the same world coordinate space");
        var scene=new ReplayScene3D();scene.Triangles.Add(new(new(0,2,0),new(10,4,0),new(0,2,10),Vector4.One));
        check(Math.Abs(scene.Ground(2,2)!.Value-2.4f)<.001&&scene.Ground(20,20)==null,"legacy ground fallback derives height only within loaded model triangles");
        var start=new DateTime(2026,10,17,0,0,0,DateTimeKind.Utc);
        var capture=new DetailedTelemetry { Version=5,Frames=new() { new() { Timestamp=start,Crystal=new() { X=2,Y=3.5f,Z=4 },Players=new() { new() { Player="Example World",X=1,Y=7.25f,Z=3,StatusTimestamp=start } } } } };
        capture.PrepareForStorage();
        var restored=BsonMapper.Global.ToObject<DetailedTelemetry>(BsonMapper.Global.ToDocument(capture));
        check(restored.Frames[0].Players[0].Y==7.25f&&restored.Frames[0].Crystal?.Y==3.5f,"observed player and crystal heights survive compressed database storage");
        using var raw=new MemoryStream();
        using(var w=new BinaryWriter(raw,Encoding.UTF8,true)) {
            w.Write(0x43434D32);w.Write(1);w.Write((ushort)1);w.Write("Legacy World");w.Write(start.ToBinary());
            w.Write(0L);w.Write(0);w.Write(0);w.Write(0);w.Write(true);w.Write(3f);w.Write(4f);w.Write((byte)1);
            w.Write((ushort)0);w.Write((byte)1);w.Write(1f);w.Write(2f);w.Write(0f);w.Write(20000u);w.Write(60000u);w.Write((byte)0);
            w.Write(19u);w.Write(true);w.Write(start.ToBinary());w.Write((byte)1);w.Write(3054u);w.Write((ushort)0);w.Write(5f);
        }
        using var packed=new MemoryStream();using(var zip=new GZipStream(packed,CompressionLevel.Fastest,true))raw.WriteTo(zip);
        var prior=MovementCodec.Decode(packed.ToArray());
        check(prior[0].Players[0].Y==null&&prior[0].Crystal?.Y==null&&prior[0].Players[0].Statuses[0].Id==3054,"previous full-status replay format remains readable without inventing measured elevations");
        var probe=new CrystalRangeProbe();probe.Scope(1,100);
        check(probe.Mark(start,new(4,0,0),Vector3.Zero,true)&&probe.Mark(start,new(6,0,0),Vector3.Zero,false)&&probe.ReferenceRadius==5,"manual inside and outside samples bracket a provisional escort radius");
        check(probe.Samples.Count==2&&probe.Samples[0].HorizontalDistance==4,"probe preserves individual measurements instead of replacing them with hitbox radius");
        probe.Mark(start,new(7,0,0),Vector3.Zero,true);check(!probe.Consistent&&probe.ReferenceRadius==null,"contradictory manual labels cannot become a confirmed radius");
        probe.Scope(2,100);check(probe.Samples.Count==0&&probe.ReferenceRadius==null,"changing territory discards incompatible circle measurements");
        probe.Scope(2,101);check(!probe.Mark(start,new(float.NaN,0,0),Vector3.Zero,true),"invalid probe coordinates are rejected");
    }
}
