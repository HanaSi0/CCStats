namespace CCStats.Core.Library;
public static class ReplayRoster {
    public static void Reconcile(ReplayBundle bundle) {
        var teams=bundle.Data.Frames.SelectMany(f=>f.Players).Where(p=>p.Team is 1 or 2).GroupBy(p=>p.Player).Select(g=>(Name:g.Key,Teams:g.Select(p=>p.Team).Distinct().ToArray())).Where(g=>g.Teams.Length==1).ToDictionary(g=>g.Name,g=>g.Teams[0]);
        foreach(var player in bundle.Match.Players)if(player.Team==0&&teams.TryGetValue(player.Name,out var team))player.Team=team;
        foreach(var frame in bundle.Data.Frames)foreach(var player in frame.Players)if(player.Team==0&&teams.TryGetValue(player.Player,out var team))player.Team=team;
    }
}
