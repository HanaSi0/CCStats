using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using PvpStats.Analysis;

internal static class Replay3DChecks {
    internal static void Run(Action<bool,string> check) {
        var start=new DateTime(2026,10,17,0,0,0,DateTimeKind.Utc);
        var a=new PlayerPosition { Player="Example World",Team=1,X=0,Y=2,Z=0,Heading=3.1f,HP=500,MaxHP=1000,StatusTimestamp=start,Statuses=new() { new() { Id=3054,Remaining=5 } } };
        var b=new PlayerPosition { Player=a.Player,Team=1,X=1,Y=3,Z=1,Heading=-3.1f,HP=200,MaxHP=1000,StatusTimestamp=start.AddMilliseconds(100) };
        var data=new DetailedTelemetry { Frames=new() { new() { Timestamp=start,Players=new() { a },Crystal=new() { X=0,Y=0,Z=0 } },new() {Timestamp=start.AddMilliseconds(100),Players=new() {b},Crystal=new() {X=.5f,Y=1,Z=.5f} } } };
        var middle=start.AddMilliseconds(50);var sample=ReplayInterpolation.Sample(data,middle)!;var player=sample.Players.Single();
        check(Math.Abs(player.X-.5f)<.001&&Math.Abs(player.Y!.Value-2.5f)<.001,"3D replay interpolates actual height and horizontal positions");
        check(player.HP==a.HP&&player.StatusTimestamp==a.StatusTimestamp&&ReferenceEquals(player.Statuses,a.Statuses),"3D replay never exposes future HP or status snapshots");
        check(Math.Abs(Math.Abs(player.Heading)-MathF.PI)<.01f,"3D heading interpolation takes the short path across angle wrap");
        check(Math.Abs(sample.Crystal!.X-.25f)<.001&&Math.Abs(sample.Crystal.Y!.Value-.5f)<.001,"crystal pose uses captured 3D coordinates");
        check(ReferenceEquals(ReplayInterpolation.Sample(data,start.AddSeconds(-1)),null),"3D playback does not invent pre-capture samples");
        check(ReferenceEquals(ReplayInterpolation.Sample(data,middle,(_,_,_)=>true)!.Players[0],a),"3D interpolation does not bridge a knockout boundary");
        b.HP=0;check(ReferenceEquals(ReplayInterpolation.Sample(data,middle)!.Players[0],a),"3D interpolation does not blend toward a dead sample");b.HP=200;
        b.X=80;check(ReferenceEquals(ReplayInterpolation.Sample(data,middle)!.Players[0],a),"teleport and respawn jumps do not animate through the map");b.X=1;
        b.Y=null;check(ReplayInterpolation.Sample(data,middle)!.Players[0].Y==a.Y,"missing future height does not fabricate a vertical path");b.Y=3;
        data.Frames[1].Timestamp=start.AddSeconds(10);check(ReferenceEquals(ReplayInterpolation.Sample(data,middle),data.Frames[0]),"3D playback preserves recording gaps");
        check(Marshal.SizeOf<ReplayVertex>()==16,"GPU vertex layout matches HLSL position/color input stride");
        var pairs=new(uint Normal,uint Custom,int Faces)[] { (1032,1058,76963),(1033,1059,161691),(1034,1060,125504),(1116,1117,182418),(1138,1139,483216),(1293,1294,279142),(1357,1358,204054) };
        foreach(var p in pairs) {
            check(ReplayMapMesh.MapKey(p.Normal)==ReplayMapMesh.MapKey(p.Custom),"normal and custom territory select the same exported arena: "+p.Normal);
            var mesh=ReplayMapMesh.LoadEmbedded(p.Normal);
            check(mesh.Indices.Length/3==p.Faces&&mesh.Vertices.Length>3,"packaged simplified map decodes completely: "+p.Normal);
            check(mesh.Min.X<mesh.Max.X&&mesh.Min.Z<mesh.Max.Z,"packaged arena has usable camera bounds: "+p.Normal);
            if(p.Normal==1138)check(mesh.Min.Y<-15&&mesh.Max.Y>9,"Red Sands retains source canyon walls below the old -8 cutoff and rocks above the old 9 cutoff");
            if(p.Normal==1293)check(mesh.EstimatedHeight(70,100)<-7&&mesh.EstimatedHeight(70,100)>-13,"Bayside retains the source sand surface between the platforms, instead of an unobserved floor gap");
            if(p.Normal is 1293 or 1357)check(mesh.Max.X>150&&mesh.Max.Z>150,"offset arena retains game world coordinates: "+p.Normal);
        }
        check(ReplayMapMesh.MapKey(0)==null,"unsupported territories do not silently select the wrong map");
        using(var raw=new MemoryStream()) {
            using(var writer=new BinaryWriter(raw,System.Text.Encoding.UTF8,true)) { writer.Write(0x344D4343);writer.Write(3);writer.Write(3);foreach(var v in new[] { Vector3.Zero,Vector3.UnitX,Vector3.UnitZ }) {writer.Write(v.X);writer.Write(v.Y);writer.Write(v.Z);writer.Write(0xffffffffu);}writer.Write(0u);writer.Write(1u);writer.Write(99u); }
            using var packed=new MemoryStream();using(var zip=new GZipStream(packed,CompressionLevel.Fastest,true))raw.WriteTo(zip);packed.Position=0;
            bool rejected=false;try { ReplayMapMesh.Read(packed); }catch(InvalidDataException) {rejected=true;}check(rejected,"corrupt map indices fail before GPU buffer creation");
        }
        var floor=new ReplayMapMesh(new[] {new ReplayVertex(new(-5,3,-5),0xffffffff),new ReplayVertex(new(5,3,-5),0xffffffff),new ReplayVertex(new(0,3,5),0xffffffff)},new uint[] {0,1,2});
        check(Math.Abs(floor.EstimatedHeight(0,0)-3)<.001,"legacy missing-height fallback comes from exported floor geometry");
        var layered=new ReplayMapMesh(new[] {
            new ReplayVertex(new(-5,10,-5),0x88ffffff),new ReplayVertex(new(5,10,-5),0x88ffffff),new ReplayVertex(new(0,10,5),0x88ffffff),
            new ReplayVertex(new(-5,3,-5),0xffffffff),new ReplayVertex(new(5,3,-5),0xffffffff),new ReplayVertex(new(0,3,5),0xffffffff),
            new ReplayVertex(new(-5,-15,-5),0xaaffffff),new ReplayVertex(new(5,-15,-5),0xaaffffff),new ReplayVertex(new(0,-15,5),0xaaffffff)
        },new uint[]{0,1,2,3,4,5,6,7,8});
        check(layered.OpaqueIndexCount==3&&layered.Indices.Length==9,"opaque terrain and translucent display layers use separate native map passes");
        check(Math.Abs(layered.EstimatedHeight(0,0)-3)<.001,"fog and light shafts cannot replace actual terrain height");
        check(layered.Vertices[layered.Indices[3]].Position.Y==-15,"low fog draws before the raised translucent layer for above-map cameras");
        var red=ReplayMapMesh.LoadEmbedded(1138);
        check(red.EstimatedHeight(-60,0)>-2&&red.EstimatedHeight(-60,0)<-.3f&&red.EstimatedHeight(60,0)>-2&&red.EstimatedHeight(60,0)<-.3f,"both missing circular floors have reconstructed display surfaces near adjacent terrain elevation");
        check(red.OpaqueIndexCount<red.Indices.Length,"Red Sands preserves native visual layers instead of discarding them or drawing solid gray plates");
        var scene=new ReplaySceneGeometry();scene.Pawn(Vector3.Zero,0,false,true,1);scene.Diamond(Vector3.Zero,1,5,0xffffffff);scene.Ring(Vector3.Zero,5,.1f,0xffffffff);
        check(scene.Vertices.Length>0&&scene.Vertices.Length%3==0,"pawn, diamond and crystal circle produce GPU triangle lists");
    }
}
