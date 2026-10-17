using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LiteDB;
using System.Text.Json.Serialization;

namespace PvpStats.Analysis;

public sealed class DetailedTelemetry {
    public int Version { get; set; } = 2;
    public string GameVersion { get; set; } = "";
    public Dictionary<uint, string> StatusDescriptions { get; set; } = new();
    public Dictionary<uint, string> ActionDescriptions { get; set; } = new();
    public string MapTexture { get; set; } = "";
    public uint MapId { get; set; }
    public int MapSizeFactor { get; set; }
    public int MapOffsetX { get; set; }
    public int MapOffsetY { get; set; }
    public long EstimatedBytes { get; set; }
    public const long ByteBudget = 8 * 1024 * 1024;
    public bool FramesTruncated { get; set; }
    public bool EffectsTruncated { get; set; }
    public double SampleIntervalSeconds { get; set; } = .5;
    public long MovementBytesEstimate { get; set; }
    [BsonIgnore, JsonIgnore] public double FrameGapSeconds => Math.Max(.15,SampleIntervalSeconds*(Version>=3?3:2));
    private List<MovementFrame>? _frames=new();
    private bool _packedRead;
    public bool MovementDownsampled { get; set; }
    [BsonIgnore] public List<MovementFrame> Frames {
        get { if(!_packedRead && (_frames?.Count??0)==0 && PackedFrames.Length>0) { _frames=MovementCodec.Decode(PackedFrames); _packedRead=true; } return _frames??=new(); }
        set => _frames=value;
    }
    [BsonField("Frames"),JsonIgnore] public List<MovementFrame>? LegacyFrames {
        get => Version>=3?null:Frames;
        set { if(value!=null)_frames=value; }
    }
    [JsonIgnore] public byte[] PackedFrames { get; set; } = Array.Empty<byte>();
    public void PrepareForStorage() {
        if(Version<3 || _frames==null)return;
        PackedFrames=MovementCodec.Encode(_frames);
        while(PackedFrames.Length>4*1024*1024 && _frames.Count>2) {
            var last=_frames[^1]; _frames=_frames.Where((f,i)=>i%2==0).ToList();
            if(_frames[^1]!=last)_frames.Add(last);
            SampleIntervalSeconds*=2; MovementDownsampled=true;
            PackedFrames=MovementCodec.Encode(_frames);
        }
    }
    public Dictionary<uint, ReplayStatusInfo> ReplayStatuses { get; set; } = new();
    public List<ReplayMapEvent> MapMechanics { get; set; } = new();
    public bool MapMechanicsTruncated { get; set; }
    public List<ReplaySummonFrame> SummonFrames { get; set; } = new();
    public bool SummonsTruncated { get; set; }
    public List<ReplaySkillEvent> ReplaySkills { get; set; } = new();
    public bool ReplaySkillsTruncated { get; set; }
    public List<ObservedEffect> Effects { get; set; } = new();
}
public sealed class MovementFrame {
    public DateTime Timestamp { get; set; }
    public List<PlayerPosition> Players { get; set; } = new();
    // Director's relative progress value, not a world-space crystal coordinate.
    public int CrystalProgress { get; set; }
    public int AstraProgress { get; set; }
    public int UmbraProgress { get; set; }
    public CrystalPosition? Crystal { get; set; }
}
public sealed class CrystalPosition {
    public float X { get; set; }
    public float? Y { get; set; }
    public float Z { get; set; }
}
public sealed class PlayerPosition {
    public string Player { get; set; } = "";
    public int Team { get; set; }
    public float X { get; set; }
    public float? Y { get; set; }
    public float Z { get; set; }
    public float Heading { get; set; }
    public uint HP { get; set; }
    public uint MaxHP { get; set; }
    public List<ObservedControl> Controls { get; set; } = new();
    public uint ClassJobId { get; set; }
    public DateTime StatusTimestamp { get; set; }
    public List<ReplayBuff> Statuses { get; set; } = new();
}
public sealed class ReplayBuff {
    public uint Id { get; set; }
    public ushort Param { get; set; }
    public float Remaining { get; set; }
}
public sealed class ReplayStatusInfo {
    public string Name { get; set; } = "";
    public uint Icon { get; set; }
}
public sealed class ReplayMapEvent {
    public DateTime Timestamp { get; set; }
    public string Label { get; set; } = "";
    public uint ActionId { get; set; }
    public uint SourceNameId { get; set; }
    public uint SourceEntityId { get; set; }
    public uint Sequence { get; set; }
    public bool MapWide { get; set; }
    public ReplayMapEventKind Kind { get; set; }
    public float? X { get; set; }
    public float? Y { get; set; }
    public float? Z { get; set; }
    public float? Heading { get; set; }
    public float? TargetX { get; set; }
    public float? TargetY { get; set; }
    public float? TargetZ { get; set; }
    public DateTime? ObservedUntil { get; set; }
    public DateTime? ExpectedEndTime { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public byte ClientCastType { get; set; }
    public int ClientEffectRange { get; set; }
    public int ClientXAxisModifier { get; set; }
}
public enum ReplayMapEventKind { ActionResolved, CastObserved, TimerWarning, TimerTriggered }
public sealed class ObservedControl {
    public uint StatusId { get; set; }
    public string Source { get; set; } = "";
    public float Remaining { get; set; }
}
public sealed class ObservedEffect {
    public DateTime Timestamp { get; set; }
    public uint Sequence { get; set; }
    public uint ActionId { get; set; }
    public string Actor { get; set; } = "";
    public string Target { get; set; } = "";
    public int ActorTeam { get; set; }
    public int TargetTeam { get; set; }
    public byte Type { get; set; }
    public uint Amount { get; set; }
    public uint StatusId { get; set; }
    public bool GuardObserved { get; set; }
    public bool SelfReturn { get; set; }
    public bool SnapshotsCaptured { get; set; }
    public bool PetSource { get; set; }
    public bool IsWeaponskill { get; set; }
    public uint ShieldPercent { get; set; }
    public List<EffectStatus> ActorStatuses { get; set; } = new();
    public List<EffectStatus> TargetStatuses { get; set; } = new();
}
public sealed class EffectStatus {
    public uint Id { get; set; }
    public uint Param { get; set; }
    public float Remaining { get; set; }
}
public sealed class PlayerDetailedStats {
    public string Player { get; set; } = "";
    public long GuardedDamage { get; set; }
    public int GuardedHits { get; set; }
    public int BlockedOrParriedHits { get; set; }
    public int ControlHits { get; set; }
    public int ControlResists { get; set; }
    public long SelfReturnHealing { get; set; }
    public long IdentifiedDrainHealing { get; set; }
    public double ObservedControlSeconds { get; set; }
}
public static class DetailedAnalysis {
    public static readonly IReadOnlyDictionary<uint,string> Controls = new Dictionary<uint,string> {
        [1343]="眩晕",[1344]="加重",[1345]="止步",[1347]="沉默",[1348]="睡眠",[3085]="自然奇迹",[3518]="缩小",
    };
    public static bool IsDamage(byte type) => type is 3 or 5 or 6;
    public static bool IsEnemy(ObservedEffect effect) => effect.ActorTeam is 1 or 2 && effect.TargetTeam is 1 or 2 && effect.ActorTeam != effect.TargetTeam;
    public static Vector2 WorldToUv(float x, float z, int factor, int offsetX, int offsetY) {
        var scale = factor / 100f;
        return new(((x + offsetX) * scale + 1024) / 2048, ((z + offsetY) * scale + 1024) / 2048);
    }
    public static void TrimToMatch(DetailedTelemetry data, DateTime start, DateTime end) {
        data.Frames.RemoveAll(frame=>frame.Timestamp<start || frame.Timestamp>end);
        data.Effects.RemoveAll(effect=>effect.Timestamp<start || effect.Timestamp>end);
        data.SummonFrames.RemoveAll(frame=>frame.Timestamp<start || frame.Timestamp>end);
        data.ReplaySkills.RemoveAll(skill=>skill.Timestamp<start || skill.Timestamp>end);
    }
    public static MovementFrame? FrameAt(DetailedTelemetry data, DateTime time) {
        // Do not invent positions in unobserved gaps or before the first sample.
        var frames = data.Frames;
        int low = 0, high = frames.Count - 1, found = -1;
        while(low <= high) { int mid = (low+high)/2; if(frames[mid].Timestamp <= time) { found=mid; low=mid+1; } else high=mid-1; }
        return found >= 0 && time - frames[found].Timestamp <= TimeSpan.FromSeconds(data.FrameGapSeconds) ? frames[found] : null;
    }
    public static Dictionary<string,PlayerDetailedStats> Summarize(DetailedTelemetry data) {
        var result = new Dictionary<string,PlayerDetailedStats>();
        PlayerDetailedStats Stat(string player) {
            if(!result.TryGetValue(player,out var stats)) result[player] = stats = new() { Player = player };
            return stats;
        }
        foreach(var effect in data.Effects) {
            if(IsDamage(effect.Type) && effect.Target.Length > 0) {
                var target = Stat(effect.Target);
                if(effect.GuardObserved) { target.GuardedDamage += effect.Amount; target.GuardedHits++; }
                if(effect.Type is 5 or 6) target.BlockedOrParriedHits++;
            }
            if(effect.Actor.Length == 0) continue;
            var actor = Stat(effect.Actor);
            if(IsEnemy(effect) && Controls.ContainsKey(effect.StatusId)) {
                if(effect.Type == 14) actor.ControlHits++;
                if(effect.Type is 20 or 55) actor.ControlResists++;
            }
            if(effect.Type == 4 && effect.SelfReturn && effect.Actor == effect.Target) {
                actor.SelfReturnHealing += effect.Amount;
                // Explicit, limited catalogue: observed self-return heal on these drain actions.
                if(effect.ActionId is 39166 or 39167) actor.IdentifiedDrainHealing += effect.Amount;
            }
        }
        for(int i=0;i<data.Frames.Count;i++) {
            var frame = data.Frames[i];
            foreach(var p in frame.Players) Stat(p.Player);
            if(i+1 == data.Frames.Count) continue;
            var next = data.Frames[i+1];
            var seconds = (next.Timestamp-frame.Timestamp).TotalSeconds;
            if(seconds <= 0 || seconds > data.FrameGapSeconds) continue;
            foreach(var player in frame.Players) {
                var after = next.Players.FirstOrDefault(p=>p.Player==player.Player);
                if(after == null || player.HP == 0 || after.HP == 0) continue;
                foreach(var status in player.Controls) {
                    if(!Controls.ContainsKey(status.StatusId)) continue;
                    var source = frame.Players.FirstOrDefault(p=>p.Player==status.Source);
                    if(source == null || source.Team is not (1 or 2) || player.Team is not (1 or 2) || source.Team == player.Team) continue;
                    var continued = after.Controls.Any(s=>s.StatusId==status.StatusId && s.Source==status.Source && s.Remaining <= status.Remaining+.05f);
                    if(continued && status.Remaining > 0) Stat(status.Source).ObservedControlSeconds += Math.Min(seconds,status.Remaining);
                }
            }
        }
        return result;
    }
}
