using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using PvpStats.Helpers;
using PvpStats.Interface;
using PvpStats.Localization;
using PvpStats.Types.Match;
using System;
using System.Linq;
using System.Numerics;

namespace PvpStats.Windows.Detail;
internal partial class CrystallineConflictMatchDetail {
    private bool _showRoster=true;
    private void DrawMatchOverview() {
        var scale=ImGuiHelpers.GlobalScale;
        using(var header=ImRaii.Table("MatchOverviewHeader",4,ImGuiTableFlags.SizingStretchProp)) {
            if(header) {
                ImGui.TableSetupColumn("###metadata",ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("###result",ImGuiTableColumnFlags.WidthFixed,64*scale);
                ImGui.TableSetupColumn("###duration",ImGuiTableColumnFlags.WidthFixed,66*scale);
                ImGui.TableSetupColumn("###actions",ImGuiTableColumnFlags.WidthFixed,100*scale);
                ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.SetCursorPosY(ImGui.GetCursorPosY()+Math.Max(0,(28*scale-ImGui.GetTextLineHeight())/2));
                ImGui.TextUnformatted(Match.Arena.HasValue?MatchHelper.GetArenaName(Match.Arena.Value):"比赛详情");
                ImGui.TableNextColumn(); ImGui.SetCursorPosY(ImGui.GetCursorPosY()+Math.Max(0,(28*scale-ImGui.GetTextLineHeight())/2));
                var result=Match.IsSpectated?"观战":Match.MatchWinner is not (CrystallineConflictTeamName.Astra or CrystallineConflictTeamName.Umbra)?"未知":Match.IsWin?"胜利":"失败";
                ImGui.TextColored(result=="胜利"?ModernTheme.Accent:ModernTheme.Gold,result);
                ImGui.TableNextColumn(); ImGui.SetCursorPosY(ImGui.GetCursorPosY()+Math.Max(0,(28*scale-ImGui.GetTextLineHeight())/2));
                ImGui.TextUnformatted(Match.MatchDuration is { } duration?StatDisplay.Duration(duration.TotalSeconds):"—");
                ImGui.TableNextColumn(); DrawFunctions();
            }
        }
        ImGui.TextDisabled($"{ChineseText.EnumName(Match.MatchType)} · {Match.DutyStartTime.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        ImGui.SameLine();
        if(ModernTheme.SmallButton(_showRoster?"收起阵容###RosterToggle":"展开阵容###RosterToggle")) _showRoster=!_showRoster;
        if(!_showRoster) { ImGui.Separator(); return; }
        var teams=Match.Teams.Values.OrderBy(t=>t.TeamName).ToArray();
        if(teams.Length!=2) { ImGui.TextDisabled("双方阵容不完整。"); return; }
        if(Plugin.Configuration.LeftPlayerTeam && Match.LocalPlayerTeam is { } local && teams[1].TeamName==local.TeamName) Array.Reverse(teams);
        using(var table=ImRaii.Table("AlignedTeamHeaders",2,ImGuiTableFlags.SizingStretchSame)) {
            if(table) foreach(var team in teams) {
                ImGui.TableNextColumn(); ImGui.TextUnformatted(MatchHelper.GetTeamName(team.TeamName));
                ImGui.SameLine(); ImGui.TextDisabled(float.IsFinite(team.Progress)?$"{team.Progress:F1}%":"—");
                var progress=float.IsFinite(team.Progress)?Math.Clamp(team.Progress/100,0,1):0;
                ImGui.ProgressBar(progress,new Vector2(-1,5*scale),"");
            }
        }
        var rows=Math.Max(teams[0].Players.Count,teams[1].Players.Count);
        using var compactStyle=ImRaii.PushStyle(ImGuiStyleVar.CellPadding,new Vector2(4,2)*scale)
            .Push(ImGuiStyleVar.ItemSpacing,new Vector2(8,2)*scale);
        var rowHeight=Math.Max(26*scale,ImGui.GetTextLineHeight()+4*scale);
        var rosterHeight=Math.Min(rows*rowHeight+8*scale,Math.Max(56*scale,ImGui.GetContentRegionAvail().Y-260*scale));
        using var rosterArea=ImRaii.Child("RosterViewport",new Vector2(0,rosterHeight),false);
        if(!rosterArea)return;
        using var roster=ImRaii.Table("AlignedTeamRoster",4,ImGuiTableFlags.RowBg|ImGuiTableFlags.BordersInnerH|ImGuiTableFlags.SizingStretchProp);
        if(!roster)return;
        ImGui.TableSetupColumn("###leftIcon",ImGuiTableColumnFlags.WidthFixed,24*scale);
        ImGui.TableSetupColumn("###leftPlayer",ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("###rightIcon",ImGuiTableColumnFlags.WidthFixed,24*scale);
        ImGui.TableSetupColumn("###rightPlayer",ImGuiTableColumnFlags.WidthStretch);
        for(int row=0;row<rows;row++) {
            ImGui.TableNextRow(ImGuiTableRowFlags.None,rowHeight);
            for(int side=0;side<2;side++) {
                var player=teams[side].Players.ElementAtOrDefault(row);
                ImGui.TableNextColumn();
                if(player?.Job is { } job && TextureHelper.JobIcons.TryGetValue(job,out var icon)) {
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY()+Math.Max(0,(rowHeight-4*scale-22*scale)/2));
                    ImGui.Image(Plugin.WindowManager.GetTextureHandle(icon),new Vector2(22*scale));
                }
                ImGui.TableNextColumn();
                if(player==null) { ImGui.TextDisabled("—"); continue; }
                var isLocal=Match.LocalPlayer?.Equals(player)==true;
                ImGui.TextColored(isLocal?ModernTheme.Gold:ImGui.GetStyle().Colors[(int)ImGuiCol.Text],player.Alias.Name);
                if(ImGui.IsItemHovered()) ImGui.SetTooltip(player.Alias.ToString()+"\n"+(player.Rank?.ToString()??"无段位"));
                var worldWidth=ImGui.CalcTextSize(player.Alias.HomeWorld).X;
                if(ImGui.GetContentRegionAvail().X>worldWidth+12*scale) {
                    ImGui.SameLine(); ImGui.TextDisabled(player.Alias.HomeWorld);
                }
            }
        }
    }
}
