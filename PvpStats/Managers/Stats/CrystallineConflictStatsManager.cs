using Dalamud.Utility;
using PvpStats.Helpers;
using PvpStats.Localization;
using PvpStats.Types.Display;
using PvpStats.Types.Match;
using PvpStats.Types.Player;
using PvpStats.Windows.Filter;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace PvpStats.Managers.Stats;
internal class CrystallineConflictStatsManager : StatsManager<CrystallineConflictMatch> {

    public static float[] KillsPerMatchRange = [1.0f, 4.0f];
    public static float[] DeathsPerMatchRange = [1.0f, 3.0f];
    public static float[] AssistsPerMatchRange = [4.0f, 8.0f];
    public static float[] DamageDealtPerMatchRange = [450000f, 850000f];
    public static float[] DamageTakenPerMatchRange = [450000f, 850000f];
    public static float[] HPRestoredPerMatchRange = [350000f, 1000000f];
    public static float[] TimeOnCrystalPerMatchRange = [35f, 110f];
    public static float AverageMatchLength = 5f;
    public static float[] KillsPerMinRange = [KillsPerMatchRange[0] / AverageMatchLength, KillsPerMatchRange[1] / AverageMatchLength];
    public static float[] DeathsPerMinRange = [DeathsPerMatchRange[0] / AverageMatchLength, DeathsPerMatchRange[1] / AverageMatchLength];
    public static float[] AssistsPerMinRange = [AssistsPerMatchRange[0] / AverageMatchLength, AssistsPerMatchRange[1] / AverageMatchLength];
    public static float[] DamageDealtPerMinRange = [DamageDealtPerMatchRange[0] / AverageMatchLength, DamageDealtPerMatchRange[1] / AverageMatchLength];
    public static float[] DamageTakenPerMinRange = [DamageTakenPerMatchRange[0] / AverageMatchLength, DamageTakenPerMatchRange[1] / AverageMatchLength];
    public static float[] HPRestoredPerMinRange = [HPRestoredPerMatchRange[0] / AverageMatchLength, HPRestoredPerMatchRange[1] / AverageMatchLength];
    public static float[] TimeOnCrystalPerMinRange = [TimeOnCrystalPerMatchRange[0] / AverageMatchLength, TimeOnCrystalPerMatchRange[1] / AverageMatchLength];
    public static float[] ContribRange = [0.15f, 0.25f];
    public static float[] DamagePerKARange = [60000f, 120000f];
    public static float[] DamagePerLifeRange = [190000f, 400000f];
    public static float[] DamageTakenPerLifeRange = [150000f, 320000f];
    public static float[] HPRestoredPerLifeRange = [120000f, 600000f];
    public static float[] KDARange = [1f, 8f];
    public static float[] KillParticipationRange = [0.6f, 0.9f];

    internal CrystallineConflictStatsManager(Plugin plugin) : base(plugin, plugin.CCCache) {
    }

    public static void IncrementAggregateStats(CCAggregateStats stats, CrystallineConflictMatch match, bool decrement = false) {
        EncounterStatistics.Adjust(stats,match.IsSpectated?null:EncounterStatistics.Outcome(match.MatchWinner,match.LocalPlayerTeam?.TeamName),decrement);
    }

    public static void IncrementAggregateStats(CCAggregateStats stats, CrystallineConflictMatch match, CrystallineConflictTeam team, CrystallineConflictPlayer player, bool decrement = false) {
        EncounterStatistics.Adjust(stats,EncounterStatistics.Outcome(match.MatchWinner,team.TeamName),decrement);
    }

    public static bool HasScoreboard(CrystallineConflictMatch match,CrystallineConflictTeam team,CrystallineConflictPlayer player) =>
        match.PostMatch?.Teams.TryGetValue(team.TeamName,out var result)==true && result.TeamStats!=null
        && result.PlayerStats.Any(row=>row.Player?.Equals(player.Alias)==true);

