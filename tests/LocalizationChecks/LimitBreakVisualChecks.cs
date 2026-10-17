using System.Numerics;
using LiteDB;
using PvpStats.Analysis;
internal static class LimitBreakVisualChecks {
    public static void Run(Action<bool,string> check) {
        var rules=LimitBreakVisuals.Rules;
        var jobs=new uint[]{19,21,32,37,24,28,33,40,20,22,30,34,39,41,23,31,38,25,27,35,42};
        check(jobs.All(id=>rules.Any(r=>r.JobId==id)),"all 21 PvP jobs have LB visual rules");
        check(rules.All(r=>r.Size>0&&float.IsFinite(r.Size)&&r.Duration>0&&r.Duration<=30),"LB catalog has finite bounded geometry and lifetimes");
        check(new uint[]{29069,29083,29097,29130,29131,29469,29230,41502,29255,29266,29267,29485,29497,29498,29499,29515,29516,29537,29553,39190,29401,29415,29432,29662,29673,29675,29678,29680,41498,39215}.All(LimitBreakVisuals.Contains),"every existing LB enum ID is covered, including pet effects");
        var start=new DateTime(2026,10,17,0,0,0,DateTimeKind.Utc);
        var whm=rules.Single(r=>r.ActionId==29230&&!r.Benefit);
        var e=new ReplaySkillEvent {Timestamp=start,Actor="Player World",ActionId=29230,X=10,Y=0,Z=20,Heading=0,Resolved=true};
        var geometry=new ReplaySceneGeometry();
        LimitBreakVisuals.Draw(geometry,whm,new(10,0,20),0,new(10,0,20),(_,_)=>0);
        var vertices=geometry.Vertices.ToArray();
        check(vertices.Min(v=>v.Position.X)==7&&vertices.Max(v=>v.Position.X)==13&&vertices.Min(v=>v.Position.Z)==20&&vertices.Max(v=>v.Position.Z)==60,"WHM beam is 40 long and client-parameter 6 wide, in facing direction");
        geometry.Clear();LimitBreakVisuals.Draw(geometry,whm,Vector3.Zero,MathF.PI/2,Vector3.Zero,(_,_)=>0);
        check(Math.Abs(geometry.Vertices.ToArray().Max(v=>v.Position.X)-40)<.001,"beam rotates with game heading convention");
        check(vertices.All(v=>(v.Color>>24)>0&&(v.Color>>24)<255),"LB fills carry real partial alpha for separate blend pass");
        check((ReplaySceneGeometry.Rgba(1,1,1)>>24)==255,"opaque map and pawn colors retain full alpha");
        check(LimitBreakVisuals.Opacity(whm,e,start.AddSeconds(.66))==0,"instant effects vanish after short impact fade");
        var persistent=rules.Single(r=>r.ActionId==29401);
        check(LimitBreakVisuals.Opacity(persistent,e,start.AddSeconds(2))==.18f,"persistent effects settle to faint background");
        check(LimitBreakVisuals.Opacity(whm,e,start.AddSeconds(-1))==0,"future skills have no visual");
        var aim=rules.Single(r=>r.ActionId==29415);var cast=new ReplaySkillEvent {Timestamp=start,ActionId=29415,Actor=e.Actor};
        check(LimitBreakVisuals.Opacity(aim,cast,start.AddSeconds(1))>0&&LimitBreakVisuals.Opacity(aim,cast,start.AddSeconds(4))==0,"single-target aim tether is bounded before impact");
        check(!LimitBreakVisuals.Center(aim,cast,out _),"missing single-target positions never produce a fabricated aim");
        var ground=rules.First(r=>r.ActionId==29266);
        check(!LimitBreakVisuals.Center(ground,e,out _),"missing ground destinations are skipped instead of using caster or origin");
        e.TargetX=float.NaN;e.TargetZ=2;check(!LimitBreakVisuals.Center(aim,e,out _),"nonfinite target coordinates never reach GPU geometry");
        check(!LimitBreakVisuals.Active(new[]{e},null,start.AddSeconds(-1)).Any(),"future packets excluded from replay");
        var frame=new MovementFrame {Timestamp=start.AddSeconds(3),Players=new() {new() {Player=e.Actor,StatusTimestamp=start.AddSeconds(3),Statuses=new(){new(){Id=3144,Remaining=20}}}}};
        var buff=new ReplaySkillEvent {Timestamp=start,ActionId=29401,Actor=e.Actor,Resolved=true};
        check(LimitBreakVisuals.Active(new[]{buff},frame,start.AddSeconds(3)).Any(),"persistent aura requires fresh observed status");
        frame.Players[0].Statuses.Clear();check(!LimitBreakVisuals.Active(new[]{buff},frame,start.AddSeconds(3)).Any(),"removed buff stops persistent aura without assuming full duration");
        var original=new ReplaySkillEvent {Timestamp=start,ActionId=29266,Actor=e.Actor,Resolved=true,GroundX=1,GroundZ=2};
        var relocated=new ReplaySkillEvent {Timestamp=start.AddSeconds(10),ActionId=29267,Actor=e.Actor,Resolved=true,GroundX=3,GroundZ=4};
        var active=LimitBreakVisuals.Active(new[]{original,relocated},null,start.AddSeconds(12)).ToArray();
        check(active.Length==2&&active.All(x=>ReferenceEquals(x.Event,relocated)),"Mesotes relocation removes old zone but retains both friendly and hostile roles");
        check(!LimitBreakVisuals.Active(new[]{original,relocated},null,start.AddSeconds(16)).Any(),"Mesotes relocation does not reset original expiry");
        var use=new ReplaySkillEvent {Timestamp=start,ActionId=29230,Actor=e.Actor};
        var hit=new ReplaySkillEvent {Timestamp=start.AddSeconds(.3),ActionId=29230,Actor=e.Actor,Resolved=true};
        check(LimitBreakVisuals.Active(new[]{use,hit},null,start.AddSeconds(.4)).Count(x=>!x.Rule.Benefit)==1,"use and impact packets do not duplicate instant damage area");
        var metadata=new ReplaySkillEvent {ClientCastType=4,ClientEffectRange=42,ClientWidth=7};
        check(LimitBreakVisuals.ClientDimensions(whm,metadata).Size==42&&LimitBreakVisuals.ClientDimensions(whm,metadata).Width==7,"new line skills use frozen client dimensions from that match");
        var aura=rules.Single(r=>r.ActionId==29230&&r.Benefit);
        check(LimitBreakVisuals.ClientDimensions(aura,metadata).Size==30,"line damage metadata never overwrites independent allied aura radius");
        check(LimitBreakVisuals.Opacity(ground,new ReplaySkillEvent {Timestamp=start},start)==0,"unresolved ground placement never creates an active field");
        var doc=BsonMapper.Global.ToDocument(original);var back=BsonMapper.Global.ToObject<ReplaySkillEvent>(doc);
        check(back.GroundX==1&&back.ActionId==29266&&back.Resolved,"new skill pose records round-trip BSON storage");
        var legacy=BsonMapper.Global.ToObject<DetailedTelemetry>(new BsonDocument());
        check(legacy.ReplaySkills.Count==0,"legacy telemetry without new skill poses remains readable");
        foreach(var rule in rules) {
            geometry.Clear();LimitBreakVisuals.Draw(geometry,rule,Vector3.Zero,1,new(3,0,4),(x,z)=>x*.01f+z*.02f);
            check(geometry.Vertices.Length<20000&&geometry.Vertices.ToArray().All(v=>LimitBreakVisuals.Finite(v.Position)),"bounded finite geometry for LB action "+rule.ActionId+" "+rule.Shape);
        }
    }
}
