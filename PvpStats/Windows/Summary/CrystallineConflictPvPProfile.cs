using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using PvpStats.Helpers;
using PvpStats.Localization;
using System;
using PvpStats.Types.Match;

namespace PvpStats.Windows.Summary;
internal class CrystallineConflictPvPProfile {

    private Plugin _plugin;

    public CrystallineConflictPvPProfile(Plugin plugin) {
        _plugin = plugin;
    }

    public void Refresh() {

    }

    public unsafe void Draw() {
        var pvpProfile = PvPProfile.Instance();
        ImGuiHelper.HelpMarker(ChineseText.Text("This data comes from SE's game servers."), false);
        if(pvpProfile == null) {
            ImGui.TextWrapped(ChineseText.Text("PvP profile unavailable"));
            return;
        }
        ImGui.TextColored(_plugin.Configuration.Colors.Header, ChineseText.Text("Casual") + "：");
        if(pvpProfile != null) {
            using(ImRaii.PushId("casualTable")) {
                DrawTable(pvpProfile->CrystallineConflictCasualMatches, pvpProfile->CrystallineConflictCasualMatchesWon);
            }
        }
        ImGui.Separator();
        ImGui.TextColored(_plugin.Configuration.Colors.Header, ChineseText.Text("Current Ranked Season:"));
        if(pvpProfile != null) {
            using(ImRaii.PushId("rankedTable")) {
                DrawTable(pvpProfile->CrystallineConflictRankedMatches, pvpProfile->CrystallineConflictRankedMatchesWon);
            }
            ImGui.NewLine();
            using(var table = ImRaii.Table("rankTable", 2, ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.NoHostExtendX | ImGuiTableFlags.NoClip | ImGuiTableFlags.NoSavedSettings)) {
                if(table) {
                    ImGui.TableSetupColumn("description", ImGuiTableColumnFlags.WidthFixed, ImGuiHelpers.GlobalScale * 158f);
                    ImGui.TableSetupColumn($"value", ImGuiTableColumnFlags.WidthFixed, ImGuiHelpers.GlobalScale * 180f);
                    ImGui.TableNextColumn();
                    ImGui.Text(ChineseText.Text("Current Rank: "));
                    ImGui.TableNextColumn();
                    PlayerRank curRank = new() {
                        Tier = (ArenaTier)pvpProfile->CrystallineConflictCurrentRank,
                        Riser = pvpProfile->CrystallineConflictCurrentRiser,
                        Stars = pvpProfile->CrystallineConflictCurrentRisingStars,
                        Credit = pvpProfile->CrystallineConflictCurrentCrystalCredit
                    };

                    ImGui.Text($"{curRank}");
                    ImGui.TableNextColumn();
                    ImGui.Text(ChineseText.Text("Highest Rank: "));
                    ImGui.TableNextColumn();
                    PlayerRank peakRank = new() {
                        Tier = (ArenaTier)pvpProfile->CrystallineConflictHighestRank,
                        Riser = pvpProfile->CrystallineConflictHighestRiser,
                        Stars = pvpProfile->CrystallineConflictHighestRisingStars,
                        Credit = pvpProfile->CrystallineConflictHighestCrystalCredit
                    };
                    ImGui.Text($"{peakRank}");
                }
            }
        }
    }

    private void DrawTable(int matches, int wins) {
        using(var table = ImRaii.Table("table", 3, ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.NoHostExtendX | ImGuiTableFlags.NoClip | ImGuiTableFlags.NoSavedSettings)) {
            if(table) {
                ImGui.TableSetupColumn("description", ImGuiTableColumnFlags.WidthFixed, ImGuiHelpers.GlobalScale * 158f);
                ImGui.TableSetupColumn($"value", ImGuiTableColumnFlags.WidthFixed, ImGuiHelpers.GlobalScale * 180f);
                ImGui.TableSetupColumn($"rate", ImGuiTableColumnFlags.WidthFixed, ImGuiHelpers.GlobalScale * 180f);

                ImGui.TableNextColumn();
                ImGui.Text(ChineseText.Text("Matches: "));
                ImGui.TableNextColumn();
                ImGui.Text($"{matches.ToString("N0")}");
                ImGui.TableNextColumn();

                ImGui.TableNextColumn();
                ImGui.Text(ChineseText.Text("Wins: "));
                ImGui.TableNextColumn();
                ImGui.Text($"{wins.ToString("N0")}");
                ImGui.TableNextColumn();
                if(matches > 0) {
                    ImGui.TextUnformatted($"胜率：{WinRateLabel.Format(wins, Math.Max(0, matches - wins))}");
                }
            }
        }
    }
}
