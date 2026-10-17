using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Linq;
using System.Runtime.InteropServices;

namespace PvpStats.Analysis;

[StructLayout(LayoutKind.Sequential)]
public struct ReplayVertex {
    public Vector3 Position;
    public uint Color;
    public ReplayVertex(Vector3 position,uint color) { Position=position;Color=color; }
}

public sealed class ReplayMapMesh {
    public ReplayVertex[] Vertices { get; }
    public uint[] Indices { get; }
    public int OpaqueIndexCount { get; }
    public Vector3 Min { get; }
    public Vector3 Max { get; }
    // The source is visual geometry, not a collision mesh. Used only for missing historical heights.
    private readonly float[] _ground=new float[256*256];
    private readonly Vector2 _groundMin;
    private const float Cell=1;
    public ReplayMapMesh(ReplayVertex[] vertices,uint[] indices) {
        Vertices=vertices;
        // Keep visual-only fog/light shafts out of the ground estimator and opaque depth pass.
        var solid=new System.Collections.Generic.List<uint>();
        var translucent=new System.Collections.Generic.List<uint>();
        for(int i=0;i<indices.Length;i+=3) {
            var target=(vertices[indices[i]].Color>>24)==255&&(vertices[indices[i+1]].Color>>24)==255&&(vertices[indices[i+2]].Color>>24)==255?solid:translucent;
            target.Add(indices[i]);target.Add(indices[i+1]);target.Add(indices[i+2]);
        }
        OpaqueIndexCount=solid.Count;
        // The replay camera stays above the arena: lower fog precedes raised light shafts.
        foreach(var i in Enumerable.Range(0,translucent.Count/3).OrderBy(i=>
            (vertices[translucent[i*3]].Position.Y+vertices[translucent[i*3+1]].Position.Y+vertices[translucent[i*3+2]].Position.Y)/3)) {
            solid.Add(translucent[i*3]);solid.Add(translucent[i*3+1]);solid.Add(translucent[i*3+2]);
        }
        Indices=solid.ToArray();
        indices=Indices;
        var min=new Vector3(float.PositiveInfinity);var max=new Vector3(float.NegativeInfinity);
        foreach(var v in vertices) { min=Vector3.Min(min,v.Position);max=Vector3.Max(max,v.Position); }
        Min=min;Max=max;_groundMin=new((min.X+max.X)/2-128,(min.Z+max.Z)/2-128);
        Array.Fill(_ground,float.NaN);
        for(int i=0;i<OpaqueIndexCount;i+=3) {
            var a=vertices[indices[i]].Position;var b=vertices[indices[i+1]].Position;var c=vertices[indices[i+2]].Position;
            var n=Vector3.Cross(b-a,c-a);if(MathF.Abs(n.Y)<n.Length()*.65f||MathF.Abs(n.Y)<.0001f)continue;
            int x0=Math.Clamp((int)MathF.Floor(MathF.Min(a.X,MathF.Min(b.X,c.X))-_groundMin.X),0,255);
            int x1=Math.Clamp((int)MathF.Ceiling(MathF.Max(a.X,MathF.Max(b.X,c.X))-_groundMin.X),0,255);
            int z0=Math.Clamp((int)MathF.Floor(MathF.Min(a.Z,MathF.Min(b.Z,c.Z))-_groundMin.Y),0,255);
            int z1=Math.Clamp((int)MathF.Ceiling(MathF.Max(a.Z,MathF.Max(b.Z,c.Z))-_groundMin.Y),0,255);
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++) {
                float px=_groundMin.X+x+.5f,pz=_groundMin.Y+z+.5f;
                float den=(b.Z-c.Z)*(a.X-c.X)+(c.X-b.X)*(a.Z-c.Z);
                if(MathF.Abs(den)<.00001f)continue;
                float u=((b.Z-c.Z)*(px-c.X)+(c.X-b.X)*(pz-c.Z))/den;
                float v=((c.Z-a.Z)*(px-c.X)+(a.X-c.X)*(pz-c.Z))/den;
                if(u<-.001f||v<-.001f||u+v>1.001f)continue;
                float y=u*a.Y+v*b.Y+(1-u-v)*c.Y;int at=z*256+x;
                if(!float.IsFinite(_ground[at])||y>_ground[at])_ground[at]=y;
            }
        }
    }
    public float EstimatedHeight(float x,float z) {
        int ix=(int)MathF.Floor((x-_groundMin.X)/Cell),iz=(int)MathF.Floor((z-_groundMin.Y)/Cell);
        if(ix>=0&&ix<256&&iz>=0&&iz<256&&float.IsFinite(_ground[iz*256+ix]))return _ground[iz*256+ix];
        return 0;
    }
    public static ReplayMapMesh Read(Stream compressed) {
        using var gzip=new GZipStream(compressed,CompressionMode.Decompress,leaveOpen:true);
        using var reader=new BinaryReader(gzip);
        if(reader.ReadUInt32()!=0x344D4343)throw new InvalidDataException("Invalid replay mesh header.");
        int vc=reader.ReadInt32(),ic=reader.ReadInt32();
        if(vc<3||vc>2_000_000||ic<3||ic>6_000_000||ic%3!=0)throw new InvalidDataException("Replay mesh size limit.");
        var vertices=new ReplayVertex[vc];var indices=new uint[ic];
        for(int i=0;i<vc;i++) {
            var p=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
            if(!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z)||p.LengthSquared()>100_000_000)throw new InvalidDataException("Invalid replay vertex.");
            vertices[i]=new(p,reader.ReadUInt32());
        }
        for(int i=0;i<ic;i++) { indices[i]=reader.ReadUInt32();if(indices[i]>=vc)throw new InvalidDataException("Invalid replay index."); }
        if(gzip.ReadByte()!=-1)throw new InvalidDataException("Unexpected replay mesh trailing data.");
        return new(vertices,indices);
    }
    public static string? MapKey(uint territory) => territory switch {
        1032 or 1058=>"palaistra",1033 or 1059=>"volcanic-heart",1034 or 1060=>"cloud-nine",
        1116 or 1117=>"castletown",1138 or 1139=>"red-sands",1293 or 1294=>"bayside",1357 or 1358=>"harmonias",_=>null
    };
    public static ReplayMapMesh LoadEmbedded(uint territory) {
        var key=MapKey(territory)??throw new InvalidDataException("此区域尚未提供三维地图。");
        using var stream=typeof(ReplayMapMesh).Assembly.GetManifestResourceStream("PvpStats.Resources.ReplayMaps."+key+".ccmap.gz")
            ??throw new FileNotFoundException("三维地图资源缺失。");
        return Read(stream);
    }
}
