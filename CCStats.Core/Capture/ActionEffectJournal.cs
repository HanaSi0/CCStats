using System.Buffers.Binary;
using PvpStats.Analysis;
namespace CCStats.Core.Capture;

/// <summary>Reads retained effect slots. A poll can miss overwritten slots and cannot recover exact impact time.</summary>
public sealed class ActionEffectJournal {
    private readonly HashSet<(uint Seq,uint Source,uint Target,uint Action,byte TargetIndex,byte Index,byte Type)> _seen=new();
    private readonly Queue<(uint Seq,uint Source,uint Target,uint Action,byte TargetIndex,byte Index,byte Type)> _order=new();
    private readonly HashSet<(uint Seq,uint Source,uint Action,bool Resolved)> _actions=new();
    private readonly Queue<(uint Seq,uint Source,uint Action,bool Resolved)> _actionOrder=new();
    private readonly HashSet<uint> _primed=new();
    public void Reset() {_seen.Clear();_order.Clear();_actions.Clear();_actionOrder.Clear();_primed.Clear();}
    public void Observe(uint holder,ReadOnlySpan<byte> bytes,IReadOnlyDictionary<uint,ObservedActor> actors,DateTime now,ObservationBatch batch) {
        if(bytes.Length<ClientLayout.EffectBytes)return;
        bool first=_primed.Add(holder);batch.EffectsPolled=true;
        for(int slot=0;slot<32;slot++) {
            var entry=bytes.Slice(slot*0x78,0x78);
            uint seq=U(entry,0),action=U(entry,0xC),source=U(entry,0x18),target=U(entry,0x28);
            if(seq==0||entry[9]!=1||entry[8]>=32||action is 0 or >1000000||U(entry,0x1C)!=0||U(entry,0x2C)!=0||source is 0 or 0xE0000000||target is 0 or 0xE0000000)continue;
            actors.TryGetValue(source,out var actor);actors.TryGetValue(target,out var recipient);
            var owner=actor!=null&&actor.OwnerId is not (0 or 0xE0000000)&&actors.TryGetValue(actor.OwnerId,out var own)?own:null;
            var credited=owner?.Player??actor?.Player;bool pet=owner?.Player!=null;
            var selfStatuses=new HashSet<uint>();bool damage=false,benefit=false,meaningful=false,targetNew=false;
            for(byte i=0;i<8;i++) {
                var effect=entry.Slice(0x38+i*8,8);byte type=effect[0];
                if(type is not (3 or 4 or 5 or 6 or 14 or 15 or 20 or 55))continue;
                meaningful=true;damage|=type is 3 or 5 or 6;benefit|=type is 4 or 14 or 15;
                bool returned=(effect[5]&0x80)!=0;
                uint value=BinaryPrimitives.ReadUInt16LittleEndian(effect[6..]);uint amount=value+((effect[5]&0x40)!=0?(uint)effect[4]<<16:0);
                if(type is 14 or 15&&(returned||source==target))selfStatuses.Add(value);
                var key=(seq,source,target,action,entry[8],i,type);if(!_seen.Add(key))continue;
                _order.Enqueue(key);while(_order.Count>65536)_seen.Remove(_order.Dequeue());
                if(first||actor==null||recipient==null)continue;
                var destination=returned?actor.Player:recipient.Player;if(destination==null)continue;
                batch.Effects.Add(new() {Timestamp=now,Sequence=seq,ActionId=action,Actor=credited?.Player??$"场地实体 {source}",Target=destination.Player,ActorTeam=credited?.Team??0,TargetTeam=destination.Team,Type=type,Amount=amount,StatusId=type is 14 or 15 or 20 or 55?value:0,PetSource=pet,SelfReturn=returned,
                    // Post-impact statuses are not event-time snapshots.
                    SnapshotsCaptured=false,GuardObserved=false});targetNew=true;
            }
            // Empty use slots must not consume a later impact with the same sequence.
            var phase=(seq,source,action,meaningful);bool newPhase=_actions.Add(phase);
            if(newPhase) {_actionOrder.Enqueue(phase);while(_actionOrder.Count>16384)_actions.Remove(_actionOrder.Dequeue());}
            if(first||actor==null||recipient==null)continue;
            if((LimitBreakVisuals.Contains(action)||ReplaySummons.IsPetAction(action))&&credited!=null) {
                // Emit each target's newly seen effect set so the writer can merge summon hit links.
                if(newPhase||targetNew) {
                    var skill=new ReplaySkillEvent {Timestamp=now,ActionId=action,Sequence=seq,Actor=credited.Player,SourceEntityId=source,SelfStatusIds=selfStatuses.ToList(),PetSource=pet,Resolved=meaningful,LegacyEstimate=true,
                        X=actor.X,Y=actor.Y,Z=actor.Z,Heading=actor.Heading,TargetPlayer=recipient.Player?.Player??"",TargetX=recipient.X,TargetY=recipient.Y,TargetZ=recipient.Z};
                    if(action==39215) {skill.GroundX=actor.X;skill.GroundY=actor.Y;skill.GroundZ=actor.Z;}
                    if(pet&&recipient.Player!=null) {
                        if(damage)skill.SummonTargets.Add(new(){Player=recipient.Player.Player,X=recipient.X,Y=recipient.Y,Z=recipient.Z});
                        if(benefit)skill.SummonTargets.Add(new(){Player=recipient.Player.Player,X=recipient.X,Y=recipient.Y,Z=recipient.Z,Benefit=true});
                    }
                    batch.Skills.Add(skill);
                }
            }
            if(newPhase&&meaningful&&actor.Kind==2&&(actor.OwnerId is 0 or 0xE0000000)&&MapMechanicVisuals.Find(action)!=null)
                batch.MapEvents.Add(new(){Timestamp=now,Kind=ReplayMapEventKind.ActionResolved,ActionId=action,Sequence=seq,SourceEntityId=source,SourceNameId=actor.NameId,X=actor.X,Y=actor.Y,Z=actor.Z,Heading=actor.Heading,Label=MapMechanicVisuals.Find(action)!.Name});
        }
    }
    private static uint U(ReadOnlySpan<byte> row,int offset)=>BinaryPrimitives.ReadUInt32LittleEndian(row[offset..]);
}
