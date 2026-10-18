using System.Globalization;
namespace CCStats.Core.Library;
public static class RecordStatistics {
    public static IEnumerable<MatchRecord> Filter(IEnumerable<MatchRecord> source,string search,string map,string result,bool favorites,string mode="全部") {
        var terms=search.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        return source.Where(r=>(map=="全部"||r.Map==map)&&(result=="全部"||r.Result==result)&&(!favorites||r.Favorite)&&(mode=="全部"||r.Mode==mode)).Where(r=> {
            var text=string.Join(' ',new[]{r.Id,r.Map,r.Mode,r.Result,r.Tags,r.StartLabel}.Concat(r.Players.Select(p=>p.Name+" "+p.Job)));
            return terms.All(t=>t.StartsWith('-')?!text.Contains(t[1..],StringComparison.OrdinalIgnoreCase):text.Contains(t,StringComparison.OrdinalIgnoreCase));
        });
    }
    public static string Rate(int wins,int losses)=>wins+losses==0?"—":$"{(double)wins/(wins+losses):P1} · {wins}/{wins+losses}";
    public static List<AggregateRow> Aggregate(IEnumerable<MatchRecord> records,string group,string? player=null,Func<string,string>? canonical=null) {
        canonical??=name=>name;
        var rows=records.Where(r=>!r.Observation).SelectMany(r=>r.Players.Where(p=>player==null||canonical(p.Name).Equals(canonical(player),StringComparison.OrdinalIgnoreCase)).Select(p=>(Match:r,Player:p)));
        return rows.GroupBy(x=>group=="职业"?x.Player.Job:group=="地图"?x.Match.Map:canonical(x.Player.Name)).Select(g=> {
            var matches=g.Select(x=>x.Match).DistinctBy(r=>r.Id).ToArray();int Outcome((MatchRecord Match,PlayerScore Player) x)=>x.Match.Winner>0&&x.Player.Team>0?(x.Match.Winner==x.Player.Team?1:-1):x.Player.Name.Equals(x.Match.LocalPlayer,StringComparison.OrdinalIgnoreCase)?x.Match.Result=="胜利"?1:x.Match.Result=="失败"?-1:0:0;
            var wins=g.Count(x=>Outcome(x)>0);var losses=g.Count(x=>Outcome(x)<0);
            string Avg(Func<PlayerScore,long?> select) {var values=g.Select(x=>select(x.Player)).Where(x=>x.HasValue).ToArray();return values.Length==0?"—":values.Average(x=>x!.Value).ToString("N0");}
            var crystals=g.Select(x=>x.Player.CrystalSeconds).Where(s=>s.HasValue).ToArray();
            return new AggregateRow(g.Key,g.Count(),wins,losses,Rate(wins,losses),Avg(p=>p.Damage),Avg(p=>p.Taken),Avg(p=>p.Healing),crystals.Length==0?"—":$"{crystals.Average(x=>x!.Value):F1} 秒");
        }).OrderByDescending(r=>r.Appearances).ToList();
    }
}
