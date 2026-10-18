using System;
using System.Numerics;

namespace PvpStats.Analysis;

// Procedural replay symbols. Small object markers are NOT hitboxes. Damage footprints use MapMechanicVisuals.
public static class ArenaMechanicRenderer {
    public static Vector3 Tint(ArenaMechanicKind k)=>k switch {
        ArenaMechanicKind.Bomb or ArenaMechanicKind.BombCore=>new(1,.4f,.08f),
        ArenaMechanicKind.Feather=>new(.63f,.5f,1),ArenaMechanicKind.Twister or ArenaMechanicKind.Turbulence=>new(.65f,.56f,1),
        ArenaMechanicKind.GaleSphere or ArenaMechanicKind.SprintZone or ArenaMechanicKind.JumpGlyph=>new(.12f,.9f,1),
        ArenaMechanicKind.TrickDoor or ArenaMechanicKind.Onmyoji=>new(.95f,.28f,.75f),
        ArenaMechanicKind.Gold=>new(1,.8f,.16f),ArenaMechanicKind.Antlion or ArenaMechanicKind.ShiftingSands=>new(1,.65f,.18f),
        ArenaMechanicKind.Oasis or ArenaMechanicKind.HealingGlyph or ArenaMechanicKind.Sandpearl or ArenaMechanicKind.Acutender=>new(.2f,1,.58f),
        ArenaMechanicKind.Aetherometer or ArenaMechanicKind.AetherBridge=>new(.35f,.65f,1),
        ArenaMechanicKind.HeatWave=>new(1,.32f,.08f),ArenaMechanicKind.Geyser=>new(.25f,.85f,1),_=>new(1,.4f,.3f)
    };
    public static void Draw(ReplaySceneGeometry g,ArenaMechanicInstance instance,float time) {
        if(!LimitBreakVisuals.Finite(instance.Position)||!float.IsFinite(instance.Heading)||!float.IsFinite(time))return;
        var p=instance.Position+Vector3.UnitY*.18f;var rgb=Tint(instance.Kind);float phase=time-MathF.Floor(time);
        uint C(float a=1)=>ReplaySceneGeometry.Rgba(rgb.X,rgb.Y,rgb.Z,a);
        var white=ReplaySceneGeometry.Rgba(.9f,.96f,1,.9f);var dark=ReplaySceneGeometry.Rgba(.08f,.1f,.16f);
        var f=LimitBreakVisuals.Forward(instance.Heading);var right=new Vector3(f.Z,0,-f.X);
        Vector3 At(float x,float y,float z)=>p+right*x+Vector3.UnitY*y+f*z;
        void L(Vector3 a,Vector3 b,float w=.12f,uint? c=null)=>g.Beam(a,b,w,c??C());
        void R(float radius,float y=0,float opacity=.8f)=>g.Ring(p+Vector3.UnitY*y,radius,.1f,C(opacity));
        void Sphere(Vector3 center,float radius,uint color) {
            Vector3 V(float lat,float lon)=>center+new Vector3(MathF.Cos(lat)*MathF.Sin(lon),MathF.Sin(lat),MathF.Cos(lat)*MathF.Cos(lon))*radius;
            for(int j=0;j<6;j++)for(int i=0;i<12;i++) {
                float a=-MathF.PI/2+j*MathF.PI/6,b=a+MathF.PI/6,c=i*MathF.Tau/12,d=c+MathF.Tau/12;
                g.Triangle(V(a,c),V(b,c),V(b,d),color);g.Triangle(V(a,c),V(b,d),V(a,d),color);
            }
        }
        void Helix(float radius,float height,bool inward=false) {
            for(int strand=0;strand<2;strand++)for(int i=0;i<40;i++) {
                Vector3 V(float t) {var a=t*MathF.Tau*2+strand*MathF.PI-time*2;var r=radius*(inward?1-.75f*t:.2f+.8f*t);return p+new Vector3(MathF.Sin(a)*r,height*t,MathF.Cos(a)*r);}
                L(V(i/40f),V((i+1)/40f),.1f);
            }
        }
        void Cross(float radius,float y,uint color) {
            g.Beam(At(-radius,y,0),At(radius,y,0),.25f,color);g.Beam(At(0,y,-radius),At(0,y,radius),.25f,color);
        }
        void Arrow(Vector3 start,Vector3 axis,float length,float width=.3f) {
            var end=start+axis*length;var side=new Vector3(axis.Z,0,-axis.X);g.Ribbon(start,end,width,C());
            g.Triangle(end+axis*.6f,end-axis*.5f+side*.6f,end-axis*.5f-side*.6f,C());
        }
        switch(instance.Kind) {
            case ArenaMechanicKind.SprintZone:
            case ArenaMechanicKind.ShiftingSands:
                for(int i=-2;i<=2;i++) {
                    var s=At(0,.1f,i*1.3f+phase*1.3f);Arrow(s-f*.6f,f,.8f,.22f);
                }
                if(instance.Kind==ArenaMechanicKind.ShiftingSands)for(int i=0;i<20;i++) {
                    float t=i/20f,u=(i+1)/20f;L(At(MathF.Sin(t*12+time)*1.4f,.15f,-3.5f+t*7),At(MathF.Sin(u*12+time)*1.4f,.15f,-3.5f+u*7),.12f);
                }
                g.Ribbon(At(-1.6f,0,-3.5f),At(-1.6f,0,3.5f),.08f,C(.5f));g.Ribbon(At(1.6f,0,-3.5f),At(1.6f,0,3.5f),.08f,C(.5f));break;
            case ArenaMechanicKind.Bomb:
                Sphere(At(0,1.3f,0),1.15f,ReplaySceneGeometry.Rgba(.15f,.18f,.24f));R(1.2f,.3f);R(1.12f,1.3f,.45f);L(At(0,2.3f,0),At(.35f,3,0),.18f);g.Diamond(At(.35f,3,0),.3f,.6f,C());
                for(int i=0;i<5;i++) {float a=i*MathF.Tau/5+time;L(At(.35f,3.2f,0),At(.35f+MathF.Cos(a)*.8f,3.2f+MathF.Sin(a)*.8f,0),.07f); }break;
            case ArenaMechanicKind.BombCore:
                g.Diamond(At(0,1+.25f*MathF.Sin(time*3),0),.65f,1.8f,C());R(.7f+phase*.8f,.05f,1-phase);break;
            case ArenaMechanicKind.JumpGlyph:
                R(1.8f);R(1.3f,.1f);R(.5f+phase*1.2f,phase*1.6f,1-phase);
                for(int i=0;i<4;i++) {float a=i*MathF.Tau/4;var d=new Vector3(MathF.Sin(a),0,MathF.Cos(a));L(p+d*1.7f,p+d*2.2f+Vector3.UnitY*.8f,.15f);}break;
            case ArenaMechanicKind.Turbulence:
                Helix(2.6f,9);R(2.6f);for(int i=0;i<3;i++)R(1+i*.5f,phase*7+i,.5f);break;
            case ArenaMechanicKind.Feather:
                var tip=At(.7f,4.8f,.2f);var stem=At(-.6f,2.5f,0);L(stem,tip,.1f,white);
                for(int i=1;i<9;i++) {float t=i/10f;var q=Vector3.Lerp(stem,tip,t);var width=MathF.Sin(t*MathF.PI)*.8f;L(q,q+right*width+Vector3.UnitY*.25f,.16f);L(q,q-right*width+Vector3.UnitY*.25f,.16f);}R(1.2f,.08f,.5f);break;
            case ArenaMechanicKind.Twister:
                Helix(2.4f,5.5f);R(2.4f);break;
            case ArenaMechanicKind.GaleSphere:
                Sphere(At(0,2+.2f*MathF.Sin(time*3),0),.8f,C(.45f));R(1.2f,1.5f);R(.5f+phase,2.5f,1-phase);break;
            case ArenaMechanicKind.TrickFloor:
                // Tilt a symbolic tatami around one edge; this does not claim the floor's exact state/size.
                float angle=(.5f+.5f*MathF.Sin(time*2))*.9f;
                Vector3 Q(float x,float z)=>At(x,MathF.Sin(angle)*(z+2),MathF.Cos(angle)*z);
                var floorA=Q(-2,-2);var floorB=Q(2,-2);var floorC=Q(2,2);var floorD=Q(-2,2);
                g.Triangle(floorA,floorB,floorC,C(.3f));g.Triangle(floorA,floorC,floorD,C(.3f));L(floorA,floorB);L(floorB,floorC);L(floorC,floorD);L(floorD,floorA);for(int i=-1;i<=1;i++)L(Q(i,-2),Q(i,2),.07f);break;
            case ArenaMechanicKind.TrickDoor:
                L(At(-1.6f,0,0),At(-1.6f,4.8f,0),.3f);L(At(1.6f,0,0),At(1.6f,4.8f,0),.3f);L(At(-1.9f,4.8f,0),At(1.9f,4.8f,0),.3f);
                for(int i=0;i<32;i++) {float t=i/32f,u=(i+1)/32f;Vector3 P(float v)=>At(MathF.Sin(v*MathF.Tau+time)*v*1.3f,2.5f+MathF.Cos(v*MathF.Tau+time)*v*1.8f,0);L(P(t),P(u));}
                if(instance.Destination is {} exit&&LimitBreakVisuals.Finite(exit)) {g.Ribbon(p,exit,.12f,C(.4f));Arrow(exit-f*1.5f,f,1.2f);}break;
            case ArenaMechanicKind.Yojimbo:
                g.Cylinder(p,.7f,2.5f,C(.8f));g.Cylinder(At(0,2.8f,0),1.1f,.18f,C());
                L(At(.7f,1.5f,0),At(2.8f,1.5f,1.4f),.2f,white);R(1.4f);
                Arrow(At(0,.05f,1.6f),f,1.7f,.15f);break;
            case ArenaMechanicKind.Onmyoji:
                // A floating eye only. The facing-dependent reach is drawn by MapMechanicVisuals.
                for(int i=0;i<24;i++) {
                    float a=i*MathF.PI/24,b=(i+1)*MathF.PI/24;
                    foreach(int sign in new[]{-1,1})L(At(MathF.Cos(a)*1.8f,4.5f,sign*MathF.Sin(a)*.8f),At(MathF.Cos(b)*1.8f,4.5f,sign*MathF.Sin(b)*.8f),.16f,white);
                }
                g.Cylinder(At(0,4.45f,0),.5f,.16f,C());g.Cylinder(At(0,4.62f,0),.22f,.1f,dark);break;
            case ArenaMechanicKind.Gold:
                for(int i=0;i<6;i++) {float a=i*2.4f;g.Cylinder(At(MathF.Sin(a)*.65f,i*.1f,MathF.Cos(a)*.65f),.45f,.2f,C());}
                L(At(0,.2f,0),At(0,4,0),.08f,C(.7f));R(.5f+phase,.1f,1-phase);break;
            case ArenaMechanicKind.Antlion:
                R(2.4f);Helix(2.2f,.2f,true);
                for(int s=-1;s<=1;s+=2) {L(At(s*2,0,-1),At(s*1.2f,2,0),.45f);L(At(s*1.2f,2,0),At(s*.25f,1,1),.35f);}break;
            case ArenaMechanicKind.Sabotender:
            case ArenaMechanicKind.Acutender:
                g.Cylinder(p,.6f,3.5f,C(.85f));g.Cylinder(At(-1.3f,1.7f,0),.35f,1.2f,C());g.Cylinder(At(1.3f,1,0),.35f,1.5f,C());
                L(At(-1.3f,1.7f,0),At(0,1.7f,0),.6f);L(At(0,1,0),At(1.3f,1,0),.6f);
                for(int i=0;i<10;i++) {float an=i*MathF.Tau/10;var dir=new Vector3(MathF.Sin(an),0,MathF.Cos(an));L(p+dir*.8f+Vector3.UnitY*2,p+dir*1.3f+Vector3.UnitY*2,.06f,white);}
                if(instance.Kind==ArenaMechanicKind.Acutender) {
                    var top=At(0,4.8f,0);var left=At(-.8f,4.2f,0);var rightEdge=At(.8f,4.2f,0);var bottom=At(0,3.4f,0);
                    g.Triangle(top,left,bottom,C(.5f));g.Triangle(top,bottom,rightEdge,C(.5f));L(top,left,.13f,white);L(left,bottom,.13f,white);L(bottom,rightEdge,.13f,white);L(rightEdge,top,.13f,white);
                }break;
            case ArenaMechanicKind.Oasis:
            case ArenaMechanicKind.HealingGlyph:
            case ArenaMechanicKind.Sandpearl:
                R(2);R(1.4f);R(.3f+phase*1.7f,.1f,1-phase);Cross(.65f,.2f,C());
                if(instance.Kind==ArenaMechanicKind.Oasis) {
                    g.Cylinder(p,.55f,.1f,C(.3f));for(int i=0;i<4;i++){float a=i*MathF.Tau/4;var d=new Vector3(MathF.Sin(a),0,MathF.Cos(a));L(p+d*.5f,p+d*1.8f+Vector3.UnitY*1.4f,.12f);}
                } else if(instance.Kind==ArenaMechanicKind.Sandpearl)g.Diamond(At(0,3+phase,0),.35f,.8f,white);
                else {for(int i=0;i<6;i++){float an=i*MathF.Tau/6;var d=new Vector3(MathF.Sin(an),0,MathF.Cos(an));L(p+d*2,p+d*2.4f,.15f);}}
                for(int i=0;i<4;i++) {float an=i*MathF.Tau/4;var offset=new Vector3(MathF.Sin(an),0,MathF.Cos(an))*1.3f;g.Diamond(p+offset+Vector3.UnitY*(phase*1.5f),.12f,.35f,white);}break;
            case ArenaMechanicKind.HeatWave:
                for(int row=-2;row<=2;row++)for(int i=0;i<16;i++) {
                    Vector3 V(float t)=>At(-3+t*6,.15f+MathF.Sin(t*12+time*3+row)*.2f,row*1.2f);
                    L(V(i/16f),V((i+1)/16f),.1f,C(.55f));
                }break;
            case ArenaMechanicKind.Geyser:
                R(2);for(int i=0;i<8;i++) {float an=i*MathF.Tau/8;var dir=new Vector3(MathF.Sin(an),0,MathF.Cos(an));
                    for(int j=0;j<8;j++) {Vector3 V(float t)=>p+dir*(t*t*2)+Vector3.UnitY*(MathF.Sin(t*MathF.PI)*6);L(V(j/8f),V((j+1)/8f),.13f,C(.7f));}
                    Sphere(p+dir*phase*2+Vector3.UnitY*(5*(1-phase)),.13f,white);
                }break;
            case ArenaMechanicKind.Aetherometer:
                g.Cylinder(p,.8f,.4f,C());for(int i=0;i<4;i++) {float an=i*MathF.Tau/4;var v=new Vector3(MathF.Sin(an),0,MathF.Cos(an));L(p+v*.7f,p+v*.7f+Vector3.UnitY*4,.1f,C(.5f));}
                if(instance.Progress.HasValue&&float.IsFinite(instance.Progress.Value))g.Cylinder(p,.6f,4*Math.Clamp(instance.Progress.Value,0,1),C(.5f));
                g.Diamond(At(0,4.2f,0),.45f,.9f,C());break;
            case ArenaMechanicKind.AetherBridge:
                // A real bridge requires both recorded endpoints. No invented direction or permanent lifetime.
                if(instance.Destination is not {} end||!LimitBreakVisuals.Finite(end)) {R(.9f);g.Diamond(At(0,1,0),.45f,1,C());break;}
                var delta=end-p;if(delta.LengthSquared()<.01f||delta.LengthSquared()>40_000)break;
                var side=Vector3.Cross(Vector3.Normalize(delta),Vector3.UnitY);if(side.LengthSquared()<.01f)break;side=Vector3.Normalize(side)*1.8f;
                for(int i=0;i<12;i++) {var start=Vector3.Lerp(p,end,i/12f);var finish=Vector3.Lerp(p,end,(i+.85f)/12);g.Triangle(start-side,start+side,finish+side,C(.3f));g.Triangle(start-side,finish+side,finish-side,C(.3f));}
                L(p-side,end-side,.14f);L(p+side,end+side,.14f);break;
        }
    }
    public static void DrawGlobal(ReplaySceneGeometry g,ArenaMechanicKind kind,Vector3 lo,Vector3 hi,float time,Func<float,float,float> height) {
        if(!LimitBreakVisuals.Finite(lo)||!LimitBreakVisuals.Finite(hi)||!float.IsFinite(time))return;
        var color=Tint(kind);uint c=ReplaySceneGeometry.Rgba(color.X,color.Y,color.Z,.10f);
        float gap=Math.Clamp(MathF.Max(hi.X-lo.X,hi.Z-lo.Z)/12,4,20);int count=0;int first=g.Vertices.Length;
        for(float z=lo.Z;z<hi.Z&&count<256;z+=gap)for(float x=lo.X;x<hi.X&&count++<256;x+=gap) {
            if(g.Vertices.Length-first>12_000)return;
            var a=new Vector3(x,height(x,z)+.2f,z);var b=new Vector3(Math.Min(x+gap*.55f,hi.X),height(Math.Min(x+gap*.55f,hi.X),z)+.25f,z);
            float phase=time-MathF.Floor(time);var v=new Vector3(0,0,gap*.12f);
            if(kind==ArenaMechanicKind.Turbulence)v.Z=gap*(.1f+.2f*phase);
            g.Triangle(a-v,a+v,b,c);
        }
    }
}
