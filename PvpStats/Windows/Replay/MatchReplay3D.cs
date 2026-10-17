using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using PvpStats.Analysis;
using PvpStats.Helpers;
using PvpStats.Interface;
using PvpStats.Services;
namespace PvpStats.Windows.Replay;
internal sealed partial class MatchReplayWindow {
    private readonly ReplayCamera3D _camera3D=new();
    private CancellationTokenSource? _sceneCancel;
    private Task<ReplayScene3D>? _sceneTask;
    private ReplayScene3D? _scene3D;
    private readonly List<SceneTriangle> _pieces3D=new(1600);
    private readonly List<ProjectedFace> _faces3D=new(14000);
    private readonly Dictionary<(int,int),float> _height3D=new();
    private float _baseHeight3D,_referenceRadius=5;
    private readonly record struct ProjectedFace(Vector2 A,Vector2 B,Vector2 C,float Depth,uint Color);
    private void Initialize3D(PlayerPosition[] points) {
        _baseHeight3D=points.FirstOrDefault(p=>p.Y.HasValue)?.Y??0;
        _camera3D.Fit(points.Select(p=>new Vector3(p.X,p.Y??_baseHeight3D,p.Z)));
        _sceneCancel=new();
        try {
            var background=_plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRow((uint)_match.TerritoryId).Bg.ToString();
            var min=new Vector2(points.Length>0?points.Min(p=>p.X):-100,points.Length>0?points.Min(p=>p.Z):-100);
            var max=new Vector2(points.Length>0?points.Max(p=>p.X):100,points.Length>0?points.Max(p=>p.Z):100);
            var loader=new ReplaySceneLoader(_plugin,min,max,_sceneCancel.Token);
            _sceneTask=Task.Run(()=>loader.Load(background),_sceneCancel.Token);
        } catch(Exception e) { _plugin.Log.Error(e,"3D replay scene initialization failed.");_scene3D=new() { Status="地图模型读取入口不可用；仅显示观测棋子" }; }
    }
    private float Ground3D(float x,float z,float? observed) {
        if(observed.HasValue&&float.IsFinite(observed.Value))return observed.Value;
        var key=((int)MathF.Round(x*2),(int)MathF.Round(z*2));
        if(_height3D.TryGetValue(key,out var value))return value;
        value=_scene3D?.Ground(x,z)??_baseHeight3D;
        if(_height3D.Count<10000)_height3D[key]=value;
        return value;
    }
    private void Draw3DScene(Vector2 size,MovementFrame? frame,DateTime now) {
        var scale=ImGuiHelpers.GlobalScale;
        ImGui.SetNextItemWidth(Math.Min(180*scale,size.X*.5f));
        ImGui.SliderFloat("参考圈半径（未校准）###CrystalRadius3D",ref _referenceRadius,1,15,"%.1f");
        size.Y=Math.Max(1,size.Y-ImGui.GetFrameHeightWithSpacing());
        if(_scene3D==null&&_sceneTask?.IsCompleted==true) {
            if(_sceneTask.IsCompletedSuccessfully)_scene3D=_sceneTask.Result;
            else { _plugin.Log.Error(_sceneTask.Exception??new Exception("Scene task cancelled"),"3D replay scene load failed.");_scene3D=new() { Status="地图模型读取失败；仅显示观测棋子" }; }
            _height3D.Clear();
        }
        var origin=ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("###Replay3DCanvas",size,ImGuiButtonFlags.MouseButtonLeft|ImGuiButtonFlags.MouseButtonRight);
        var hovered=ImGui.IsItemHovered();var io=ImGui.GetIO();
        if(hovered&&io.MouseWheel!=0)_camera3D.Zoom(MathF.Pow(1.15f,-io.MouseWheel));
        if(ImGui.IsItemActive()) {
            if(ImGui.IsMouseDragging(ImGuiMouseButton.Right))_camera3D.Orbit(io.MouseDelta);
            if(ImGui.IsMouseDragging(ImGuiMouseButton.Left))_camera3D.Pan(io.MouseDelta,size.Y);
        }
        var draw=ImGui.GetWindowDrawList();draw.PushClipRect(origin,origin+size,true);
        try {
            draw.AddRectFilled(origin,origin+size,ImGui.GetColorU32(new Vector4(.025f,.04f,.065f,1)));
            var matrix=_camera3D.Matrix(size.X/Math.Max(1,size.Y));
            bool Project(Vector3 p,out Vector2 screen,out float depth) {
                var ok=ReplayCamera3D.Project(p,matrix,size,out var pixel,out depth);screen=origin+pixel;return ok;
            }
            void Line(Vector3 a,Vector3 b,Vector4 color,float width=1) {
                if(Project(a,out var pa,out _)&&Project(b,out var pb,out _))draw.AddLine(pa,pb,ImGui.GetColorU32(color),width*scale);
            }
            // Grid is only a fallback coordinate reference, not a fabricated terrain mesh.
            if((_scene3D?.Triangles.Count??0)==0) {
                var center=_camera3D.Target;var grid=new Vector4(.18f,.26f,.34f,.65f);
                for(int i=-10;i<=10;i++) {
                    Line(new(center.X-100,_baseHeight3D,center.Z+i*10),new(center.X+100,_baseHeight3D,center.Z+i*10),grid);
                    Line(new(center.X+i*10,_baseHeight3D,center.Z-100),new(center.X+i*10,_baseHeight3D,center.Z+100),grid);
                }
            }
            _faces3D.Clear();_pieces3D.Clear();
            if(frame!=null) foreach(var p in frame.Players) {
                if(!float.IsFinite(p.X)||!float.IsFinite(p.Z))continue;
                var foot=new Vector3(p.X,Ground3D(p.X,p.Z,p.Y),p.Z);
                var color=p.HP==0?new Vector4(.38f,.4f,.44f,1):p.Team==1?new Vector4(.2f,.7f,1,1):p.Team==2?new Vector4(1,.32f,.4f,1):new Vector4(.72f,.76f,.83f,1);
                if(p.Player==_selected)color=ModernTheme.Gold;
                ReplayPrimitives.Pawn(_pieces3D,foot,color);
            }
            if(frame?.Crystal is {} crystal&&float.IsFinite(crystal.X)&&float.IsFinite(crystal.Z))
                ReplayPrimitives.Crystal(_pieces3D,new(crystal.X,Ground3D(crystal.X,crystal.Z,crystal.Y),crystal.Z),new(.35f,.65f,1,1));
            void Face(SceneTriangle tri) {
                if(!Project(tri.A,out var a,out var da)||!Project(tri.B,out var b,out var db)||!Project(tri.C,out var c,out var dc))return;
                if(Math.Max(a.X,Math.Max(b.X,c.X))<origin.X||Math.Min(a.X,Math.Min(b.X,c.X))>origin.X+size.X||Math.Max(a.Y,Math.Max(b.Y,c.Y))<origin.Y||Math.Min(a.Y,Math.Min(b.Y,c.Y))>origin.Y+size.Y)return;
                var normal=Vector3.Cross(tri.B-tri.A,tri.C-tri.A);var length=normal.Length();if(length<.00001f)return;
                var light=.35f+.65f*Math.Abs(Vector3.Dot(normal/length,Vector3.Normalize(new Vector3(.3f,1,.5f))));
                var color=tri.Color; color.X*=light;color.Y*=light;color.Z*=light;
                _faces3D.Add(new(a,b,c,(da+db+dc)/3,ImGui.GetColorU32(color)));
            }
            if(_scene3D!=null)foreach(var tri in _scene3D.Triangles)Face(tri);
            foreach(var tri in _pieces3D)Face(tri);
            _faces3D.Sort((a,b)=>b.Depth.CompareTo(a.Depth));
            foreach(var f in _faces3D)draw.AddTriangleFilled(f.A,f.B,f.C,f.Color);
            if(_showCrystalTrail) {
                MovementFrame? previous=null;
                foreach(var sample in _data.Frames) {
                    if(sample.Timestamp>now)break;
                    if(previous!=null&&ReplayObservation.CrystalSegment(previous,sample,_data.FrameGapSeconds)) {
                        var a=previous.Crystal!;var b=sample.Crystal!;
                        // Skip imperceptible segments; no gap is bridged by this filter.
                        if(Vector2.DistanceSquared(new(a.X,a.Z),new(b.X,b.Z))>.00001f)
                            Line(new(a.X,Ground3D(a.X,a.Z,a.Y)+.12f,a.Z),new(b.X,Ground3D(b.X,b.Z,b.Y)+.12f,b.Z),ModernTheme.Gold with { W=.65f },1.5f);
                    }
                    previous=sample;
                }
            }
            if(frame?.Crystal is {} observed&&float.IsFinite(observed.X)&&float.IsFinite(observed.Z)) {
                var y=Ground3D(observed.X,observed.Z,observed.Y)+.1f;
                for(int i=0;i<64;i++) {
                    if(i%4==3)continue; // Dashed: adjustable reference, not a verified escort radius.
                    Vector3 P(int n)=>new(observed.X+MathF.Cos(n*MathF.PI/32)*_referenceRadius,y,observed.Z+MathF.Sin(n*MathF.PI/32)*_referenceRadius);
                    Line(P(i),P(i+1),ModernTheme.Gold,2);
                }
            }
            if(frame!=null)foreach(var p in frame.Players) {
                if(!float.IsFinite(p.X)||!float.IsFinite(p.Z))continue;
                var foot=new Vector3(p.X,Ground3D(p.X,p.Z,p.Y),p.Z);
                if(float.IsFinite(p.Heading))Line(foot+Vector3.UnitY*.1f,foot+new Vector3(MathF.Sin(p.Heading)*2,.1f,MathF.Cos(p.Heading)*2),ModernTheme.Gold);
                if(!Project(foot,out var screen,out _)||screen.X<origin.X||screen.X>origin.X+size.X||screen.Y<origin.Y||screen.Y>origin.Y+size.Y)continue;
                var job=ReplayJob(p);var label=job.HasValue?PlayerJobHelper.GetNameFromJob(job.Value):"职业未知";
                var textSize=ImGui.CalcTextSize(label);var at=screen+new Vector2(-textSize.X/2,6*scale);
                draw.AddRectFilled(at-new Vector2(3*scale),at+textSize+new Vector2(3*scale),ImGui.GetColorU32(new Vector4(.02f,.03f,.05f,.88f)),3*scale);
                draw.AddText(at,ImGui.GetColorU32(p.Player==_selected?ModernTheme.Gold:new Vector4(.95f,.96f,1,1)),label);
                if(hovered&&Vector2.Distance(io.MousePos,screen)<18*scale) {
                    ImGui.SetTooltip(p.Player+$"\n体力 {p.HP:N0}/{p.MaxHP:N0}");
                    if(ImGui.IsMouseClicked(ImGuiMouseButton.Left))_selected=p.Player;
                }
            }
            if(_showMechanics)foreach(var entry in _data.MapMechanics.Where(e=>e.Timestamp<=now&&e.Timestamp>=now.AddSeconds(-3))) {
                if(entry.MapWide)draw.AddText(origin+new Vector2(10,36)*scale,ImGui.GetColorU32(ModernTheme.Gold),"地图机制触发");
                else if(entry.X.HasValue&&entry.Z.HasValue&&Project(new(entry.X.Value,Ground3D(entry.X.Value,entry.Z.Value,null)+1,entry.Z.Value),out var at,out _))
                    draw.AddCircle(at,8*scale,ImGui.GetColorU32(ModernTheme.Gold),0,2*scale);
            }
            var status=_scene3D?.Status??"正在读取本机地图模型…";
            draw.AddText(origin+new Vector2(8*scale),ImGui.GetColorU32(ModernTheme.Gold),"试验 · "+status);
            if(_data.CrystalReferenceLabel.Length>0)draw.AddText(origin+new Vector2(8,72)*scale,ImGui.GetColorU32(ModernTheme.Gold),_data.CrystalReferenceLabel);
            if(_data.Version<5)draw.AddText(origin+new Vector2(8,20)*scale,ImGui.GetColorU32(ModernTheme.Gold),"旧记录缺少高度：高度为地图表面推定");
            if(_scene3D?.Limited==true||_scene3D?.MissingModels>0)draw.AddText(origin+new Vector2(8,52)*scale,ImGui.GetColorU32(ModernTheme.Gold),"地图几何可能不完整（资源缺失或达到显示预算）");
        } finally { draw.PopClipRect(); }
    }
}
