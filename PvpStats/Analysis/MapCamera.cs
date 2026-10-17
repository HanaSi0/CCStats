using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
namespace PvpStats.Analysis;
public sealed class MapCamera {
    public Vector2 Center { get; private set; } = new(.5f);
    public float Zoom { get; private set; } = 1;
    private Vector2 _initialCenter=new(.5f);
    private float _initialZoom=1;
    public void Fit(IEnumerable<Vector2> source) {
        var points=source.Where(v=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&v.X>=0&&v.X<=1&&v.Y>=0&&v.Y<=1).ToArray();
        if(points.Length>0) {
            var min=new Vector2(points.Min(p=>p.X),points.Min(p=>p.Y)); var max=new Vector2(points.Max(p=>p.X),points.Max(p=>p.Y));
            Center=(min+max)/2; Zoom=Math.Clamp(.82f/Math.Max(.20f,Math.Max(max.X-min.X,max.Y-min.Y)),1,4);
        }
        _initialCenter=Center; _initialZoom=Zoom;
    }
    public void Reset() { Center=_initialCenter; Zoom=_initialZoom; }
    public void FullMap() { Center=new(.5f); Zoom=1; }
    public Vector2 Project(Vector2 uv,float side) => ((uv-Center)*Zoom+new Vector2(.5f))*side;
    public Vector2 Unproject(Vector2 pixel,float side) => Center+(pixel/side-new Vector2(.5f))/Zoom;
    public void ZoomAt(float factor,Vector2 pixel,float side) {
        if(side<=0||!float.IsFinite(factor)||factor<=0)return;
        var anchor=Unproject(pixel,side); Zoom=Math.Clamp(Zoom*factor,.5f,8);
        Center=anchor-(pixel/side-new Vector2(.5f))/Zoom;
    }
    public void Pan(Vector2 pixels,float side) { if(side>0&&float.IsFinite(pixels.X)&&float.IsFinite(pixels.Y))Center-=pixels/(side*Zoom); }
}
