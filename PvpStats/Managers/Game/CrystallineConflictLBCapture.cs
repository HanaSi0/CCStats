using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using PvpStats.Analysis;
namespace PvpStats.Managers.Game;
internal partial class CrystallineConflictMatchManager {
    private DateTime _lastSummonSample;
    private void RecordSummons(DateTime now) {
        var data=_currentMatchTimeline?.Detailed;if(data==null)return;
        if((now-_lastSummonSample).TotalSeconds<.2)return;
        _lastSummonSample=now;
        lock(_detailedLock) {
            if(data.SummonFrames.Count>=8192||data.EstimatedBytes>=DetailedTelemetry.ByteBudget) { data.SummonsTruncated=true;return; }
            var sample=new ReplaySummonFrame {Timestamp=now};
            foreach(var pet in _plugin.ObjectTable.OfType<IBattleNpc>().Where(p=>ReplaySummons.IsPetName(p.NameId)).Take(10)) {
                if(!LimitBreakVisuals.Finite(pet.Position))continue;
                if(_plugin.ObjectTable.SearchByEntityId(pet.OwnerId) is not IPlayerCharacter owner)continue;
                var alias=owner.Name+" "+owner.HomeWorld.Value.Name.ToString();
                if(DetailedTeam(alias) is not (1 or 2))continue;
                sample.Summons.Add(new() {EntityId=pet.EntityId,NameId=pet.NameId,Owner=alias,Phoenix=pet.NameId==8228,X=pet.Position.X,Y=pet.Position.Y,Z=pet.Position.Z});
            }
            if(sample.Summons.Count==0&&(data.SummonFrames.Count==0||data.SummonFrames[^1].Summons.Count==0))return;
            data.SummonFrames.Add(sample);data.EstimatedBytes+=80+sample.Summons.Count*160;
        }
    }
    private unsafe void RecordLBSkill(DateTime now,uint sourceId,string actor,Vector3* ground,ActionEffectHandler.Header* header,ActionEffectHandler.TargetEffects* effects,GameObjectId* trail) {
        var data=_currentMatchTimeline?.Detailed;
        if(data==null||_currentMatch?.IsCompleted!=false||_scoreboardPayloadReceived||header->ActionType!=1||!(LimitBreakVisuals.Contains(header->SpellId)||ReplaySummons.IsPetAction(header->SpellId)))return;
        var source=_plugin.ObjectTable.SearchByEntityId(sourceId) as IBattleChara;
        if(source==null||!LimitBreakVisuals.Finite(source.Position))return;
        var petAction=ReplaySummons.IsPetAction(header->SpellId);
        if(petAction&&source is not IBattleNpc)return;
        var entry=new ReplaySkillEvent { Timestamp=now,ActionId=header->SpellId,Sequence=header->GlobalSequence,Variation=header->AnimationVariation,Actor=actor,
            PetSource=petAction,SourceEntityId=sourceId,X=source.Position.X,Y=source.Position.Y,Z=source.Position.Z,Heading=source.Rotation,Resolved=header->AnimationVariation!=0 };
        // Ground coordinates are meaningful only for designated-location spells. PCT field is placed at the caster's feet.
        if(entry.ActionId==39215) { entry.GroundX=source.Position.X;entry.GroundY=source.Position.Y;entry.GroundZ=source.Position.Z; }
        else if(entry.ActionId is 29266 or 29267 or 29673 or 29678 && ground!=null&&LimitBreakVisuals.Finite(*ground)) {
            entry.GroundX=ground->X;entry.GroundY=ground->Y;entry.GroundZ=ground->Z;
        }
        IBattleChara? primary=null;
        var statusIds=new HashSet<uint>();
        if(trail!=null&&effects!=null)for(int i=0;i<Math.Min((int)header->NumTargets,32);i++) {
            var id=(uint)(trail[i]&uint.MaxValue);
            var target=_plugin.ObjectTable.SearchByEntityId(id) as IBattleChara;
            // Prefer the selected target only if actually present in this packet. Never use an unrelated current selection.
            if(target!=null&&(primary==null||id==(uint)source.TargetObjectId)&&id!=sourceId)primary=target;
            bool damage=false,benefit=false;
            for(int j=0;j<8;j++) {
                var effect=effects[i].Effects[j];
                if((effect.Type==15&&(effect.Param4&128)!=0)||(effect.Type is 14 or 15 && id==sourceId))statusIds.Add(effect.Value);
                if(effect.Type is 3 or 4)entry.Resolved=true;
                if(effect.Type is 3 or 5 or 6)damage=true;
                if(effect.Type==4||(effect.Type is 14 or 15 && entry.ActionId==29680))benefit=true;
            }
            if(petAction&&target is IPlayerCharacter hit&&LimitBreakVisuals.Finite(hit.Position)) {
                if(damage||benefit)entry.Resolved=true;
                var name=hit.Name+" "+hit.HomeWorld.Value.Name.ToString();
                if(damage)entry.SummonTargets.Add(new() {Player=name,X=hit.Position.X,Y=hit.Position.Y,Z=hit.Position.Z});
                if(benefit)entry.SummonTargets.Add(new() {Player=name,X=hit.Position.X,Y=hit.Position.Y,Z=hit.Position.Z,Benefit=true});
            }
        }
        if(primary!=null&&LimitBreakVisuals.Finite(primary.Position)) {
            entry.TargetX=primary.Position.X;entry.TargetY=primary.Position.Y;entry.TargetZ=primary.Position.Z;
            if(primary is IPlayerCharacter player)entry.TargetPlayer=player.Name+" "+player.HomeWorld.Value.Name.ToString();
        }
        entry.SelfStatusIds=statusIds.ToList();
        var row=_plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRow(entry.ActionId);
        entry.ClientCastType=row.CastType;entry.ClientEffectRange=row.EffectRange;entry.ClientWidth=row.XAxisModifier;
        lock(_detailedLock) {
            if(data.ReplaySkills.Count>=8192||data.EstimatedBytes>=DetailedTelemetry.ByteBudget) { data.ReplaySkillsTruncated=true;return; }
            data.ReplaySkills.Add(entry);
            data.EstimatedBytes+=512+(entry.Actor.Length+entry.TargetPlayer.Length)*4+entry.SelfStatusIds.Count*8+entry.SummonTargets.Count*160;
        }
    }
}
