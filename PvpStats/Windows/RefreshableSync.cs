using PvpStats.Types.Match;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PvpStats.Windows;
internal abstract class RefreshableSync<T> : Refreshable<T> where T : PvpMatch {
    protected override Task ProcessMatches(List<T> matches, bool remove = false) {
        foreach(var match in matches) {
            try { ProcessMatch(match,remove); }
            catch(Exception e) { RefreshFailures++; Plugin.Log2.Error(e,"Skipping a record that failed statistics processing."); }
            finally { MatchesProcessed++; RefreshProgress=MatchesTotal>0?(double)MatchesProcessed/MatchesTotal:1; }
        }
        return Task.CompletedTask;
    }
}
