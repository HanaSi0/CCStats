using LiteDB;
using PvpStats.Analysis;
namespace CCStats.Core.Library;
public static class DatabaseImport {
    private static string Text(BsonValue value)=>value.IsNull?"":value.IsString?value.AsString:value.IsObjectId?value.AsObjectId.ToString():value.ToString().Trim('"');
    private static BsonDocument Doc(BsonValue v)=>v.IsDocument?v.AsDocument:new();
    private static IEnumerable<BsonValue> Items(BsonValue v)=>v.IsArray?v.AsArray:v.IsDocument?v.AsDocument.Values:Enumerable.Empty<BsonValue>();
    private static int Team(BsonValue v)=>Text(v) switch {"Astra" or "1"=>1,"Umbra" or "2"=>2,_=>0};
    private static string Alias(BsonValue v)=>v.IsDocument?(Text(v.AsDocument["Name"])+" "+Text(v.AsDocument["HomeWorld"])).Trim():Text(v);
    private static long? Number(BsonValue v)=>v.IsNumber?Convert.ToInt64(v.RawValue):null;
    private static DateTime? Date(BsonValue v)=>v.IsDateTime?v.AsDateTime.ToUniversalTime():null;
    private static double? Seconds(BsonValue v)=>v.IsNumber?TimeSpan.FromTicks(Convert.ToInt64(v.RawValue)).TotalSeconds:TimeSpan.TryParse(Text(v),out var span)?span.TotalSeconds:null;
    public static (int Imported,int Skipped) Read(string path,RecordLibrary destination,Action<string>? onSkipped=null) {
        if(Path.GetFullPath(path).Equals(Path.Combine(Path.GetDirectoryName(path)!,"library.db"),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("请选择历史数据文件，不要选择软件正在使用的数据文件。");
        using var db=new LiteDatabase(new ConnectionString {Filename=Path.GetFullPath(path),ReadOnly=true,Connection=ConnectionType.Direct});
        if(!db.CollectionExists("ccmatch"))throw new InvalidDataException("未找到可识别的水晶冲突记录表。");
        int imported=0,skipped=0;
        foreach(var raw in db.GetCollection<BsonDocument>("ccmatch").FindAll()) {
            if(raw["IsDeleted"].IsBoolean&&raw["IsDeleted"].AsBoolean)continue;
            try {
                var match=new MatchRecord {Id=Text(raw["_id"]),StartUtc=Date(raw["MatchStartTime"])??Date(raw["DutyStartTime"])??throw new InvalidDataException("缺少开始时间"),EndUtc=Date(raw["MatchEndTime"]),Territory=(uint)(Number(raw["TerritoryId"])??0),LocalPlayer=Alias(raw["LocalPlayer"]),Favorite=raw["IsBookmarked"].IsBoolean&&raw["IsBookmarked"].AsBoolean,Tags=Text(raw["Tags"]),SourcePath=Path.GetFullPath(path)};
                if(match.Id.Length!=24)throw new InvalidDataException("记录编号无效");
                match.Mode=Text(raw["MatchType"]) switch {"Casual" or "1"=>"练习赛","Ranked" or "2"=>"段位赛","Custom" or "3"=>"自定赛",_=>"未知"};
                var post=Doc(raw["PostMatch"]);var teams=Doc(post["Teams"]);int localTeam=0;
                foreach(var pair in teams) {
                    var team=Doc(pair.Value);var teamId=Team(team["TeamName"]);if(teamId==0)teamId=Team(pair.Key);
                    foreach(var value in Items(team["PlayerStats"])) {
                        var row=Doc(value);var score=new PlayerScore {Name=Alias(row["Player"]),Team=teamId,Job=JobNames.Name(Text(row["Job"])),Kills=(int?)Number(row["Kills"]),Deaths=(int?)Number(row["Deaths"]),Assists=(int?)Number(row["Assists"]),Damage=Number(row["DamageDealt"]),Taken=Number(row["DamageTaken"]),Healing=Number(row["HPRestored"]),CrystalSeconds=Seconds(row["TimeOnCrystal"])};
                        if(score.Name.Length==0)continue;match.Players.Add(score);
                        if(score.Name.Equals(match.LocalPlayer,StringComparison.OrdinalIgnoreCase))localTeam=teamId;
                    }
                }
                if(match.Players.Count==0)foreach(var pair in Doc(raw["Teams"]))foreach(var value in Items(Doc(pair.Value)["Players"])) {
                    var player=Doc(value);match.Players.Add(new() {Name=Alias(player["Alias"]),Job=JobNames.Name(Text(player["Job"])),Team=Team(player["Team"])});
                }
                int winner=Team(raw["MatchWinner"]);if(winner==0)winner=Team(post["MatchWinner"]);
                match.Winner=winner;
                bool spectator=raw["IsSpectated"].IsBoolean&&raw["IsSpectated"].AsBoolean;
                match.Result=!spectator&&localTeam>0&&winner>0?(localTeam==winner?"胜利":"失败"):"未记录";
                var rank=Doc(post["RankAfter"]);match.Credit=(int?)Number(rank["Credit"]);match.Rank=Text(rank["Tier"]);
                var bundle=new ReplayBundle {Match=match};
                var timelineId=raw["TimelineId"];
                if(!timelineId.IsNull) {
                    var timeline=db.GetCollection<BsonDocument>("cctimeline").FindById(timelineId);
                    if(timeline!=null) {
                        if(timeline["Detailed"].IsDocument)bundle.Data=BsonMapper.Global.ToObject<DetailedTelemetry>(timeline["Detailed"].AsDocument);
                        foreach(var value in Items(timeline["Kills"])) {var row=Doc(value);if(Date(row["Timestamp"]) is {} t)bundle.Timeline.Add(new(t,"减员",Alias(row["CreditedKiller"]),Alias(row["Victim"]),""));}
                        foreach(var value in Items(timeline["MapEvents"])) {var row=Doc(value);if(Date(row["Timestamp"]) is {} t)bundle.Timeline.Add(new(t,"地图事件","","",Text(row["Event"])));}
                    }
                }
                destination.Save(bundle);imported++;
            } catch(Exception error) when(error is InvalidDataException or InvalidCastException or FormatException or ArgumentException or OverflowException) {skipped++;onSkipped?.Invoke(error.Message);}
        }
        return (imported,skipped);
    }
}
