using System;
using System.Collections.Generic;
using System.Numerics;

namespace PvpStats.Analysis;

// Projects flat XZ footprints (skill and arena-mechanic areas) onto the ground.
// With a walk grid, a footprint is clipped to standable floor cells: it never climbs walls, rock tops or
// canyon faces, and gaps such as pits stay empty. Each cell uses the floor layer nearest the reference height.
// Without one (tests, missing resource), a supplied height function is sampled on a 1-unit lattice.
public sealed class ReplayGroundDecal {
    public const float Climb=.6f;
    private const int MaxCells=160_000;
    private readonly ReplayWalkGrid? _grid;
    private readonly Func<float,float,float>? _height;
    private readonly float _reference;
    private readonly Vector2 _origin;
    private readonly Dictionary<long,float> _cells=new();
    public float Cell { get; }
    public float Lift { get; }

    private ReplayGroundDecal(ReplayWalkGrid? grid,Func<float,float,float>? height,float reference,float lift,float cell,Vector2 origin) {
        _grid=grid;_height=height;_reference=reference;Lift=lift;Cell=cell;_origin=origin;
    }
    public static ReplayGroundDecal Walkable(ReplayWalkGrid grid,float referenceY,float lift)=>new(grid,null,referenceY,lift,grid.Cell,grid.Origin);
    public static ReplayGroundDecal Draped(Func<float,float,float> height,float lift,float cell=1)=>new(null,height,0,lift,cell,Vector2.Zero);
    public bool Clipped=>_grid!=null;

    // Height of a cell centre, NaN when no standable floor exists there.
    public float CellHeight(int x,int z) {
        long key=((long)z<<32)^(uint)x;
        if(_cells.TryGetValue(key,out var cached))return cached;
        float h;
        if(_grid!=null) h=_grid.Nearest(x,z,_reference,out var floor)?floor:float.NaN;
        else { h=_height!(_origin.X+(x+.5f)*Cell,_origin.Y+(z+.5f)*Cell);if(!float.IsFinite(h))h=float.NaN; }
        _cells[key]=h;return h;
    }
    // Shared corner height: neighbours on the same floor (within a step) are averaged so slopes stay seamless.
    private float Corner(int i,int j,float own) {
        if(_grid==null) { var h=_height!(_origin.X+i*Cell,_origin.Y+j*Cell);return float.IsFinite(h)?h:own; }
        float sum=0;int count=0;
        for(int dz=-1;dz<=0;dz++)for(int dx=-1;dx<=0;dx++) {
            var h=CellHeight(i+dx,j+dz);
            if(float.IsFinite(h)&&MathF.Abs(h-own)<=Climb) { sum+=h;count++; }
        }
        return count>0?sum/count:own;
    }
    public float SurfaceHeight(Vector2 p,out bool standable) {
        int x=(int)MathF.Floor((p.X-_origin.X)/Cell),z=(int)MathF.Floor((p.Y-_origin.Y)/Cell);
        var h=CellHeight(x,z);standable=float.IsFinite(h);if(!standable)return float.NaN;
        float c00=Corner(x,z,h),c10=Corner(x+1,z,h),c01=Corner(x,z+1,h),c11=Corner(x+1,z+1,h);
        float t=Math.Clamp((p.X-_origin.X)/Cell-x,0,1),s=Math.Clamp((p.Y-_origin.Y)/Cell-z,0,1);
        return (c00+(c10-c00)*t)*(1-s)+(c01+(c11-c01)*t)*s;
    }

    public void Quad(ReplaySceneGeometry geometry,Vector2 a,Vector2 b,Vector2 c,Vector2 d,uint color) {
        Span<Vector2> polygon=stackalloc Vector2[4];polygon[0]=a;polygon[1]=b;polygon[2]=c;polygon[3]=d;Fill(geometry,polygon,color);
    }
    public void Line(ReplaySceneGeometry geometry,Vector2 a,Vector2 b,float width,uint color) {
        var axis=b-a;if(axis.LengthSquared()<.00001f)return;
        var side=Vector2.Normalize(new Vector2(axis.Y,-axis.X))*width/2;
        Quad(geometry,a-side,a+side,b+side,b-side,color);
    }

    private static float Cross(Vector2 a,Vector2 b)=>a.X*b.Y-a.Y*b.X;

