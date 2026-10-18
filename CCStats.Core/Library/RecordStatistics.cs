namespace CCStats.Core.Library;
public static class RecordStatistics {
    public static IEnumerable<MatchRecord> Filter(IEnumerable<MatchRecord> source,string search,string map,string result,bool favorites,string mode="全部") {
        var terms=PvpStats.Helpers.MatchQuery.Parse(search);
        return source.Where(r=>(map=="全部"||r.Map==map)&&(result=="全部"||r.Result==result)&&(!favorites||r.Favorite)&&(mode=="全部"||r.Mode==mode)).Where(r=> {
            var text=string.Join(' ',new[]{r.Id,r.Map,r.Mode,r.Result,r.Tags,r.StartLabel,r.Rank}.Concat(r.Players.Select(p=>p.Name+" "+p.Job)));
            return PvpStats.Helpers.MatchQuery.Matches(text,terms);
        });
    }
    public static string Rate(int wins,int losses)=>wins+losses==0?"—":$"{(double)wins/(wins+losses):P1} · {wins}/{wins+losses}";
    public static List<AggregateRow> Aggregate(IEnumerable<MatchRecord> records,string group,string? player=null,Func<string,string>? canonical=null,string metric="场均") {
        canonical??=name=>name;
        var rows=records.Where(r=>!r.Observation&&r.Completed&&!r.Quarantined).SelectMany(r=>r.Players.Where(p=>player==null||canonical(p.Name).Equals(canonical(player),StringComparison.OrdinalIgnoreCase)).Select(p=>(Match:r,Player:p)));
        return rows.GroupBy(x=>group=="职业"?x.Player.Job:group=="地图"?x.Match.Map:canonical(x.Player.Name)).Select(g=> {
            int Outcome((MatchRecord Match,PlayerScore Player) x)=>x.Match.Winner>0&&x.Player.Team>0?(x.Match.Winner==x.Player.Team?1:-1):x.Player.Name.Equals(x.Match.LocalPlayer,StringComparison.OrdinalIgnoreCase)?x.Match.Result=="胜利"?1:x.Match.Result=="失败"?-1:0:0;
            var wins=g.Count(x=>Outcome(x)>0);var losses=g.Count(x=>Outcome(x)<0);
            string Value(Func<PlayerScore,double?> select,string format="N0") {
                var valid=g.Where(x=>select(x.Player).HasValue).ToArray();if(valid.Length==0)return "—";
                if(metric=="每分钟") {valid=valid.Where(x=>x.Match.DurationSeconds>0).ToArray();return valid.Length==0?"—":(valid.Sum(x=>select(x.Player)!.Value)*60/valid.Sum(x=>x.Match.DurationSeconds)).ToString(format);}
                if(metric=="队伍贡献中位数") {
                    var percentages=valid.Where(x=>x.Player.Team>0&&x.Match.ScoreboardComplete).Select(x=> {
                        var team=x.Match.Players.Where(p=>p.Team==x.Player.Team).ToArray();if(team.Any(p=>!select(p).HasValue))return double.NaN;
                        var denominator=team.Sum(p=>select(p)??0);return denominator>0?select(x.Player)!.Value/denominator:double.NaN;
                    }).Where(double.IsFinite).Order().ToArray();if(percentages.Length==0)return "—";
                    int mid=percentages.Length/2;double median=percentages.Length%2==1?percentages[mid]:(percentages[mid-1]+percentages[mid])/2;return median.ToString("P1");
                }
                return valid.Average(x=>select(x.Player)!.Value).ToString(format);
            }
            return new AggregateRow(g.Key,g.Count(),wins,losses,Rate(wins,losses),Value(p=>p.Damage),Value(p=>p.Taken),Value(p=>p.Healing),Value(p=>p.CrystalSeconds,"N1"),Value(p=>p.Kills,"N1"),Value(p=>p.Deaths,"N1"),Value(p=>p.Assists,"N1"));
        }).OrderByDescending(r=>r.Appearances).ToList();
    }
}
