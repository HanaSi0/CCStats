using System.Buffers.Binary;
using System.Text;
using CCStats.Core.Capture;
using CCStats.Desktop.Capture;
using PvpStats.Analysis;
internal static class NativeReaderChecks {
    internal static async Task Run(Action<bool,string> check) {
        check(ExternalGameReader.IsGameExecutable("FFXIV_DX11")&&ExternalGameReader.IsGameExecutable("ffxiv")&&!ExternalGameReader.IsGameExecutable("ffxivlauncher"),"client names are case insensitive and launcher processes are excluded");
#if !CCSTATS_CHINA
        var ranking=new PvpStats.Leaderboards.RankingEntry {CharacterName="Own Player",World="Ixion [Mana]"};
        check(PvpStats.Leaderboards.RankingIdentity.MatchesFullIdentity(ranking," Own  Player Ixion ")&&!PvpStats.Leaderboards.RankingIdentity.MatchesFullIdentity(ranking,"Own Player Titan"),"desktop ranking identity normalizes names and DC suffixes without matching another home world");
#else
        check(CCStats.Core.Distribution.IsChina&&typeof(ClientCatalog).Assembly.GetType("PvpStats.Leaderboards.LeaderboardService")==null,"China edition excludes leaderboard implementation");
        check(ClientCatalog.PlayerName("测试角色",1167)=="测试角色 红玉海"&&CCStats.Core.Distribution.DataDirectoryName=="CCStatsDesktop-CN","China edition identifies server names and isolates local records");
#endif
        var memory=new Dictionary<long,byte[]>();
        void Put(long at,byte[] bytes)=>memory[at]=bytes;
        byte[] Pointer(long value){var b=new byte[8];BinaryPrimitives.WriteInt64LittleEndian(b,value);return b;}
        bool Read(long at,byte[] output) {
            foreach(var (start,data) in memory)if(at>=start&&at-start+output.Length<=data.Length){data.AsSpan((int)(at-start),output.Length).CopyTo(output);return true;}
            return false;
        }
        const long objects=0x100000,game=0x200000,frameworkStatic=0x300000,framework=0x400000,director=0x500000,rosterAddress=0x700000,pc=0x800000,npc=0x900000,eobj=0xA00000,localStatic=0xB00000;
        var table=new byte[ClientLayout.ObjectSlots*8];Pointer(npc).CopyTo(table,0);Pointer(pc).CopyTo(table,4*8);Pointer(eobj).CopyTo(table,500*8);Put(objects+ClientLayout.ObjectArrayOffset,table);
        var territory=new byte[4];BinaryPrimitives.WriteUInt32LittleEndian(territory,1116);Put(game+ClientLayout.TerritoryOffset,territory);
        Put(frameworkStatic,Pointer(framework));Put(framework+ClientLayout.ActiveDirectorOffset,Pointer(director));Put(director+ClientLayout.DirectorTypeOffset,new byte[]{5});Put(director+ClientLayout.DirectorPlayerPointerOffset,Pointer(rosterAddress));
        var roster=new byte[ClientLayout.DirectorPlayerBytes*10];Encoding.UTF8.GetBytes("Stale Player").CopyTo(roster,0x8A);BinaryPrimitives.WriteUInt32LittleEndian(roster.AsSpan(0xE0),999);roster[0xE8]=1;roster[0xE9]=24;BinaryPrimitives.WriteUInt16LittleEndian(roster.AsSpan(0x128),21);Put(rosterAddress,roster);
        var states=new byte[0x608];BinaryPrimitives.WriteUInt32LittleEndian(states,77);BinaryPrimitives.WriteUInt16LittleEndian(states.AsSpan(8),2);BinaryPrimitives.WriteUInt16LittleEndian(states.AsSpan(0x602),1);Put(director+0xDE0,states);
        byte[] Header(byte kind,uint id,string name="") {var h=new byte[ClientLayout.HeaderBytes];h[0x90]=kind;BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0x78),id);Encoding.UTF8.GetBytes(name).CopyTo(h,0x30);return h;}
        var player=Header(1,100,"Own Player");player[0x1CA]=24;BinaryPrimitives.WriteUInt32LittleEndian(player.AsSpan(0x1AC),9000);BinaryPrimitives.WriteUInt32LittleEndian(player.AsSpan(0x1B0),10000);Put(pc,player);
        Put(npc,Header(2,200));var nameId=new byte[4];BinaryPrimitives.WriteUInt32LittleEndian(nameId,12278);Put(npc+0x2338,nameId);
        var cast=new byte[ClientLayout.CastBytes];cast[0]=1;cast[1]=1;BinaryPrimitives.WriteUInt32LittleEndian(cast.AsSpan(4),32365);BinaryPrimitives.WriteSingleLittleEndian(cast.AsSpan(0x34),1);BinaryPrimitives.WriteSingleLittleEndian(cast.AsSpan(0x3C),5);Put(npc+ClientLayout.CastOffset,cast);
        var obj=Header(7,0xE0000000);BinaryPrimitives.WriteUInt32LittleEndian(obj.AsSpan(0x84),2010780);Put(eobj,obj);Put(localStatic,Pointer(0));
        using var reader=new ExternalGameReader(Read,objects,game,frameworkStatic,localStatic);
        var first=reader.Sample();
        check(first.Frame.Players.Single().Player.Contains("Own Player")&&first.Frame.Players[0].HP==9000,"real object-reader pipeline keeps pose and HP when optional world/status memory is unreadable");
        var neutralBundle=new CCStats.Core.Library.ReplayBundle {Data=new(){Frames=new(){first.Frame}}};
        check(CCStats.Core.Library.ReplaySceneBuilder.Build(neutralBundle,null,first.Frame.Timestamp,false,false).Length>0,"unconfirmed teams still render neutral characters rather than disappearing from the map");
        check(first.Frame.Players.Single().Team==0&&reader.Telemetry!.ChannelStatus["rosterMatches"]=="0","a mismatched director roster cannot erase all loaded players or invent a team");
        check(reader.Telemetry!.MapStates.Single().LayoutId==77&&!reader.Telemetry.DirectorAvailable,"scene states are collected even when director progress is unreadable");
        check(reader.Telemetry.ArenaObjects.Any(o=>o.Kind==ArenaMechanicKind.Yojimbo)&&reader.Telemetry.ArenaObjects.Any(o=>o.Kind==ArenaMechanicKind.Gold)&&reader.Telemetry.MapEvents.Single().ActionId==32365,"loaded NPC casts and non-network event objects survive the full memory-read pipeline");
        check(reader.Telemetry.LocalPlayer=="","an NPC in slot zero and an unlogged local pointer cannot lock a character");
        Put(pc+ClientLayout.WorldOffset,new byte[]{21,0});Put(pc+ClientLayout.StatusOffset,new byte[ClientLayout.StatusBytes]);Put(localStatic,Pointer(pc));await Task.Delay(220);
        reader.Sample();check(reader.Telemetry!.LocalPlayer==ClientCatalog.PlayerName("Own Player",21),"login identity comes from the native local-player pointer even when its table slot is not zero");
        const long otherPc=0xC00000;var other=Header(1,300,"Other Player");other[0x1CA]=21;BinaryPrimitives.WriteUInt32LittleEndian(other.AsSpan(0x1AC),12000);BinaryPrimitives.WriteUInt32LittleEndian(other.AsSpan(0x1B0),12000);Put(otherPc,other);Put(otherPc+ClientLayout.WorldOffset,new byte[]{22,0});Put(otherPc+ClientLayout.StatusOffset,new byte[ClientLayout.StatusBytes]);Pointer(otherPc).CopyTo(table,6*8);Put(localStatic,Pointer(otherPc));reader.Sample();
        check(reader.Telemetry!.LocalPlayer==ClientCatalog.PlayerName("Other Player",22),"switching the native login pointer binds the new character without reusing the previous role");
        memory.Remove(objects+ClientLayout.ObjectArrayOffset);reader.Sample();check(reader.Telemetry!.LocalPlayer==ClientCatalog.PlayerName("Other Player",22)&&reader.Telemetry.ChannelStatus["objectTable"].Contains("暂不可读"),"a transient object-table read failure does not erase independently validated login identity");Put(objects+ClientLayout.ObjectArrayOffset,table);
        Put(localStatic,Pointer(0));reader.Sample();check(reader.Telemetry!.LocalPlayer==""&&reader.Telemetry.Profile==null,"logout clears the local identity and profile instead of keeping the last observed character locked");
        player[0x1CA]=0;Put(localStatic,Pointer(pc));await Task.Delay(220);reader.Sample();check(reader.Telemetry!.LocalPlayer==ClientCatalog.PlayerName("Own Player",21),"login identity remains readable when the optional battle job field is not initialized");check(reader.Telemetry!.ChannelStatus["rejectedPlayers"]=="1"&&reader.Telemetry.ChannelStatus["playerRejections"].Contains("position-hp-job"),"incompatible player fields have an explicit diagnostic reason rather than silently producing an empty recording");
        var directory=Path.Combine(Path.GetTempPath(),"ccstats-loading-"+Guid.NewGuid().ToString("N"));
        try {
            var source=new LoadingSource();var capture=new IndependentCapture(()=>source);capture.Connect(directory);
            for(int i=0;i<100&&source.Samples<16;i++)await Task.Delay(20);
            await capture.StopAsync();int completed=0;while(capture.TryDequeueCompleted(out _))completed++;
            check(completed==1,"brief territory-zero loading frames do not split one observation into additional records");
            var waiting=new SessionSource(false);var waitingCapture=new IndependentCapture(()=>waiting);waitingCapture.Connect(Path.Combine(directory,"waiting"));
            for(int i=0;i<100&&waiting.Samples<8;i++)await Task.Delay(20);
            await waitingCapture.StopAsync();check(!waitingCapture.TryDequeueCompleted(out _)&&!Directory.Exists(Path.Combine(directory,"waiting")),"standing in an arena while waiting never creates a standalone match or file");
            var session=new SessionSource(true);var sessionCapture=new IndependentCapture(()=>session);sessionCapture.Connect(Path.Combine(directory,"session"));
            for(int i=0;i<150&&session.Samples<35;i++)await Task.Delay(20);
            await sessionCapture.StopAsync();var paths=new List<string>();while(sessionCapture.TryDequeueCompleted(out var path))paths.Add(path);
            check(paths.Count==1,"waiting, combat, visible result and postmatch waiting create exactly one recording");
            var recorded=await CCStats.Core.Library.ReplayImport.ReadAsync(paths.Single());
            check(recorded.Data.Frames.Count>0&&recorded.Data.Frames.Last().Timestamp<session.SettlementTime&&recorded.Match.EndUtc==recorded.Data.Frames.Last().Timestamp,"postmatch waiting is excluded and replay end follows the last actual frame");
            check(recorded.ResultPanels.Count==1&&recorded.ResultPanels[0].Panel=="PvPResult","the result panel that stops recording is retained in the replay instead of discarded before finalization");
            using(var library=new CCStats.Core.Library.RecordLibrary(Path.Combine(directory,"settled-library"))){library.Save(recorded);check(library.Load(recorded.Match.Id).ResultPanels.Count==1,"result evidence survives database persistence for later schema verification");}
            check(recorded.Match.StartUtc==recorded.Data.Frames.First().Timestamp,"bounded preroll timestamps agree with the replay origin");
            var probe=new ObservationBatch();probe.ChannelStatus["rejectedPlayers"]="10";CaptureDiagnostics.SaveProbe(directory,1116,probe);
            check(File.Exists(Path.Combine(directory,"diagnostics","Probe-1116.json")),"zero-player arena failures retain a diagnostic snapshot independent of whether a recording could start");
        } finally {if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    private sealed class SessionSource(bool active):IObservedGameSource {
        public int Samples;public int ProcessId=>0;public ObservationBatch? Telemetry {get;private set;}public DateTime SettlementTime;
        public (uint Territory,MovementFrame Frame) Sample() {
            int n=Interlocked.Increment(ref Samples);var now=DateTime.UtcNow;
            Telemetry=new(){CombatActive=active&&n>=10};
            if(active&&n==25){SettlementTime=now;Telemetry.SettlementPanels.Add(new(now,"PvPResult",new()));}
            return (1116,new(){Timestamp=now,Players=new(){new(){Player="Own",HP=100,MaxHP=100,ClassJobId=24}}});
        }
        public void Dispose(){}
    }
    private sealed class LoadingSource:IObservedGameSource {
        public int Samples;public int ProcessId=>0;public ObservationBatch? Telemetry=>new(){CombatActive=true};
        public (uint Territory,MovementFrame Frame) Sample() {int n=Interlocked.Increment(ref Samples);return ((uint)(n is 5 or 6?0:1116),new(){Timestamp=DateTime.UtcNow,Players=new(){new(){Player="Own",HP=100,MaxHP=100,ClassJobId=24}}});}
        public void Dispose() {}
    }
}
