using System.IO.Compression;
using System.Numerics;
using PvpStats.Analysis;

internal static class ReplayWalkChecks {
    private static ReplayWalkGrid Grid(int width,int depth,float cell,Func<int,int,float[]> layers) {
        var counts=new byte[width*depth];var heights=new List<float>();
        for(int z=0;z<depth;z++)for(int x=0;x<width;x++) { var l=layers(x,z);counts[z*width+x]=(byte)l.Length;heights.AddRange(l); }
        return new ReplayWalkGrid(width,depth,Vector2.Zero,cell,counts,heights.ToArray());
    }
    private static MemoryStream Packed(Action<BinaryWriter> write) {
        var raw=new MemoryStream();using(var writer=new BinaryWriter(raw,System.Text.Encoding.UTF8,true))write(writer);
        var packed=new MemoryStream();using(var zip=new GZipStream(packed,CompressionLevel.Fastest,true))raw.WriteTo(zip);packed.Position=0;return packed;
    }
    private static bool Rejected(Action<BinaryWriter> write) {
        try { ReplayWalkGrid.Read(Packed(write));return false; } catch(InvalidDataException) { return true; } catch(EndOfStreamException) { return true; }
    }

    internal static void Run(Action<bool,string> check) {
        // Pit in the middle of a flat floor: the footprint stays out of it.
        var pit=Grid(8,8,1,(x,z)=>x is 3 or 4&&z is 3 or 4?Array.Empty<float>():new[]{0f});
        var scene=new ReplaySceneGeometry();
        ReplayGroundDecal.Walkable(pit,0,.25f).Quad(scene,new(.5f,.5f),new(7.5f,.5f),new(7.5f,7.5f),new(.5f,7.5f),0x80ffffff);
        var tris=scene.Vertices.ToArray().Chunk(3).Select(t=>(t[0].Position+t[1].Position+t[2].Position)/3).ToArray();
        check(tris.Length>0&&!tris.Any(c=>c.X>3&&c.X<5&&c.Z>3&&c.Z<5),"ground footprint is clipped out of cells without standable floor");
        check(scene.Vertices.ToArray().All(v=>Math.Abs(v.Position.Y-.25f)<.001f),"clipped footprint rests on the floor plus its lift, never on a wall or rim");

        // Bridge over a path: each cell uses the floor layer nearest the event elevation.
        var bridge=Grid(3,3,1,(_,_)=>new[]{0f,5f});
        scene.Clear();ReplayGroundDecal.Walkable(bridge,4.6f,.25f).Quad(scene,new(.2f,.2f),new(2.8f,.2f),new(2.8f,2.8f),new(.2f,2.8f),0x80ffffff);
        check(scene.Vertices.Length>0&&scene.Vertices.ToArray().All(v=>Math.Abs(v.Position.Y-5.25f)<.001f),"upper floor layer is chosen for an event on the bridge");
        scene.Clear();ReplayGroundDecal.Walkable(bridge,.3f,.25f).Quad(scene,new(.2f,.2f),new(2.8f,.2f),new(2.8f,2.8f),new(.2f,2.8f),0x80ffffff);
        check(scene.Vertices.ToArray().All(v=>Math.Abs(v.Position.Y-.25f)<.001f),"lower floor layer is chosen for an event beneath the bridge");

        // Interior cells on one plane merge into one strip per row; edges on cell lines emit no slivers.
        var flat=Grid(20,20,1,(_,_)=>new[]{0f});
        scene.Clear();ReplayGroundDecal.Walkable(flat,0,.25f).Quad(scene,new(2,2),new(18,2),new(18,18),new(2,18),0x80ffffff);
        check(scene.Vertices.Length==16*6,"flat interior footprint merges cells into row strips");

        // Steps within climb height share corners; a wall-height jump is not bridged.
        var step=Grid(4,1,1,(x,_)=>new[]{x<2?0f:x==2?.4f:3f});
        var ground=ReplayGroundDecal.Walkable(step,0,0);
        check(Math.Abs(ground.SurfaceHeight(new(2,.5f),out var standable)-.2f)<.001f&&standable,"adjacent floor cells within a step meet at a shared corner height");
        check(Math.Abs(ground.SurfaceHeight(new(3.5f,.5f),out _)-3)<.001f,"a wall-height jump keeps its own floor height instead of a ramp across the wall");

        check(Rejected(w=>{w.Write(0x31574344);w.Write(1);w.Write(1);w.Write(0f);w.Write(0f);w.Write(.5f);w.Write((byte)0);}),"walk grid rejects a wrong header");
        check(Rejected(w=>{w.Write(0x31574343);w.Write(1);w.Write(1);w.Write(0f);w.Write(0f);w.Write(.5f);w.Write((byte)1);w.Write(1f);w.Write(9f);}),"walk grid rejects trailing data");
        check(Rejected(w=>{w.Write(0x31574343);w.Write(1);w.Write(1);w.Write(0f);w.Write(0f);w.Write(.5f);w.Write((byte)2);w.Write(3f);w.Write(1f);}),"walk grid rejects unsorted floor layers");
        check(Rejected(w=>{w.Write(0x31574343);w.Write(1);w.Write(1);w.Write(0f);w.Write(0f);w.Write(.5f);w.Write((byte)1);w.Write(float.NaN);}),"walk grid rejects nonfinite heights");

        // Official area-map silhouette: a 256x256 texture whose centre 64x64 block is arena floor colour.
        var pixels=new byte[256*256*4];
        for(int y=0;y<256;y++)for(int x=0;x<256;x++) {
            bool arena=x>=96&&x<160&&y>=96&&y<160;int i=(y*256+x)*4;
            // BGRA: interior (240,235,214), parchment background (230,221,181).
            pixels[i]=(byte)(arena?214:181);pixels[i+1]=(byte)(arena?235:221);pixels[i+2]=(byte)(arena?240:230);pixels[i+3]=255;
        }
        // Size factor 100 maps 1 world unit to 1/8 pixel of a 256 texture: pixel = (world+1024)/8.
        var silhouette=new ReplayMapSilhouette(256,256,pixels,100,0,0);
        check(silhouette.Inside(0,0)&&!silhouette.Inside(400,0)&&!silhouette.Inside(0,-400),"official area map separates the arena silhouette from out-of-bounds parchment");
        check(ReplayMapSilhouette.Interior(240,235,214)&&ReplayMapSilhouette.Interior(214,235,240)&&!ReplayMapSilhouette.Interior(230,221,181)&&!ReplayMapSilhouette.Interior(181,221,230),"silhouette colour test is independent of BGRA or RGBA channel order");
        var wide=new ReplayWalkGrid(4,1,new Vector2(-400,0),200,new byte[]{1,1,1,1},new[]{0f,0f,0f,0f});
        var trimmed=wide.Restrict(silhouette.Inside);
        check(trimmed.LayerCount==2&&trimmed.Layers(1,0).Length==1&&trimmed.Layers(2,0).Length==1&&trimmed.Layers(0,0).Length==0&&trimmed.Layers(3,0).Length==0,"floors outside the official arena silhouette are removed from the walk grid");
        bool blank=false;try { new ReplayMapSilhouette(256,256,new byte[256*256*4],100,0,0); } catch(ArgumentException) { blank=true; }
        check(blank,"an unrecognised area-map texture is rejected instead of erasing every floor");

        foreach(var territory in new uint[]{1032,1033,1034,1116,1138,1293,1357}) {
            var mesh=ReplayMapMesh.LoadEmbedded(territory);
            check(mesh.Walk is {LayerCount:>10000},"packaged arena has a walkable-floor grid: "+territory);
        }

        // Red Sands west plaza: the 22-unit Therapeutic Needles footprint stays on standable floor.
        var red=ReplayMapMesh.LoadEmbedded(1138);var walk=red.Walk!;
        check(walk.NearestAt(60f,0f,0f,out var plaza)&&plaza>-2&&plaza<0,"Red Sands plaza floor is standable");
        var now=new DateTime(2026,10,18,0,0,0,DateTimeKind.Utc);
        var shield=MapMechanicVisuals.Active(new[]{new ReplayMapEvent{ActionId=35753,Timestamp=now,X=60,Y=plaza,Z=0}},1138,now).Single();
        scene.Clear();MapMechanicVisuals.Draw(scene,shield,now,ReplayGroundDecal.Walkable(walk,plaza,MapMechanicVisuals.GroundLift));
        var vertices=scene.Vertices.ToArray();
        bool OnFloor(Vector3 p) {
            int cx=walk.CellX(p.X),cz=walk.CellZ(p.Z);
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)foreach(var h in walk.Layers(cx+dx,cz+dz))if(Math.Abs(p.Y-MapMechanicVisuals.GroundLift-h)<=ReplayGroundDecal.Climb)return true;
            return false;
        }
        check(vertices.Length>0&&vertices.Length<90_000,"plaza barrier footprint is drawn within the overlay budget");
        check(vertices.All(OnFloor),"barrier footprint never rises onto walls or rock tops around the plaza");
        check(vertices.All(v=>Vector2.Distance(new(v.Position.X,v.Position.Z),new(60,0))<22.2f),"clipped barrier keeps the client range");
        check(Math.Abs(red.EstimatedHeight(60,0)-plaza)<.01f,"missing historical heights resolve to the standable floor");
    }
}