    public static void AddPlayerJobStat(CCPlayerJobStats statsModel, ConcurrentDictionary<int, CCScoreboardDouble> teamContributions,
        CrystallineConflictMatch match, CrystallineConflictTeam team, CrystallineConflictPlayer player, bool remove = false) {
        bool isLocalPlayer = player.Alias.Equals(match.LocalPlayer);
        bool isTeammate = !match.IsSpectated && !isLocalPlayer && team.TeamName == match.LocalPlayerTeam!.TeamName;
        bool isOpponent = !match.IsSpectated && !isLocalPlayer && !isTeammate;

        var resolved=match.MatchWinner is CrystallineConflictTeamName.Astra or CrystallineConflictTeamName.Umbra;
        EncounterStatistics.Add(statsModel,match.IsSpectated,isLocalPlayer,isTeammate,
            resolved?match.MatchWinner==team.TeamName:null,resolved&&!match.IsSpectated?match.IsWin:null,remove);

        if(match.PostMatch != null) {
            if(!match.PostMatch.Teams.TryGetValue(team.TeamName,out var teamPostMatch) || teamPostMatch.TeamStats==null) return;
            var playerPostMatch = teamPostMatch.PlayerStats.FirstOrDefault(x => x.Player?.Equals(player.Alias) ?? false);
            if(playerPostMatch != null) {
                var playerScoreboard = playerPostMatch.ToScoreboard();
                var teamScoreboard = teamPostMatch.TeamStats.ToScoreboard();
                playerScoreboard.TeamKills = teamScoreboard.Kills;
                var hashCode = HashCode.Combine(match.GetHashCode(), player.Alias);
                if(remove) {
                    statsModel.ScoreboardTotal.RemoveScoreboard(playerScoreboard);
                    if(!teamContributions.TryRemove(hashCode, out _)) {
#if DEBUG
                        Plugin.Log2.Warning($"failed to remove team contrib!, {match.DutyStartTime} {player.Alias}");
#endif
                    }
                } else {
                    statsModel.ScoreboardTotal.AddScoreboard(playerScoreboard);
                    teamContributions.TryAdd(hashCode, new(playerScoreboard, teamScoreboard));
                }
            }
        }
    }

    public static void SetScoreboardStats(CCPlayerJobStats stats, List<CCScoreboardDouble> teamContributions, TimeSpan time) {
        ScoreboardSummary.Update(stats,teamContributions,time);
    }

    protected List<CrystallineConflictMatch> ApplyFilter(MatchSearchFilter filter, List<CrystallineConflictMatch> matches) {
        var terms = MatchQuery.Parse(filter.SearchText);
        if(terms.Count == 0) return matches;
        return matches.Where(match => {
            var document = string.Join(" ", match.Id.ToString(), match.Tags, match.DutyStartTime.ToLocalTime().ToString("yyyy-MM-dd"),
                match.Arena?.ToString(), match.Arena.HasValue ? MatchHelper.GetArenaName(match.Arena.Value) : "",
                match.MatchType.ToString(), ChineseText.EnumName(match.MatchType), match.LocalPlayer?.FullName);
            foreach(var team in match.Teams.Values) {
                foreach(var player in team.Players) {
                    document += " " + player.Alias.FullName + " " + player.Job;
                    if(player.Job.HasValue) {
                        document += " " + PlayerJobHelper.GetNameFromJob(player.Job.Value)
                            + " " + PlayerJobHelper.AbbreviationNameMap.GetValueOrDefault(player.Job.Value, "");
                    }
                }
            }
            return MatchQuery.Matches(document, terms);
        }).ToList();
    }

    protected List<CrystallineConflictMatch> ApplyFilter(MatchTypeFilter filter, List<CrystallineConflictMatch> matches) {
        List<CrystallineConflictMatch> filteredMatches = new(matches);
        filteredMatches = filteredMatches.Where(x => filter.FilterState[x.MatchType]).ToList();
        return filteredMatches;
    }

    protected List<CrystallineConflictMatch> ApplyFilter(ArenaFilter filter, List<CrystallineConflictMatch> matches) {
        List<CrystallineConflictMatch> filteredMatches = new(matches);
        filteredMatches = filteredMatches.Where(x => (x.Arena == null && filter.AllSelected) || filter.FilterState[(CrystallineConflictMap)x.Arena!]).ToList();
        return filteredMatches;
    }

