using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;

namespace PvpStats.Analysis;

// Standable display surfaces on a regular XZ grid, built offline by tools/replay_walkable.py.
// A cell may hold several floor layers (bridge over path). Derived from the display mesh, not game collision.
public sealed class ReplayWalkGrid {
    public int Width { get; }
    public int Depth { get; }
    public Vector2 Origin { get; }
    public float Cell { get; }
    private readonly int[] _start;
    private readonly float[] _heights;
    public int LayerCount => _heights.Length;

    public ReplayWalkGrid(int width,int depth,Vector2 origin,float cell,ReadOnlySpan<byte> counts,float[] heights) {
        if(width<1||depth<1||width>8192||depth>8192||counts.Length!=width*depth)throw new InvalidDataException("Invalid walk grid size.");
        if(!(cell>=.05f&&cell<=4)||!float.IsFinite(origin.X)||!float.IsFinite(origin.Y)||origin.LengthSquared()>100_000_000)throw new InvalidDataException("Invalid walk grid frame.");
        Width=width;Depth=depth;Origin=origin;Cell=cell;_heights=heights;_start=new int[counts.Length+1];
        for(int i=0;i<counts.Length;i++)_start[i+1]=_start[i]+counts[i];
        if(_start[^1]!=heights.Length)throw new InvalidDataException("Walk grid layer count mismatch.");
        for(int i=0;i<counts.Length;i++)for(int k=_start[i];k<_start[i+1];k++) {
            var h=heights[k];
            if(!float.IsFinite(h)||MathF.Abs(h)>10_000||(k>_start[i]&&h<heights[k-1]))throw new InvalidDataException("Invalid walk grid height.");
        }
    }

    public static ReplayWalkGrid Read(Stream compressed) {
        using var gzip=new GZipStream(compressed,CompressionMode.Decompress,leaveOpen:true);
        using var reader=new BinaryReader(gzip);
        if(reader.ReadUInt32()!=0x31574343)throw new InvalidDataException("Invalid walk grid header.");
        int width=reader.ReadInt32(),depth=reader.ReadInt32();
        var origin=new Vector2(reader.ReadSingle(),reader.ReadSingle());var cell=reader.ReadSingle();
        if(width<1||depth<1||width>8192||depth>8192)throw new InvalidDataException("Invalid walk grid size.");
        var counts=reader.ReadBytes(width*depth);if(counts.Length!=width*depth)throw new InvalidDataException("Truncated walk grid.");
        long total=0;foreach(var c in counts)total+=c;
        if(total>4_000_000)throw new InvalidDataException("Walk grid size limit.");
        var heights=new float[total];
        for(int i=0;i<heights.Length;i++)heights[i]=reader.ReadSingle();
        if(gzip.ReadByte()!=-1)throw new InvalidDataException("Unexpected walk grid trailing data.");
        return new(width,depth,origin,cell,counts,heights);
    }

    public int CellX(float x)=>(int)MathF.Floor((x-Origin.X)/Cell);
    public int CellZ(float z)=>(int)MathF.Floor((z-Origin.Y)/Cell);
    public bool Contains(int x,int z)=>x>=0&&z>=0&&x<Width&&z<Depth;
    public ReadOnlySpan<float> Layers(int x,int z) {
        if(!Contains(x,z))return ReadOnlySpan<float>.Empty;
        int i=z*Width+x;return new ReadOnlySpan<float>(_heights,_start[i],_start[i+1]-_start[i]);
    }

    // Floor layer closest to a reference elevation (the caster, target or recorded event height).
    public bool Nearest(int x,int z,float reference,out float height) {
        height=float.NaN;var layers=Layers(x,z);float best=float.PositiveInfinity;
        foreach(var h in layers) { var d=MathF.Abs(h-reference);if(d<best) { best=d;height=h; } }
        return layers.Length>0;
    }
    public bool Highest(int x,int z,out float height) {
        var layers=Layers(x,z);height=layers.Length>0?layers[^1]:float.NaN;return layers.Length>0;
    }
    public bool NearestAt(float x,float z,float reference,out float height)=>Nearest(CellX(x),CellZ(z),reference,out height);
    // Searches outward ring by ring (cells hugging a wall have no standable floor of their own).
    // With a reference, the layer nearest it wins; otherwise the highest floor in the first ring that has one.
    public bool Around(float x,float z,float? reference,int radius,out float height) {
        int cx=CellX(x),cz=CellZ(z);height=float.NaN;float best=float.PositiveInfinity;
        for(int r=0;r<=radius;r++) {
            for(int dz=-r;dz<=r;dz++)for(int dx=-r;dx<=r;dx++) {
                if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=r)continue;
                foreach(var h in Layers(cx+dx,cz+dz)) {
                    float score=reference.HasValue?MathF.Abs(h-reference.Value):-h;
                    if(score<best) { best=score;height=h; }
                }
            }
            if(float.IsFinite(height))return true;
        }
        return false;
    }
}
