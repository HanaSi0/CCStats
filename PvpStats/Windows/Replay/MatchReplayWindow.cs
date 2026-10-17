using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using PvpStats.Analysis;
using PvpStats.Interface;
using PvpStats.Helpers;
using PvpStats.Types.Match;
using PvpStats.Types.Match.Timeline;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;

namespace PvpStats.Windows.Replay;
internal sealed partial class MatchReplayWindow : Window {
    private readonly Plugin _plugin;
    private readonly CrystallineConflictMatch _match;
    private readonly CrystallineConflictMatchTimeline _timeline;
    private readonly DetailedTelemetry _data;
    private readonly DateTime _origin;
    private readonly float _duration;
    private readonly Vector2 _min, _span;
    private readonly MapCamera _camera=new();
    private bool _showInfo=true, _showCrystalTrail=true, _showMechanics=true;
    private Vector2 _lastWindowSize;
    private float _seconds;
    private int _speed = 1;
    private bool _playing, _exporting;
    private string _selected = "", _exportStatus = "";
    internal MatchReplayWindow(Plugin plugin, CrystallineConflictMatch match, CrystallineConflictMatchTimeline timeline) : base("地图回放###CCReplay" + match.Id) {
        _plugin=plugin; _match=match; _timeline=timeline; _data=timeline.Detailed ?? new();
        _origin=match.MatchStartTime ?? _data.Frames.FirstOrDefault()?.Timestamp ?? match.DutyStartTime;
        _duration=Math.Max(1,(float)((match.MatchEndTime ?? _data.Frames.LastOrDefault()?.Timestamp ?? _origin)-_origin).TotalSeconds);
        Size=ViewLayout.ClampWindow(plugin.Configuration.ReplayWindowSize??new Vector2(1120,800),ImGui.GetMainViewport().WorkSize/ImGuiHelpers.GlobalScale); SizeCondition=ImGuiCond.FirstUseEver;
        SizeConstraints=new() { MinimumSize=new(800,580), MaximumSize=new(2400,1800) };
        var points=_data.Frames.SelectMany(f=>f.Players).Where(p=>float.IsFinite(p.X)&&float.IsFinite(p.Z)).ToArray();
        _min=points.Length==0 ? new(-100,-100) : new(points.Min(p=>p.X)-10,points.Min(p=>p.Z)-10);
        var max=points.Length==0 ? new Vector2(100,100) : new Vector2(points.Max(p=>p.X)+10,points.Max(p=>p.Z)+10);
        var extent=Math.Max(max.X-_min.X,max.Y-_min.Y);
        _span=new(Math.Max(1,extent));
        _camera.Fit(points.Select(Uv).Concat(_data.Frames.Where(f=>f.Crystal!=null).Select(f=>Uv(new PlayerPosition { X=f.Crystal!.X,Z=f.Crystal.Z }))));
        IsOpen=true;
    }
    public override void PreDraw() => WindowChrome.Constrain(this);
    public override void OnClose() {
        if(_lastWindowSize.X>0) { _plugin.Configuration.ReplayWindowSize=_lastWindowSize; _plugin.DataQueue.QueueDataOperation(_plugin.Configuration.Save); }
        _plugin.WindowManager.RemoveWindow(this);
    }
    private Vector2 Uv(PlayerPosition p) => _data.MapSizeFactor > 0
        ? DetailedAnalysis.WorldToUv(p.X,p.Z,_data.MapSizeFactor,_data.MapOffsetX,_data.MapOffsetY) : (new Vector2(p.X,p.Z)-_min)/_span;
    private static bool InMap(Vector2 p) => float.IsFinite(p.X)&&float.IsFinite(p.Y)&&p.X>=0&&p.X<=1&&p.Y>=0&&p.Y<=1;
    public override void Draw() {
        ImGui.SetWindowFontScale(1);
        _lastWindowSize=ImGui.GetWindowSize()/ImGuiHelpers.GlobalScale;
        var scale=ImGuiHelpers.GlobalScale;
        ImGui.TextDisabled($"已加载对象观测 · 目标采样 {1/_data.SampleIntervalSeconds:F0} 次/秒 · 不补造缺失坐标");
        if(_data.MovementDownsampled)ImGui.TextDisabled("长局记录已为存储容量降低采样密度。");
        if(_data.Frames.Count==0) ImGui.TextWrapped("此记录没有走位采样。旧战绩无法补抓；仍可拖动时间轴查看已有减员与水晶推进记录。");
        if(_data.FramesTruncated||_data.EffectsTruncated) ImGui.TextColored(ModernTheme.Gold,"记录达到容量上限，部分数据未保存。");
        if(ModernTheme.Button(_playing ? "暂停###ReplayPlay" : "播放###ReplayPlay")) _playing=!_playing;
        ImGui.SameLine(); if(ModernTheme.Button("回到开始###ReplayReset")) { _seconds=0; _playing=false; }
        ImGui.SameLine(); ImGui.SetNextItemWidth(100*scale);
        ImGui.Combo("倍速###ReplaySpeed",ref _speed,new[]{"0.5×","1×","2×","4×"},4);
        ImGui.SameLine(); if(ModernTheme.Button(_showInfo?"收起信息###ReplayInfo":"显示信息###ReplayInfo"))_showInfo=!_showInfo;
        if(ModernTheme.Button("−###MapZoomOut"))_camera.ZoomAt(1/1.3f,new Vector2(.5f),1);
        ImGui.SameLine(); if(ModernTheme.Button("＋###MapZoomIn"))_camera.ZoomAt(1.3f,new Vector2(.5f),1);
        ImGui.SameLine(); if(ModernTheme.Button("重置视角###MapFit"))_camera.Reset();
        ImGui.SameLine(); if(ModernTheme.Button("完整地图###MapFull"))_camera.FullMap();
        ImGui.SameLine(); ImGui.TextDisabled($"{_camera.Zoom:F1}× · 滚轮缩放，左键拖动地图");
        ImGui.Checkbox("水晶轨迹###CrystalTrail",ref _showCrystalTrail); ImGui.SameLine();
        ImGui.Checkbox("地图事件###MapMechanics",ref _showMechanics);
        ImGui.SameLine(); if(ModernTheme.SmallButton("上一帧###ReplayPrevious"))StepFrame(-1);
        ImGui.SameLine(); if(ModernTheme.SmallButton("下一帧###ReplayNext"))StepFrame(1);
        if(_playing) { _seconds=Math.Min(_duration,_seconds+ImGui.GetIO().DeltaTime*new[]{.5f,1,2,4}[_speed]); if(_seconds>=_duration)_playing=false; }
        ImGui.SetNextItemWidth(-1);
        if(ImGui.SliderFloat("###ReplayTime",ref _seconds,0,_duration,"%.2f 秒")) _playing=false;
        var now=_origin.AddSeconds(_seconds);
        ImGui.TextUnformatted($"{TimeSpan.FromSeconds(_seconds):mm\\:ss} / {TimeSpan.FromSeconds(_duration):mm\\:ss}  ·  {_data.Frames.Count:N0} 帧");
        var frame=DetailedAnalysis.FrameAt(_data,now);
        var available=ImGui.GetContentRegionAvail();
        var flank=_showInfo?Math.Clamp(available.X*.22f,150*scale,220*scale):0;
        var mapWidth=Math.Max(1,available.X-(_showInfo?2*(flank+ImGui.GetStyle().ItemSpacing.X):0));
        var logHeight=_showReplayLog?Math.Min(160*scale,available.Y*.3f):0;
        var innerMapWidth=Math.Max(1,mapWidth-2*ImGui.GetStyle().WindowPadding.X);
        var mapHeight=Math.Max(1,available.Y-logHeight-ImGui.GetFrameHeightWithSpacing()-24*scale);
        var side=Math.Max(1,Math.Min(innerMapWidth,mapHeight));
        if(_showInfo) { DrawTeamPanel(1,frame,now,new Vector2(flank,0)); ImGui.SameLine(); }
        using(var canvas=ImRaii.Child("ReplayMap",new Vector2(mapWidth,0),false)) {
            if(canvas) {
                var mapStart=ImGui.GetCursorPos();
                ImGui.SetCursorPos(mapStart+new Vector2(Math.Max(0,(ImGui.GetContentRegionAvail().X-side)/2),Math.Max(0,(mapHeight-side)/2)));
                var origin=ImGui.GetCursorScreenPos(); var draw=ImGui.GetWindowDrawList();
                ImGui.InvisibleButton("###ReplayCanvas",new Vector2(side));
                var hovered=ImGui.IsItemHovered();
                if(hovered && ImGui.GetIO().MouseWheel!=0)_camera.ZoomAt(MathF.Pow(1.2f,ImGui.GetIO().MouseWheel),ImGui.GetMousePos()-origin,side);
                if(ImGui.IsItemActive()&&ImGui.IsMouseDragging(ImGuiMouseButton.Left))_camera.Pan(ImGui.GetIO().MouseDelta,side);
                Vector2 At(Vector2 uv)=>origin+_camera.Project(uv,side);
                draw.PushClipRect(origin,origin+new Vector2(side),true);
                draw.AddRectFilled(origin,origin+new Vector2(side),ImGui.GetColorU32(new Vector4(.035f,.055f,.08f,1)));
                bool loaded=false;
                if(_data.MapTexture.Length>0) {
                    try { var texture=_plugin.TextureProvider.GetFromGame(_data.MapTexture).GetWrapOrEmpty(); if(texture.Width>=256) { draw.AddImage(texture.Handle,At(Vector2.Zero),At(Vector2.One)); loaded=true; } } catch { }
                }
                if(!loaded) for(int i=1;i<8;i++) {
                    var t=side*i/8f; var color=ImGui.GetColorU32(new Vector4(.2f,.27f,.35f,.6f));
                    draw.AddLine(At(new Vector2(i/8f,0)),At(new Vector2(i/8f,1)),color);
                    draw.AddLine(At(new Vector2(0,i/8f)),At(new Vector2(1,i/8f)),color);
                }
                if(_showCrystalTrail) DrawCrystalTrail(draw,At,now,scale);
                if(_showMechanics) DrawMechanics(draw,At,origin,side,now,scale,hovered);
                if(frame!=null) {
                    if(_selected.Length>0) {
                        PlayerPosition? previous=null; DateTime? previousTime=null;
                        foreach(var f in _data.Frames.Where(f=>f.Timestamp<=now&&f.Timestamp>=now.AddSeconds(-10))) {
                            var p=f.Players.FirstOrDefault(p=>p.Player==_selected);
                            if(p!=null && previous!=null && previousTime.HasValue && f.Timestamp-previousTime.Value<=TimeSpan.FromSeconds(_data.FrameGapSeconds)
                                && p.HP>0 && previous.HP>0 && InMap(Uv(p)) && InMap(Uv(previous))
                                && !(_timeline.Kills?.Any(k=>k.Victim.ToString()==_selected&&k.Timestamp>previousTime&&k.Timestamp<=f.Timestamp)??false))
                                draw.AddLine(At(Uv(previous)),At(Uv(p)),ImGui.GetColorU32(ModernTheme.Gold),2*scale);
                            previous=p; previousTime=f.Timestamp;
                        }
                    }
                    foreach(var p in frame.Players) {
                        var uv=Uv(p); if(!InMap(uv))continue;
                        var at=At(uv);
                        var color=p.HP==0?new Vector4(.55f,.55f,.58f,1):p.Team==1?new Vector4(.20f,.75f,1,1):new Vector4(1,.35f,.42f,1);
                        draw.AddCircleFilled(at,10*scale,ImGui.GetColorU32(color));
                        var job=ReplayJob(p);
                        if(job is {} knownJob&&TextureHelper.JobIcons.TryGetValue(knownJob,out var jobIcon)) {
                            var half=new Vector2(8*scale);
                            draw.AddImage(_plugin.WindowManager.GetTextureHandle(jobIcon),at-half,at+half);
                            if(p.HP==0)draw.AddRectFilled(at-half,at+half,ImGui.GetColorU32(new Vector4(.15f,.15f,.18f,.7f)));
                        }
                        // Direction extends outside the icon so it remains readable at small sizes.
                        var heading=new Vector2(MathF.Sin(p.Heading),MathF.Cos(p.Heading));
                        draw.AddLine(at+heading*11*scale,at+heading*20*scale,ImGui.GetColorU32(new Vector4(.03f,.03f,.05f,1)),4*scale);
                        draw.AddLine(at+heading*11*scale,at+heading*20*scale,ImGui.GetColorU32(color),2*scale);
                        if(p.Player==_selected)draw.AddCircle(at,12*scale,ImGui.GetColorU32(ModernTheme.Gold),0,2*scale);
                        if(Vector2.Distance(ImGui.GetMousePos(),at)<12*scale && hovered) {
                            ImGui.SetTooltip($"{p.Player}\n{(job.HasValue?PlayerJobHelper.GetNameFromJob(job.Value):"职业未知")}\n体力 {p.HP:N0}/{p.MaxHP:N0}\n世界坐标 {p.X:F1}, {p.Z:F1}");
                            if(ImGui.IsMouseClicked(ImGuiMouseButton.Left))_selected=p.Player;
                        }
                    }
                }
                if(frame?.Crystal is {} tacticalCrystal) {
                    var crystalUv=Uv(new PlayerPosition { X=tacticalCrystal.X,Z=tacticalCrystal.Z });
                    if(InMap(crystalUv)) {
                        var point=At(crystalUv); var d=9*scale; var gold=ImGui.GetColorU32(ModernTheme.Gold);
                        draw.AddCircleFilled(point,13*scale,ImGui.GetColorU32(new Vector4(.04f,.04f,.06f,.95f)));
                        draw.AddQuadFilled(point+new Vector2(0,-d),point+new Vector2(d,0),point+new Vector2(0,d),point+new Vector2(-d,0),gold);
                        draw.AddCircle(point,13*scale,gold,0,2*scale);
                        if(hovered&&Vector2.Distance(ImGui.GetMousePos(),point)<15*scale) ImGui.SetTooltip($"战术水晶\n世界坐标 {tacticalCrystal.X:F1}, {tacticalCrystal.Z:F1}");
                    }
                }
                foreach(var death in _timeline.Kills?.Where(k=>k.Timestamp<=now&&k.Timestamp>=now.AddSeconds(-3))??[]) {
                    var deathFrame=DetailedAnalysis.FrameAt(_data,death.Timestamp);
                    var victim=deathFrame?.Players.FirstOrDefault(p=>p.Player==death.Victim.ToString()); if(victim==null||!InMap(Uv(victim)))continue;
                    var at=At(Uv(victim));var delta=new Vector2(7*scale);var color=ImGui.GetColorU32(new Vector4(1,.25f,.3f,1));
                    draw.AddLine(at-delta,at+delta,color,2*scale);draw.AddLine(at+new Vector2(-delta.X,delta.Y),at+new Vector2(delta.X,-delta.Y),color,2*scale);
                }
                draw.PopClipRect();
                ImGui.SetCursorPos(mapStart+new Vector2(0,mapHeight+ImGui.GetStyle().ItemSpacing.Y));
                if(!loaded) draw.AddText(origin+new Vector2(8*scale),ImGui.GetColorU32(ModernTheme.Gold),_data.MapSizeFactor>0?"地图纹理未加载 · 坐标网格":"相对坐标示意");
                DrawReplayLog(now,logHeight);
            }
        }
        if(_showInfo) { ImGui.SameLine(); DrawTeamPanel(2,frame,now,new Vector2(flank,0)); }
    }
    private void StepFrame(int direction) {
        var time=ReplayObservation.Step(_data,_origin.AddSeconds(_seconds),direction);
        if(time.HasValue)_seconds=Math.Clamp((float)(time.Value-_origin).TotalSeconds,0,_duration);
        _playing=false;
    }
    private void Export() {
        _exporting=true;_exportStatus="正在导出…";
        var path=Path.Combine(_plugin.PluginInterface.GetPluginConfigDirectory(),"replays",_match.Id+".json");
        _=Task.Run(async()=> {
            try {
                var payload=new { MatchId=_match.Id.ToString(), _match.TerritoryId, StartUtc=_origin, _match.MatchEndTime, Telemetry=_data,
                    Knockouts=_timeline.Kills?.Select(k=>new { k.Timestamp,Victim=k.Victim.ToString(),Killer=k.CreditedKiller?.ToString() }),
                    CrystalProgress=_timeline.CrystalPosition, MapEvents=_timeline.MapEvents };
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path,JsonSerializer.Serialize(payload,new JsonSerializerOptions { WriteIndented=true }));
                _exportStatus="已导出："+path;
            } catch(Exception e) { _exportStatus="导出失败，请查看日志。";_plugin.Log.Error(e,"Replay export failed."); }
            finally { _exporting=false; }
        });
    }
}
