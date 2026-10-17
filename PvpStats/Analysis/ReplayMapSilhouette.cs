using System;
using System.Numerics;

namespace PvpStats.Analysis;

// The game's own area map (ui/map/<id>_m.tex) draws the playable arena as a light silhouette on a darker
// parchment background, with walls, rock fields and out-of-bounds areas in the background colour.
// Floors in the walk grid outside that silhouette (for example dunes behind a plaza wall that the display
// mesh connects through a gap) are removed. Sampled 2026-10-18 from Palaistra, Volcanic Heart and Cloud Nine:
// interior about (240,235,214), background about (230,221,181).
public sealed class ReplayMapSilhouette {
    private readonly int _width,_height;
    private readonly bool[] _inside;
    private readonly float _scale;
    private readonly Vector2 _offset;

    public ReplayMapSilhouette(int width,int height,ReadOnlySpan<byte> pixels,int sizeFactor,int offsetX,int offsetY) {
        if(width<64||height<64||width>8192||height>8192||pixels.Length<width*height*4||sizeFactor<=0)throw new ArgumentException("Invalid area map texture.");
        _width=width;_height=height;_inside=new bool[width*height];
        int count=0;
        for(int i=0;i<_inside.Length;i++) {
            byte c0=pixels[i*4],c1=pixels[i*4+1],c2=pixels[i*4+2];
            _inside[i]=Interior(c0,c1,c2);if(_inside[i])count++;
        }
        // A texture without a recognisable silhouette must not erase every floor.
        if(count<_inside.Length/200||count>_inside.Length/2)throw new ArgumentException("Area map silhouette not recognised.");
        _scale=sizeFactor/100f;_offset=new(offsetX,offsetY);
    }
    // Symmetric in the first and third channel, so BGRA and RGBA data classify identically.
    public static bool Interior(byte c0,byte c1,byte c2)=>Math.Min(c0,c2)>=200&&c0+c1+c2>=660;

    // World position to texture pixel, as DetailedAnalysis.WorldToUv scaled to the texture size.
    public bool Inside(float x,float z) {
        float px=((x+_offset.X)*_scale+1024)/2048*_width,py=((z+_offset.Y)*_scale+1024)/2048*_height;
        int cx=(int)MathF.Floor(px),cy=(int)MathF.Floor(py);
        // One-pixel tolerance: thin lane markings inside the arena are drawn darker than the floor.
        for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++) {
            int x0=cx+dx,y0=cy+dy;
            if(x0>=0&&y0>=0&&x0<_width&&y0<_height&&_inside[y0*_width+x0])return true;
        }
        return false;
    }
}
