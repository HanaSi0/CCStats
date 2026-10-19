using System.Globalization;
using System.Text;
using CCStats.Core.Library;
namespace CCStats.Core.Capture;

// Read labelled cells only. Unlabelled numeric AtkValues never establish a scoreboard schema.
public static class SettlementDecoder {
    private static readonly Dictionary<string,string> Columns=new(StringComparer.OrdinalIgnoreCase) {
        ["K"]="Kills",["Kills"]="Kills",["击杀"]="Kills",["キル"]="Kills",
        ["D"]="Deaths",["Deaths"]="Deaths",["死亡"]="Deaths",["デス"]="Deaths",
        ["A"]="Assists",["Assists"]="Assists",["助攻"]="Assists",["アシスト"]="Assists",
        ["Damage Dealt"]="Damage",["造成伤害"]="Damage",["与ダメージ"]="Damage",
        ["Damage Taken"]="Taken",["受到伤害"]="Taken",["被ダメージ"]="Taken",
        ["HP Restored"]="Healing",["Healing"]="Healing",["治疗量"]="Healing",["回復量"]="Healing",
        ["Time on Crystal"]="Crystal",["Crystal Time"]="Crystal",["推车时间"]="Crystal",["クリスタル運搬時間"]="Crystal"
    };
    private static readonly Dictionary<string,int> TeamOutcomes=new();
    static SettlementDecoder(){using var stream=typeof(SettlementDecoder).Assembly.GetManifestResourceStream("CCStats.ResultColumnLabels.json")??throw new InvalidDataException("缺少结算列名称资料。");using var document=System.Text.Json.JsonDocument.Parse(stream);foreach(var label in document.RootElement.GetProperty("Labels").EnumerateObject())Columns[label.Name]=label.Value.GetString()!;if(Distribution.IsChina){using var cn=typeof(SettlementDecoder).Assembly.GetManifestResourceStream("CCStats.ChinaResultLabels.json")??throw new InvalidDataException("缺少国服结算名称资料。");using var localized=System.Text.Json.JsonDocument.Parse(cn);foreach(var label in localized.RootElement.GetProperty("Labels").EnumerateObject())Columns[label.Name]=label.Value.GetString()!;foreach(var result in localized.RootElement.GetProperty("TeamOutcomes").EnumerateObject())TeamOutcomes[Clean(result.Name)]=result.Value.GetInt32();}}
    private static string Clean(string text)=>string.Join(' ',text.Normalize(NormalizationForm.FormKC).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries));
    public static bool Apply(ReplayBundle bundle) {
        if(bundle.Match.ScoreboardComplete&&!bundle.Match.Observation||bundle.Match.Players.Count!=10||bundle.Match.Players.Select(p=>p.Name).Distinct().Count()!=10)return false;
        var roster=bundle.Match.Players;
        foreach(var panel in bundle.ResultPanels.OrderByDescending(p=>p.ObservedAt)) {
            if(!SettlementTextProbe.IsResultPanel(panel.Panel)||panel.ObservedAt<bundle.Match.StartUtc||panel.ObservedAt>(bundle.Match.EndUtc??bundle.Match.StartUtc).AddMinutes(2))continue;
            var nodes=panel.TextNodes.Where(n=>(n.Flags&0x10)!=0&&float.IsFinite(n.X)&&float.IsFinite(n.Y)).Select(n=>(Node:n,Text:Clean(n.EncodedText.Length>0?SettlementTextProbe.PlainText(n.EncodedText):n.Text))).ToArray();
            var headers=nodes.Where(n=>Columns.ContainsKey(n.Text)).GroupBy(n=>Columns[n.Text]).ToDictionary(g=>g.Key,g=>g.ToArray());
            if(new[]{"Kills","Deaths","Assists","Damage","Taken","Healing","Crystal"}.Any(k=>!headers.ContainsKey(k)))continue;
            var scores=new List<PlayerScore>();bool failed=false;
            foreach(var player in roster) {
                var fullName=Clean(player.Name);var shortName=fullName.LastIndexOf(' ') is var split&&split>0?fullName[..split]:fullName;
                var names=nodes.Where(n=>n.Text==shortName||n.Text==fullName).ToArray();if(names.Length!=1){failed=true;break;}var row=names[0].Node;
                var values=new Dictionary<string,double>();
                foreach(var (column,candidates) in headers) {
                    var above=candidates.Where(h=>h.Node.Y<row.Y).OrderBy(h=>row.Y-h.Node.Y).ToArray();if(above.Length==0){failed=true;break;}var header=above[0].Node;
                    var cells=nodes.Where(n=>Math.Abs(n.Node.Y-row.Y)<=6&&Math.Abs(n.Node.X-header.X)<=24&&n.Node.X>row.X).ToArray();
                    var numbers=cells.Select(n=>Number(n.Text,column=="Crystal")).Where(n=>n.HasValue).ToArray();if(numbers.Length!=1){failed=true;break;}values[column]=numbers[0]!.Value;
                }
                if(failed)break;
                if(values.Where(p=>p.Key!="Crystal").Any(p=>p.Value<0||p.Value>int.MaxValue)||values["Crystal"]<0||values["Crystal"]>Math.Max(3600,bundle.Match.DurationSeconds+60)){failed=true;break;}
                if(new[]{"Kills","Deaths","Assists"}.Any(k=>values[k]>999)){failed=true;break;}
                scores.Add(new(){Name=player.Name,Job=player.Job,Team=player.Team,Kills=(int)values["Kills"],Deaths=(int)values["Deaths"],Assists=(int)values["Assists"],Damage=(long)values["Damage"],Taken=(long)values["Taken"],Healing=(long)values["Healing"],CrystalSeconds=values["Crystal"]});
            }
            if(failed||scores.Count!=10)continue;
            foreach(var score in scores){var player=roster.Single(p=>p.Name==score.Name);player.Kills=score.Kills;player.Deaths=score.Deaths;player.Assists=score.Assists;player.Damage=score.Damage;player.Taken=score.Taken;player.Healing=score.Healing;player.CrystalSeconds=score.CrystalSeconds;}
            bundle.Match.ScoreboardComplete=true;
            var outcomes=nodes.Select(n=>n.Text.TrimEnd('!','！').ToUpperInvariant()).Select(n=>n switch {"VICTORY" or "WIN" or "勝利" or "胜利" or "VICTOIRE" or "SIEG"=>1,"DEFEAT" or "LOSE" or "敗北" or "失败" or "DÉFAITE" or "NIEDERLAGE"=>2,_=>0}).Where(n=>n!=0).Distinct().ToArray();
            var self=roster.FirstOrDefault(p=>p.Name==bundle.Match.LocalPlayer);
            var winners=nodes.Where(n=>TeamOutcomes.ContainsKey(n.Text)).Select(n=>TeamOutcomes[n.Text]).ToHashSet();if(outcomes.Length==1&&self?.Team is 1 or 2)winners.Add(outcomes[0]==1?self.Team:3-self.Team);
            if(winners.Count==1&&outcomes.Length<=1&&self?.Team is 1 or 2&&roster.Count(p=>p.Team==1)==5&&roster.Count(p=>p.Team==2)==5){bundle.Match.Winner=winners.Single();bundle.Match.Result=bundle.Match.Winner==self.Team?"胜利":"失败";bundle.Match.Completed=true;bundle.Match.Observation=false;}
            return true;
        }
        return false;
    }
    private static double? Number(string text,bool duration) {
        if(duration){var parts=text.Split(':');if(parts.Length is 2 or 3&&parts.All(p=>p.Length>0&&p.All(char.IsAsciiDigit)&&int.TryParse(p,NumberStyles.None,CultureInfo.InvariantCulture,out _))){var values=parts.Select(p=>int.Parse(p,CultureInfo.InvariantCulture)).ToArray();if(values.Skip(1).Any(v=>v is <0 or >59))return null;return values.Length==2?(double)values[0]*60+values[1]:(double)values[0]*3600+values[1]*60+values[2];}return null;}
        if(!System.Text.RegularExpressions.Regex.IsMatch(text,@"^(?:[0-9]+|[0-9]{1,3}(?<sep>[,. '  ])[0-9]{3}(?:\k<sep>[0-9]{3})*)$"))return null;
        return long.TryParse(new string(text.Where(char.IsDigit).ToArray()),NumberStyles.None,CultureInfo.InvariantCulture,out var n)?n:null;
    }
}
