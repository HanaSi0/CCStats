using PvpStats.Analysis;

namespace CCStats.Core.Library;

public sealed class MatchRecord {
    public string Id {get;set;} = "";
    public DateTime StartUtc {get;set;}
    public DateTime? EndUtc {get;set;}
    public uint Territory {get;set;}
    public string LocalPlayer {get;set;} = "";
    public string Mode {get;set;} = "未知";
    public int Winner {get;set;}
    public string Result {get;set;} = "未记录";
    public bool Completed {get;set;}=true;
    public bool Quarantined {get;set;}
    public bool ScoreboardComplete {get;set;}
    public bool Observation {get;set;}
    public bool Favorite {get;set;}
    public string Tags {get;set;} = "";
    public int? Credit {get;set;}
    public int? RankPoints {get;set;}
    public int? RankValue=>RankPoints??Credit;
    public string Rank {get;set;} = "";
    public string SourcePath {get;set;} = "";
    public List<PlayerScore> Players {get;set;} = new();
    public int FrameCount {get;set;}
    public string Kind=>Observation?"回放记录":"比赛记录";
    public string Map=>Import.ReplayFileReader.MapName(Territory);
    public string StartLabel=>StartUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public double DurationSeconds=>EndUtc.HasValue?Math.Max(0,(EndUtc.Value-StartUtc).TotalSeconds):0;
    public string Duration=>$"{(int)DurationSeconds/60}:{(int)DurationSeconds%60:00}";
}
public sealed class PlayerScore {
    public string Name {get;set;}="";
    public string Job {get;set;}="未知";
    public int Team {get;set;}
    public int? Kills {get;set;}
    public int? Deaths {get;set;}
    public int? Assists {get;set;}
    public long? Damage {get;set;}
    public long? Taken {get;set;}
    public long? Healing {get;set;}
    public double? CrystalSeconds {get;set;}
}
public sealed class ReplayBundle {
    public MatchRecord Match {get;set;}=new();
    public Capture.PlayerPvpProfile? ClientProfile {get;set;}
    public DetailedTelemetry Data {get;set;}=new();
    public List<ActionSummary> Actions {get;set;}=new();
    public List<ProgressSample> Progress {get;set;}=new();
    public List<TimelineRow> Timeline {get;set;}=new();
}
public sealed record ActionSummary(string Player,uint ActionId,string Kind,int Casts,int Targets,long Damage,long Healing,int StatusHits,int StatusMisses);
public sealed record ProgressSample(DateTime Timestamp,string Series,int Points);
public sealed record TimelineRow(DateTime Timestamp,string Kind,string Actor,string Target,string Description);
public sealed record AggregateRow(string Name,int Appearances,int Wins,int Losses,string WinRate,string Damage,string Taken,string Healing,string Crystal,string Kills="—",string Deaths="—",string Assists="—");
public static class JobNames {
    private static readonly string[] Names={"骑士","战士","暗黑骑士","绝枪战士","白魔法师","学者","占星术士","贤者","武僧","龙骑士","忍者","武士","钐镰客","蝰蛇剑士","吟游诗人","机工士","舞者","黑魔法师","召唤师","赤魔法师","绘灵法师"};
    private static readonly string[] Keys={"PLD","WAR","DRK","GNB","WHM","SCH","AST","SGE","MNK","DRG","NIN","SAM","RPR","VPR","BRD","MCH","DNC","BLM","SMN","RDM","PCT"};
    private static readonly uint[] Ids={19,21,32,37,24,28,33,40,20,22,30,34,39,41,23,31,38,25,27,35,42};
    public static string Name(string value)=>string.IsNullOrWhiteSpace(value)?"未知":int.TryParse(value,out var n)&&n>=0&&n<Names.Length?Names[n]:System.Array.IndexOf(Keys,value) is var i&&i>=0?Names[i]:value;
    public static string Name(uint id)=>System.Array.IndexOf(Ids,id) is var i&&i>=0?Names[i]:"未知";
    public static string Initial(uint id)=>id switch {19=>"骑",21=>"战",32=>"暗",37=>"绝",24=>"白",28=>"学",33=>"占",40=>"贤",20=>"僧",22=>"龙",30=>"忍",34=>"侍",39=>"镰",41=>"蛇",23=>"诗",31=>"机",38=>"舞",25=>"黑",27=>"召",35=>"赤",42=>"绘",_=>"?"};
}
