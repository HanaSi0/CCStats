using System;
using System.Numerics;

namespace PvpStats.Analysis;

// Display-only surface footprint. This is deliberately separate from collision and hit detection.
public sealed class ReplayArenaMask {
    public const float Cell=.5f;
    public Vector2 Origin { get; }
    public int Width { get; }
    public int Height { get; }
    private readonly bool[] _cells;
    private readonly int[] _sum;
    private readonly float[]? _heights;
    // Standable floor layers (official-silhouette walk grid). When present, footprints rest on the layer nearest
    // the event's own elevation, so they never climb walls, rock tops or bridge railings.
    private readonly ReplayWalkGrid? _walk;
    public bool Layered => _walk!=null;
    public int CoveredCells { get; }
    public ReplayArenaMask(Vector2 origin,int width,int height,bool[] cells,float[]? heights=null) {
        if(width<1||height<1||width>1024||height>1024||cells.Length!=width*height)throw new ArgumentException("Invalid arena footprint.");
        if(heights!=null&&heights.Length!=cells.Length)throw new ArgumentException("Invalid surface heights.");
        _heights=heights;Origin=origin;Width=width;Height=height;_cells=(bool[])cells.Clone();_sum=new int[(width+1)*(height+1)];
        int covered=0;
        for(int z=0;z<height;z++)for(int x=0;x<width;x++) {
            int value=_cells[z*width+x]?1:0;covered+=value;
            _sum[(z+1)*(width+1)+x+1]=value+_sum[z*(width+1)+x+1]+_sum[(z+1)*(width+1)+x]-_sum[z*(width+1)+x];
        }
        CoveredCells=covered;
    }
    private ReplayArenaMask(ReplayWalkGrid walk,bool[] cells):this(walk.Origin,walk.Width,walk.Depth,cells) { _walk=walk; }
    public static ReplayArenaMask? FromWalk(ReplayWalkGrid? walk) {
        if(walk==null||MathF.Abs(walk.Cell-Cell)>.0001f||walk.Width>1024||walk.Depth>1024)return null;
        var cells=new bool[walk.Width*walk.Depth];
        for(int z=0;z<walk.Depth;z++)for(int x=0;x<walk.Width;x++)cells[z*walk.Width+x]=walk.Layers(x,z).Length>0;
        var mask=new ReplayArenaMask(walk,cells);
        return mask.CoveredCells>0?mask:null;
    }
    public bool Contains(float x,float z) => Inside((int)MathF.Floor((x-Origin.X)/Cell),(int)MathF.Floor((z-Origin.Y)/Cell));
    private bool Inside(int x,int z)=>x>=0&&z>=0&&x<Width&&z<Height&&_cells[z*Width+x];
    private int Count(int x0,int z0,int x1,int z1) {
        x0=Math.Clamp(x0,0,Width);x1=Math.Clamp(x1,0,Width);z0=Math.Clamp(z0,0,Height);z1=Math.Clamp(z1,0,Height);
        return _sum[z1*(Width+1)+x1]-_sum[z0*(Width+1)+x1]-_sum[z1*(Width+1)+x0]+_sum[z0*(Width+1)+x0];
    }
    public static ReplayArenaMask? Build(ReplayMapMesh mesh,uint territory) {
        var band=ReplayMapMesh.MapKey(territory) switch {
            "palaistra"=>(-.6f,5.6f),"volcanic-heart"=>(-2.3f,.8f),"cloud-nine"=>(-1f,3.1f),
            "castletown"=>(-1.6f,1.6f),"red-sands"=>(-3f,3.7f),"bayside"=>(-13f,1f),"harmonias"=>(-2.5f,6.6f),_=> (float.NaN,float.NaN)
        };
        if(!float.IsFinite(band.Item1))return null;
        var origin=new Vector2(MathF.Floor(mesh.Min.X/Cell)*Cell-Cell,MathF.Floor(mesh.Min.Z/Cell)*Cell-Cell);
        int width=(int)MathF.Ceiling((mesh.Max.X-origin.X)/Cell)+1,height=(int)MathF.Ceiling((mesh.Max.Z-origin.Y)/Cell)+1;
        if(width<1||height<1||width>1024||height>1024)return null;
        var raw=new bool[width*height];var heights=new float[raw.Length];Array.Fill(heights,float.NaN);
        for(int i=0;i<mesh.OpaqueIndexCount;i+=3) {
            var a=mesh.Vertices[mesh.Indices[i]].Position;var b=mesh.Vertices[mesh.Indices[i+1]].Position;var c=mesh.Vertices[mesh.Indices[i+2]].Position;
            if(MathF.Min(a.Y,MathF.Min(b.Y,c.Y))<band.Item1||MathF.Max(a.Y,MathF.Max(b.Y,c.Y))>band.Item2)continue;
            var n=Vector3.Cross(b-a,c-a);if(MathF.Abs(n.Y)<n.Length()*.72f||MathF.Abs(n.Y)<.00001f)continue;
            int x0=Math.Clamp((int)MathF.Floor((MathF.Min(a.X,MathF.Min(b.X,c.X))-origin.X)/Cell),0,width-1);
            int x1=Math.Clamp((int)MathF.Floor((MathF.Max(a.X,MathF.Max(b.X,c.X))-origin.X)/Cell),0,width-1);
            int z0=Math.Clamp((int)MathF.Floor((MathF.Min(a.Z,MathF.Min(b.Z,c.Z))-origin.Y)/Cell),0,height-1);
            int z1=Math.Clamp((int)MathF.Floor((MathF.Max(a.Z,MathF.Max(b.Z,c.Z))-origin.Y)/Cell),0,height-1);
            float den=(b.Z-c.Z)*(a.X-c.X)+(c.X-b.X)*(a.Z-c.Z);
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++) {
                float px=origin.X+(x+.5f)*Cell,pz=origin.Y+(z+.5f)*Cell;
                float u=((b.Z-c.Z)*(px-c.X)+(c.X-b.X)*(pz-c.Z))/den;
                float v=((c.Z-a.Z)*(px-c.X)+(a.X-c.X)*(pz-c.Z))/den;
                if(u>=-.001f&&v>=-.001f&&u+v<=1.001f) {
                    int at=z*width+x;raw[at]=true;float y=u*a.Y+v*b.Y+(1-u-v)*c.Y;
                    if(!float.IsFinite(heights[at])||y>heights[at])heights[at]=y;
                }
            }
        }
        // One-cell tolerance avoids shaving narrow ramps and the edge of raised platforms.
        var cells=(bool[])raw.Clone();var displayHeights=(float[])heights.Clone();
        for(int z=0;z<height;z++)for(int x=0;x<width;x++)if(raw[z*width+x])
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)if(x+dx>=0&&z+dz>=0&&x+dx<width&&z+dz<height){ int at=(z+dz)*width+x+dx;cells[at]=true;if(!raw[at])displayHeights[at]=heights[z*width+x]; }
        var result=new ReplayArenaMask(origin,width,height,cells,displayHeights);
        return result.CoveredCells>0?result:null;
    }
    // Floors farther than this from the effect's elevation belong to another level (sand under a platform).
    public const float MaxLayerDrop=4f;
    private Vector3 Surface(Vector3 p,float? reference,out bool onFloor) {
        onFloor=true;
        if(_walk!=null) {
            // Nearest layer to the event height (or the draped vertex height); boundary vertices may use a neighbour.
            float target=reference.HasValue&&float.IsFinite(reference.Value)?reference.Value:p.Y;
            int wx=(int)MathF.Floor((p.X-Origin.X)/Cell),wz=(int)MathF.Floor((p.Z-Origin.Y)/Cell);
            for(int radius=0;radius<=1;radius++) {
                float best=float.PositiveInfinity,height=float.NaN;
                for(int dz=-radius;dz<=radius;dz++)for(int dx=-radius;dx<=radius;dx++)
                    foreach(var h in _walk.Layers(wx+dx,wz+dz)) { var d=MathF.Abs(h-target);if(d<best) { best=d;height=h; } }
                // A vertex on a cell edge may sit next to a floor on another level; then try the neighbours.
                if(float.IsFinite(height)&&best<=MaxLayerDrop)return new(p.X,height+.24f,p.Z);
            }
            onFloor=false;return p;
        }
        if(_heights==null)return p;
        int x=Math.Clamp((int)MathF.Floor((p.X-Origin.X)/Cell),0,Width-1),z=Math.Clamp((int)MathF.Floor((p.Z-Origin.Y)/Cell),0,Height-1);
        // A boundary vertex belongs to both cells. Use an adjacent covered cell when it falls on an open side.
        for(int radius=0;radius<=1;radius++)for(int dz=-radius;dz<=radius;dz++)for(int dx=-radius;dx<=radius;dx++) {
            int sx=x+dx,sz=z+dz;
            if(Inside(sx,sz)&&float.IsFinite(_heights[sz*Width+sx]))return new(p.X,_heights[sz*Width+sx]+.24f,p.Z);
        }
        return p;
    }
    // Clip triangles, not just their centers: border fragments retain their true interpolated height/color.
    public void Clip(ReplaySceneGeometry output,Vector3 a,Vector3 b,Vector3 c,uint color,float? reference=null) {
        if(!LimitBreakVisuals.Finite(a)||!LimitBreakVisuals.Finite(b)||!LimitBreakVisuals.Finite(c))return;
        int x0=(int)MathF.Floor((MathF.Min(a.X,MathF.Min(b.X,c.X))-Origin.X)/Cell),x1=(int)MathF.Floor((MathF.Max(a.X,MathF.Max(b.X,c.X))-Origin.X)/Cell)+1;
        int z0=(int)MathF.Floor((MathF.Min(a.Z,MathF.Min(b.Z,c.Z))-Origin.Y)/Cell),z1=(int)MathF.Floor((MathF.Max(a.Z,MathF.Max(b.Z,c.Z))-Origin.Y)/Cell)+1;
        int count=Count(x0,z0,x1,z1);if(count==0)return;
        // Layered masks: a cell counts only when it has a floor on the effect's own level.
        float? level=_walk!=null?(reference.HasValue&&float.IsFinite(reference.Value)?reference.Value:(a.Y+b.Y+c.Y)/3):null;
        bool Covered(int x,int z)=>level.HasValue?OnLevel(x,z,level.Value):Inside(x,z);
        if(x0>=0&&z0>=0&&x1<=Width&&z1<=Height&&count==(x1-x0)*(z1-z0)) {
            bool all=true;
            if(level.HasValue)for(int z=z0;z<z1&&all;z++)for(int x=x0;x<x1;x++)if(!OnLevel(x,z,level.Value)) { all=false;break; }
            if(all) { Emit(output,a,b,c,color,reference);return; }
        }
        x0=Math.Clamp(x0,0,Width);x1=Math.Clamp(x1,0,Width);z0=Math.Clamp(z0,0,Height);z1=Math.Clamp(z1,0,Height);
        Span<Vector3> p=stackalloc Vector3[10];Span<Vector3> q=stackalloc Vector3[10];
        for(int z=z0;z<z1;z++)for(int x=x0;x<x1;) {
            if(!Covered(x,z)) {x++;continue;}
            int start=x++;while(x<x1&&Covered(x,z))x++;
            p[0]=a;p[1]=b;p[2]=c;int length=3;
            for(int plane=0;plane<4&&length>=3;plane++) {
                float bound=plane switch {0=>Origin.X+start*Cell,1=>Origin.X+x*Cell,2=>Origin.Y+z*Cell,_=>Origin.Y+(z+1)*Cell};
                bool useX=plane<2;float sign=plane%2==0?1:-1;int written=0;
                for(int j=0;j<length;j++) {
                    var v=p[j];var w=p[(j+1)%length];float dv=((useX?v.X:v.Z)-bound)*sign,dw=((useX?w.X:w.Z)-bound)*sign;
                    if(dv>=0)q[written++]=v;
                    if((dv>=0)!=(dw>=0))q[written++]=Vector3.Lerp(v,w,dv/(dv-dw));
                }
                length=written;q[..length].CopyTo(p);
            }
            for(int j=1;j<length-1;j++)if(Vector3.Cross(p[j]-p[0],p[j+1]-p[0]).LengthSquared()>1e-10f)Emit(output,p[0],p[j],p[j+1],color,reference);
        }
    }
    private bool OnLevel(int x,int z,float level) {
        foreach(var h in _walk!.Layers(x,z))if(MathF.Abs(h-level)<=MaxLayerDrop)return true;
        return false;
    }
    // Triangles touching a floor on another level are dropped instead of stretching down to it.
    private void Emit(ReplaySceneGeometry output,Vector3 a,Vector3 b,Vector3 c,uint color,float? reference) {
        var sa=Surface(a,reference,out var fa);var sb=Surface(b,reference,out var fb);var sc=Surface(c,reference,out var fc);
        if(fa&&fb&&fc)output.UnclippedTriangle(sa,sb,sc,color);
    }
}
