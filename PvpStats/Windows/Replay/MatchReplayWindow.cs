using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using PvpStats.Analysis;
using PvpStats.Interface;
using PvpStats.Types.Match;
using PvpStats.Types.Match.Timeline;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;

namespace PvpStats.Windows.Replay;
internal sealed class MatchReplayWindow : Window {
    private readonly Plugin _plugin;
    private readonly CrystallineConflictMatch _match;
    private readonly CrystallineConflictMatchTimeline _timeline;
    private readonly DetailedTelemetry _data;
    private readonly DateTime _origin;
    private readonly float _duration;
    private readonly Vector2 _min, _span;
    private float _seconds;
    private int _speed = 1;
    private bool _playing, _exporting;
    private string _selected = "", _exportStatus = "";
    internal MatchReplayWindow(Plugin plugin, CrystallineConflictMatch match, CrystallineConflictMatchTimeline timeline) : base("地图回放###CCReplay" + match.Id) {
        _plugin=plugin; _match=match; _timeline=timeline; _data=timeline.Detailed ?? new();
        _origin=match.MatchStartTime ?? _data.Frames.FirstOrDefault()?.Timestamp ?? match.DutyStartTime;
        _duration=Math.Max(1,(float)((match.MatchEndTime ?? _data.Frames.LastOrDefault()?.Timestamp ?? _origin)-_origin).TotalSeconds);
        Size=new Vector2(1100,800); SizeCondition=ImGuiCond.FirstUseEver;
        SizeConstraints=new() { MinimumSize=new(800,580), MaximumSize=new(2400,1800) };
        var points=_data.Frames.SelectMany(f=>f.Players).Where(p=>float.IsFinite(p.X)&&float.IsFinite(p.Z)).ToArray();
        _min=points.Length==0 ? new(-100,-100) : new(points.Min(p=>p.X)-10,points.Min(p=>p.Z)-10);
        var max=points.Length==0 ? new Vector2(100,100) : new Vector2(points.Max(p=>p.X)+10,points.Max(p=>p.Z)+10);
        var extent=Math.Max(max.X-_min.X,max.Y-_min.Y);
        _span=new(Math.Max(1,extent));
        IsOpen=true;
    }
    public override void OnClose() => _plugin.WindowManager.RemoveWindow(this);
    private Vector2 Uv(PlayerPosition p) => _data.MapSizeFactor > 0
        ? DetailedAnalysis.WorldToUv(p.X,p.Z,_data.MapSizeFactor,_data.MapOffsetX,_data.MapOffsetY) : (new Vector2(p.X,p.Z)-_min)/_span;
    private static bool InMap(Vector2 p) => float.IsFinite(p.X)&&float.IsFinite(p.Y)&&p.X>=0&&p.X<=1&&p.Y>=0&&p.Y<=1;
    public override void Draw() {
        ImGui.SetWindowFontScale(1);
        var scale=ImGuiHelpers.GlobalScale;
        ImGui.TextWrapped("客户端观测数据重建；采集已加载对象（含镜头外），每 0.5 秒采样，不插补缺失位置。轨迹线仅连接采样点，不是录像或完整服务端记录。");
        if(_data.Frames.Count==0) ImGui.TextWrapped("此记录没有走位采样。旧战绩无法补抓；仍可拖动时间轴查看已有减员与水晶推进记录。");
        if(_data.FramesTruncated||_data.EffectsTruncated) ImGui.TextColored(ModernTheme.Gold,"记录达到容量上限，部分数据未保存。");
        if(ModernTheme.Button(_playing ? "暂停###ReplayPlay" : "播放###ReplayPlay")) _playing=!_playing;
        ImGui.SameLine(); if(ModernTheme.Button("回到开始###ReplayReset")) { _seconds=0; _playing=false; }
        ImGui.SameLine(); ImGui.SetNextItemWidth(100*scale);
        ImGui.Combo("倍速###ReplaySpeed",ref _speed,new[]{"0.5×","1×","2×","4×"},4);
        if(_playing) { _seconds=Math.Min(_duration,_seconds+ImGui.GetIO().DeltaTime*new[]{.5f,1,2,4}[_speed]); if(_seconds>=_duration)_playing=false; }
        ImGui.SetNextItemWidth(-1);
        if(ImGui.SliderFloat("###ReplayTime",ref _seconds,0,_duration,"%.1f 秒")) _playing=false;
        var now=_origin.AddSeconds(_seconds);
        ImGui.TextUnformatted($"{TimeSpan.FromSeconds(_seconds):mm\\:ss} / {TimeSpan.FromSeconds(_duration):mm\\:ss}  ·  {_data.Frames.Count:N0} 帧");
        var frame=DetailedAnalysis.FrameAt(_data,now);
        var side=Math.Max(180*scale,Math.Min(ImGui.GetContentRegionAvail().X*.64f,ImGui.GetContentRegionAvail().Y-90*scale));
        using(var canvas=ImRaii.Child("ReplayMap",new Vector2(side+8*scale,0),false)) {
            if(canvas) {
                var origin=ImGui.GetCursorScreenPos(); var draw=ImGui.GetWindowDrawList();
                ImGui.InvisibleButton("###ReplayCanvas",new Vector2(side));
                draw.AddRectFilled(origin,origin+new Vector2(side),ImGui.GetColorU32(new Vector4(.035f,.055f,.08f,1)));
                bool loaded=false;
                if(_data.MapTexture.Length>0) {
                    try { var texture=_plugin.TextureProvider.GetFromGame(_data.MapTexture).GetWrapOrEmpty(); if(texture.Width>=256) { draw.AddImage(texture.Handle,origin,origin+new Vector2(side)); loaded=true; } } catch { }
                }
                if(!loaded) for(int i=1;i<8;i++) {
                    var t=side*i/8f; var color=ImGui.GetColorU32(new Vector4(.2f,.27f,.35f,.6f));
                    draw.AddLine(origin+new Vector2(t,0),origin+new Vector2(t,side),color);
                    draw.AddLine(origin+new Vector2(0,t),origin+new Vector2(side,t),color);
                }
                if(frame!=null) {
                    if(_selected.Length>0) {
                        PlayerPosition? previous=null; DateTime? previousTime=null;
                        foreach(var f in _data.Frames.Where(f=>f.Timestamp<=now&&f.Timestamp>=now.AddSeconds(-10))) {
                            var p=f.Players.FirstOrDefault(p=>p.Player==_selected);
                            if(p!=null && previous!=null && previousTime.HasValue && f.Timestamp-previousTime.Value<=TimeSpan.FromSeconds(1)
                                && p.HP>0 && previous.HP>0 && InMap(Uv(p)) && InMap(Uv(previous))
                                && !(_timeline.Kills?.Any(k=>k.Victim.ToString()==_selected&&k.Timestamp>previousTime&&k.Timestamp<=f.Timestamp)??false))
                                draw.AddLine(origin+Uv(previous)*side,origin+Uv(p)*side,ImGui.GetColorU32(ModernTheme.Gold),2*scale);
                            previous=p; previousTime=f.Timestamp;
                        }
                    }
                    foreach(var p in frame.Players) {
                        var uv=Uv(p); if(!InMap(uv))continue;
                        var at=origin+uv*side;
                        var color=p.HP==0?new Vector4(.55f,.55f,.58f,1):p.Team==1?new Vector4(.20f,.75f,1,1):new Vector4(1,.35f,.42f,1);
                        draw.AddCircleFilled(at,5*scale,ImGui.GetColorU32(color));
                        draw.AddLine(at,at+new Vector2(MathF.Sin(p.Heading),MathF.Cos(p.Heading))*12*scale,ImGui.GetColorU32(color),1.5f*scale);
                        if(p.Player==_selected)draw.AddCircle(at,9*scale,ImGui.GetColorU32(ModernTheme.Gold),0,2*scale);
                        if(Vector2.Distance(ImGui.GetMousePos(),at)<10*scale && ImGui.IsWindowHovered()) {
                            ImGui.SetTooltip($"{p.Player}\n体力 {p.HP:N0}/{p.MaxHP:N0}\n世界坐标 {p.X:F1}, {p.Z:F1}");
                            if(ImGui.IsMouseClicked(ImGuiMouseButton.Left))_selected=p.Player;
                        }
                    }
                }
                foreach(var death in _timeline.Kills?.Where(k=>k.Timestamp<=now&&k.Timestamp>=now.AddSeconds(-3))??[]) {
                    var deathFrame=DetailedAnalysis.FrameAt(_data,death.Timestamp);
                    var victim=deathFrame?.Players.FirstOrDefault(p=>p.Player==death.Victim.ToString()); if(victim==null||!InMap(Uv(victim)))continue;
                    var at=origin+Uv(victim)*side;var delta=new Vector2(7*scale);var color=ImGui.GetColorU32(new Vector4(1,.25f,.3f,1));
                    draw.AddLine(at-delta,at+delta,color,2*scale);draw.AddLine(at+new Vector2(-delta.X,delta.Y),at+new Vector2(delta.X,-delta.Y),color,2*scale);
                }
                ImGui.TextWrapped(loaded ? "游戏内地图纹理 · 蓝：星极队，红：灵极队，灰：采样体力归零；十字为减员事件的相邻采样位置。" : "地图纹理未加载，当前为坐标网格；有地图参数时按真实地图坐标定位，否则为相对坐标示意。");
                if(frame==null)ImGui.TextColored(ModernTheme.Gold,"当前时点没有可用位置采样，不显示推测位置。");
            }
        }
        ImGui.SameLine();
        using var panel=ImRaii.Child("ReplayEvents",Vector2.Zero,true);
        if(!panel)return;
        ImGui.TextUnformatted($"已加载并识别角色：{frame?.Players.Count??0}/10");
        foreach(var team in new[]{1,2}) {
            var observed=frame?.Players.Where(p=>p.Team==team).ToArray()??[];
            var expected=_match.Players.Count(p=>(int?)p.Team==team);
            ImGui.TextUnformatted($"{(team==1?"星极队":"灵极队")}  存活 {observed.Count(p=>p.HP>0)} · 倒地 {observed.Count(p=>p.HP==0)} · 未知 {Math.Max(0,expected-observed.Length)}");
        }
        ImGui.Separator();
        var crystal=frame?.CrystalProgress ?? _timeline.CrystalPosition?.LastOrDefault(p=>p.Timestamp<=now)?.Points;
        ImGui.TextUnformatted("水晶相对推进（非地图坐标）");
        if(crystal.HasValue) ImGui.ProgressBar(Math.Clamp((crystal.Value/10f+100)/200,0,1),new Vector2(-1,0),$"{crystal.Value/10f:F1}%");
        else ImGui.TextDisabled("此时点未保存水晶进度。");
        if(frame!=null)ImGui.TextUnformatted($"星极 {frame.AstraProgress/10f:F1}% · 灵极 {frame.UmbraProgress/10f:F1}%");
        ImGui.Separator();ImGui.TextUnformatted("选择角色查看最近 10 秒采样轨迹");
        foreach(var player in _match.Players) {
            var alias=player.Alias.ToString(); if(ImGui.Selectable(alias+"###ReplayPlayer"+alias,_selected==alias))_selected=alias;
        }
        ImGui.Separator();ImGui.TextUnformatted("最近减员");
        foreach(var death in (_timeline.Kills??[]).Where(k=>k.Timestamp<=now).OrderByDescending(k=>k.Timestamp).Take(6))
            ImGui.TextWrapped($"{(death.Timestamp-_origin):mm\\:ss}  {death.Victim}  ←  {death.CreditedKiller?.ToString()??"未归因"}");
        ImGui.Separator();
        using(var disabled=ImRaii.Disabled(_exporting)) if(ModernTheme.Button("导出观测记录###ExportReplay"))Export();
        ImGui.TextWrapped(_exportStatus);
    }
    private void Export() {
        _exporting=true;_exportStatus="正在导出…";
        var path=Path.Combine(_plugin.PluginInterface.GetPluginConfigDirectory(),"replays",_match.Id+".json");
        _=Task.Run(async()=> {
            try {
                var payload=new { MatchId=_match.Id.ToString(), _match.TerritoryId, StartUtc=_origin, _match.MatchEndTime, Telemetry=_data,
                    Knockouts=_timeline.Kills?.Select(k=>new { k.Timestamp,Victim=k.Victim.ToString(),Killer=k.CreditedKiller?.ToString() }),
                    CrystalProgress=_timeline.CrystalPosition };
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path,JsonSerializer.Serialize(payload,new JsonSerializerOptions { WriteIndented=true }));
                _exportStatus="已导出："+path;
            } catch(Exception e) { _exportStatus="导出失败，请查看日志。";_plugin.Log.Error(e,"Replay export failed."); }
            finally { _exporting=false; }
        });
    }
}
