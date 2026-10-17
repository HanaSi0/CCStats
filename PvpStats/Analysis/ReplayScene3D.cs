using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
namespace PvpStats.Analysis;
public readonly record struct SceneTriangle(Vector3 A,Vector3 B,Vector3 C,Vector4 Color);
public sealed class ReplayScene3D {
    public List<SceneTriangle> Triangles { get; } = new();
    public List<string> AssetPaths { get; } = new();
    public int Models { get; set; }
    public int TerrainPlates { get; set; }
    public int MissingModels { get; set; }
    public bool Limited { get; set; }
    public string Status { get; set; } = "";
    public float? Ground(float x,float z) {
        float? best=null;
        foreach(var t in Triangles) {
            var den=(t.B.Z-t.C.Z)*(t.A.X-t.C.X)+(t.C.X-t.B.X)*(t.A.Z-t.C.Z);
            if(Math.Abs(den)<.0001f)continue;
            var a=((t.B.Z-t.C.Z)*(x-t.C.X)+(t.C.X-t.B.X)*(z-t.C.Z))/den;
            var b=((t.C.Z-t.A.Z)*(x-t.C.X)+(t.A.X-t.C.X)*(z-t.C.Z))/den;
            if(a<0||b<0||a+b>1)continue;
            var y=a*t.A.Y+b*t.B.Y+(1-a-b)*t.C.Y;
            if(!best.HasValue||y<best)best=y;
        }
        return best;
    }
    public static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
    public static Matrix4x4 Transform(Vector3 translation,Vector3 rotation,Vector3 scale) =>
        Matrix4x4.CreateScale(scale)*Matrix4x4.CreateRotationX(rotation.X)*Matrix4x4.CreateRotationY(rotation.Y)*Matrix4x4.CreateRotationZ(rotation.Z)*Matrix4x4.CreateTranslation(translation);
}
public sealed class ReplayCamera3D {
    public Vector3 Target { get; private set; }
    public float Yaw { get; private set; } = -.7f;
    public float Pitch { get; private set; } = .8f;
    public float Distance { get; private set; } = 100;
    private Vector3 _initialTarget;
    private float _initialDistance=100;
    public Vector3 Eye=>Target+new Vector3(MathF.Sin(Yaw)*MathF.Cos(Pitch),MathF.Sin(Pitch),MathF.Cos(Yaw)*MathF.Cos(Pitch))*Distance;
    public void Fit(IEnumerable<Vector3> input) {
        var points=input.Where(ReplayScene3D.Finite).ToArray();
        if(points.Length>0) {
            var min=points.Aggregate(Vector3.Min);var max=points.Aggregate(Vector3.Max);
            Target=(min+max)/2;Distance=Math.Clamp(Vector3.Distance(min,max)*1.1f,30,700);
        }
        _initialTarget=Target;_initialDistance=Distance;
    }
    public void Reset() { Target=_initialTarget;Distance=_initialDistance;Yaw=-.7f;Pitch=.8f; }
    public void Orbit(Vector2 delta) {
        if(!float.IsFinite(delta.X)||!float.IsFinite(delta.Y))return;
        Yaw-=delta.X*.008f;Pitch=Math.Clamp(Pitch+delta.Y*.008f,.12f,1.48f);
    }
    public void Zoom(float factor) { if(float.IsFinite(factor)&&factor>0)Distance=Math.Clamp(Distance*factor,8,1200); }
    public void Pan(Vector2 delta,float height) {
        if(height<=0||!float.IsFinite(height)||!float.IsFinite(delta.X)||!float.IsFinite(delta.Y))return;
        var right=new Vector3(MathF.Cos(Yaw),0,-MathF.Sin(Yaw));
        var forward=new Vector3(MathF.Sin(Yaw),0,MathF.Cos(Yaw));
        Target+=(-right*delta.X+forward*delta.Y)*(Distance/height);
    }
    public Matrix4x4 Matrix(float aspect)=>Matrix4x4.CreateLookAt(Eye,Target,Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(.9f,Math.Clamp(aspect,.05f,20),.5f,2500);
    public static bool Project(Vector3 point,Matrix4x4 matrix,Vector2 size,out Vector2 pixel,out float depth) {
        pixel=default;depth=0;if(!ReplayScene3D.Finite(point)||size.X<=0||size.Y<=0)return false;
        var clip=Vector4.Transform(new Vector4(point,1),matrix);
        if(!float.IsFinite(clip.W)||clip.W<=.01f)return false;
        var p=new Vector3(clip.X,clip.Y,clip.Z)/clip.W;
        if(!ReplayScene3D.Finite(p)||p.Z<0||p.Z>1)return false;
        pixel=new((p.X+1)*.5f*size.X,(1-p.Y)*.5f*size.Y);depth=p.Z;return float.IsFinite(pixel.X)&&float.IsFinite(pixel.Y);
    }
}
public static class ReplayPrimitives {
    public static void Pawn(List<SceneTriangle> output,Vector3 foot,Vector4 color) {
        var profile=new[]{new Vector2(0,.9f),new Vector2(.25f,1),new Vector2(.5f,.6f),new Vector2(1.6f,.38f),new Vector2(1.9f,.65f),new Vector2(2.45f,.35f),new Vector2(2.6f,0)};
        const int sides=10;
        for(int ring=0;ring<profile.Length-1;ring++)for(int i=0;i<sides;i++) {
            Vector3 P(Vector2 p,int j)=>foot+new Vector3(MathF.Cos(j*2*MathF.PI/sides)*p.Y,p.X,MathF.Sin(j*2*MathF.PI/sides)*p.Y);
            var a=P(profile[ring],i);var b=P(profile[ring],i+1);var c=P(profile[ring+1],i);var d=P(profile[ring+1],i+1);
            output.Add(new(a,b,c,color));output.Add(new(b,d,c,color));
        }
    }
    public static void Crystal(List<SceneTriangle> output,Vector3 foot,Vector4 color) {
        var middle=foot+Vector3.UnitY*2.7f;var top=foot+Vector3.UnitY*5.8f;var bottom=foot+Vector3.UnitY*.4f;
        for(int i=0;i<4;i++) {
            var a=middle+new Vector3(MathF.Cos(i*MathF.PI/2),0,MathF.Sin(i*MathF.PI/2))*1.3f;
            var b=middle+new Vector3(MathF.Cos((i+1)*MathF.PI/2),0,MathF.Sin((i+1)*MathF.PI/2))*1.3f;
            output.Add(new(top,a,b,color));output.Add(new(bottom,b,a,(color*.8f) with { W=1 }));
        }
    }
}
