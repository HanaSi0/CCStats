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
        if(frame!=null)ImGui.TextDisabled($"推进 {(team==1?frame.AstraProgress:frame.UmbraProgress)/10f:F1}%");
        foreach(var player in _match.Players.Where(p=>(int?)p.Team==team)) {
            var alias=player.Alias.ToString();
            var observed=frame?.Players.FirstOrDefault(p=>p.Player==alias);
            using var id=ImRaii.PushId(alias);
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
                if(observed.HP==0)ImGui.TextDisabled("倒地");
                if(_data.Version<4)ImGui.TextDisabled("旧记录未保存完整状态");
                else DrawBuffs(observed,now);
            }
            ImGui.Separator();
        }
        if(team!=2)return;
        ImGui.TextUnformatted("地图事件");
        if(_data.MapMechanicsTruncated)ImGui.TextColored(ModernTheme.Gold,"环境事件达到记录上限。");
        if(_showMechanics) {
            foreach(var entry in _data.MapMechanics.Where(e=>e.Timestamp<=now).OrderByDescending(e=>e.Timestamp).Take(5)) {
                if(ImGui.Selectable($"{(entry.Timestamp-_origin):mm\\:ss} · {entry.Label}###Mechanic"+entry.Timestamp.Ticks)) { _seconds=Math.Clamp((float)(entry.Timestamp-_origin).TotalSeconds,0,_duration);_playing=false; }
                if(ImGui.IsItemHovered())ImGui.SetTooltip(entry.MapWide?"地图计时器触发记录；不推测作用范围或持续时间。":"环境对象动作；标记为来源坐标，未确认作用范围。\n技能 ID "+entry.ActionId+" · 对象名称 ID "+entry.SourceNameId);
            }
            if(_data.MapMechanics.Count==0)ImGui.TextDisabled("未保存环境动作；旧记录仅显示已有地图触发。");
        }
        ImGui.TextUnformatted("最近减员");
        foreach(var death in (_timeline.Kills??[]).Where(k=>k.Timestamp<=now).OrderByDescending(k=>k.Timestamp).Take(3))
            ImGui.TextWrapped($"{(death.Timestamp-_origin):mm\\:ss} · {death.Victim}");
        using(var disabled=ImRaii.Disabled(_exporting)) if(ModernTheme.Button("导出观测记录###ExportReplay"))Export();
        ImGui.TextWrapped(_exportStatus);
    }
    private void DrawBuffs(PlayerPosition player,DateTime now) {
        var scale=ImGuiHelpers.GlobalScale;
        var statuses=player.Statuses.Where(s=>s.Remaining<=0||ReplayObservation.Remaining(s,player.StatusTimestamp,now)>0).ToArray();
        var columns=Math.Max(1,(int)((ImGui.GetContentRegionAvail().X+4*scale)/(24*scale)));
        for(int i=0;i<statuses.Length;i++) {
            var status=statuses[i];
            if(i%columns!=0)ImGui.SameLine(0,4*scale);
            using var id=ImRaii.PushId(i);
            _data.ReplayStatuses.TryGetValue(status.Id,out var info);
            if(info?.Icon>0)ImGui.Image(_plugin.WindowManager.GetTextureHandle(info.Icon),new Vector2(20*scale));
            else ImGui.Button("?###Buff",new Vector2(20*scale));
            if(ImGui.IsItemHovered())ImGui.SetTooltip($"{info?.Name??("状态 "+status.Id)}\n"+(status.Remaining>0?$"剩余 {ReplayObservation.Remaining(status,player.StatusTimestamp,now):F1} 秒":"无倒计时")+$"\n状态参数 {status.Param} · ID {status.Id}");
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
        var entries=_data.MapMechanics.Where(e=>e.Timestamp<=now&&e.Timestamp>=now.AddSeconds(-3)).ToArray();
        var global=entries.LastOrDefault(e=>e.MapWide);
        if(global!=null || (_timeline.MapEvents?.Any(e=>e.Type==CrystallineConflictMatchEvent.SpecialEvent&&e.Timestamp<=now&&e.Timestamp>=now.AddSeconds(-3))??false)) {
            draw.AddRect(origin,origin+new Vector2(side),ImGui.GetColorU32(ModernTheme.Gold),0,ImDrawFlags.None,3*scale);
            draw.AddRectFilled(origin,origin+new Vector2(side,30*scale),ImGui.GetColorU32(new Vector4(.05f,.05f,.08f,.94f)));
            draw.AddText(origin+new Vector2(8*scale),ImGui.GetColorU32(ModernTheme.Gold),"地图机制触发 · 计时器观测");
        }
        foreach(var entry in entries.Where(e=>!e.MapWide&&e.X.HasValue&&e.Z.HasValue)) {
            var uv=Uv(new PlayerPosition { X=entry.X!.Value,Z=entry.Z!.Value }); if(!InMap(uv))continue;
            var point=at(uv);var d=8*scale;var color=ImGui.GetColorU32(ModernTheme.Gold);
            draw.AddTriangleFilled(point+new Vector2(0,-d),point+new Vector2(d,d),point+new Vector2(-d,d),color);
            if(hovered&&Vector2.Distance(ImGui.GetMousePos(),point)<12*scale)ImGui.SetTooltip(entry.Label+"\n环境动作来源位置；未确认作用范围。");
        }
    }
}
