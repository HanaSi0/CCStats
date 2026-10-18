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
        ArenaMechanicKind.Sabotender=>new(.28f,.72f,.24f),
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
        uint Shade(uint color,Vector3 normal) {
            float light=.5f+.5f*MathF.Max(0,Vector3.Dot(Vector3.Normalize(normal),Vector3.Normalize(new Vector3(.3f,.88f,.36f))));
            return ReplaySceneGeometry.Rgba((color&255)/255f*light,((color>>8)&255)/255f*light,((color>>16)&255)/255f*light,(color>>24)/255f);
        }
        void Sphere(Vector3 center,float radius,uint color) {
            int bands=radius>=.6f?10:4,sectors=bands*2;
            Vector3 V(float lat,float lon)=>center+new Vector3(MathF.Cos(lat)*MathF.Sin(lon),MathF.Sin(lat),MathF.Cos(lat)*MathF.Cos(lon))*radius;
            for(int j=0;j<bands;j++)for(int i=0;i<sectors;i++) {
                float a=-MathF.PI/2+j*MathF.PI/bands,b=a+MathF.PI/bands,c=i*MathF.Tau/sectors,d=c+MathF.Tau/sectors;
                var q=V(a,c);var r=V(b,c);var u=V(b,d);var v=V(a,d);
                var n=(q+r+u+v)*.25f-center;
                g.Triangle(q,r,u,Shade(color,n));g.Triangle(q,u,v,Shade(color,n));
            }
        }
        void Flame(Vector3 bottom,float radius,float height,float spin,uint color) {
            for(int i=0;i<10;i++) {
                float a=i*MathF.Tau/10+spin,b=(i+1)*MathF.Tau/10+spin;
                var q=bottom+new Vector3(MathF.Sin(a),0,MathF.Cos(a))*radius;
                var r=bottom+new Vector3(MathF.Sin(b),0,MathF.Cos(b))*radius;
                var tip=bottom+new Vector3(MathF.Sin(a+time)*radius*.5f,height*(.7f+.3f*MathF.Sin(time*4+i)),MathF.Cos(a-time)*radius*.5f);
                g.Triangle(q,r,tip,color);
            }
        }
        void Rune(float radius,float y,int marks,uint color) {
            for(int i=0;i<marks;i++) {
                float a=i*MathF.Tau/marks;var d=LimitBreakVisuals.Forward(a);var side=new Vector3(d.Z,0,-d.X);
                var q=p+Vector3.UnitY*y+d*radius;
                g.Ribbon(q-d*.16f,q+d*.16f,.08f,color);g.Ribbon(q,q+side*.14f,.07f,color);
            }
        }
        void Helix(float radius,float height,bool inward=false) {
            for(int strand=0;strand<2;strand++)for(int i=0;i<40;i++) {
                Vector3 V(float t) {var a=t*MathF.Tau*2+strand*MathF.PI-time*2;var r=radius*(inward?1-.75f*t:.2f+.8f*t);return p+new Vector3(MathF.Sin(a)*r,height*t,MathF.Cos(a)*r);}
                var a=V(i/40f);var b=V((i+1)/40f);var da=a-p;da.Y=0;var db=b-p;db.Y=0;
                if(da.LengthSquared()<.001f||db.LengthSquared()<.001f)continue;
                da=Vector3.Normalize(da)*.16f;db=Vector3.Normalize(db)*.16f;
                g.Triangle(a-da,a+da,b+db,C(.22f));g.Triangle(a-da,b+db,b-db,C(.22f));L(a,b,.055f);
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
                if(instance.Kind==ArenaMechanicKind.SprintZone) {
                    g.Triangle(At(-1.6f,-.03f,-3.5f),At(1.6f,-.03f,-3.5f),At(1.6f,-.03f,3.5f),C(.07f));
                    g.Triangle(At(-1.6f,-.03f,-3.5f),At(1.6f,-.03f,3.5f),At(-1.6f,-.03f,3.5f),C(.07f));
                }
                for(int i=-2;i<=2;i++) {
                    var s=At(0,.1f,i*1.3f+phase*1.3f);Arrow(s-f*.6f,f,.8f,.22f);
                }
                if(instance.Kind==ArenaMechanicKind.ShiftingSands)for(int i=0;i<20;i++) {
                    float t=i/20f,u=(i+1)/20f;L(At(MathF.Sin(t*12+time)*1.4f,.15f,-3.5f+t*7),At(MathF.Sin(u*12+time)*1.4f,.15f,-3.5f+u*7),.12f);
                }
                g.Ribbon(At(-1.6f,0,-3.5f),At(-1.6f,0,3.5f),.08f,C(.5f));g.Ribbon(At(1.6f,0,-3.5f),At(1.6f,0,3.5f),.08f,C(.5f));break;
            case ArenaMechanicKind.Bomb:
                // Recognisable Bomb silhouette and face; still a lightweight procedural approximation.
                Sphere(At(0,1.6f,0),1.35f,ReplaySceneGeometry.Rgba(.65f,.24f,.09f));R(1.35f,.08f,.3f);
                foreach(int sign in new[]{-1,1}) {
                    Sphere(At(sign*.42f,1.9f,1.13f),.22f,ReplaySceneGeometry.Rgba(1,.87f,.38f));
                    Sphere(At(sign*.42f,1.9f,1.32f),.09f,dark);
                    L(At(sign*.2f,2.18f,1.12f),At(sign*.7f,2.32f,.96f),.12f,dark);
                }
                for(int i=0;i<8;i++) {
                    float x=-.75f+i*.2f,y=.95f+.25f*MathF.Abs(x);
                    g.Triangle(At(x,y,1.13f),At(x+.17f,y+.02f,1.13f),At(x+.08f,y-.18f,1.13f),white);
                }
                L(At(0,2.8f,0),At(.35f,3.3f,0),.16f,dark);Flame(At(.35f,3.3f,0),.22f,.9f,time,ReplaySceneGeometry.Rgba(1,.66f,.1f,.9f));
                for(int i=0;i<6;i++) {float a=i*MathF.Tau/6;Flame(p+new Vector3(MathF.Sin(a)*.9f,2.4f,MathF.Cos(a)*.9f),.28f,.8f,a,C(.7f));}break;
            case ArenaMechanicKind.BombCore:
                Sphere(At(0,1.15f,0),.42f,ReplaySceneGeometry.Rgba(1,.85f,.3f));
                Flame(At(0,.45f,0),.7f,2.4f,time,C(.6f));Flame(At(0,.65f,0),.38f,1.7f,-time,ReplaySceneGeometry.Rgba(1,.9f,.4f,.8f));R(.7f+phase*.8f,.05f,1-phase);break;
            case ArenaMechanicKind.JumpGlyph:
                R(1.8f);R(1.55f,.05f,.4f);R(1.3f,.1f);Rune(1.65f,.08f,16,white);R(.5f+phase*1.2f,phase*1.6f,1-phase);
                for(int i=0;i<4;i++) {float a=i*MathF.Tau/4;var d=new Vector3(MathF.Sin(a),0,MathF.Cos(a));L(p+d*1.7f,p+d*2.2f+Vector3.UnitY*.8f,.15f);}break;
            case ArenaMechanicKind.Turbulence:
                Helix(2.6f,9);R(2.6f);for(int i=0;i<3;i++)R(1+i*.5f,phase*7+i,.5f);break;
            case ArenaMechanicKind.Feather:
                var tip=At(.55f,4.9f,.1f);var stem=At(-.6f,2.4f,0);var feather=ReplaySceneGeometry.Rgba(.15f,.12f,.23f);var edge=ReplaySceneGeometry.Rgba(.5f,.43f,.65f);
                L(stem,tip,.065f,white);
                for(int i=0;i<15;i++) {
                    float a=i/16f,b=(i+1)/16f;var q=Vector3.Lerp(stem,tip,a);var r=Vector3.Lerp(stem,tip,b);
                    foreach(int sign in new[]{-1,1}) {
                        var u=q+right*(sign*MathF.Sin(a*MathF.PI)*.65f)+Vector3.UnitY*.28f;
                        var v=r+right*(sign*MathF.Sin(b*MathF.PI)*.65f)+Vector3.UnitY*.28f;
                        g.Triangle(q,r,v,feather);g.Triangle(q,v,u,feather);L(q,u,.025f,edge);
                    }
                }
                R(1.2f,.08f,.35f);break;
            case ArenaMechanicKind.Twister:
                Helix(2.4f,5.5f);R(2.4f);break;
            case ArenaMechanicKind.GaleSphere:
                Sphere(At(0,2+.2f*MathF.Sin(time*3),0),.8f,C(.32f));
                for(int i=0;i<24;i++){float a=i*MathF.Tau/24+time;float b=(i+1)*MathF.Tau/24+time;L(At(MathF.Sin(a),2+MathF.Cos(a)*.65f,MathF.Cos(a)),At(MathF.Sin(b),2+MathF.Cos(b)*.65f,MathF.Cos(b)),.045f,white);}
                R(.5f+phase,1.3f,1-phase);break;
            case ArenaMechanicKind.TrickFloor:
                // Tilt a symbolic tatami around one edge; this does not claim the floor's exact state/size.
                float angle=(.5f+.5f*MathF.Sin(time*2))*.9f;
                Vector3 Q(float x,float z)=>At(x,MathF.Sin(angle)*(z+2),MathF.Cos(angle)*z);
                var floorA=Q(-2,-2);var floorB=Q(2,-2);var floorC=Q(2,2);var floorD=Q(-2,2);
                var straw=ReplaySceneGeometry.Rgba(.62f,.59f,.36f);var binding=ReplaySceneGeometry.Rgba(.1f,.22f,.17f);
                g.Triangle(floorA,floorB,floorC,straw);g.Triangle(floorA,floorC,floorD,straw);L(floorA,floorB,.2f,binding);L(floorB,floorC,.2f,binding);L(floorC,floorD,.2f,binding);L(floorD,floorA,.2f,binding);
                for(int i=1;i<20;i++)L(Q(-1.85f,-2+i*.2f),Q(1.85f,-2+i*.2f),.02f,ReplaySceneGeometry.Rgba(.34f,.39f,.21f));break;
            case ArenaMechanicKind.TrickDoor:
                var wood=ReplaySceneGeometry.Rgba(.38f,.13f,.09f);var gilt=ReplaySceneGeometry.Rgba(.9f,.7f,.32f);
                L(At(-1.6f,0,0),At(-1.6f,4.8f,0),.35f,wood);L(At(1.6f,0,0),At(1.6f,4.8f,0),.35f,wood);L(At(-1.9f,4.8f,0),At(1.9f,4.8f,0),.28f,gilt);
                for(int i=-3;i<=3;i++)L(At(i*.42f,.2f,-.12f),At(i*.42f,4.5f,-.12f),.07f,wood);
                L(At(-2.1f,5,0),At(0,5.6f,0),.3f,wood);L(At(0,5.6f,0),At(2.1f,5,0),.3f,wood);
                for(int i=0;i<32;i++) {float t=i/32f,u=(i+1)/32f;Vector3 P(float v)=>At(MathF.Sin(v*MathF.Tau+time)*v*1.3f,2.5f+MathF.Cos(v*MathF.Tau+time)*v*1.8f,0);L(P(t),P(u));}
                if(instance.Destination is {} exit&&LimitBreakVisuals.Finite(exit)) {g.Ribbon(p,exit,.12f,C(.4f));Arrow(exit-f*1.5f,f,1.2f);}break;
            case ArenaMechanicKind.Yojimbo:
                var armor=ReplaySceneGeometry.Rgba(.18f,.22f,.33f);var trim=ReplaySceneGeometry.Rgba(.9f,.68f,.3f);
                g.Cylinder(p,.7f,.25f,dark);g.Cylinder(At(0,.4f,0),.65f,1.6f,armor);Sphere(At(0,2.5f,0),.4f,ReplaySceneGeometry.Rgba(.6f,.48f,.36f));
                for(int i=0;i<16;i++){float a=i*MathF.Tau/16,b=(i+1)*MathF.Tau/16;g.Triangle(At(0,3.2f,0),At(MathF.Sin(a)*1.25f,2.75f,MathF.Cos(a)*1.25f),At(MathF.Sin(b)*1.25f,2.75f,MathF.Cos(b)*1.25f),trim);}
                for(int sign=-1;sign<=1;sign+=2){L(At(sign*.65f,1.9f,0),At(sign*1.15f,1.45f,.15f),.45f,armor);L(At(sign*.35f,.6f,0),At(sign*.55f,.1f,.35f),.3f,armor);}
                for(int i=0;i<4;i++)L(At(-.5f,.6f+i*.3f,.6f),At(.5f,.6f+i*.3f,.6f),.055f,trim);
                L(At(.7f,1.5f,0),At(2.3f,1.65f,1.4f),.14f,white);L(At(2.3f,1.65f,1.4f),At(2.8f,1.8f,1.7f),.1f,white);
                Arrow(At(0,.05f,1.6f),f,1.7f,.15f);break;
            case ArenaMechanicKind.Onmyoji:
                // A closed lid while travelling/waiting, opening only during a matching observed gaze.
                float openness=instance.Reference?ArenaMechanicScene.ReferenceEye(time):float.IsFinite(instance.EyeOpen)?Math.Clamp(instance.EyeOpen,0,1):0;
                for(int i=0;i<24;i++) {
                    float a=i*MathF.PI/24,b=(i+1)*MathF.PI/24;
                    if(openness<.015f)L(At(MathF.Cos(a)*1.8f,4.5f,MathF.Sin(a)*.06f),At(MathF.Cos(b)*1.8f,4.5f,MathF.Sin(b)*.06f),.14f,white);
                    else foreach(int sign in new[]{-1,1})L(At(MathF.Cos(a)*1.8f,4.5f,sign*MathF.Sin(a)*.8f*openness),At(MathF.Cos(b)*1.8f,4.5f,sign*MathF.Sin(b)*.8f*openness),.13f,white);
                }
                if(openness>.02f)for(int i=0;i<32;i++) {
                    float a=i*MathF.Tau/32,b=(i+1)*MathF.Tau/32;
                    Vector3 EyePoint(float angle,float radius,float y)=>At(MathF.Sin(angle)*radius,y,MathF.Cos(angle)*radius*openness);
                    g.Triangle(At(0,4.55f,0),EyePoint(a,.48f,4.55f),EyePoint(b,.48f,4.55f),C());
                    g.Triangle(At(0,4.6f,0),EyePoint(a,.22f,4.6f),EyePoint(b,.22f,4.6f),dark);
                }
                if(openness>.3f)g.Cylinder(At(.1f,4.66f,.1f*openness),.07f,.025f,white);break;
            case ArenaMechanicKind.Gold:
                for(int i=0;i<6;i++) {
                    float a=i*2.4f;var coin=At(MathF.Sin(a)*.65f,i*.1f,MathF.Cos(a)*.65f);
                    g.Cylinder(coin,.45f,.2f,C());g.Ring(coin+Vector3.UnitY*.22f,.34f,.045f,ReplaySceneGeometry.Rgba(.6f,.35f,.06f));
                    g.Ribbon(coin+Vector3.UnitY*.24f-right*.15f,coin+Vector3.UnitY*.24f+right*.15f,.035f,dark);
                }
                L(At(0,.2f,0),At(0,4,0),.08f,C(.7f));R(.5f+phase,.1f,1-phase);break;
            case ArenaMechanicKind.Antlion:
                R(2.4f);Helix(2.2f,.2f,true);Sphere(At(0,.65f,0),.9f,ReplaySceneGeometry.Rgba(.57f,.28f,.09f));
                for(int sign=-1;sign<=1;sign+=2)for(int j=0;j<3;j++){var start=At(sign*.65f,.55f,j*.5f-.5f);var bend=At(sign*1.25f,.35f,j*.7f-.7f);L(start,bend,.18f);L(bend,At(sign*1.8f,.1f,j-.9f),.12f);}
                for(int s=-1;s<=1;s+=2) {L(At(s*2,0,-1),At(s*1.2f,2,0),.45f);L(At(s*1.2f,2,0),At(s*.25f,1,1),.35f);}break;
            case ArenaMechanicKind.Sabotender:
            case ArenaMechanicKind.Acutender:
                g.Cylinder(p,.6f,3.5f,C(.85f));g.Cylinder(At(-1.3f,1.7f,0),.35f,1.2f,C());g.Cylinder(At(1.3f,1,0),.35f,1.5f,C());
                L(At(-1.3f,1.7f,0),At(0,1.7f,0),.6f);L(At(0,1,0),At(1.3f,1,0),.6f);
                foreach(int sign in new[]{-1,1})Sphere(At(sign*.2f,2.85f,.55f),.09f,dark);
                g.Cylinder(At(0,2.5f,.58f),.1f,.07f,dark);
                for(int i=0;i<8;i++){float a=i*MathF.Tau/8;var d=new Vector3(MathF.Sin(a),0,MathF.Cos(a))*.62f;L(p+d+Vector3.UnitY*.2f,p+d+Vector3.UnitY*3.3f,.025f,ReplaySceneGeometry.Rgba(.12f,.36f,.15f));}
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
                    for(int i=0;i<24;i++){float a=i*MathF.Tau/24,b=(i+1)*MathF.Tau/24;g.Triangle(p,p+new Vector3(MathF.Sin(a)*1.7f,.02f,MathF.Cos(a)*1.7f),p+new Vector3(MathF.Sin(b)*1.7f,.02f,MathF.Cos(b)*1.7f),ReplaySceneGeometry.Rgba(.12f,.6f,.55f,.17f));}
                    g.Cylinder(p,.55f,.1f,C(.3f));for(int i=0;i<4;i++){float a=i*MathF.Tau/4;var d=new Vector3(MathF.Sin(a),0,MathF.Cos(a));L(p+d*.5f,p+d*1.8f+Vector3.UnitY*1.4f,.12f);}
                } else if(instance.Kind==ArenaMechanicKind.Sandpearl) {
                    Sphere(At(0,3.5f*(1-phase)+.4f,0),.24f,white);L(At(0,.2f,0),At(0,3.5f*(1-phase)+.4f,0),.06f,ReplaySceneGeometry.Rgba(.7f,1,.85f,.3f));
                } else {Rune(1.7f,.12f,18,white);for(int i=0;i<6;i++){float an=i*MathF.Tau/6;var d=new Vector3(MathF.Sin(an),0,MathF.Cos(an));L(p+d*2,p+d*2.4f,.15f);}}
                for(int i=0;i<4;i++) {float an=i*MathF.Tau/4;var offset=new Vector3(MathF.Sin(an),0,MathF.Cos(an))*1.3f;g.Diamond(p+offset+Vector3.UnitY*(phase*1.5f),.12f,.35f,white);}break;
            case ArenaMechanicKind.HeatWave:
                for(int row=-2;row<=2;row++)for(int i=0;i<16;i++) {
                    Vector3 V(float t)=>At(-3+t*6,.15f+MathF.Sin(t*12+time*3+row)*.2f,row*1.2f);
                    L(V(i/16f),V((i+1)/16f),.1f,C(.55f));
                }
                for(int i=0;i<10;i++) {var q=At(-2.7f+i*.6f,0,(i%3-1)*1.3f);var drift=Vector3.UnitY*(.4f+phase);
                    g.Triangle(q,q+right*.22f+drift,q-right*.1f+drift,ReplaySceneGeometry.Rgba(1,.5f,.2f,.07f));}
                break;
            case ArenaMechanicKind.Geyser:
                g.Cylinder(p,.3f,4.5f+MathF.Sin(time*3)*.3f,C(.16f));R(.5f+phase*1.5f,.06f,1-phase);R(2);for(int i=0;i<8;i++) {float an=i*MathF.Tau/8;var dir=new Vector3(MathF.Sin(an),0,MathF.Cos(an));
                    for(int j=0;j<8;j++) {Vector3 V(float t)=>p+dir*(t*t*2)+Vector3.UnitY*(MathF.Sin(t*MathF.PI)*6);L(V(j/8f),V((j+1)/8f),.13f,C(.7f));}
                    Sphere(p+dir*phase*2+Vector3.UnitY*(5*(1-phase)),.13f,white);
                }break;
            case ArenaMechanicKind.Aetherometer:
                g.Cylinder(p,1,.2f,ReplaySceneGeometry.Rgba(.35f,.31f,.2f));R(.9f,.25f);Rune(.9f,.28f,12,white);for(int i=0;i<4;i++) {float an=i*MathF.Tau/4;var v=new Vector3(MathF.Sin(an),0,MathF.Cos(an));L(p+v*.7f,p+v*.7f+Vector3.UnitY*4,.1f,C(.5f));}
                if(instance.Progress.HasValue&&float.IsFinite(instance.Progress.Value))g.Cylinder(p,.6f,4*Math.Clamp(instance.Progress.Value,0,1),C(.5f));
                g.Diamond(At(0,4.2f,0),.45f,.9f,C());break;
            case ArenaMechanicKind.AetherBridge:
                // A real bridge requires both recorded endpoints. No invented direction or permanent lifetime.
                if(instance.Destination is not {} end||!LimitBreakVisuals.Finite(end)) {R(.9f);g.Diamond(At(0,1,0),.45f,1,C());break;}
                var delta=end-p;if(delta.LengthSquared()<.01f||delta.LengthSquared()>40_000)break;
                var side=Vector3.Cross(Vector3.Normalize(delta),Vector3.UnitY);if(side.LengthSquared()<.01f)break;side=Vector3.Normalize(side)*1.8f;
                for(int i=0;i<12;i++) {var start=Vector3.Lerp(p,end,i/12f);var finish=Vector3.Lerp(p,end,(i+.85f)/12);g.Triangle(start-side,start+side,finish+side,C(.3f));g.Triangle(start-side,finish+side,finish-side,C(.3f));}
                L(p-side,end-side,.14f);L(p+side,end+side,.14f);g.Ribbon(p,end,.07f,white);
                for(int i=0;i<6;i++){var q=Vector3.Lerp(p,end,(i+.5f)/6);g.Ribbon(q-side*.35f,q+side*.35f,.09f,white);}break;
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
