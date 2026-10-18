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
            var source=Path.Combine(directory,"history.db");var id=new ObjectId("507f1f77bcf86cd799439012");var timelineId=ObjectId.NewObjectId();var now=DateTime.UtcNow;
            BsonDocument Alias(string name)=>new(){["Name"]=name,["HomeWorld"]="Ixion"};
            var player=new BsonDocument {["Player"]=Alias("Own Player"),["Team"]="Astra",["Job"]="PLD",["Kills"]=2,["Deaths"]=1,["Assists"]=5,["DamageDealt"]=123456,["DamageTaken"]=234567,["HPRestored"]=34567,["TimeOnCrystal"]="00:00:32"};
            using(var db=new LiteDatabase(source)) {
                db.GetCollection<BsonDocument>("ccmatch").Insert(new BsonDocument {["_id"]=id,["TerritoryId"]=1034,["DutyStartTime"]=now,["MatchStartTime"]=now,["MatchEndTime"]=now.AddMinutes(4),["LocalPlayer"]=Alias("Own Player"),["MatchType"]="Ranked",["MatchWinner"]="Astra",["TimelineId"]=timelineId,["PostMatch"]=new BsonDocument {["RankAfter"]=new BsonDocument {["Tier"]="Crystal",["Credit"]=500},["Teams"]=new BsonDocument {["Astra"]=new BsonDocument {["TeamName"]="Astra",["PlayerStats"]=new BsonArray{player}}}}});
                var detailed=new DetailedTelemetry {Version=3,Frames=new(){new(){Timestamp=now,Players=new(){new(){Player="Own Player Ixion",Team=1,X=0,Y=0,Z=0,HP=100,MaxHP=100,ClassJobId=19}}}}};detailed.PrepareForStorage();
                db.GetCollection<BsonDocument>("cctimeline").Insert(new BsonDocument {["_id"]=timelineId,["Detailed"]=BsonMapper.Global.ToDocument(detailed)});
            }
            var before=SHA256.HashData(await File.ReadAllBytesAsync(source));
            using(var library=new RecordLibrary(Path.Combine(directory,"owned"))) {
                var report=DatabaseImport.Read(source,library,Console.WriteLine);check(report.Imported==1&&report.Skipped==0,"history import finds one valid match");
                var after=SHA256.HashData(await File.ReadAllBytesAsync(source));check(before.SequenceEqual(after),"history database remains byte-for-byte unchanged");
                var row=library.All().Single();check(row.Map=="九霄云上"&&row.Result=="胜利"&&row.Mode=="段位赛"&&row.Credit==500,"history match metadata and local result retain their meaning");
                check(row.Players.Single().Damage==123456&&row.Players.Single().CrystalSeconds==32,"settlement retains all damage and push-time values");
                var bundle=library.Load(row.Id);check(bundle.Data.Frames.Single().Players.Single().ClassJobId==19,"compressed historic movement remains available after import");
                library.Annotate(row.Id,true,"my note");DatabaseImport.Read(source,library);row=library.All().Single();check(row.Favorite&&row.Tags=="my note"&&library.All().Count==1,"repeat import deduplicates and preserves user annotations");
                var export=Path.Combine(directory,"export.json");await ReplayImport.ExportAsync(library.Load(row.Id),export);var reimport=await ReplayImport.ReadAsync(export);
                check(reimport.Match.Players.Single().Damage==123456&&reimport.Match.LocalPlayer=="Own Player Ixion"&&reimport.Match.Winner==1,"JSON export and reimport preserve complete scoreboard and perspective");
                check(RecordStatistics.Filter(library.All(),"Own -火山","全部","胜利",true).Count()==1,"combined query, exclusion, result and favorite filters agree");
                check(RecordStatistics.Aggregate(library.All(),"职业","Own Player Ixion").Single().Wins==1,"default-player statistics use own team outcome");
                var opponent=new PlayerScore {Name="Opponent",Team=2,Job="骑士"};row.Players.Add(opponent);
                check(RecordStatistics.Aggregate(new[]{row},"职业","Opponent").Single().Losses==1,"opponent statistics reverse the local result rather than copying it");
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
