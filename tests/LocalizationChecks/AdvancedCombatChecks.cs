using System.Numerics;
using PvpStats.Analysis;
internal static class AdvancedCombatChecks {
    public static void Run(Action<bool,string> check) {
        var t=new DateTime(2026,10,17,0,0,0,DateTimeKind.Utc);
        var e=new ObservedEffect {Timestamp=t,Actor="enemy",Target="me",ActorTeam=2,TargetTeam=1,ActionId=29230,Type=3,Amount=1800,GuardObserved=true,SnapshotsCaptured=true,TargetStatuses=new(){new(){Id=3054,Remaining=3}},ActorStatuses=new(){new(){Id=4119,Remaining=4}}};
        var data=new DetailedTelemetry {GameVersion="sample",ActionDescriptions=new(){[29230]="前方直線範囲に無属性魔法攻撃。"},StatusDescriptions=new(){[3054]="被ダメージを90％軽減する。"},Effects=new(){e}};
        var estimate=MitigationEstimator.EstimateAdvanced(data,e);
        check(estimate.Supported&&estimate.GuardPrevented==16200&&estimate.BeforeKnownModifiers==null,"Japanese client Guard marginal estimate preserves supported damage buffs without inventing base damage");
        e.ShieldPercent=10;check(!MitigationEstimator.EstimateAdvanced(data,e).Supported,"actual shields keep inverse damage unresolved");e.ShieldPercent=0;
        e.TargetStatuses.Add(new(){Id=99999,Remaining=4});check(!MitigationEstimator.EstimateAdvanced(data,e).Supported,"unrecognized status cannot enter marginal sum");e.TargetStatuses.RemoveAt(1);
        e.ActionId=29537;check(!MitigationEstimator.EstimateAdvanced(data,e).Supported,"Zantetsuken Guard bypass excluded");e.ActionId=29230;
        data.Effects.AddRange(new[]{new ObservedEffect {Timestamp=t,Sequence=1,Actor="enemy",Target="me",ActorTeam=2,TargetTeam=1,StatusId=1343,Type=20,GuardObserved=true},new ObservedEffect {Timestamp=t,Sequence=1,Actor="enemy",Target="me",ActorTeam=2,TargetTeam=1,StatusId=1343,Type=55,GuardObserved=true},new ObservedEffect {Timestamp=t,Actor="me",Target="me",Type=4,ActionId=29543,Amount=6000,SelfReturn=true}});
        var result=AdvancedCombatAnalysis.Calculate(data,new(){estimate});
        check(result["me"].EstimatedGuardPrevented==16200&&result["me"].ControlResistsUnderGuard==1,"advanced Guard damage and resisted control deduplicate packet outcomes");
        check(result["me"].DrainHealing==6000&&result["me"].SelfReturnHealing==6000,"uploaded Reaper drain action recognized without counting generic self heals");
        data.Effects.Add(new(){Actor="me",Target="me",Type=4,ActionId=39217,Amount=20000,SelfReturn=true});
        check(AdvancedCombatAnalysis.Calculate(data,new())["me"].DrainHealing==6000,"Star Prism healing is not mislabeled as lifesteal");
        var player=new PlayerPosition {Player="me",HP=10,StatusTimestamp=t,Statuses=new(){new(){Id=3054,Remaining=3}}};
        check(ReplaySceneGeometry.GuardVisible(player,t.AddSeconds(1))&&!ReplaySceneGeometry.GuardVisible(player,t.AddSeconds(4)),"Guard sphere follows actual status expiry");
        player.HP=0;check(!ReplaySceneGeometry.GuardVisible(player,t),"dead players have no Guard sphere");player.HP=10;
        var geometry=new ReplaySceneGeometry();geometry.GuardBubble(Vector3.Zero);var vertices=geometry.Vertices.ToArray();
        check(vertices.Length<5000&&vertices.All(v=>LimitBreakVisuals.Finite(v.Position))&&vertices.Max(v=>v.Position.Y)>3,"Guard effect is bounded real 3D sphere geometry");
        var badges=new List<Vector2>();
        for(int i=0;i<10;i++)ReplayLabelLayout.Place(new(150,150),i<5?1:2,12,Vector2.Zero,new(300,300),badges);
        check(badges.All(p=>p.X>=14&&p.Y>=14&&p.X<=286&&p.Y<=286),"crowded job badges remain inside the replay canvas");
        check(badges.SelectMany((a,i)=>badges.Skip(i+1).Select(b=>Math.Abs(a.X-b.X)>=26||Math.Abs(a.Y-b.Y)>=26)).All(x=>x),"ten overlapping pawns retain individually separated job badges without moving world positions");
        var rule=LimitBreakVisuals.Rules.First(r=>r.ActionId==29230&&!r.Benefit);var skill=new ReplaySkillEvent {Timestamp=t,Resolved=true};
        check(LimitBreakVisuals.Opacity(rule,skill,t.AddSeconds(.4))==1&&LimitBreakVisuals.Opacity(rule,skill,t.AddSeconds(.8))>.4f,"instant range holds a clearly visible initial plateau before fading");
        data.Frames=new(){new(){Timestamp=t,Players=new(){player}},new(){Timestamp=t.AddSeconds(.1),Players=new(){new(){Player="me",HP=10,StatusTimestamp=t,Statuses=player.Statuses}}}};
        check(Math.Abs(AdvancedCombatAnalysis.Calculate(data,new())["me"].GuardSeconds-.1)<.001,"Guard uptime integrates only adjacent living observations");
    }
}
