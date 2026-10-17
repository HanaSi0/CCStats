using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using PvpStats.Analysis;
using PvpStats.Helpers;
using PvpStats.Interface;
using PvpStats.Interface.Rendering;

namespace PvpStats.Windows.Replay;

internal sealed partial class MatchReplayWindow {
    private Task<ReplayMapMesh>? _meshTask;
    private ReplayMapMesh? _mesh;
    private ReplayGpuRenderer? _renderer;
    private readonly ReplaySceneGeometry _scene=new();
    private bool _disposed,_cameraDirty=true,_followPlayer;
    private string _renderError="";
    private Vector3 _target3D;
    private float _yaw3D=.65f,_pitch3D=.85f,_distance3D=190,_radius3D=5;
    private double _renderAt=-1;
    private float _renderSeconds=-1;
    private Matrix4x4 _projection3D;
    private MovementFrame? _renderedFrame;
    private bool _has3DFrame;
    private string _renderSelected="";
    private bool _renderFollow;
    private void Reset3DCamera() {
        if(_mesh==null)return;
        _target3D=(_mesh.Min+_mesh.Max)/2;_target3D.Y=1;
        _yaw3D=.65f;_pitch3D=.85f;_distance3D=Math.Clamp(Math.Max(_mesh.Max.X-_mesh.Min.X,_mesh.Max.Z-_mesh.Min.Z)*1.05f,70,300);_cameraDirty=true;
    }
    private void Draw3D() {
        var scale=ImGuiHelpers.GlobalScale;
        _meshTask??=Task.Run(()=>ReplayMapMesh.LoadEmbedded(_match.TerritoryId));
        if(_mesh==null&&_meshTask.IsCompletedSuccessfully) { _mesh=_meshTask.Result;Reset3DCamera(); }
        if(_meshTask.IsFaulted) { ImGui.TextWrapped("地图加载失败，请查看日志。二维回放仍可使用。");if(_renderError.Length==0) { _renderError="地图资源加载失败";_plugin.Log.Error(_meshTask.Exception!,"3D replay map load failed."); }return; }
        if(ModernTheme.Button(_playing?"暂停###Replay3DPlay":"播放###Replay3DPlay"))_playing=!_playing;
        ImGui.SameLine();if(ModernTheme.Button("回到开始###Replay3DStart")) { _seconds=0;_playing=false;_cameraDirty=true; }
        ImGui.SameLine();ImGui.SetNextItemWidth(80*scale);ImGui.Combo("###Replay3DSpeed",ref _speed,new[]{"0.5×","1×","2×","4×"},4);
        ImGui.SameLine();if(ModernTheme.Button("重置视角###Replay3DFit"))Reset3DCamera();
        ImGui.SameLine();if(ModernTheme.SmallButton("上一帧###Replay3DPrev")) { StepFrame(-1);_cameraDirty=true; }
        ImGui.SameLine();if(ModernTheme.SmallButton("下一帧###Replay3DNext")) { StepFrame(1);_cameraDirty=true; }
        ImGui.SameLine();ImGui.Checkbox("跟随选中玩家###Replay3DFollow",ref _followPlayer);
        if(_playing) { _seconds=Math.Min(_duration,_seconds+ImGui.GetIO().DeltaTime*new[]{.5f,1,2,4}[_speed]);if(_seconds>=_duration)_playing=false; }
        ImGui.SetNextItemWidth(-1);if(ImGui.SliderFloat("###Replay3DTime",ref _seconds,0,_duration,"%.2f 秒")) { _playing=false;_cameraDirty=true; }
        ImGui.TextUnformatted($"{TimeSpan.FromSeconds(_seconds):mm\\:ss} / {TimeSpan.FromSeconds(_duration):mm\\:ss} · {_data.Frames.Count:N0} 帧 · 目标采样 {1/_data.SampleIntervalSeconds:F0} 次/秒");
        ImGui.SameLine();ImGui.Checkbox("水晶轨迹###Replay3DTrail",ref _showCrystalTrail);if(ImGui.IsItemEdited())_cameraDirty=true;
        ImGui.SameLine();ImGui.Checkbox("地图事件###Replay3DEvents",ref _showMechanics);if(ImGui.IsItemEdited())_cameraDirty=true;
        ImGui.SameLine();ImGui.SetNextItemWidth(76*scale);if(ImGui.DragFloat("水晶参考圈###Replay3DRadius",ref _radius3D,.1f,1,12,"%.1f"))_cameraDirty=true;
        if(ImGui.IsItemHovered())ImGui.SetTooltip("参考半径默认 5 游戏单位，尚未验证为服务器实际判定半径。");
        if(_data.MovementDownsampled)ImGui.TextDisabled("此记录已降低采样密度；不补造缺失观测。");
        if(_data.Frames.Count==0)ImGui.TextDisabled("此比赛没有走位采样，仅可查看地图及已记录事件。");
        var now=_origin.AddSeconds(_seconds);
        var frame=ReplayInterpolation.Sample(_data,now,(player,a,b)=>_timeline.Kills?.Any(k=>k.Victim.ToString()==player&&k.Timestamp>a&&k.Timestamp<=b)==true);
        if(frame?.Players.Any(p=>!p.Y.HasValue)==true||frame?.Crystal is {Y:null})ImGui.TextDisabled("旧记录缺少高度，使用地形估计。");
        var available=ImGui.GetContentRegionAvail();var flank=Math.Clamp(available.X*.19f,170*scale,240*scale);
        var bodyHeight=Math.Max(1,available.Y-ImGui.GetFrameHeightWithSpacing()-(_showReplayLog?140*scale:0));
        var mapWidth=Math.Max(1,available.X-2*(flank+ImGui.GetStyle().ItemSpacing.X));
        DrawTeamPanel(1,frame,now,new Vector2(flank,bodyHeight));ImGui.SameLine();
        using(var canvas=ImRaii.Child("Replay3DCanvas",new Vector2(mapWidth,bodyHeight),false,ImGuiWindowFlags.NoScrollbar|ImGuiWindowFlags.NoScrollWithMouse)) {
            if(canvas)Draw3DCanvas(frame,now);
        }
        ImGui.SameLine();DrawTeamPanel(2,frame,now,new Vector2(flank,bodyHeight));
        DrawReplayLog(now,_showReplayLog?140*scale:0);
    }
    private float Height(float x,float? y,float z) => y.HasValue&&float.IsFinite(y.Value)?y.Value:_mesh?.EstimatedHeight(x,z)??0;
    private Vector3 World(PlayerPosition p) => new(p.X,Height(p.X,p.Y,p.Z),p.Z);
    private Vector3 World(CrystalPosition p) => new(p.X,Height(p.X,p.Y,p.Z),p.Z);
    private void Draw3DCanvas(MovementFrame? frame,DateTime now) {
        if(_mesh==null) { ImGui.TextUnformatted("正在加载精简地图…");return; }
        if(_renderError.Length>0) { ImGui.TextWrapped(_renderError);if(ModernTheme.Button("重试三维渲染###Retry3D")) { _renderer?.DisposeAfterFrame(_plugin.PluginInterface.UiBuilder);_renderer=null;_renderError="";_cameraDirty=true; }return; }
        var origin=ImGui.GetCursorScreenPos();var size=Vector2.Max(new(1),ImGui.GetContentRegionAvail());
        ImGui.InvisibleButton("###Replay3DViewport",size,ImGuiButtonFlags.MouseButtonLeft|ImGuiButtonFlags.MouseButtonRight);
        var hovered=ImGui.IsItemHovered();var io=ImGui.GetIO();
        if(_selected!=_renderSelected||_followPlayer!=_renderFollow)_cameraDirty=true;
        if(hovered&&io.MouseWheel!=0) { _distance3D=Math.Clamp(_distance3D*MathF.Pow(.85f,io.MouseWheel),12,600);_cameraDirty=true; }
        if(hovered&&ImGui.IsMouseDragging(ImGuiMouseButton.Left)) { _yaw3D-=io.MouseDelta.X*.008f;_pitch3D=Math.Clamp(_pitch3D+io.MouseDelta.Y*.008f,.12f,1.52f);_cameraDirty=true; }
        if(hovered&&ImGui.IsMouseDragging(ImGuiMouseButton.Right)) {
            var right=new Vector3(MathF.Cos(_yaw3D),0,-MathF.Sin(_yaw3D));var ahead=new Vector3(MathF.Sin(_yaw3D),0,MathF.Cos(_yaw3D));
            _target3D+=(-io.MouseDelta.X*right+io.MouseDelta.Y*ahead)*(_distance3D/Math.Max(100,size.Y));_cameraDirty=true;_followPlayer=false;
        }
        if(_followPlayer&&frame?.Players.FirstOrDefault(p=>p.Player==_selected) is {} followed)_target3D=World(followed);
        var eye=_target3D+new Vector3(MathF.Sin(_yaw3D)*MathF.Cos(_pitch3D),MathF.Sin(_pitch3D),MathF.Cos(_yaw3D)*MathF.Cos(_pitch3D))*_distance3D;
        var projection=Matrix4x4.CreateLookAt(eye,_target3D,Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfView(.82f,size.X/size.Y,.2f,3000);
        try {
            _renderer??=new ReplayGpuRenderer(_plugin.PluginInterface.UiBuilder.DeviceHandle,_mesh);
            float ratio=Math.Min(1,Math.Min(1280/size.X,900/size.Y));int width=Math.Max(64,(int)(size.X*ratio)),height=Math.Max(64,(int)(size.Y*ratio));
            if(_renderer.Width!=width||_renderer.Height!=height) { _renderer.Resize(width,height);_cameraDirty=true; }
            if(_cameraDirty||!_has3DFrame||(_renderSeconds!=_seconds&&ImGui.GetTime()-_renderAt>=1.0/30)) {
                Build3DScene(frame,now);_renderer.Render(projection,_scene.Vertices);_renderAt=ImGui.GetTime();_renderSeconds=_seconds;_projection3D=projection;_renderedFrame=frame;_has3DFrame=true;_cameraDirty=false;_renderSelected=_selected;_renderFollow=_followPlayer;
            }
            var draw=ImGui.GetWindowDrawList();draw.AddImage(_renderer.Texture,origin,origin+size);
            draw.PushClipRect(origin,origin+size,true);
            try {
            if(_renderedFrame!=null)foreach(var player in _renderedFrame.Players) {
                if(!float.IsFinite(player.X)||!float.IsFinite(player.Z))continue;
                if(!Project3D(World(player)+new Vector3(0,.15f,0),origin,size,out var at))continue;
                var job=ReplayJob(player);var half=new Vector2(8*ImGuiHelpers.GlobalScale);at+=new Vector2(0,12*ImGuiHelpers.GlobalScale);
                if(job is {} known&&TextureHelper.JobIcons.TryGetValue(known,out var icon)) {
                    draw.AddRectFilled(at-half-new Vector2(2),at+half+new Vector2(2),ImGui.GetColorU32(new Vector4(.02f,.03f,.05f,.9f)),3);
                    draw.AddImage(_plugin.WindowManager.GetTextureHandle(icon),at-half,at+half);
                }
                if(hovered&&Vector2.Distance(ImGui.GetMousePos(),at)<14*ImGuiHelpers.GlobalScale) {
                    if(ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { _selected=player.Player;_cameraDirty=true; }
                    ImGui.SetTooltip(player.Player+"\n"+(job.HasValue?PlayerJobHelper.GetNameFromJob(job.Value):"职业未知"));
                }
            }
            if(_showMechanics&&(_data.MapMechanics.Any(e=>e.MapWide&&e.Timestamp<=now&&e.Timestamp>=now.AddSeconds(-3))||_timeline.MapEvents?.Any(e=>e.Type==PvpStats.Types.Match.CrystallineConflictMatchEvent.SpecialEvent&&e.Timestamp<=now&&e.Timestamp>=now.AddSeconds(-3))==true))
                draw.AddText(origin+new Vector2(10),ImGui.GetColorU32(ModernTheme.Gold),"地图机制触发 · 计时器观测");
            } finally { draw.PopClipRect(); }
        } catch(Exception e) { _renderError="三维渲染初始化失败，可重试或使用二维回放。";_plugin.Log.Error(e,"Experimental 3D replay failed.");_renderer?.DisposeAfterFrame(_plugin.PluginInterface.UiBuilder);_renderer=null; }
    }
    private bool Project3D(Vector3 world,Vector2 origin,Vector2 size,out Vector2 screen) {
        var clip=Vector4.Transform(new Vector4(world,1),_projection3D);screen=default;
        if(clip.W<=.01f||clip.Z<0||clip.Z>clip.W)return false;
        screen=origin+new Vector2((clip.X/clip.W+1)*.5f*size.X,(1-clip.Y/clip.W)*.5f*size.Y);return true;
    }
    private void Build3DScene(MovementFrame? frame,DateTime now) {
        _scene.Clear();
        if(_showCrystalTrail) {
            MovementFrame? prev=null,anchor=null;int segments=0;
            foreach(var f in _data.Frames) {
                if(f.Timestamp>now)break;
                if(prev!=null&&ReplayObservation.CrystalSegment(prev,f,_data.FrameGapSeconds)) {
                    var a=World((anchor??prev).Crystal!);var b=World(f.Crystal!);
                    if(Vector3.DistanceSquared(a,b)>=.16f||(f.Timestamp-(anchor??prev).Timestamp).TotalSeconds>=1) {
                        if(segments++<5000)_scene.Ribbon(a,b,.18f,ReplaySceneGeometry.Rgba(1,.74f,.15f));anchor=f;
                    }
                } else anchor=f;
                prev=f;
            }
        }
        if(frame!=null)foreach(var p in frame.Players)if(float.IsFinite(p.X)&&float.IsFinite(p.Z))_scene.Pawn(World(p),float.IsFinite(p.Heading)?p.Heading:0,p.HP==0,p.Player==_selected,p.Team);
        if(frame?.Crystal is {} crystal&&float.IsFinite(crystal.X)&&float.IsFinite(crystal.Z)) {
            var at=World(crystal);_scene.Diamond(at+new Vector3(0,.5f,0),1.4f,5.5f,ReplaySceneGeometry.Rgba(.25f,.75f,1));
            _scene.Ring(at+new Vector3(0,.08f,0),_radius3D,.15f,ReplaySceneGeometry.Rgba(.3f,.85f,1));
        }
        if(_showMechanics)foreach(var e in _data.MapMechanics.Where(e=>!e.MapWide&&e.X.HasValue&&e.Z.HasValue&&e.Timestamp<=now&&e.Timestamp>=now.AddSeconds(-3))) {
            var at=new Vector3(e.X!.Value,_mesh!.EstimatedHeight(e.X.Value,e.Z!.Value),e.Z!.Value);_scene.Diamond(at,.65f,2,ReplaySceneGeometry.Rgba(1,.8f,.2f));
        }
        foreach(var death in _timeline.Kills?.Where(k=>k.Timestamp<=now&&k.Timestamp>=now.AddSeconds(-3))??[]) {
            var p=DetailedAnalysis.FrameAt(_data,death.Timestamp)?.Players.FirstOrDefault(p=>p.Player==death.Victim.ToString());if(p==null)continue;var at=World(p)+new Vector3(0,.1f,0);
            _scene.Ribbon(at+new Vector3(-1,0,-1),at+new Vector3(1,0,1),.3f,ReplaySceneGeometry.Rgba(1,.15f,.2f));_scene.Ribbon(at+new Vector3(-1,0,1),at+new Vector3(1,0,-1),.3f,ReplaySceneGeometry.Rgba(1,.15f,.2f));
        }
    }
    public void Dispose() {
        if(_disposed)return;_disposed=true;_renderer?.DisposeAfterFrame(_plugin.PluginInterface.UiBuilder);_renderer=null;_mesh=null;
    }
}
