using PvpStats.Types.Display;
using System;
using System.Collections.Generic;
using System.Linq;
namespace PvpStats.Helpers;
public static class ScoreboardSummary {
    public static double Median(IEnumerable<double> source) {
        var values=source.Where(double.IsFinite).OrderBy(v=>v).ToArray();
        if(values.Length==0)return double.NaN;
        var mid=values.Length/2;
        return values.Length%2==1?values[mid]:values[mid-1]+(values[mid]-values[mid-1])/2;
    }
    public static void Update(CCPlayerJobStats stats,List<CCScoreboardDouble> teamContributions,TimeSpan time) {
        var statMatches = teamContributions.Count;
        //set average stats

        stats.ScoreboardTotal.TimeOnCrystal = TimeSpan.FromTicks(stats.ScoreboardTotal.TimeOnCrystalTicks);
        //subtract one to account for starting with size one
        stats.ScoreboardTotal.Size = statMatches;

        stats.ScoreboardPerMatch = (CCScoreboardDouble)stats.ScoreboardTotal / statMatches;
        stats.ScoreboardPerMin = (CCScoreboardDouble)stats.ScoreboardTotal / time.TotalMinutes;

        if(statMatches > 0) {
            stats.ScoreboardContrib.Kills = Median(teamContributions.Select(x=>x.Kills));
            stats.ScoreboardContrib.Deaths = Median(teamContributions.Select(x=>x.Deaths));
            stats.ScoreboardContrib.Assists = Median(teamContributions.Select(x=>x.Assists));
            stats.ScoreboardContrib.DamageDealt = Median(teamContributions.Select(x=>x.DamageDealt));
            stats.ScoreboardContrib.DamageTaken = Median(teamContributions.Select(x=>x.DamageTaken));
            stats.ScoreboardContrib.HPRestored = Median(teamContributions.Select(x=>x.HPRestored));
            stats.ScoreboardContrib.TimeOnCrystal = Median(teamContributions.Select(x=>x.TimeOnCrystal));
            stats.ScoreboardContrib.KillsAndAssists = Median(teamContributions.Select(x=>x.KillsAndAssists));
        } else {
            stats.ScoreboardContrib = (CCScoreboardDouble)new CCScoreboardTally() / 0;
        }
    }
}
