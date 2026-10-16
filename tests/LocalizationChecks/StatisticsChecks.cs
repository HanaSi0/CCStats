using PvpStats.Helpers;
using PvpStats.Types.Display;
using PvpStats.Types.Match;
internal static class StatisticsChecks {
    internal static void Run(Action<bool,string> check) {
        var stats=new CCPlayerJobStats();
        EncounterStatistics.Add(stats,false,true,false,true,true);
        check(stats.StatsPersonal.Matches==1&&stats.StatsPersonal.Wins==1&&stats.StatsAll.Wins==1,"self job appearances participate in own-team results");
        EncounterStatistics.Add(stats,false,false,false,false,true);
        check(stats.StatsAll.Losses==1&&stats.StatsPersonal.Wins==2&&stats.StatsOpponent.Wins==1,"opponent job victory and the local team's victory retain different perspectives");
        EncounterStatistics.Add(stats,false,false,true,null,null);
        check(stats.StatsAll.Matches==3&&stats.StatsAll.Wins+stats.StatsAll.Losses==2&&stats.StatsTeammate.Matches==1,"unresolved results count appearances but do not become losses");
        EncounterStatistics.Add(stats,true,false,false,true,null);
        check(stats.StatsAll.Matches==4&&stats.StatsPersonal.Matches==3,"spectated appearances do not fabricate own-team matches");
        EncounterStatistics.Add(stats,true,false,false,true,null,true);
        EncounterStatistics.Add(stats,false,false,true,null,null,true);
        EncounterStatistics.Add(stats,false,false,false,false,true,true);
        EncounterStatistics.Add(stats,false,true,false,true,true,true);
        check(stats.StatsAll.Matches==0&&stats.StatsPersonal.Matches==0&&stats.StatsOpponent.Wins==0,"removal exactly reverses every result category");
        check(EncounterStatistics.Outcome(CrystallineConflictTeamName.Unknown,CrystallineConflictTeamName.Astra)==null&&EncounterStatistics.Outcome(CrystallineConflictTeamName.Astra,null)==null,"invalid or missing winner/team is unresolved instead of a loss");
        var selfOnly=new SourceSelection(true,false,false,false);
        check(selfOnly.Includes(false,true,false)&&!selfOnly.Includes(false,false,true)&&!selfOnly.Includes(false,false,false)&&!selfOnly.Includes(true,true,false),"self-only source filter rejects teammates, opponents and spectators");
        var opposing=new SourceSelection(false,false,true,false);
        check(opposing.Includes(false,false,false)&&!opposing.Includes(false,true,false)&&!opposing.Includes(false,false,true),"opponent source selection is mutually exclusive");
        check(new SourceSelection(false,false,false,true).Includes(true,false,false)&&!new SourceSelection(false,false,false,false).Includes(false,true,false),"spectated-only and no-source selections work");
        check(ScoreboardSummary.Median(new[]{.1,.3})==.2&&ScoreboardSummary.Median(new[]{.1,.5,.3})==.3,"contribution median averages the middle pair for even samples");
        check(ScoreboardSummary.Median(new[]{double.NaN,.5,double.PositiveInfinity})==.5&&double.IsNaN(ScoreboardSummary.Median(Array.Empty<double>())),"undefined contributions do not contaminate medians or become zero");
        stats.ScoreboardTotal.DamageDealt=60000; stats.ScoreboardTotal.TimeOnCrystalTicks=TimeSpan.FromSeconds(100).Ticks;
        EncounterStatistics.Add(stats,false,true,false,true,true);
        ScoreboardSummary.Update(stats,new() { new() { DamageDealt=.1 },new() { DamageDealt=.3 } },TimeSpan.FromMinutes(5));
        check(stats.ScoreboardPerMatch.DamageDealt==30000&&stats.ScoreboardPerMin.DamageDealt==12000&&stats.ScoreboardPerMatch.TimeOnCrystal==50,"scoreboard averages use eligible rows and their duration with correct units");
        check(stats.StatsPersonal.Wins==1&&stats.StatsPersonal.Matches==1,"normalizing scoreboard data never overwrites own-team results");
        check(stats.ScoreboardTotal.Size==2&&stats.ScoreboardContrib.DamageDealt==.2,"settlement sample count and median stay consistent");
        ScoreboardSummary.Update(stats,new(),TimeSpan.Zero);
        check(double.IsNaN(stats.ScoreboardPerMatch.DamageDealt)&&double.IsNaN(stats.ScoreboardPerMin.DamageDealt)&&double.IsNaN(stats.ScoreboardContrib.DamageDealt),"no-scoreboard refresh clears previous averages and contributions to unavailable");
        var divided=new CCScoreboardDouble { DamageDealt=10,TimeOnCrystal=20 } / -1;
        check(double.IsNaN(divided.DamageDealt)&&double.IsNaN(divided.TimeOnCrystal),"negative denominators cannot produce plausible negative combat rates");
        var ratio=new CCScoreboardDouble(new CCScoreboardTally(),new CCScoreboardTally());
        check(double.IsNaN(ratio.DamageDealt)&&double.IsNaN(ratio.TimeOnCrystal),"zero team totals are undefined contributions rather than fabricated zeros");
    }
}
