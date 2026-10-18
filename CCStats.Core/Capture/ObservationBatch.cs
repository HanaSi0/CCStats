using PvpStats.Analysis;
namespace CCStats.Core.Capture;

/// <summary>Supplemental data read in the same sampling cycle. Never implies packet completeness.</summary>
public sealed class ObservationBatch {
    public string GameVersion {get;set;}="";
    public string LocalPlayer {get;set;}="";
    public PlayerPvpProfile? Profile {get;set;}
    public bool UnconfirmedRosterLimited {get;set;}
    public bool DirectorAvailable {get;set;}
    public bool EffectsPolled {get;set;}
    public List<ObservedEffect> Effects {get;}=new();
    public List<ReplaySkillEvent> Skills {get;}=new();
    public List<ReplayMapEvent> MapEvents {get;}=new();
    public List<ReplayArenaObject> ArenaObjects {get;}=new();
    public ReplaySummonFrame? Summons {get;set;}
    public List<SettlementUiSnapshot> SettlementPanels {get;}=new();
    public List<ReplayArenaState> MapStates {get;}=new();
}
public sealed record ObservedActor(uint EntityId,uint OwnerId,uint NameId,byte Kind,float X,float Y,float Z,float Heading,PlayerPosition? Player=null);
public sealed record DirectorPlayer(uint EntityId,int Team,uint Job,ushort World,string Name);
public sealed record DirectorSnapshot(int CrystalProgress,int AstraProgress,int UmbraProgress,int AstraOnPoint,int UmbraOnPoint,int AstraCheckpoint,int UmbraCheckpoint);
