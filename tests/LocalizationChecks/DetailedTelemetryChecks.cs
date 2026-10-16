using LiteDB;
using PvpStats.Analysis;
using PvpStats.Leaderboards;

internal static class DetailedTelemetryChecks {
    internal static void Run(Action<bool,string> check) {
        check(!PvpStats.Helpers.MatchVersionPolicy.NeedsLegacyDirectorWarning("CCStats","0.2.0.0",new DateTime(2026,10,17)),"new fork recordings do not lose their timeline after restart due to the independent 0.x version sequence");
        check(PvpStats.Helpers.MatchVersionPolicy.NeedsLegacyDirectorWarning(null,"2.5.2.0",new DateTime(2025,6,1)),"old upstream director corruption warning remains intact for legacy imports");
        check(!PvpStats.Helpers.MatchVersionPolicy.NeedsLegacyDirectorWarning(null,"bad version",DateTime.MaxValue),"malformed imported plugin versions cannot break validation");
        var t=new DateTime(2026,10,17,0,0,0,DateTimeKind.Utc);
        PlayerPosition Player(string name,int team,uint hp, params ObservedControl[] statuses)=>new() { Player=name,Team=team,HP=hp,MaxHP=10000,Controls=statuses.ToList() };
        var data=new DetailedTelemetry { GameVersion="test-version" };
        data.Effects.AddRange(new[]{
            new ObservedEffect { Actor="attacker",Target="victim",ActorTeam=1,TargetTeam=2,Type=3,Amount=100,GuardObserved=true },
            new ObservedEffect { Actor="attacker",Target="victim",ActorTeam=1,TargetTeam=2,Type=5,Amount=200 },
            new ObservedEffect { Actor="attacker",Target="victim",ActorTeam=1,TargetTeam=2,Type=14,StatusId=1343 },
            new ObservedEffect { Actor="attacker",Target="ally",ActorTeam=1,TargetTeam=1,Type=14,StatusId=1343 },
            new ObservedEffect { Actor="attacker",Target="victim",ActorTeam=1,TargetTeam=2,Type=20,StatusId=1343 },
            new ObservedEffect { Actor="attacker",Target="attacker",Type=4,Amount=150,SelfReturn=true,ActionId=39166 },
            new ObservedEffect { Actor="attacker",Target="attacker",Type=4,Amount=300,SelfReturn=true,ActionId=123 },
            new ObservedEffect { Actor="healer",Target="attacker",Type=4,Amount=1000,ActionId=39166 },
        });
        MovementFrame Frame(double time,float remaining,uint hp=10000)=>new() { Timestamp=t.AddSeconds(time), Players=new() { Player("attacker",1,10000),Player("victim",2,hp,new ObservedControl() { StatusId=1343,Source="attacker",Remaining=remaining }) } };
        data.Frames.AddRange(new[]{Frame(0,2),Frame(.5,1.5f),Frame(1,1),Frame(3,.5f),Frame(3.5,.2f,0)});
        var stats=DetailedAnalysis.Summarize(data);
        check(stats["victim"].GuardedDamage==100 && stats["victim"].GuardedHits==1,"Guard statistics retain delivered damage rather than claiming reconstructed raw damage");
        check(stats["victim"].BlockedOrParriedHits==1,"physical block hits are separate from Guard");
        check(stats["attacker"].ControlHits==1 && stats["attacker"].ControlResists==1,"control counts exclude friendly applications and retain resisted applications separately");
        check(stats["attacker"].ObservedControlSeconds==1,"control seconds exclude missing frames and death boundaries");
        check(stats["attacker"].SelfReturnHealing==450 && stats["attacker"].IdentifiedDrainHealing==150,"self-return healing is separated from identified drains and healing by other players");
        check(DetailedAnalysis.FrameAt(data,t.AddSeconds(-.1))==null && DetailedAnalysis.FrameAt(data,t.AddSeconds(2.1))==null,"map replay never fills positions before recording or across gaps");
        check(DetailedAnalysis.FrameAt(data,t.AddSeconds(.7))==data.Frames[1],"map timeline selects the last real sample");
        var uv=DetailedAnalysis.WorldToUv(100,-100,200,10,-10);
        check(Math.Abs(uv.X-(1244f/2048))<.00001 && Math.Abs(uv.Y-(804f/2048))<.00001,"world coordinates apply recorded map factor and offsets");
        var estimateData=new DetailedTelemetry { GameVersion="test", ActionDescriptions=new() { [29058]="Delivers an attack with a potency of 3,000." }, StatusDescriptions=new() { [3054]="Reduces damage taken by 90% and grants immunity to Stun.", [3042]="Damage dealt is increased by 20%.", [3051]="Damage taken is reduced by 20%." } };
        var hit=new ObservedEffect { Actor="attacker",Target="victim",ActorTeam=1,TargetTeam=2,Type=3,Amount=960,ActionId=29058,GuardObserved=true,SnapshotsCaptured=true,
            ActorStatuses=new() { new() { Id=3042,Remaining=5 } }, TargetStatuses=new() { new() { Id=3054,Remaining=4 },new() { Id=3051,Remaining=6 } } };
        var estimated=MitigationEstimator.Estimate(estimateData,hit);
        check(estimated.Supported && Math.Abs(estimated.GuardPrevented!.Value-8640)<.01 && Math.Abs(estimated.WithoutGuard!.Value-9600)<.01,"removing only Guard preserves concurrent supported buffs");
        check(Math.Abs(estimated.Modifier-.96)<.00001 && Math.Abs(estimated.BeforeKnownModifiers!.Value-10000)<.01,"outgoing bonus and incoming reduction multiply correctly when reconstructing the reference baseline");
        hit.TargetStatuses.Add(new() { Id=999999,Remaining=2 });
        check(!MitigationEstimator.Estimate(estimateData,hit).Supported,"unknown buffs never silently enter the supported estimate"); hit.TargetStatuses.RemoveAt(2);
        hit.ShieldPercent=10; check(!MitigationEstimator.Estimate(estimateData,hit).Supported,"shield absorption prevents an unjustified inverse estimate"); hit.ShieldPercent=0;
        hit.ActionId=29537; check(!MitigationEstimator.Estimate(estimateData,hit).Supported,"special damage and guard bypass actions are not treated as ordinary hits"); hit.ActionId=29058;
        hit.PetSource=true; check(!MitigationEstimator.Estimate(estimateData,hit).Supported,"pet damage cannot use owner buffs as pet snapshots"); hit.PetSource=false;
        hit.TargetStatuses[0].Remaining=.1f; check(!MitigationEstimator.Estimate(estimateData,hit).Supported,"near-expiry Guard state is unresolved"); hit.TargetStatuses[0].Remaining=4;
        hit.TargetStatuses.Add(new() { Id=3673,Remaining=3 }); check(!MitigationEstimator.Estimate(estimateData,hit).Supported,"broken Guard is not equivalent to the normal Guard multiplier"); hit.TargetStatuses.RemoveAt(2);
        estimateData.StatusDescriptions[3054]="未知说明"; check(!MitigationEstimator.Estimate(estimateData,hit).Supported,"unsupported client-language rules stay unresolved");
        estimateData.StatusDescriptions[3054]="Reduces damage taken by 90%.";
        estimateData.ActionDescriptions[29058]="Delivers an attack. Ignores Guard.";
        check(!MitigationEstimator.Estimate(estimateData,hit).Supported,"current recorded action descriptions can invalidate a baseline action rule");
        var countdown=new DetailedTelemetry {
            Frames=new() { new() { Timestamp=t.AddSeconds(-10) },new() { Timestamp=t },new() { Timestamp=t.AddSeconds(1) },new() { Timestamp=t.AddSeconds(10) } },
            Effects=new() { new() { Timestamp=t.AddSeconds(-1) },new() { Timestamp=t },new() { Timestamp=t.AddSeconds(8) } },
        };
        DetailedAnalysis.TrimToMatch(countdown,t,t.AddSeconds(5));
        check(countdown.Frames.Count==2&&countdown.Effects.Count==1,"results-time authoritative start trims countdown and post-match samples without requiring an unknown live start timestamp");
        var loaded=BsonMapper.Global.ToObject<DetailedTelemetry>(BsonMapper.Global.ToDocument(data));
        check(loaded.Effects.Count==8 && loaded.Frames[0].Players[1].Controls[0].Source=="attacker","detailed telemetry survives LiteDB serialization with identities and control sources intact");
        var empty=BsonMapper.Global.ToObject<DetailedTelemetry>(new BsonDocument());
        check(empty.Frames.Count==0 && empty.Effects.Count==0,"legacy records without detailed fields deserialize safely");
        var capacity=new DetailedTelemetry(); long budget=0;
        for(int i=0;i<2400 && budget<DetailedTelemetry.ByteBudget;i++) {
            var frame=Frame(i*.5,10); frame.Players=Enumerable.Range(0,10).Select(n=>Player("Full Player Name@LongWorld"+n,n<5?1:2,10000,new ObservedControl() { StatusId=1343,Source="Full Player Name@LongWorld0",Remaining=10 })).ToList();
            capacity.Frames.Add(frame); budget+=256+frame.Players.Sum(p=>384+p.Controls.Count*192);
            var effect=new ObservedEffect { Actor="Full Player Name@LongWorld1",Target="Full Player Name@LongWorld6",Type=3,Amount=500,ActorStatuses=new() { new() { Id=3042,Remaining=6 } },TargetStatuses=new() { new() { Id=3054,Remaining=6 } } };
            for(int n=0;n<3;n++) { capacity.Effects.Add(effect); budget+=512+384; }
        }
        var serialized=BsonSerializer.Serialize(BsonMapper.Global.ToDocument(capacity));
        check(serialized.Length < 8*1024*1024 && serialized.Length<budget,"conservative telemetry budget bounds a full-length ten-player match below the reserved BSON budget");
        var entry=new RankingEntry { CharacterName="Djinn Piston",World="Durandal [Gaia]" };
        check(RankingIdentity.Matches(entry,"Djinn Piston","Durandal"),"ranking identification strips DC suffix but matches home world");
        check(!RankingIdentity.Matches(entry,"Djinn Piston","Siren")&&!RankingIdentity.Matches(entry,"Other Player","Durandal"),"same name in another world is never highlighted as the local character");
        check(!RankingIdentity.Matches(entry,"","")&&RankingIdentity.Matches(entry,"  Djinn  Piston ","Durandal"),"ranking identity handles login absence and normalized whitespace");
    }
}
