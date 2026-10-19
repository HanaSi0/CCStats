using CCStats.Core;
using CCStats.Core.Library;
using CCStats.Core.Capture;
using CCStats.Desktop.Capture;
using LiteDB;
using PvpStats.Analysis;
using System.Security.Cryptography;
internal static class PortChecks {
    public static async Task Run(Action<bool,string> check) {
        var directory=Path.Combine(Path.GetTempPath(),"ccstats-port-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try {
            var legacyDirectory=Path.Combine(directory,"legacy-owned");Directory.CreateDirectory(legacyDirectory);
            var stamp=new DateTime(2026,10,19,10,53,0,DateTimeKind.Utc);
            var legacyData=new DetailedTelemetry {Version=3,Frames=new(){new(){Timestamp=stamp,Players=new(){new(){Player="Saved Player Ixion",ClassJobId=19,Team=1,X=123,Y=7,Z=-42,HP=9000,MaxHP=10000}}}}};legacyData.PrepareForStorage();
            using(var legacy=new LiteDatabase(Path.Combine(legacyDirectory,"library.db"))) {
                legacy.GetCollection<MatchRecord>("records").Insert(new MatchRecord {Id="507f1f77bcf86cd799439099",StartUtc=stamp,EndUtc=stamp.AddSeconds(1),Observation=true,FrameCount=1});
                legacy.GetCollection<BsonDocument>("replays").Insert(new BsonDocument {["_id"]="507f1f77bcf86cd799439099",["Data"]=BsonMapper.Global.ToDocument(legacyData),["Timeline"]="[]"});
            }
            using(var restored=new RecordLibrary(legacyDirectory)) {
                var saved=restored.Load("507f1f77bcf86cd799439099");
                check(saved.Match.StartUtc.Kind==DateTimeKind.Utc&&saved.Match.StartUtc==stamp&&ReplaySceneBuilder.Build(saved,null,saved.Match.StartUtc.AddMilliseconds(50),false,false).Length>0,"existing databases created with local-date defaults regain coordinates without re-recording");
                var playerData=ReplayPlayerData.Build(saved).Single();check(playerData is {PositionSamples:1,X:123,Y:7,Z:-42,HP:9000},"player details use stored coordinates and HP independently of an absent scoreboard");
            }
            var source=Path.Combine(directory,"history.db");var id=new ObjectId("507f1f77bcf86cd799439012");var timelineId=ObjectId.NewObjectId();var now=DateTime.UtcNow;
            BsonDocument Alias(string name)=>new(){["Name"]=name,["HomeWorld"]="Ixion"};
            var player=new BsonDocument {["Player"]=Alias("Own Player"),["Team"]="Astra",["Job"]="PLD",["Kills"]=2,["Deaths"]=1,["Assists"]=5,["DamageDealt"]=123456,["DamageTaken"]=234567,["HPRestored"]=34567,["TimeOnCrystal"]="00:00:32"};
            using(var db=new LiteDatabase(source)) {
                db.GetCollection<BsonDocument>("ccmatch").Insert(new BsonDocument {["_id"]=id,["TerritoryId"]=1034,["DutyStartTime"]=now,["MatchStartTime"]=now,["MatchEndTime"]=now.AddMinutes(4),["LocalPlayer"]=Alias("Own Player"),["MatchType"]="Ranked",["MatchWinner"]="Astra",["TimelineId"]=timelineId,["PostMatch"]=new BsonDocument {["RankAfter"]=new BsonDocument {["Tier"]="Crystal",["Credit"]=500},["Teams"]=new BsonDocument {["Astra"]=new BsonDocument {["TeamName"]="Astra",["PlayerStats"]=new BsonArray{player}}}}});
                var detailed=new DetailedTelemetry {Version=3,Frames=new(){new(){Timestamp=now,Players=new(){new(){Player="Own Player Ixion",Team=1,X=0,Y=0,Z=0,HP=100,MaxHP=100,ClassJobId=19}}}}};detailed.PrepareForStorage();
                db.GetCollection<BsonDocument>("cctimeline").Insert(new BsonDocument {["_id"]=timelineId,["Detailed"]=BsonMapper.Global.ToDocument(detailed),["PlayerTargetedActionAnalytics"]=new BsonDocument {["Own Player Ixion"]=new BsonDocument {["29230"]=new BsonDocument {["Casts"]=1,["Targets"]=1,["PlayerAnalytics"]=new BsonDocument {["Opponent"]=new BsonDocument {["Damage"]=555,["Heal"]=50}}}}},["TeamMidProgress"]=new BsonDocument {["Astra"]=new BsonArray {new BsonDocument {["Timestamp"]=now,["Points"]=75}}},["CrystalPosition"]=new BsonArray {new BsonDocument {["Timestamp"]=now,["Points"]=25}}});
            }
            var before=SHA256.HashData(await File.ReadAllBytesAsync(source));
            using(var library=new RecordLibrary(Path.Combine(directory,"owned"))) {
                var report=DatabaseImport.Read(source,library,Console.WriteLine);check(report.Imported==1&&report.Skipped==0,"history import finds one valid match");
                var after=SHA256.HashData(await File.ReadAllBytesAsync(source));check(before.SequenceEqual(after),"history database remains byte-for-byte unchanged");
                var row=library.All().Single();check(row.Map=="九霄云上"&&row.Result=="胜利"&&row.Mode=="段位赛"&&row.Credit==500,"history match metadata and local result retain their meaning");
                check(row.Players.Single().Damage==123456&&row.Players.Single().CrystalSeconds==32,"settlement retains all damage and push-time values");
                var bundle=library.Load(row.Id);check(bundle.Data.Frames.Single().Players.Single().ClassJobId==19,"compressed historic movement remains available after import");
                check(bundle.Match.StartUtc.Kind==DateTimeKind.Utc&&Math.Abs((bundle.Match.StartUtc-bundle.Data.Frames[0].Timestamp).TotalSeconds)<.01,"database match origin stays UTC and aligned with compressed position timestamps");
                check(ReplaySceneBuilder.Build(bundle,null,bundle.Match.StartUtc.AddMilliseconds(50),false,false).Length>0,"database replay origin resolves real pawn geometry in non-UTC timezones");
                check(bundle.Actions.Single().Damage==555&&bundle.Actions.Single().Casts==1&&bundle.Progress.Single(p=>p.Series=="水晶位置").Points==25,"legacy skill summaries and director timeline retain their numeric data");
                check(bundle.Progress.Single(p=>p.Series=="星极队检查点进度").Points==750,"historic checkpoint progress uses its separate channel and correct percent scale");
                library.Annotate(row.Id,true,"my note");DatabaseImport.Read(source,library);row=library.All().Single();check(row.Favorite&&row.Tags=="my note"&&library.All().Count==1,"repeat import deduplicates and preserves user annotations");
                var export=Path.Combine(directory,"export.json");await ReplayImport.ExportAsync(library.Load(row.Id),export);var reimport=await ReplayImport.ReadAsync(export);
                check(reimport.Match.Players.Single().Damage==123456&&reimport.Match.LocalPlayer=="Own Player Ixion"&&reimport.Match.Winner==1,"JSON export and reimport preserve complete scoreboard and perspective");
                check(reimport.Actions.Single().Damage==555&&reimport.Progress.Single(p=>p.Series=="水晶位置").Points==25,"legacy action and progress summaries survive JSON interchange");
                check(RecordStatistics.Filter(library.All(),"Own -火山","全部","胜利",true).Count()==1,"combined query, exclusion, result and favorite filters agree");
                check(RecordStatistics.Aggregate(library.All(),"职业","Own Player Ixion").Single().Wins==1,"default-player statistics use own team outcome");
                var opponent=new PlayerScore {Name="Opponent",Team=2,Job="骑士"};row.Players.Add(opponent);
                check(RecordStatistics.Aggregate(new[]{row},"职业","Opponent").Single().Losses==1,"opponent statistics reverse the local result rather than copying it");
                check(RecordStatistics.Aggregate(new[]{row},"职业","Own Player Ixion",metric:"每分钟").Single().Damage==30864d.ToString("N0"),"per-minute damage uses actual match duration");
                row.ScoreboardComplete=true;var ownMate=new PlayerScore {Name="Teammate",Team=1,Job="骑士",Damage=123456};row.Players.Add(ownMate);
                check(RecordStatistics.Aggregate(new[]{row},"职业","Own Player Ixion",metric:"队伍贡献中位数").Single().Damage==.5d.ToString("P1"),"team contribution uses the correct side denominator");
                ownMate.Damage=null;check(RecordStatistics.Aggregate(new[]{row},"职业","Own Player Ixion",metric:"队伍贡献中位数").Single().Damage=="—","missing teammate damage cannot become a fabricated contribution");
                row.Quarantined=true;check(RecordStatistics.Aggregate(new[]{row},"职业").Count==0,"quarantined records stay out of valid statistics");row.Quarantined=false;
                row.Observation=true;check(RecordStatistics.Aggregate(new[]{row},"职业").Count==0,"observation segments never become win-rate samples");
                library.Delete(row.Id);check(library.All().Count==0,"owned record deletion removes the matching replay");
            }
            var links=new PlayerLinks();links.Link("Alias Ixion","Main Titan");check(links.Canonical("Alias Ixion")=="Main Titan","manual player links resolve to the selected main identity");
            bool cycle=false;try {links.Link("Main Titan","Alias Ixion");}catch(ArgumentException){cycle=true;}check(cycle,"identity cycles are rejected");
            var prefsPath=Path.Combine(directory,"settings.json");var prefs=new DesktopPreferences {CloseBehavior="Tray"};prefs.Save(prefsPath);check(DesktopPreferences.Load(prefsPath).CloseBehavior=="Tray","remembered close-to-tray choice survives restart");
            prefs.CloseBehavior="Exit";prefs.Save(prefsPath);check(DesktopPreferences.Load(prefsPath).CloseBehavior=="Exit","settings can replace close-to-tray with immediate exit");
            await File.WriteAllTextAsync(prefsPath,"{\"CloseBehavior\":\"invalid\",\"Links\":null}");var recovered=DesktopPreferences.Load(prefsPath);check(recovered.CloseBehavior=="Ask"&&recovered.Links!=null,"invalid close preferences restore the choice dialog and recover identity settings");
            check(MapMetadata.For(1034)?.Factor>0&&MapMetadata.For(1358)==MapMetadata.For(1357),"all regular and custom arenas use official coordinate metadata");
            using var fake=new FakeSource();var factories=0;
            var capture=new IndependentCapture(()=>{Interlocked.Increment(ref factories);return new FakeSource();});capture.Configure(false);capture.Connect(Path.Combine(directory,"observations"));capture.Record(true);
            for(int i=0;i<80&&capture.State.Frames<3;i++)await Task.Delay(25);
            check(capture.State.Recording&&capture.State.Frames>=3,"background capture records frames without any visible window");
            capture.Poll(directory);check(factories==1,"automatic polling cannot start a duplicate capture session");
            await capture.StopAsync();check(!capture.State.Connected&&!capture.State.Recording&&File.Exists(capture.State.SavedPath),"exit finalizes background data before disconnecting");
            var firstPath=capture.State.SavedPath;
            capture.Connect(Path.Combine(directory,"observations"));capture.Record(true);
            for(int i=0;i<80&&(!capture.State.Recording||capture.State.Frames<3);i++)await Task.Delay(25);
            await capture.StopAsync();check(factories==2&&capture.State.SavedPath!=firstPath,"a stopped recorder can reconnect and preserve a separate segment");
            var completed=new List<string>();while(capture.TryDequeueCompleted(out var saved))completed.Add(saved);
            check(completed.SequenceEqual(new[]{firstPath,capture.State.SavedPath}),"rapid finalization and reconnection retain every completed segment for background import");
            var observed=await ReplayImport.ReadAsync(capture.State.SavedPath!);check(observed.Match.Observation&&observed.Data.Frames.Count>=3,"background output is a readable observation rather than a fabricated match");
            var unavailable=new IndependentCapture(()=>null);unavailable.Poll(directory);await unavailable.StopAsync();check(!unavailable.State.Connected,"waiting for a game has a clean cancellation path");
            var geometry=ReplaySceneBuilder.Build(observed,null,observed.Data.Frames[0].Timestamp,false,false);
            check(geometry.Length>0&&geometry.All(v=>float.IsFinite(v.Position.X)),"portable scene builder renders actual observed pawns with finite geometry");
        } finally {Directory.Delete(directory,true);}
    }
    private sealed class FakeSource : IObservedGameSource {
        public int ProcessId=>1;
        public (uint Territory,MovementFrame Frame) Sample()=>(1034,new(){Timestamp=DateTime.UtcNow,Players=new(){new(){Player="Fixture Player",ClassJobId=19,HP=100,MaxHP=100,X=0,Y=0,Z=0}}});
        public void Dispose() {}
    }
}
