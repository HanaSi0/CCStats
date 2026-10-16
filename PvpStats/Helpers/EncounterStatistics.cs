using PvpStats.Types.Display;
using System.Threading;
using PvpStats.Types.Match;

namespace PvpStats.Helpers;
public readonly record struct SourceSelection(bool LocalPlayer,bool Teammate,bool Opponent,bool Spectated) {
    public bool Includes(bool spectated,bool local,bool teammate) => spectated?Spectated:local?LocalPlayer:teammate?Teammate:Opponent;
}
public static class EncounterStatistics {
    public static bool? Outcome(CrystallineConflictTeamName? winner,CrystallineConflictTeamName? team) =>
        winner is CrystallineConflictTeamName.Astra or CrystallineConflictTeamName.Umbra
        && team is CrystallineConflictTeamName.Astra or CrystallineConflictTeamName.Umbra ? winner==team : null;
    public static void Adjust(CCAggregateStats stats,bool? won,bool remove=false) {
        var delta=remove?-1:1;
        Interlocked.Add(ref stats.Matches,delta);
        if(won==true) Interlocked.Add(ref stats.Wins,delta);
        else if(won==false) Interlocked.Add(ref stats.Losses,delta);
    }
    public static void Add(CCPlayerJobStats stats,bool spectated,bool local,bool teammate,bool? teamWon,bool? localWon,bool remove=false) {
        Adjust(stats.StatsAll,teamWon,remove);
        if(spectated)return;
        Adjust(stats.StatsPersonal,localWon,remove);
        if(teammate)Adjust(stats.StatsTeammate,localWon,remove);
        else if(!local)Adjust(stats.StatsOpponent,localWon,remove);
    }
}
