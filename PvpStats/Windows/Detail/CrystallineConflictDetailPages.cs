using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using PvpStats.Interface;
using System;
using System.Linq;
using System.Numerics;
namespace PvpStats.Windows.Detail;
internal partial class CrystallineConflictMatchDetail {
    private void DrawDetailPages() {
        var scale=ImGuiHelpers.GlobalScale;
        var pages=new[]{("Scoreboard","结算数据"),("Detailed","细项回放"),("Timeline","事件时间线"),("Graphs","推进曲线"),("Actions","技能分析")};
        if(!pages.Any(p=>p.Item1==CurrentTab))CurrentTab="Scoreboard";
        var available=ImGui.GetContentRegionAvail().X;
        var used=0f;
        foreach(var page in pages) {
            var width=ImGui.CalcTextSize(page.Item2).X+24*scale;
            if(used>0 && used+ImGui.GetStyle().ItemSpacing.X+width<=available) {
                ImGui.SameLine(); used+=ImGui.GetStyle().ItemSpacing.X;
            } else used=0;
            using var tint=ImRaii.PushColor(ImGuiCol.Button,CurrentTab==page.Item1?ModernTheme.Selection:ModernTheme.ButtonBackground);
            if(ModernTheme.Button(page.Item2+"###DetailPage"+page.Item1,new Vector2(width,28*scale)))CurrentTab=page.Item1;
            used+=width;
        }
        using var content=ImRaii.Child("MatchPageContent",Vector2.Zero,false);
        if(!content)return;
        try {
            switch(CurrentTab) {
                case "Scoreboard":
                    if(Match.PostMatch==null) ImGui.TextWrapped("此局没有保存结算数据。"); else DrawScoreboard();
                    break;
                case "Detailed":
                    if(_timeline==null) { ImGui.TextWrapped("此局没有可用的细项或时间线记录，旧比赛无法补抓。"); break; }
                    _detailedPanel??=new(_timeline.Detailed);
                    _detailedPanel.Draw(Match.IsCompleted,()=>Plugin.WindowManager.OpenReplay(Match,_timeline));
                    break;
                case "Timeline":
                    if(_timeline==null || _consolidatedEvents.Count==0) ImGui.TextWrapped("此局没有事件时间线记录。"); else DrawTimeline();
                    break;
                case "Graphs":
                    if(_timeline==null || (_crystalPosition.Xs.Length==0&&!_teamPoints.Values.Any(s=>s.Xs.Length>0))) ImGui.TextWrapped("此局没有可用推进数据，无法绘制曲线。"); else DrawGraphs();
                    break;
                case "Actions":
                    if(_timeline?.PlayerTargetedActionAnalytics?.Count>0) DrawCasts(); else ImGui.TextWrapped("此局没有技能分析记录。启用技能记录后，新比赛才会保存这部分数据。");
                    break;
            }
        } catch(Exception e) {
            Plugin.Log.Error(e,"Match detail page failed: "+CurrentTab);
            ImGui.TextWrapped("此页数据暂时无法显示，可切换其他页面；详情见插件日志。");
        }
    }
}
