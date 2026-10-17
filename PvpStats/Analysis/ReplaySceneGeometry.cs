using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;

namespace PvpStats.Analysis;

public sealed class ReplaySceneGeometry {
    private readonly List<ReplayVertex> _vertices=new(16384);
    public ReadOnlySpan<ReplayVertex> Vertices => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices);
    public void Clear()=>_vertices.Clear();
    public static uint Rgba(float r,float g,float b,float alpha=1) => (uint)(Math.Clamp(r,0,1)*255)|((uint)(Math.Clamp(g,0,1)*255)<<8)|((uint)(Math.Clamp(b,0,1)*255)<<16)|((uint)(Math.Clamp(alpha,0,1)*255)<<24);
    public void Triangle(Vector3 a,Vector3 b,Vector3 c,uint color) { _vertices.Add(new(a,color));_vertices.Add(new(b,color));_vertices.Add(new(c,color)); }
    public void Ribbon(Vector3 a,Vector3 b,float width,uint color) {
        var axis=b-a;var right=new Vector3(-axis.Z,0,axis.X);if(right.LengthSquared()<.00001f)return;
        right=Vector3.Normalize(right)*width/2;a.Y+=.06f;b.Y+=.06f;
        Triangle(a-right,a+right,b+right,color);Triangle(a-right,b+right,b-right,color);
    }
    public void Beam(Vector3 a,Vector3 b,float width,uint color) {
        var axis=b-a;if(axis.LengthSquared()<.00001f)return;
        axis=Vector3.Normalize(axis);
        var right=Vector3.Cross(axis,Vector3.UnitY);
        if(right.LengthSquared()<.00001f)right=Vector3.Cross(axis,Vector3.UnitZ);
        right=Vector3.Normalize(right)*width/2;
        var other=Vector3.Normalize(Vector3.Cross(axis,right))*width/2;
        foreach(var side in new[]{right,other}) {
            Triangle(a-side,a+side,b+side,color);Triangle(a-side,b+side,b-side,color);
        }
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
    public void GuardBubble(Vector3 basePoint) {
        var center=basePoint+new Vector3(0,1.4f,0);const float radius=1.9f;
        var fill=Rgba(.2f,.65f,1,.14f);var edge=Rgba(.35f,.85f,1,.8f);
        Vector3 Point(float latitude,float longitude)=>center+new Vector3(MathF.Cos(latitude)*MathF.Sin(longitude),MathF.Sin(latitude),MathF.Cos(latitude)*MathF.Cos(longitude))*radius;
        for(int y=0;y<12;y++)for(int x=0;x<24;x++) {
            float a=-MathF.PI/2+MathF.PI*y/12,b=-MathF.PI/2+MathF.PI*(y+1)/12,c=MathF.Tau*x/24,d=MathF.Tau*(x+1)/24;
            Triangle(Point(a,c),Point(b,c),Point(b,d),fill);Triangle(Point(a,c),Point(b,d),Point(a,d),fill);
        }
        Ring(center,radius,.075f,edge);
        for(int axis=0;axis<2;axis++)for(int i=0;i<64;i++) {
            float a=MathF.Tau*i/64,b=MathF.Tau*(i+1)/64;
            Vector3 P(float angle,float r)=>center+(axis==0?new Vector3(MathF.Sin(angle),MathF.Cos(angle),0):new Vector3(0,MathF.Cos(angle),MathF.Sin(angle)))*r;
            Triangle(P(a,radius-.025f),P(a,radius+.025f),P(b,radius+.025f),edge);
            Triangle(P(a,radius-.025f),P(b,radius+.025f),P(b,radius-.025f),edge);
        }
    }
    public static bool GuardVisible(PlayerPosition player,DateTime now) => player.HP>0&&player.StatusTimestamp<=now&&player.Statuses.Any(s=>s.Id is 3054 or 3673&&(s.Remaining<=0||s.Remaining>(now-player.StatusTimestamp).TotalSeconds));
    public void Pawn(Vector3 at,float heading,bool dead,bool selected,int team) {
        var color=dead?Rgba(.45f,.46f,.49f):team==1?Rgba(.05f,.65f,1):Rgba(1,.12f,.2f);
        var basePoint=at+new Vector3(0,.2f,0);var apex=at+new Vector3(0,2.7f,0);
        var corners=new Vector3[3];
        for(int i=0;i<3;i++) {
            var angle=heading+i*MathF.Tau/3;
            corners[i]=basePoint+new Vector3(MathF.Sin(angle),0,MathF.Cos(angle))*.9f;
        }
        for(int i=0;i<3;i++) {
            var face=dead?color:i==0?(team==1?Rgba(.18f,.82f,1):Rgba(1,.32f,.38f)):i==1?color:Rgba((color&255)/255f*.88f,((color>>8)&255)/255f*.88f,((color>>16)&255)/255f*.88f);
            Triangle(corners[i],corners[(i+1)%3],apex,face);
        }
        Triangle(corners[0],corners[2],corners[1],color);
        if(dead) { Ribbon(at+new Vector3(-.7f,.25f,-.7f),at+new Vector3(.7f,.25f,.7f),.22f,Rgba(1,.2f,.2f));Ribbon(at+new Vector3(-.7f,.25f,.7f),at+new Vector3(.7f,.25f,-.7f),.22f,Rgba(1,.2f,.2f));return; }
        var direction=new Vector3(MathF.Sin(heading),0,MathF.Cos(heading));var side=new Vector3(direction.Z,0,-direction.X);
        var ground=at+new Vector3(0,.16f,0);
        // Bright arrow outside the pawn silhouette, with a dark backing and visible shaft.
        var outline=Rgba(.025f,.03f,.05f);var arrow=Rgba(1,.9f,.4f);
        Triangle(ground+direction*3.25f,ground+direction*1.55f+side*.8f,ground+direction*1.55f-side*.8f,outline);
        Ribbon(ground+direction*.85f,ground+direction*2.05f,.48f,outline);
        var raised=ground+new Vector3(0,.025f,0);
        Triangle(raised+direction*3.05f,raised+direction*1.7f+side*.57f,raised+direction*1.7f-side*.57f,arrow);
        Ribbon(raised+direction*1,raised+direction*2,.25f,arrow);
        if(selected)Ring(ground,1.25f,.15f,Rgba(1,.8f,.25f));
    }
}