    // Fills a convex XZ polygon (either winding). Interior cells on one plane are merged along each row.
    public void Fill(ReplaySceneGeometry geometry,ReadOnlySpan<Vector2> polygon,uint color) {
        int n=polygon.Length;if(n<3||n>256)return;
        float area=0;var min=new Vector2(float.PositiveInfinity);var max=new Vector2(float.NegativeInfinity);
        for(int i=0;i<n;i++) {
            var p=polygon[i];if(!float.IsFinite(p.X)||!float.IsFinite(p.Y))return;
            area+=Cross(p,polygon[(i+1)%n]);min=Vector2.Min(min,p);max=Vector2.Max(max,p);
        }
        if(MathF.Abs(area)<1e-6f)return;
        float sign=area>0?1:-1;
        int x0=(int)MathF.Floor((min.X-_origin.X)/Cell),x1=(int)MathF.Floor((max.X-_origin.X)/Cell);
        int z0=(int)MathF.Floor((min.Y-_origin.Y)/Cell),z1=(int)MathF.Floor((max.Y-_origin.Y)/Cell);
        if(_grid!=null) { x0=Math.Max(x0,0);z0=Math.Max(z0,0);x1=Math.Min(x1,_grid.Width-1);z1=Math.Min(z1,_grid.Depth-1); }
        if(x1<x0||z1<z0||(long)(x1-x0+1)*(z1-z0+1)>MaxCells)return;
        Span<Vector2> a=stackalloc Vector2[n+8];Span<Vector2> b=stackalloc Vector2[n+8];
        for(int z=z0;z<=z1;z++) {
            float zA=_origin.Y+z*Cell,zB=zA+Cell;
            int runStart=-1;float r00=0,r01=0,slopeLow=0,slopeHigh=0,end10=0,end11=0;
            void Flush(int last) {
                if(runStart<0)return;
                float xA=_origin.X+runStart*Cell,xB=_origin.X+(last+1)*Cell;
                Emit(geometry,new(xA,r00,zA),new(xB,end10,zA),new(xB,end11,zB),new(xA,r01,zB),color);runStart=-1;
            }
            for(int x=x0;x<=x1;x++) {
                var h=CellHeight(x,z);
                if(!float.IsFinite(h)) { Flush(x-1);continue; }
                float xA=_origin.X+x*Cell,xB=xA+Cell;
                float c00=Corner(x,z,h),c10=Corner(x+1,z,h),c01=Corner(x,z+1,h),c11=Corner(x+1,z+1,h);
                bool inside=Inside(polygon,sign,new(xA,zA))&&Inside(polygon,sign,new(xB,zA))&&Inside(polygon,sign,new(xB,zB))&&Inside(polygon,sign,new(xA,zB));
                if(inside) {
                    if(runStart>=0&&MathF.Abs(c00-end10)<.001f&&MathF.Abs(c01-end11)<.001f&&MathF.Abs(c10-c00-slopeLow)<.001f&&MathF.Abs(c11-c01-slopeHigh)<.001f) {
                        end10=c10;end11=c11;continue;
                    }
                    Flush(x-1);runStart=x;r00=c00;r01=c01;end10=c10;end11=c11;slopeLow=c10-c00;slopeHigh=c11-c01;continue;
                }
                Flush(x-1);
                // Boundary cell: clip the cell square by every polygon edge (Sutherland-Hodgman).
                a[0]=new(xA,zA);a[1]=new(xB,zA);a[2]=new(xB,zB);a[3]=new(xA,zB);int count=4;
                for(int e=0;e<n&&count>=3;e++) {
                    var p=polygon[e];var q=polygon[(e+1)%n];var edge=q-p;int next=0;
                    for(int k=0;k<count;k++) {
                        var s=a[k];var t=a[(k+1)%count];
                        float ds=sign*Cross(edge,s-p),dt=sign*Cross(edge,t-p);
                        if(ds>=0)b[next++]=s;
                        if((ds>=0)!=(dt>=0))b[next++]=s+(t-s)*(ds/(ds-dt));
                    }
                    b[..next].CopyTo(a);count=next;
                }
                if(count<3)continue;
                float clipped=0;for(int k=0;k<count;k++)clipped+=Cross(a[k],a[(k+1)%count]);
                if(MathF.Abs(clipped)<1e-5f)continue; // edge lying exactly on a cell line
                Vector3 Lift3(Vector2 p) {
                    float t=Math.Clamp((p.X-xA)/Cell,0,1),s=Math.Clamp((p.Y-zA)/Cell,0,1);
                    return new(p.X,(c00+(c10-c00)*t)*(1-s)+(c01+(c11-c01)*t)*s+Lift,p.Y);
                }
                var first=Lift3(a[0]);
                for(int k=1;k+1<count;k++)geometry.Triangle(first,Lift3(a[k]),Lift3(a[k+1]),color);
            }
            Flush(x1);
        }
    }
    private static bool Inside(ReadOnlySpan<Vector2> polygon,float sign,Vector2 p) {
        for(int i=0;i<polygon.Length;i++) {
            var a=polygon[i];var b=polygon[(i+1)%polygon.Length];
            if(sign*Cross(b-a,p-a)<-1e-5f)return false;
        }
        return true;
    }
    private void Emit(ReplaySceneGeometry geometry,Vector3 a,Vector3 b,Vector3 c,Vector3 d,uint color) {
        var up=new Vector3(0,Lift,0);
        geometry.Triangle(a+up,b+up,c+up,color);geometry.Triangle(a+up,c+up,d+up,color);
    }
}
