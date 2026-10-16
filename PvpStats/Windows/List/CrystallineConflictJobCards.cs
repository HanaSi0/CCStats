using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using PvpStats.Helpers;
using PvpStats.Interface;
using PvpStats.Localization;
using PvpStats.Types.Display;
using PvpStats.Types.Player;
using PvpStats.Windows.Filter;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;

namespace PvpStats.Windows.List;
internal partial class CrystallineConflictJobList {
    internal bool ShowDetailedTable;
    private int _cardMetric, _cardRole, _cardSort;
    internal void DrawCards() {
        if(ShowDetailedTable) { Draw(); return; }
        var scale=ImGuiHelpers.GlobalScale;
        // All primary controls share two equal columns and comfortable hit areas.
        using(var padding=ImRaii.PushStyle(ImGuiStyleVar.FramePadding,new Vector2(10,10)*scale))
        using(var toolbar=ImRaii.Table("JobCardsToolbar",2,ImGuiTableFlags.SizingStretchSame)) {
            if(toolbar) {
                ImGui.TableNextColumn();
                var enabled=StatSourceFilter.FilterState.Where(p=>p.Value).Select(p=>p.Key).ToArray();
                var source=enabled.Length==4?"全部角色":enabled.Length==0?"未选择统计来源":string.Join("、",enabled.Select(p=>p switch { StatSource.LocalPlayer=>"我自己",StatSource.Teammate=>"队友",StatSource.Opponent=>"对手",_=>"观战" }));
                if(ModernTheme.Button("统计范围："+source+"###JobCardSource",new(-1,0))) ImGui.OpenPopup("JobCardSourcePopup");
                if(ImGui.IsItemHovered()) ImGui.SetTooltip("统计范围："+source);
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.Combo("###JobCardMetric",ref _cardMetric,new[]{"场均数据","每分钟数据","累计数据"},3);
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.Combo("###JobCardRole",ref _cardRole,new[]{"全部职业","防护职业","治疗职业","近战输出职业","远程物理输出职业","远程魔法输出职业"},6);
                ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
                ImGui.Combo("###JobCardSort",ref _cardSort,new[]{"按出场次数","按有效胜率","按职业顺序"},3);
            }
        }
        ImGui.SetNextWindowSize(new Vector2(Math.Min(600*scale,ImGui.GetMainViewport().WorkSize.X-40*scale),0),ImGuiCond.Appearing);
        using(var popup=ImRaii.Popup("JobCardSourcePopup")) if(popup) { ImGui.TextUnformatted("选择纳入职业统计的角色"); StatSourceFilter.Draw(); }
        ImGui.TextDisabled("出场按角色人次统计；胜率只计胜负场。无数据用 — 表示。");
        if(RefreshActive) { ImGui.TextUnformatted("正在更新职业数据……"); return; }
        var model=StatsModel;
        var jobs=Enum.GetValues<Job>().Where(j=>_cardRole==0 || (int?)PlayerJobHelper.GetSubRoleFromJob(j)==_cardRole-1);
        jobs=_cardSort switch {
            0=>jobs.OrderByDescending(j=>model.GetValueOrDefault(j)?.StatsAll.Matches??0).ThenBy(j=>j),
            1=>jobs.OrderByDescending(j=>model.TryGetValue(j,out var s)&&s.StatsAll.Wins+s.StatsAll.Losses>0?WinRateLabel.Rate(s.StatsAll.Wins,s.StatsAll.Losses):-1).ThenBy(j=>j),
            _=>jobs.OrderBy(j=>j),
        };
        using var area=ImRaii.Child("JobCardsScroll",Vector2.Zero,false);
        if(!area) return;
        var columns=StatDisplay.CardColumns(ImGui.GetContentRegionAvail().X,scale);
        using var grid=ImRaii.Table("JobCardsGrid",columns,ImGuiTableFlags.SizingStretchSame);
        if(!grid)return;
        foreach(var job in jobs) {
            ImGui.TableNextColumn();
            DrawJobCard(job,model.GetValueOrDefault(job)??new());
        }
    }
    private void DrawJobCard(Job job,CCPlayerJobStats stats) {
        var scale=ImGuiHelpers.GlobalScale;
        var height=ImGui.GetTextLineHeightWithSpacing()*15+32*scale;
        using var card=ImRaii.Child("JobCard"+job,new Vector2(0,height),true,ImGuiWindowFlags.NoScrollbar|ImGuiWindowFlags.NoScrollWithMouse);
        if(!card)return;
        ImGui.SetWindowFontScale(1);
        if(TextureHelper.JobIcons.TryGetValue(job,out var icon)) ImGui.Image(_plugin.WindowManager.GetTextureHandle(icon),new Vector2(42*scale));
        else ImGui.Dummy(new Vector2(42*scale));
        ImGui.SameLine();
        using(var label=ImRaii.Group()) {
            ImGui.TextUnformatted(PlayerJobHelper.GetNameFromJob(job));
            ImGui.TextDisabled(ChineseText.EnumName(PlayerJobHelper.GetSubRoleFromJob(job)));
        }
        var all=stats.StatsAll;
        ImGui.TextUnformatted($"{all.Matches:N0} 次出场  ·  {all.Wins:N0} 胜 / {all.Losses:N0} 负");
        ImGui.TextUnformatted("有效胜率  "+WinRateLabel.Format(all.Wins,all.Losses));
        var valid=all.Wins+(long)all.Losses>0;
        ImGui.ProgressBar(valid?(float)WinRateLabel.Rate(all.Wins,all.Losses):0,new(-1,5*scale),"");
        ImGui.Spacing();
        var values=_cardMetric switch { 1=>stats.ScoreboardPerMin,2=>(CCScoreboardDouble)stats.ScoreboardTotal,_=>stats.ScoreboardPerMatch };
        var hasData=all.Matches>0 && stats.ScoreboardTotal.Size>0;
        using(var metrics=ImRaii.Table("JobMetrics",2,ImGuiTableFlags.SizingStretchSame)) {
            if(metrics) {
                Cell("击倒",StatDisplay.Number(values.Kills,hasData,"N1")); Cell("死亡",StatDisplay.Number(values.Deaths,hasData,"N1"));
                Cell("助攻",StatDisplay.Number(values.Assists,hasData,"N1")); Cell("造成伤害",StatDisplay.Number(values.DamageDealt,hasData));
                Cell("受到伤害",StatDisplay.Number(values.DamageTaken,hasData)); Cell("治疗量",StatDisplay.Number(values.HPRestored,hasData));
                Cell("推车时间",StatDisplay.Duration(values.TimeOnCrystal,hasData)); Cell("结算样本",Math.Max(0,all.Matches>0?stats.ScoreboardTotal.Size:0).ToString("N0"));
            }
        }
        if(!hasData) ImGui.TextDisabled(all.Matches==0?"当前筛选范围内暂无该职业数据":"当前记录缺少可用结算数据");
    }
    private static void Cell(string caption,string value) {
        ImGui.TableNextColumn(); ImGui.TextDisabled(caption); ImGui.TextUnformatted(value);
    }
}
