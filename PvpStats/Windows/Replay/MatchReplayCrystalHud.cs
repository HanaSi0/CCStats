using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using PvpStats.Analysis;
using PvpStats.Types.Match;
namespace PvpStats.Windows.Replay;
internal sealed partial class MatchReplayWindow {
    private void DrawCrystalHud(MovementFrame? frame,DateTime now) {
        var scale=ImGuiHelpers.GlobalScale;var slot=ImGui.GetCursorScreenPos();var available=ImGui.GetContentRegionAvail().X;
        float width=Math.Min(740*scale,available),height=58*scale;
        ImGui.Dummy(new Vector2(available,height));
        var origin=slot+new Vector2((available-width)/2,0);var draw=ImGui.GetWindowDrawList();
        var blue=ImGui.GetColorU32(new Vector4(.25f,.75f,1,1));var red=ImGui.GetColorU32(new Vector4(1,.38f,.45f,1));
        var white=ImGui.GetColorU32(new Vector4(.9f,.96f,1,1));var muted=ImGui.GetColorU32(new Vector4(.6f,.68f,.8f,1));
        int? Team(CrystallineConflictTeamName team)=>_timeline.TeamProgress?.GetValueOrDefault(team)?.LastOrDefault(e=>e.Timestamp<=now)?.Points;
        int? Mid(CrystallineConflictTeamName team)=>_timeline.TeamMidProgress?.GetValueOrDefault(team)?.LastOrDefault(e=>e.Timestamp<=now)?.Points;
        var astra=ReplayCrystalHud.Progress(frame?.AstraProgress??Team(CrystallineConflictTeamName.Astra));
        var umbra=ReplayCrystalHud.Progress(frame?.UmbraProgress??Team(CrystallineConflictTeamName.Umbra));
        var position=ReplayCrystalHud.Position(frame?.CrystalProgress??_timeline.CrystalPosition?.LastOrDefault(e=>e.Timestamp<=now)?.Points);
        var astraMid=ReplayCrystalHud.Checkpoint(Mid(CrystallineConflictTeamName.Astra));var umbraMid=ReplayCrystalHud.Checkpoint(Mid(CrystallineConflictTeamName.Umbra));
        string Percent(float? value)=>value.HasValue?(value.Value*100).ToString("F1")+"%":"—";
        var left="星极队 "+Percent(astra);var right="灵极队 "+Percent(umbra);
        draw.AddText(origin,blue,left);draw.AddText(origin+new Vector2(width-ImGui.CalcTextSize(right).X,0),red,right);
        var elapsed=Math.Max(0,(now-_origin).TotalSeconds);
        var overtime=_timeline.MapEvents?.LastOrDefault(e=>e.Type==CrystallineConflictMatchEvent.OvertimeCommenced&&e.Timestamp<=now);
        string clock=overtime!=null?"加时 "+(now-overtime.Timestamp).ToString(@"mm\:ss"):
            elapsed>=300&&_duration>300?"加时 "+TimeSpan.FromSeconds(elapsed-300).ToString(@"mm\:ss"):
            "剩余 "+TimeSpan.FromSeconds(Math.Max(0,300-elapsed)).ToString(@"mm\:ss");
        if(!_match.MatchStartTime.HasValue)clock="比赛时间未记录";
        draw.AddText(origin+new Vector2((width-ImGui.CalcTextSize(clock).X)/2,0),white,clock);
        float mid=origin.X+width/2,y=origin.Y+25*scale,half=width/2,h=10*scale;
        draw.AddRectFilled(new(origin.X,y),new(origin.X+width,y+h),ImGui.GetColorU32(new Vector4(.12f,.16f,.24f,1)),5*scale);
        if(astra>0)draw.AddRectFilled(new(mid-half*astra.Value,y),new(mid,y+h),blue);
        if(umbra>0)draw.AddRectFilled(new(mid,y),new(mid+half*umbra.Value,y+h),red);
        foreach(var x in new[]{mid-half/2,mid,mid+half/2})draw.AddLine(new(x,y-3*scale),new(x,y+h+3*scale),muted,1*scale);
        if(position.HasValue) {
            float x=origin.X+width*position.Value,cy=y+h/2,r=7*scale;
            draw.AddTriangleFilled(new(x,cy-r),new(x+r,cy),new(x,cy+r),white);
            draw.AddTriangleFilled(new(x,cy-r),new(x,cy+r),new(x-r,cy),white);
        }
        var leftMid="检测点 "+Percent(astraMid);var rightMid="检测点 "+Percent(umbraMid);
        draw.AddText(origin+new Vector2(0,41*scale),muted,leftMid);
        draw.AddText(origin+new Vector2(width-ImGui.CalcTextSize(rightMid).X,41*scale),muted,rightMid);
    }
}
