using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using PvpStats.Analysis;
using PvpStats.Helpers;
using PvpStats.Interface;
using PvpStats.Types.Match;
using PvpStats.Types.Player;
namespace PvpStats.Windows.Replay;
internal sealed partial class MatchReplayWindow {
    private readonly Dictionary<uint,Job?> _replayJobs=new();
    private Lazy<ReplayArenaMask?>? _arenaMask2D;
    private Job? ReplayJob(PlayerPosition sample) {
        if(sample.ClassJobId>0) {
            if(!_replayJobs.TryGetValue(sample.ClassJobId,out var job)) {
                try { job=Enum.TryParse<Job>(_plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>().GetRow(sample.ClassJobId).Abbreviation.ToString(),true,out var known)?known:null; }
                catch { job=null; }
                _replayJobs[sample.ClassJobId]=job;
            }
            if(job.HasValue)return job;
        }
        return _match.Players.FirstOrDefault(p=>p.Alias.ToString()==sample.Player)?.Job;
    }
    private void DrawTeamPanel(int team,MovementFrame? frame,DateTime now,Vector2 size) {
        using var panel=ImRaii.Child("ReplayTeam"+team,size,true);
        if(!panel)return;
        var scale=ImGuiHelpers.GlobalScale;
        var color=team==1?new Vector4(.2f,.75f,1,1):new Vector4(1,.35f,.42f,1);
        ImGui.TextColored(color,team==1?"星极队":"灵极队");
        var observedTeam=frame?.Players.Where(p=>p.Team==team).ToArray()??[];
        var total=_match.Players.Count(p=>(int?)p.Team==team);
        ImGui.TextDisabled($"存活 {observedTeam.Count(p=>p.HP>0)} · 倒地 {observedTeam.Count(p=>p.HP==0)} · 未知 {Math.Max(0,total-observedTeam.Length)}");
        if(frame!=null&&ImGui.IsItemHovered())ImGui.SetTooltip($"推进 {(team==1?frame.AstraProgress:frame.UmbraProgress)/10f:F1}%");
        var members=_match.Players.Where(p=>(int?)p.Team==team).ToArray();
        var rowHeight=Math.Max(1,(ImGui.GetContentRegionAvail().Y-ImGui.GetStyle().ItemSpacing.Y*Math.Max(0,members.Length-1))/Math.Max(1,members.Length));
        foreach(var player in members) {
            var alias=player.Alias.ToString();
            var observed=frame?.Players.FirstOrDefault(p=>p.Player==alias);
            using var id=ImRaii.PushId(alias);
            using var rowPadding=ImRaii.PushStyle(ImGuiStyleVar.WindowPadding,Vector2.Zero);
            using var row=ImRaii.Child("ReplayMember",new Vector2(0,rowHeight),false,ImGuiWindowFlags.NoScrollbar|ImGuiWindowFlags.NoScrollWithMouse);
            if(!row)continue;
            using var compact=ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing,new Vector2(4,2)*scale);
            var job=observed==null?player.Job:ReplayJob(observed);
            if(job is {} known&&TextureHelper.JobIcons.TryGetValue(known,out var icon)) {
                ImGui.Image(_plugin.WindowManager.GetTextureHandle(icon),new Vector2(22*scale));
                if(ImGui.IsItemHovered())ImGui.SetTooltip(PlayerJobHelper.GetNameFromJob(known));
                ImGui.SameLine();
            }
            if(ImGui.Selectable(player.Alias.Name+"###ChooseReplayPlayer",_selected==alias))_selected=alias;
            if(ImGui.IsItemHovered())ImGui.SetTooltip(alias+"\n"+(job.HasValue?PlayerJobHelper.GetNameFromJob(job.Value):"职业未知"));
            if(observed==null) ImGui.TextDisabled("血量／状态：未观测");
            else {
                using(var tint=ImRaii.PushColor(ImGuiCol.PlotHistogram,color))
                    ImGui.ProgressBar(observed.MaxHP>0?Math.Clamp((float)observed.HP/observed.MaxHP,0,1):0,new Vector2(-1,Math.Max(18*scale,ImGui.GetTextLineHeight())),observed.MaxHP>0?$"{observed.HP:N0} / {observed.MaxHP:N0}":"血量上限未知");
                if(observed.HP==0&&ImGui.IsItemHovered())ImGui.SetTooltip("倒地");
                if(_data.Version<4)ImGui.TextDisabled("旧记录未保存完整状态");
                else {
                    using var buffs=ImRaii.Child("ReplayMemberBuffs",new Vector2(0,Math.Max(1,ImGui.GetContentRegionAvail().Y)),false);
                    if(buffs)DrawBuffs(observed,now);
                }
            }
        }
    }
    private bool _showReplayLog;
    private void DrawReplayLog(DateTime now,float height) {
        if(ModernTheme.SmallButton(_showReplayLog?"收起回放记录###ReplayLogToggle":"地图事件与减员###ReplayLogToggle"))_showReplayLog=!_showReplayLog;
        ImGui.SameLine();
        using(var disabled=ImRaii.Disabled(_exporting)) if(ModernTheme.SmallButton("导出观测记录###ExportReplay"))Export();
        if(_exportStatus.Length>0&&ImGui.IsItemHovered())ImGui.SetTooltip(_exportStatus);
        if(!_showReplayLog)return;
        using var log=ImRaii.Child("ReplayLog",new Vector2(0,Math.Max(1,height)),true);
        if(!log)return;
        var summonEvents=_lbEvents??_data.ReplaySkills;
        var summons=ReplaySummons.Resolve(_data.SummonFrames,summonEvents,now);
        ImGui.TextDisabled($"召唤记录：{_data.SummonFrames.Count} 组采样 · {summonEvents.Count(e=>ReplaySummons.IsPetAction(e.ActionId))} 个效果 · 当前 {summons.Summons.Count} 个（估计 {summons.Summons.Count(p=>p.Estimated)}）");
        if(_data.SummonsTruncated)ImGui.TextColored(ModernTheme.Gold,"召唤物记录达到容量上限。");
        if(_data.MapMechanicsTruncated)ImGui.TextColored(ModernTheme.Gold,"环境事件达到记录上限。");
        using var columns=ImRaii.Table("ReplayLogColumns",2,ImGuiTableFlags.SizingStretchSame);
        if(!columns)return;
        ImGui.TableNextColumn(); ImGui.TextUnformatted("地图事件");
        using(var events=ImRaii.Child("ReplayMapEventList",Vector2.Zero,false)) if(events) {
            var entries=_data.MapMechanics.Where(e=>e.Timestamp<=now).OrderByDescending(e=>e.Timestamp).ToArray();
            if(entries.Length==0)ImGui.TextDisabled("暂无事件记录");
            foreach(var entry in entries) {
                using var entryId=ImRaii.PushId(_data.MapMechanics.IndexOf(entry));
                if(ImGui.Selectable($"{(entry.Timestamp-_origin):mm\\:ss} · {ReplayMechanicObservation.Caption(entry,entry.Timestamp)}###Mechanic")) { _seconds=Math.Clamp((float)(entry.Timestamp-_origin).TotalSeconds,0,_duration);_playing=false; }
                if(ImGui.IsItemHovered())ImGui.SetTooltip(entry.Label+"\n"+ReplayMechanicObservation.SourceDescription(entry));
            }
        }
        ImGui.TableNextColumn(); ImGui.TextUnformatted("最近减员");
        using(var deaths=ImRaii.Child("ReplayKnockoutList",Vector2.Zero,false)) if(deaths) {
            var entries=(_timeline.Kills??[]).Where(k=>k.Timestamp<=now).OrderByDescending(k=>k.Timestamp).ToArray();
            if(entries.Length==0)ImGui.TextDisabled("暂无减员记录");
            foreach(var death in entries)ImGui.TextWrapped($"{(death.Timestamp-_origin):mm\\:ss} · {death.Victim}");
        }
    }
    private void DrawBuffs(PlayerPosition player,DateTime now) {
        var scale=ImGuiHelpers.GlobalScale;
        var statuses=player.Statuses.Where(s=>s.Remaining<=0||ReplayObservation.Remaining(s,player.StatusTimestamp,now)>0).ToArray();
        const float iconSize=20;
        var columns=Math.Max(1,(int)((ImGui.GetContentRegionAvail().X+4*scale)/(28*scale)));
        for(int i=0;i<statuses.Length;i++) {
            var status=statuses[i];
            if(i%columns!=0)ImGui.SameLine(0,4*scale);
            using var id=ImRaii.PushId(i);
            using var group=ImRaii.Group();
            _data.ReplayStatuses.TryGetValue(status.Id,out var info);
            if(info?.Icon>0)ImGui.Image(_plugin.WindowManager.GetTextureHandle(info.Icon),new Vector2(iconSize*scale));
            else ImGui.Button("?###Buff",new Vector2(iconSize*scale));
            var label=status.Remaining>0?Math.Ceiling(ReplayObservation.Remaining(status,player.StatusTimestamp,now)).ToString("0"):"—";
            var fontSize=ImGui.GetFontSize()*.72f;
            var textWidth=ImGui.CalcTextSize(label).X*.72f;
            var origin=ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddText(ImGui.GetFont(),fontSize,origin+new Vector2(Math.Max(0,(iconSize*scale-textWidth)/2),0),ImGui.GetColorU32(ImGuiCol.Text),label);
            ImGui.Dummy(new Vector2(Math.Max(iconSize*scale,textWidth),fontSize));
        }
        if(statuses.Length==0)ImGui.TextDisabled("无已记录状态");
    }
    private void DrawCrystalTrail(ImDrawListPtr draw,Func<Vector2,Vector2> at,DateTime now,float scale) {
        MovementFrame? previous=null, anchor=null;
        foreach(var sample in _data.Frames) {
            if(sample.Timestamp>now)break;
            if(previous!=null&&ReplayObservation.CrystalSegment(previous,sample,_data.FrameGapSeconds)) {
                var a=Uv(new PlayerPosition { X=previous.Crystal!.X,Z=previous.Crystal.Z });
                var b=Uv(new PlayerPosition { X=sample.Crystal!.X,Z=sample.Crystal.Z });
                if(anchor?.Crystal is {} start) a=Uv(new PlayerPosition { X=start.X,Z=start.Z });
                if(InMap(a)&&InMap(b) && (Vector2.Distance(at(a),at(b))>=1.5f*scale || (sample.Timestamp-(anchor??previous).Timestamp).TotalSeconds>=1)) {
                    draw.AddLine(at(a),at(b),ImGui.GetColorU32(new Vector4(.08f,.06f,.02f,.8f)),4*scale);
                    draw.AddLine(at(a),at(b),ImGui.GetColorU32(ModernTheme.Gold with { W=.65f }),2*scale);
                    anchor=sample;
                }
            } else anchor=sample;
            previous=sample;
        }
    }
    private void DrawMechanics(ImDrawListPtr draw,Func<Vector2,Vector2> at,Vector2 origin,float side,DateTime now,float scale,bool hovered) {
        var geometry=new ReplaySceneGeometry();
        // Same standable-floor footprint as the 3D view; the small grid is loaded once per window.
        _arenaMask2D??=new(()=>_mesh?.ArenaMask??ReplayArenaMask.FromWalk(ReplayMapMesh.LoadEmbeddedWalk(_match.TerritoryId)));
        var mask=_clipAreas?_arenaMask2D.Value:null;
        foreach(var visual in MapMechanicVisuals.Active(_data.MapMechanics,_match.TerritoryId,now)) {
            using(geometry.ClipTo(mask,(visual.Rule.Ground?visual.Event.TargetY:visual.Event.Y)??visual.Center.Y))MapMechanicVisuals.Draw(geometry,visual,now,(_,_)=>0);
            if(geometry.Vertices.Length>30_000)break;
        }
        var vertices=geometry.Vertices;
        Vector2 Point(Vector3 p)=>at(Uv(new PlayerPosition { X=p.X,Z=p.Z }));
        for(int i=0;i<vertices.Length;i+=3)
            draw.AddTriangleFilled(Point(vertices[i].Position),Point(vertices[i+1].Position),Point(vertices[i+2].Position),vertices[i].Color);
    }
}
