using PvpStats.Analysis;
namespace CCStats.Core.Capture;

// Being present in an arena is not evidence that a match has started.
public sealed class MatchActivityGate {
    private uint _territory;
    private bool _active,_finished;
    public bool Update(uint territory,MovementFrame frame,ObservationBatch? batch) {
        if(territory!=0&&territory!=_territory){_territory=territory;_active=false;_finished=false;}
        if(batch?.SettlementPanels.Count>0&&_active){_active=false;_finished=true;}
        if(_finished||ReplayMapMesh.MapKey(territory)==null)return false;
        bool evidence=batch?.CombatActive==true||batch?.DirectorAvailable==true&&(frame.AstraProgress>0||frame.UmbraProgress>0||frame.CrystalProgress!=0)
            ||batch?.Effects.Any(e=>e.Actor.Length>0&&e.Target.Length>0&&e.Actor!=e.Target&&DetailedAnalysis.IsDamage(e.Type))==true;
        if(frame.Players.Count>0&&evidence)_active=true;
        return _active;
    }
}
