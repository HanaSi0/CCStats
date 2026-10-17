using System;
using System.Numerics;

namespace PvpStats.Analysis;

// Ground-plane picking keeps the world point under the mouse fixed during pan/zoom.
public sealed class ReplayOrbitCamera {
    public Vector3 Target { get; set; }
    public float Yaw { get; private set; }=.65f;
    public float Pitch { get; private set; }=.85f;
    public float Distance { get; private set; }=190;
    public float DesiredDistance { get; private set; }=190;
    public bool Dragging { get; private set; }
    private bool _orbit,_haveAnchor;
    private Vector2 _lastPixel,_zoomPixel;
    private Vector3 _anchor;
    public void Reset(Vector3 target,float distance) {
        Target=target;Distance=DesiredDistance=Math.Clamp(distance,12,600);Yaw=.65f;Pitch=.85f;Dragging=false;
    }
    public Matrix4x4 ViewProjection(Vector2 size) {
        var eye=Target+new Vector3(MathF.Sin(Yaw)*MathF.Cos(Pitch),MathF.Sin(Pitch),MathF.Cos(Yaw)*MathF.Cos(Pitch))*Distance;
        return Matrix4x4.CreateLookAt(eye,Target,Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(.82f,Math.Max(1,size.X)/Math.Max(1,size.Y),.2f,3000);
    }
    public bool GroundPoint(Vector2 pixel,Vector2 size,out Vector3 point) {
        point=default;
        float x=2*pixel.X/Math.Max(1,size.X)-1,y=1-2*pixel.Y/Math.Max(1,size.Y);
        var offset=new Vector3(MathF.Sin(Yaw)*MathF.Cos(Pitch),MathF.Sin(Pitch),MathF.Cos(Yaw)*MathF.Cos(Pitch));
        var start=Target+offset*Distance;var forward=-offset;
        var right=Vector3.Normalize(Vector3.Cross(forward,Vector3.UnitY));var up=Vector3.Cross(right,forward);
        float tangent=MathF.Tan(.82f/2);var ray=Vector3.Normalize(forward+right*x*tangent*(Math.Max(1,size.X)/Math.Max(1,size.Y))+up*y*tangent);
        if(MathF.Abs(ray.Y)<.00001f)return false;
        float t=-start.Y/ray.Y;if(t<0||t>10000)return false;
        point=start+ray*t;point.Y=0;
        return float.IsFinite(point.X)&&float.IsFinite(point.Z)&&point.LengthSquared()<100_000_000;
    }
    public void BeginDrag(bool orbit,Vector2 pixel,Vector2 size) {
        Dragging=true;_orbit=orbit;_lastPixel=pixel;DesiredDistance=Distance;
        _haveAnchor=GroundPoint(pixel,size,out _anchor);if(!_haveAnchor)_anchor=Target;
    }
    public bool Drag(Vector2 pixel,Vector2 size) {
        if(!Dragging)return false;
        var delta=pixel-_lastPixel;_lastPixel=pixel;if(delta.LengthSquared()<.000001f)return false;
        if(_orbit) {
            float yaw=Yaw-delta.X*.008f,pitch=Math.Clamp(Pitch+delta.Y*.008f,.12f,1.52f);
            var old=Matrix4x4.CreateRotationX(-Pitch)*Matrix4x4.CreateRotationY(Yaw);
            var next=Matrix4x4.CreateRotationX(-pitch)*Matrix4x4.CreateRotationY(yaw);
            var rotation=Matrix4x4.Transpose(old)*next;
            // Rotate both the eye and look target about the picked pivot, without snapping it to the center.
            Target=_anchor+Vector3.TransformNormal(Target-_anchor,rotation);Yaw=yaw;Pitch=pitch;
        } else if(_haveAnchor&&GroundPoint(pixel,size,out var current))Target+=_anchor-current;
        else {
            var right=new Vector3(MathF.Cos(Yaw),0,-MathF.Sin(Yaw));var ahead=new Vector3(MathF.Sin(Yaw),0,MathF.Cos(Yaw));
            Target+=(-delta.X*right+delta.Y*ahead)*(Distance/Math.Max(100,size.Y));
        }
        return true;
    }
    public void EndDrag()=>Dragging=false;
    public void Zoom(float wheel,Vector2 pixel,Vector2 size) {
        if(!float.IsFinite(wheel)||wheel==0)return;
        _zoomPixel=new(pixel.X/Math.Max(1,size.X),pixel.Y/Math.Max(1,size.Y));
        DesiredDistance=Math.Clamp(DesiredDistance*MathF.Pow(.85f,wheel),12,600);
    }
    public bool Update(float deltaSeconds,Vector2 size) {
        if(Dragging||!float.IsFinite(deltaSeconds)||deltaSeconds<=0||Math.Abs(DesiredDistance-Distance)<.0001f)return false;
        var pixel=_zoomPixel*size;bool anchored=GroundPoint(pixel,size,out var before);
        float next=float.Lerp(Distance,DesiredDistance,1-MathF.Exp(-18*Math.Min(.1f,deltaSeconds)));
        Distance=Math.Abs(next-DesiredDistance)<.01f?DesiredDistance:next;
        if(anchored&&GroundPoint(pixel,size,out var after))Target+=before-after;
        return true;
    }
}
