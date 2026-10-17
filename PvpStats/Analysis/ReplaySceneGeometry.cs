using System;
using System.Collections.Generic;
using System.Numerics;

namespace PvpStats.Analysis;

public sealed class ReplaySceneGeometry {
    private readonly List<ReplayVertex> _vertices=new(16384);
    public ReadOnlySpan<ReplayVertex> Vertices => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices);
    public void Clear()=>_vertices.Clear();
    public static uint Rgba(float r,float g,float b) => (uint)(Math.Clamp(r,0,1)*255)|((uint)(Math.Clamp(g,0,1)*255)<<8)|((uint)(Math.Clamp(b,0,1)*255)<<16)|0xff000000;
    public void Triangle(Vector3 a,Vector3 b,Vector3 c,uint color) { _vertices.Add(new(a,color));_vertices.Add(new(b,color));_vertices.Add(new(c,color)); }
    public void Ribbon(Vector3 a,Vector3 b,float width,uint color) {
        var axis=b-a;var right=new Vector3(-axis.Z,0,axis.X);if(right.LengthSquared()<.00001f)return;
        right=Vector3.Normalize(right)*width/2;a.Y+=.06f;b.Y+=.06f;
        Triangle(a-right,a+right,b+right,color);Triangle(a-right,b+right,b-right,color);
    }
    public void Ring(Vector3 center,float radius,float width,uint color) {
        for(int i=0;i<64;i++) {
            float a=i*MathF.Tau/64,b=(i+1)*MathF.Tau/64;
            var da=new Vector3(MathF.Cos(a),0,MathF.Sin(a));var db=new Vector3(MathF.Cos(b),0,MathF.Sin(b));
            Triangle(center+da*(radius-width/2),center+da*(radius+width/2),center+db*(radius+width/2),color);
            Triangle(center+da*(radius-width/2),center+db*(radius+width/2),center+db*(radius-width/2),color);
        }
    }
    public void Cylinder(Vector3 at,float radius,float height,uint color) {
        var top=at+new Vector3(0,height,0);
        for(int i=0;i<12;i++) {
            float a=i*MathF.Tau/12,b=(i+1)*MathF.Tau/12;
            var da=new Vector3(MathF.Cos(a)*radius,0,MathF.Sin(a)*radius);var db=new Vector3(MathF.Cos(b)*radius,0,MathF.Sin(b)*radius);
            Triangle(at+da,at+db,top+db,color);Triangle(at+da,top+db,top+da,color);Triangle(top,top+da,top+db,color);
        }
    }
    public void Diamond(Vector3 bottom,float radius,float height,uint color) {
        var center=bottom+new Vector3(0,height*.45f,0);var top=bottom+new Vector3(0,height,0);
        for(int i=0;i<4;i++) {
            var a=center+new Vector3(MathF.Cos(i*MathF.Tau/4)*radius,0,MathF.Sin(i*MathF.Tau/4)*radius);
            var b=center+new Vector3(MathF.Cos((i+1)*MathF.Tau/4)*radius,0,MathF.Sin((i+1)*MathF.Tau/4)*radius);
            var shade=(i%2)==0?color:Rgba(((color&255)/255f)*.72f,(((color>>8)&255)/255f)*.72f,(((color>>16)&255)/255f)*.72f);
            Triangle(bottom,a,b,shade);Triangle(top,b,a,color);
        }
    }
    public void Pawn(Vector3 at,float heading,bool dead,bool selected,int team) {
        var color=dead?Rgba(.45f,.46f,.49f):team==1?Rgba(.1f,.7f,1):Rgba(1,.23f,.32f);
        Cylinder(at+new Vector3(0,.05f,0),.9f,.18f,color);
        if(dead) { Ribbon(at+new Vector3(-.7f,.25f,-.7f),at+new Vector3(.7f,.25f,.7f),.22f,Rgba(1,.2f,.2f));Ribbon(at+new Vector3(-.7f,.25f,.7f),at+new Vector3(.7f,.25f,-.7f),.22f,Rgba(1,.2f,.2f));return; }
        Cylinder(at+new Vector3(0,.2f,0),.5f,1.3f,color);Diamond(at+new Vector3(0,1.45f,0),.58f,1.25f,color);
        var direction=new Vector3(MathF.Sin(heading),0,MathF.Cos(heading));var side=new Vector3(direction.Z,0,-direction.X);
        var ground=at+new Vector3(0,.1f,0);Triangle(ground+direction*2,ground+direction*.9f+side*.4f,ground+direction*.9f-side*.4f,color);
        if(selected)Ring(ground,1.25f,.15f,Rgba(1,.8f,.25f));
    }
}
