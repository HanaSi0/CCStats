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
    public static (int Imported,int Skipped) Read(string path,RecordLibrary destination,Action<string>? onSkipped=null,CancellationToken token=default) {
        if(Path.GetFullPath(path).Equals(Path.Combine(Path.GetDirectoryName(path)!,"library.db"),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("请选择历史数据文件，不要选择软件正在使用的数据文件。");
        using var db=new LiteDatabase(new ConnectionString {Filename=Path.GetFullPath(path),ReadOnly=true,Connection=ConnectionType.Direct});
        if(!db.CollectionExists("ccmatch"))throw new InvalidDataException("未找到可识别的水晶冲突记录表。");
        int imported=0,skipped=0;
        foreach(var raw in db.GetCollection<BsonDocument>("ccmatch").FindAll()) {
            token.ThrowIfCancellationRequested();
            if(raw["IsDeleted"].IsBoolean&&raw["IsDeleted"].AsBoolean)continue;
            try {
                var match=new MatchRecord {Id=Text(raw["_id"]),Completed=raw["IsCompleted"].IsNull||(raw["IsCompleted"].IsBoolean&&raw["IsCompleted"].AsBoolean),Quarantined=raw["IsQuarantined"].IsBoolean&&raw["IsQuarantined"].AsBoolean,StartUtc=Date(raw["MatchStartTime"])??Date(raw["DutyStartTime"])??throw new InvalidDataException("缺少开始时间"),EndUtc=Date(raw["MatchEndTime"]),Territory=(uint)(Number(raw["TerritoryId"])??0),LocalPlayer=Alias(raw["LocalPlayer"]),Favorite=raw["IsBookmarked"].IsBoolean&&raw["IsBookmarked"].AsBoolean,Tags=Text(raw["Tags"]),SourcePath=Path.GetFullPath(path)};
                if(match.Id.Length!=24)throw new InvalidDataException("记录编号无效");
                match.Mode=Text(raw["MatchType"]) switch {"Casual" or "1"=>"练习赛","Ranked" or "2"=>"段位赛","Custom" or "3"=>"自定赛",_=>"未知"};
                var post=Doc(raw["PostMatch"]);var teams=Doc(post["Teams"]);match.ScoreboardComplete=teams.Count==2&&teams.Values.All(v=>Items(Doc(v)["PlayerStats"]).Any());int localTeam=0;
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
                var rank=Doc(post["RankAfter"]);match.Credit=(int?)Number(rank["Credit"]);match.Rank=PvpStats.Localization.ChineseText.Text(Text(rank["Tier"]));
                match.RankPoints=match.Credit;
                if(match.RankPoints==null&&Number(rank["Riser"]) is {} riser&&Number(rank["Stars"]) is {} stars) {
                    var tiers=new[]{"None","Bronze","Silver","Gold","Platinum","Diamond"};int tier=System.Array.IndexOf(tiers,Text(rank["Tier"]));var levels=new[]{1,3,3,4,4,5};
                    if(tier>=0&&riser>=1&&riser<=levels[tier]&&stars>=0&&stars<=3) {int total=levels.Take(tier).Sum()*3+(levels[tier]-(int)riser)*3+(int)stars;match.RankPoints=(total-60)*100;match.Rank+=$" {riser} "+new string('★',(int)stars);}
                }
                var bundle=new ReplayBundle {Match=match};
                var timelineId=raw["TimelineId"];
                if(!timelineId.IsNull) {
                    var timeline=db.GetCollection<BsonDocument>("cctimeline").FindById(timelineId);
                    if(timeline!=null) {
                        if(timeline["Detailed"].IsDocument)bundle.Data=BsonMapper.Global.ToObject<DetailedTelemetry>(timeline["Detailed"].AsDocument);
                        foreach(var (field,kind) in new[]{("PlayerTargetedActionAnalytics","技能"),("PlayerTargetedStatusAnalytics","状态")})foreach(var actor in Doc(timeline[field]))foreach(var action in Doc(actor.Value)) {
                            if(!uint.TryParse(action.Key,out var actionId))continue;var detail=Doc(action.Value);
                            var values=Items(detail["PlayerAnalytics"]).Concat(Items(detail["NameIdAnalytics"])).Select(Doc).ToArray();
                            bundle.Actions.Add(new(actor.Key,actionId,kind,(int)(Number(detail["Casts"])??0),(int)(Number(detail["Targets"])??0),values.Sum(v=>Number(v["Damage"])??0),values.Sum(v=>Number(v["Heal"])??0),(int)values.Sum(v=>Number(v["StatusHits"])??0),(int)values.Sum(v=>Number(v["StatusMisses"])??0)));
                        }
                        foreach(var value in Items(timeline["CrystalPosition"])) {var point=Doc(value);if(Date(point["Timestamp"]) is {} at)bundle.Progress.Add(new(at,"水晶位置",(int)(Number(point["Points"])??0)));}
                        foreach(var series in Doc(timeline["TeamProgress"]))foreach(var value in Items(series.Value)) {var point=Doc(value);if(Date(point["Timestamp"]) is {} at)bundle.Progress.Add(new(at,(Team(series.Key)==1?"星极队":"灵极队")+(Number(point["Type"])==1?"检查点进度":"推进进度"),(int)(Number(point["Points"])??0)));}
                        foreach(var value in Items(timeline["LimitBreakCasts"]).Concat(Items(timeline["LimitBreakEffects"]))) {
                            var action=Doc(value);if(Date(action["Timestamp"]) is not {} at)continue;uint actionId=(uint)(Number(action["ActionId"])??0);var actor=Alias(action["Actor"]);
                            bundle.Timeline.Add(new(at,"极限技",actor,"",PvpStats.Localization.ChineseText.ActionName(actionId,$"技能 {actionId}")));
                            if(!action["NameIdActor"].IsNull||bundle.Data.ReplaySkills.Any(e=>e.ActionId==actionId&&e.Actor==actor&&Math.Abs((e.Timestamp-at).TotalSeconds)<2))continue;
                            var frame=DetailedAnalysis.FrameAt(bundle.Data,at);var caster=frame?.Players.FirstOrDefault(p=>p.Player==actor);if(caster==null)continue;
                            var skill=new ReplaySkillEvent {Timestamp=at,ActionId=actionId,Actor=actor,X=caster.X,Y=caster.Y??0,Z=caster.Z,Heading=caster.Heading,LegacyEstimate=true,Resolved=(Number(action["Variation"])??0)!=0};
                            var targets=Items(action["PlayerTargets"]).Select(Alias).ToArray();if(targets.Length==1&&frame!.Players.FirstOrDefault(p=>p.Player==targets[0]) is {} target) {skill.TargetPlayer=target.Player;skill.TargetX=target.X;skill.TargetY=target.Y;skill.TargetZ=target.Z;}
                            bundle.Data.ReplaySkills.Add(skill);
                        }
                        foreach(var value in Items(timeline["Kills"])) {var row=Doc(value);if(Date(row["Timestamp"]) is {} t)bundle.Timeline.Add(new(t,"减员",Alias(row["CreditedKiller"]),Alias(row["Victim"]),""));}
                        foreach(var value in Items(timeline["MapEvents"])) {var row=Doc(value);if(Date(row["Timestamp"]) is {} t)bundle.Timeline.Add(new(t,"地图事件","","",Text(row["Type"]) switch {"CrystalUnchained" or "0"=>"战术水晶已解除束缚。","OvertimeCommenced" or "1"=>"加时阶段已开始。","MatchEnded" or "2"=>"比赛结束","SpecialEvent" or "3"=>"地图特殊事件",_=>"地图事件"}));}
                    }
                }
                ReplaySummons.RecoverEffects(bundle.Data,bundle.Data.ReplaySkills);
                destination.Save(bundle);imported++;
            } catch(Exception error) when(error is InvalidDataException or InvalidCastException or FormatException or ArgumentException or OverflowException) {skipped++;onSkipped?.Invoke(error.Message);}
        }
        return (imported,skipped);
    }
}