    protected List<CrystallineConflictMatch> ApplyFilter(LocalPlayerJobFilter filter, List<CrystallineConflictMatch> matches) {
        List<CrystallineConflictMatch> filteredMatches = new(matches);
        if(!filter.AnyJob) {
            if(filter.JobRole != null) {
                filteredMatches = filteredMatches.Where(x => x.LocalPlayer != null && x.LocalPlayerTeamMember != null && PlayerJobHelper.GetSubRoleFromJob(x.LocalPlayerTeamMember.Job) == filter.JobRole).ToList();
            } else {
                filteredMatches = filteredMatches.Where(x => x.LocalPlayer != null && x.LocalPlayerTeamMember != null && x.LocalPlayerTeamMember.Job == filter.PlayerJob).ToList();
            }
        }
        return filteredMatches;
    }

    protected List<CrystallineConflictMatch> ApplyFilter(OtherPlayerFilter filter, List<CrystallineConflictMatch> matches) {
        List<CrystallineConflictMatch> filteredMatches = new(matches);
        List<PlayerAlias> linkedPlayerAliases = new();
        if(!filter.PlayerNamesRaw.IsNullOrEmpty() && Plugin.Configuration.EnablePlayerLinking) {
            linkedPlayerAliases = Plugin.PlayerLinksService.GetAllLinkedAliases(filter.PlayerNamesRaw);
        }
        filteredMatches = filteredMatches.Where(x => {
            foreach(var team in x.Teams) {
                if(filter.TeamStatus == TeamStatus.Teammate && team.Key != x.LocalPlayerTeam?.TeamName) {
                    continue;
                } else if(filter.TeamStatus == TeamStatus.Opponent && team.Key == x.LocalPlayerTeam?.TeamName) {
                    continue;
                }
                foreach(var player in team.Value.Players) {
                    if(!filter.AnyJob && player.Job != filter.PlayerJob) {
                        continue;
                    }
                    if(player.Alias.FullName.Contains(filter.PlayerNamesRaw, StringComparison.OrdinalIgnoreCase)) {
                        return true;
                    }
                    if(Plugin.Configuration.EnablePlayerLinking) {
                        if(linkedPlayerAliases.Any(x => x.Equals(player.Alias))) {
                            return true;
                        }
                    }
                }
            }
            return false;
        }).ToList();
        return filteredMatches;
    }

    protected List<CrystallineConflictMatch> ApplyFilter(TierFilter filter, List<CrystallineConflictMatch> matches) {
        List<CrystallineConflictMatch> filteredMatches = new(matches);
        filteredMatches = filteredMatches.Where(x => {
            CrystallineConflictPlayer highestPlayer = new();
            try {
                highestPlayer = x.Players.OrderByDescending(y => y.Rank).First();
            } catch {
                //Plugin.Log.Error($"{x.Id} {x.DutyStartTime}");
                return true;
            }
            return x.MatchType != CrystallineConflictMatchType.Ranked || highestPlayer.Rank == null
            || (highestPlayer.Rank >= filter.TierLow && highestPlayer.Rank <= filter.TierHigh) || (highestPlayer.Rank >= filter.TierHigh && highestPlayer.Rank <= filter.TierLow);
        }).ToList();
        return filteredMatches;
    }

    protected List<CrystallineConflictMatch> ApplyFilter(ResultFilter filter, List<CrystallineConflictMatch> matches) {
        List<CrystallineConflictMatch> filteredMatches = new(matches);
        if(filter.Result == MatchResult.Win) {
            filteredMatches = filteredMatches.Where(x => x.IsWin).ToList();
        } else if(filter.Result == MatchResult.Loss) {
            filteredMatches = filteredMatches.Where(x => !x.IsWin && x.MatchWinner != null && !x.IsSpectated).ToList();
        } else if(filter.Result == MatchResult.Other) {
            filteredMatches = filteredMatches.Where(x => x.IsSpectated || x.MatchWinner == null).ToList();
        }
        return filteredMatches;
    }

    protected List<CrystallineConflictMatch> ApplyFilter(MiscFilter filter, List<CrystallineConflictMatch> matches) {
        List<CrystallineConflictMatch> filteredMatches = new(matches);
        if(filter.MustHaveStats) {
            filteredMatches = filteredMatches.Where(x => x.PostMatch is not null).ToList();
        }
        if(!filter.IncludeSpectated) {
            filteredMatches = filteredMatches.Where(x => !x.IsSpectated).ToList();
        }
        return filteredMatches;
    }
}
