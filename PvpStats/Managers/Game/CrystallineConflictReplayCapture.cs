using System;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.SubKinds;
using PvpStats.Analysis;
namespace PvpStats.Managers.Game;
internal partial class CrystallineConflictMatchManager {
    private void CaptureEnvironmentAction(DateTime now,IBattleNpc source,uint action,uint sequence) {
        if(source.NameId==11350 || _plugin.ObjectTable.SearchByEntityId(source.OwnerId) is IPlayerCharacter)return;
        var data=_currentMatchTimeline?.Detailed;
        if(data==null||_scoreboardPayloadReceived||_currentMatch?.IsCompleted!=false)return;
        // Do not infer an AoE or classify unknown NPCs as confirmed map mechanics.
        var last=data.MapMechanics.LastOrDefault(e=>e.ActionId==action&&e.SourceEntityId==source.EntityId&&e.Sequence==sequence);
        if(last!=null&&(now-last.Timestamp).TotalSeconds<1)return;
        var row=_plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRow(action);
        RecordReplayMapEvent(new() { Timestamp=now,ActionId=action,Sequence=sequence,SourceNameId=source.NameId,SourceEntityId=source.EntityId,
            Label=source.Name+" · "+PvpStats.Localization.ChineseText.ActionName(action,row.Name.ToString()),
            X=float.IsFinite(source.Position.X)?source.Position.X:null,Z=float.IsFinite(source.Position.Z)?source.Position.Z:null });
    }
    private void RecordReplayMapEvent(ReplayMapEvent entry) {
        var data=_currentMatchTimeline?.Detailed;
        if(data==null||_scoreboardPayloadReceived)return;
        lock(_detailedLock) {
            if(data.MapMechanics.Count>=1024) { data.MapMechanicsTruncated=true;return; }
            data.MapMechanics.Add(entry);
        }
    }
}
