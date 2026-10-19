using System.Buffers.Binary;
using System.Text;
using CCStats.Core.Capture;
using CCStats.Core.Library;
using PvpStats.Analysis;
internal static class CaptureAdaptationChecks {
    public static async Task Run(Action<bool,string> check) {
        var now=DateTime.UtcNow;
        var progress=new byte[28];for(int i=0;i<7;i++)BinaryPrimitives.WriteInt32LittleEndian(progress.AsSpan(i*4),new[]{-500,420,600,3,2,999,1000}[i]);
        check(DirectorDecoder.Progress(progress) is {CrystalProgress:-500,AstraProgress:420,UmbraProgress:600},"director preserves signed crystal position and native team progress");
        BinaryPrimitives.WriteInt32LittleEndian(progress.AsSpan(12),6);check(DirectorDecoder.Progress(progress)==null,"invalid point occupancy rejects a mismatched director layout");
        var roster=new byte[ClientLayout.DirectorPlayerBytes*10];var row=roster.AsSpan(0,ClientLayout.DirectorPlayerBytes);
        Encoding.UTF8.GetBytes("Sample Player").CopyTo(row[0x8A..]);BinaryPrimitives.WriteUInt32LittleEndian(row[0xE0..],100);row[0xE8]=1;row[0xE9]=24;BinaryPrimitives.WriteUInt16LittleEndian(row[0x128..],21);
        check(DirectorDecoder.Roster(roster)?.Single() is {EntityId:100,Team:2,Job:24,World:21},"native director team 0/1 becomes replay team 1/2");
        row[0xE8]=2;check(DirectorDecoder.Roster(roster)==null,"unknown director team never becomes an opponent");
        var own=new PlayerPosition {Player="Own [世界 21]",Team=1,ClassJobId=24,HP=100,MaxHP=100};var enemy=new PlayerPosition {Player="Enemy [世界 22]",Team=2,HP=100,MaxHP=100};
        var actors=new Dictionary<uint,ObservedActor> {[100]=new(100,0,0,1,1,2,3,0,own),[200]=new(200,0,0,1,4,5,6,0,enemy),[300]=new(300,100,6566,2,7,8,9,0)};
        var statusData=new byte[ClientLayout.StatusBytes];BinaryPrimitives.WriteUInt16LittleEndian(statusData.AsSpan(8),1343);BinaryPrimitives.WriteInt32LittleEndian(statusData.AsSpan(12),BitConverter.SingleToInt32Bits(3));BinaryPrimitives.WriteUInt32LittleEndian(statusData.AsSpan(16),100);
        var controls=ControlSourceDecoder.Resolve(ControlSourceDecoder.Decode(statusData),now,now.AddSeconds(.2),actors);
        check(controls.Single().Source==own.Player&&Math.Abs(controls.Single().Remaining-2.8)<.001,"observed control duration retains the real status source and subtracts cache age");
        check(ControlSourceDecoder.Resolve(ControlSourceDecoder.Decode(statusData),now,now.AddSeconds(4),actors).Count==0,"expired cached control cannot add false control duration");
        var journal=new ActionEffectJournal();var bytes=new byte[ClientLayout.EffectBytes];var batch=new ObservationBatch();
        journal.Observe(200,bytes,actors,now,batch);
        Fill(bytes,1,29230,100,200,3,100,0x40,1);
        journal.Observe(200,bytes,actors,now,batch);
        check(batch.Effects.Single().Amount==65636&&batch.Effects.Single().TargetTeam==2&&batch.Skills.Single().ActionId==29230,"retained effect slots decode large amounts, attribution and LB action identity");
        check(!batch.Effects.Single().SnapshotsCaptured&&!batch.Effects.Single().GuardObserved&&batch.Skills.Single().LegacyEstimate,"post-impact polling cannot advertise event-time snapshots or exact LB geometry");
        journal.Observe(100,bytes,actors,now,batch);journal.Observe(200,bytes,actors,now,batch);
        check(batch.Effects.Count==1,"duplicated source/target effect slots cannot double-count a hit");
        Fill(bytes,2,29675,300,200,3,999,0,0);journal.Observe(200,bytes,actors,now,batch);
        check(batch.Effects.Last().PetSource&&batch.Effects.Last().Actor==own.Player&&batch.Skills.Last().SummonTargets.Single().Player==enemy.Player,"summon attacks credit their real owner and retain the physical source and hit target");
        Fill(bytes,3,29230,100,200,4,500,0x80,0);journal.Observe(200,bytes,actors,now,batch);
        check(batch.Effects.Last().SelfReturn&&batch.Effects.Last().Target==own.Player,"returned healing targets the caster rather than the original victim");
        var phases=new ActionEffectJournal();var phaseBatch=new ObservationBatch();var phaseBytes=new byte[ClientLayout.EffectBytes];phases.Observe(200,phaseBytes,actors,now,phaseBatch);
        Fill(phaseBytes,50,29230,100,200,0,0,0,0);phases.Observe(200,phaseBytes,actors,now,phaseBatch);
        Fill(phaseBytes,50,29230,100,200,3,222,0,0);phases.Observe(200,phaseBytes,actors,now.AddSeconds(.2),phaseBatch);
        check(phaseBatch.Effects.Single().Amount==222&&phaseBatch.Skills.Any(s=>!s.Resolved)&&phaseBatch.Skills.Any(s=>s.Resolved),"an empty use event cannot suppress the later damage impact with the same action sequence");
        phases.Observe(200,phaseBytes,actors,now.AddSeconds(.3),phaseBatch);check(phaseBatch.Effects.Count==1&&phaseBatch.Skills.Count==2,"re-reading a completed impact cannot double-count damage");
        var fresh=new ActionEffectJournal();var stale=new ObservationBatch();fresh.Observe(200,bytes,actors,now,stale);check(stale.Effects.Count==0,"attachment does not replay stale effect slots as a new action");
        journal.Reset();var afterReset=new ObservationBatch();journal.Observe(200,bytes,actors,now,afterReset);check(afterReset.Effects.Count==0,"map transitions re-prime the retained effect baseline");
        check(ClientCatalog.PlayerName("Test",48)=="Test Ixion"&&ClientCatalog.PlayerName("Test",65535)=="Test [世界 65535]","official world identities match historical names while unmapped IDs remain explicit");
        var profileBytes=new byte[0x8C];profileBytes[0]=1;profileBytes[0x62]=6;profileBytes[0x63]=6;profileBytes[0x4D]=17;
        BinaryPrimitives.WriteUInt16LittleEndian(profileBytes.AsSpan(0x4E),10);BinaryPrimitives.WriteUInt16LittleEndian(profileBytes.AsSpan(0x50),6);
        var profile=PlayerPvpProfile.Decode(profileBytes,own.Player,now)!;check(profile is {CasualMatches:10,CasualWins:6,RankName:"水晶"},"client profile counters and rank decode without reconstructing them from saved matches");
        BinaryPrimitives.WriteUInt16LittleEndian(profileBytes.AsSpan(0x50),11);check(PlayerPvpProfile.Decode(profileBytes,own.Player,now)==null,"invalid lifetime counters reject a mismatched profile layout");
        var ui=new byte[64];BinaryPrimitives.WriteInt32LittleEndian(ui,5);BinaryPrimitives.WriteUInt32LittleEndian(ui.AsSpan(8),123456);BinaryPrimitives.WriteInt32LittleEndian(ui.AsSpan(16),8);BinaryPrimitives.WriteInt64LittleEndian(ui.AsSpan(24),12345);BinaryPrimitives.WriteInt32LittleEndian(ui.AsSpan(32),12);BinaryPrimitives.WriteInt64LittleEndian(ui.AsSpan(40),98765);
        var values=ClientUiDecoder.Decode(ui,4,(_,_)=>"Player Name");check(values.Count==2&&values[0].Value=="123456"&&values[1].Value=="Player Name","settlement probes export primitive fields while omitting arbitrary pointers");
        var eventHeader=new byte[ClientLayout.HeaderBytes];eventHeader[0x90]=7;BinaryPrimitives.WriteUInt32LittleEndian(eventHeader.AsSpan(0x78),0xE0000000);BinaryPrimitives.WriteUInt32LittleEndian(eventHeader.AsSpan(0x84),2010780);BinaryPrimitives.WriteUInt32LittleEndian(eventHeader.AsSpan(0x7C),777);
        var eventObject=ArenaObjectDecoder.Decode(eventHeader,0,1116,3,now);
        check(eventObject is {EntityId:0,Kind:ArenaMechanicKind.Gold,ObjectId:>0}&&eventObject.ObjectId!=ArenaObjectDecoder.Decode(eventHeader,0,1116,4,now)!.ObjectId,"event objects without network entity IDs retain distinct scene identities and mechanism classification");
        eventHeader[0x90]=2;check(ArenaObjectDecoder.Decode(eventHeader,0,1116,3,now)==null,"an unidentified NPC cannot be rendered as an active map mechanism");
        check(SettlementTextProbe.IsResultPanel("ColosseumRecord")&&!SettlementTextProbe.IsResultPanel("ChatLog"),"native PVP result panel identity is recognized while unrelated UI is excluded");
        var memory=new Dictionary<long,byte[]>();var uld=new byte[0x58];BinaryPrimitives.WriteUInt16LittleEndian(uld.AsSpan(0x42),1);BinaryPrimitives.WriteInt64LittleEndian(uld.AsSpan(0x50),0x30000);memory[0x20028]=uld;
        var list=new byte[8];BinaryPrimitives.WriteInt64LittleEndian(list,0x40000);memory[0x30000]=list;
        var node=new byte[0xD8];BinaryPrimitives.WriteUInt32LittleEndian(node.AsSpan(8),77);BinaryPrimitives.WriteUInt16LittleEndian(node.AsSpan(0x40),3);BinaryPrimitives.WriteInt64LittleEndian(node.AsSpan(0xD0),0x50000);memory[0x40000]=node;
        var label=new byte[512];Encoding.UTF8.GetBytes("Damage Dealt").CopyTo(label,0);memory[0x50000]=label;
        var labels=SettlementTextProbe.Read(0x20000,(address,size)=>memory.TryGetValue(address,out var value)&&value.Length>=size?value.AsSpan(0,size).ToArray():null);
        check(labels.Single().Text=="Damage Dealt"&&labels.Single().NodeId==77&&Encoding.UTF8.GetString(labels.Single().EncodedText)=="Damage Dealt","result label probes preserve semantic headers and their exact bounded text bytes");
        var directory=Path.Combine(Path.GetTempPath(),"ccstats-adaptation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try {
            string path;
            using(var writer=new ObservationWriter(Path.Combine(directory,"observations"),1034)) {
                path=writer.Destination;batch.Profile=profile;batch.GameVersion="2026.10.13";batch.LocalPlayer=own.Player;batch.DirectorAvailable=true;batch.Summons=new(){Timestamp=now,Summons=new(){new(){EntityId=300,Owner=own.Player,X=7,Y=8,Z=9}}};
                batch.MapStates.Add(new(now,77,1,2));batch.MapEvents.Add(new(){Timestamp=now,ActionId=28731,SourceEntityId=500,Kind=ReplayMapEventKind.CastObserved,ObservedUntil=now,ExpectedEndTime=now.AddSeconds(2)});
                batch.ArenaObjects.Add(new(){Timestamp=now,ObservedUntil=now,ObjectId=500,EntityId=500,Kind=ArenaMechanicKind.Twister,X=1,Y=2,Z=3});
                var frame=new MovementFrame {Timestamp=now,Players=new(){own,enemy},AstraProgress=500,UmbraProgress=200,Crystal=new(){X=3,Y=4,Z=5}};
                writer.Append(1034,frame,batch);
                var next=new ObservationBatch {GameVersion=batch.GameVersion,DirectorAvailable=true,Summons=new(){Timestamp=now.AddSeconds(.2)}};
                next.MapEvents.Add(new(){Timestamp=now.AddSeconds(.2),ActionId=28731,SourceEntityId=500,Kind=ReplayMapEventKind.ActionResolved});
                frame.Timestamp=now.AddSeconds(.2);writer.Append(1034,frame,next);
                frame.Timestamp=now.AddSeconds(.4);frame.AstraProgress=-1;frame.UmbraProgress=-1;writer.Append(1034,frame);
            }
            using(var summary=System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Directory.EnumerateFiles(Path.Combine(directory,"observations","diagnostics"),"Observation-*.json").Single()))) {
                check(summary.RootElement.GetProperty("Effects").GetInt32()==3&&summary.RootElement.GetProperty("Actions").EnumerateArray().Sum(a=>a.GetProperty("Damage").GetInt64())==66635&&summary.RootElement.GetProperty("EventTimeSnapshots").GetInt32()==0,"diagnostic summaries preserve observed damage totals without inventing event-time snapshots");
            }
            var bundle=await ReplayImport.ReadAsync(path);
            check(bundle.ClientProfile?.CasualMatches==10,"client profile snapshot survives recording finalization");
            check(bundle.Match.Observation&&bundle.Match.LocalPlayer==own.Player&&bundle.Match.Winner==0&&!bundle.Match.ScoreboardComplete,"enriched observations preserve local identity without fabricating official results");
            check(bundle.Data.Effects.Count==3&&bundle.Data.SummonFrames.Count==2&&bundle.Data.SummonFrames.Last().Summons.Count==0,"effects and summon appearance/disappearance survive recording finalization");
            check(bundle.Data.DirectorObserved&&bundle.Data.Frames.First().Crystal?.X==3&&bundle.Progress.Count==3,"crystal coordinates and director progress reach replay and numeric analysis");
            check(bundle.Data.ArenaStates.Single().State==1&&bundle.Data.MapMechanics.First().ResolvedAt.HasValue&&bundle.Data.CaptureWarnings.Count>0,"scene states, actual cast resolution and provenance are saved with the recording");
            using(var library=new RecordLibrary(Path.Combine(directory,"library"))) {library.Save(bundle);library.SaveProfile(profile with {ObservedAt=now.AddDays(-1),CasualMatches=1});check(library.Profile(own.Player)?.CasualMatches==10,"older imports cannot overwrite newer client profile totals");var stored=library.Load(bundle.Match.Id);check(stored.Data.Frames.Last().AstraProgress==-1&&stored.Data.Frames.Last().UmbraProgress==-1,"missing director observations remain missing after movement compression");check(stored.Data.ArenaStates.Single().LayoutId==77&&stored.Data.CaptureMethod=="ReadOnlyMemoryPolling","supplemental client state survives the real database codec");}
            var export=Path.Combine(directory,"export.json");await ReplayImport.ExportAsync(bundle,export);var again=await ReplayImport.ReadAsync(export);
            check(again.Data.ArenaStates.Count==1&&again.Data.Effects.Count==3&&again.Match.Observation,"export and reimport retain supplemental event provenance");
            batch.SettlementPanels.Add(new(now,"PvPResult",values));CaptureDiagnostics.SavePanels(Path.Combine(directory,"observations"),1034,batch);
            var capture=new CCStats.Desktop.Capture.IndependentCapture(()=>new EnrichedSource());capture.Connect(Path.Combine(directory,"automatic"));
            for(int i=0;i<80&&capture.State.Frames<3;i++)await Task.Delay(25);
            await capture.FlushAsync();check(capture.State.Connected,"diagnostic flush persists buffered data without disconnecting or splitting the match");
            await capture.StopAsync();var completed=new List<string>();while(capture.TryDequeueCompleted(out var saved))completed.Add(saved);check(completed.Count==1,"exporting diagnostics during combat does not create a second recording");var automatic=await ReplayImport.ReadAsync(capture.State.SavedPath!);
            check(automatic.Data.Effects.Count==automatic.Data.Frames.Count&&automatic.Data.DirectorObserved&&automatic.Data.Frames[0].Players[0].Team==2,"automatic background capture consumes supplemental source channels in the same cycle as movement");
            check(capture.State.LocalPlayer==""&&automatic.Match.LocalPlayer=="Own"&&capture.State.GameVersion=="test-build"&&capture.State.ChannelStatus?["director"]=="ok","stopping clears the active login while saved observations retain identity and diagnostics retain client/channel state");
            var zip=Path.Combine(directory,"diagnostics.zip");await CaptureDiagnostics.ExportAsync(directory,zip,new {State="fixture"});
            using(var archive=System.IO.Compression.ZipFile.OpenRead(zip))check(archive.Entries.Any(e=>e.FullName=="status.json")&&archive.Entries.Any(e=>e.FullName.StartsWith("observations/"))&&archive.Entries.Any(e=>e.FullName.StartsWith("settlement/")),"diagnostic export includes bounded settlement fields and status without recording arbitrary memory");
        } finally {Directory.Delete(directory,true);}
    }
    private sealed class EnrichedSource : IObservedGameSource {
        public int ProcessId=>1;
        public ObservationBatch? Telemetry {get;private set;}
        public (uint Territory,MovementFrame Frame) Sample() {
            var now=DateTime.UtcNow;Telemetry=new(){LocalPlayer="Own",GameVersion="test-build",CombatActive=true,DirectorAvailable=true,EffectsPolled=true};Telemetry.ChannelStatus["director"]="ok";Telemetry.Effects.Add(new(){Timestamp=now,ActionId=29230,Actor="Own",Target="Other",Type=3,Amount=100});
            return (1034,new(){Timestamp=now,Players=new(){new(){Player="Own",Team=2,ClassJobId=24,HP=100,MaxHP=100}}});
        }
        public void Dispose() {}
    }
    private static void Fill(byte[] bytes,uint sequence,uint action,uint source,uint target,byte type,ushort value,byte flags,byte upper) {
        bytes.AsSpan().Clear();BinaryPrimitives.WriteUInt32LittleEndian(bytes,sequence);bytes[9]=1;BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0xC),action);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x18),source);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x28),target);
        bytes[0x38]=type;bytes[0x3C]=upper;bytes[0x3D]=flags;BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x3E),value);
    }
}
