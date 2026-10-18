using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using PvpStats.Analysis;
using PvpStats.Interface;

namespace PvpStats.Windows.Replay;
internal sealed partial class MatchReplayWindow {
    private int _mechanicExample;
    private float _mechanicExampleYaw=.5f;
    private readonly ReplaySceneGeometry _mechanicExampleGeometry=new();
    private void DrawMechanicGallery() {
        if(ModernTheme.SmallButton("机制图例###ArenaMechanicGallery"))ImGui.OpenPopup("地图机制图例###ArenaMechanicGalleryPopup");
        ImGui.SetNextWindowSize(new Vector2(560,470)*ImGuiHelpers.GlobalScale,ImGuiCond.Appearing);
        if(!ImGui.BeginPopup("地图机制图例###ArenaMechanicGalleryPopup"))return;
        try {
            var kinds=ArenaMechanicScene.GalleryKinds;ImGui.SetNextItemWidth(-1);
            ImGui.Combo("###MechanicExampleKind",ref _mechanicExample,kinds.Select(ArenaMechanicScene.Name).ToArray(),kinds.Length);
            ImGui.TextWrapped("样式示意，不代表本局发生、场地位置或真实判定尺寸。左键拖动旋转。");
            var pos=ImGui.GetCursorScreenPos();var size=new Vector2(Math.Max(1,ImGui.GetContentRegionAvail().X),310*ImGuiHelpers.GlobalScale);
            ImGui.InvisibleButton("###MechanicExampleCanvas",size);
            if(ImGui.IsItemActive()&&ImGui.IsMouseDragging(ImGuiMouseButton.Left))_mechanicExampleYaw+=ImGui.GetIO().MouseDelta.X*.01f;
            var geometry=_mechanicExampleGeometry;geometry.Clear();
            var kind=kinds[_mechanicExample];float time=(float)(ImGui.GetTime()%3600);
            ArenaMechanicRenderer.Draw(geometry,new(kind,Vector3.Zero,Destination:kind is ArenaMechanicKind.AetherBridge or ArenaMechanicKind.TrickDoor?new Vector3(0,0,10):null,Progress:kind==ArenaMechanicKind.Aetherometer?.65f:null,Reference:true),time);
            var vertices=geometry.Vertices.ToArray();var draw=ImGui.GetWindowDrawList();
            draw.AddRectFilled(pos,pos+size,0xff20120b,6);draw.PushClipRect(pos,pos+size,true);
            try {
                var right=new Vector3(MathF.Cos(_mechanicExampleYaw),0,-MathF.Sin(_mechanicExampleYaw));
                var forward=new Vector3(MathF.Sin(_mechanicExampleYaw),0,MathF.Cos(_mechanicExampleYaw));
                Vector2 Project(Vector3 p)=>new(Vector3.Dot(p,right),Vector3.Dot(p,forward)*.5f-p.Y);
                var points=vertices.Select(v=>Project(v.Position)).ToArray();
                var lo=points.Length==0?Vector2.Zero:new Vector2(points.Min(p=>p.X),points.Min(p=>p.Y));
                var hi=points.Length==0?Vector2.One:new Vector2(points.Max(p=>p.X),points.Max(p=>p.Y));
                var span=Vector2.Max(hi-lo,new Vector2(2));float zoom=.72f*Math.Min(size.X/span.X,size.Y/span.Y);
                var center=pos+size/2-(lo+hi)*.5f*zoom;
                Vector2 At(Vector3 p)=>center+Project(p)*zoom;
                float Depth(Vector3 p)=>Vector3.Dot(p,forward)+p.Y*.5f;
                foreach(var i in Enumerable.Range(0,vertices.Length/3).OrderBy(i=>Depth(vertices[i*3].Position+vertices[i*3+1].Position+vertices[i*3+2].Position))) {
                    int a=i*3;draw.AddTriangleFilled(At(vertices[a].Position),At(vertices[a+1].Position),At(vertices[a+2].Position),vertices[a].Color);
                }
            }finally {draw.PopClipRect();}
            ImGui.TextWrapped(ArenaMechanicGuide.Detail(kind));
        }finally {ImGui.EndPopup();}
    }
}
