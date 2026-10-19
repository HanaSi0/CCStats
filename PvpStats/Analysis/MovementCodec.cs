using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Text;
namespace PvpStats.Analysis;
public static class MovementCodec {
    private static DateTime Utc(DateTime time)=>time.Kind==DateTimeKind.Local?time.ToUniversalTime():DateTime.SpecifyKind(time,DateTimeKind.Utc);
    public const int MaxFrames=36000;
    public const int RawBudget=24*1024*1024;
    public static byte[] Encode(IReadOnlyList<MovementFrame> frames) {
        if(frames.Count>MaxFrames)throw new InvalidDataException("Too many movement frames.");
        using var raw=new MemoryStream();
        using(var w=new BinaryWriter(raw,Encoding.UTF8,true)) {
            w.Write(0x43434D33); w.Write(frames.Count);
            var names=frames.SelectMany(f=>f.Players).SelectMany(p=>new[]{p.Player}.Concat(p.Controls.Select(s=>s.Source))).Where(s=>s.Length>0).Distinct().ToArray();
            if(names.Length>512)throw new InvalidDataException("Too many identities.");
            var indices=names.Select((s,i)=>(s,i)).ToDictionary(p=>p.s,p=>(ushort)p.i);
            w.Write((ushort)names.Length); foreach(var name in names) { if(name.Length>256)throw new InvalidDataException("Identity too long."); w.Write(name); }
            var origin=Utc(frames.FirstOrDefault()?.Timestamp??DateTime.UnixEpoch); w.Write(origin.ToBinary());
            var snapshots=new Dictionary<string,PlayerPosition>();
            foreach(var f in frames) {
                if(f.Players.Count>10)throw new InvalidDataException("Invalid player count.");
                w.Write(Utc(f.Timestamp).Ticks-origin.Ticks); w.Write(f.CrystalProgress); w.Write(f.AstraProgress); w.Write(f.UmbraProgress);
                w.Write(f.Crystal!=null); if(f.Crystal is {} crystal) { w.Write(crystal.X); w.Write(crystal.Z); w.Write(crystal.Y.HasValue); if(crystal.Y.HasValue)w.Write(crystal.Y.Value); }
                w.Write((byte)f.Players.Count);
                foreach(var p in f.Players) {
                    if(p.Controls.Count>32)throw new InvalidDataException("Too many control statuses.");
                    w.Write(indices[p.Player]); w.Write((byte)p.Team); w.Write(p.X); w.Write(p.Z); w.Write(p.Heading); w.Write(p.HP); w.Write(p.MaxHP); w.Write((byte)p.Controls.Count);
                    foreach(var s in p.Controls) { w.Write(s.StatusId); w.Write(indices.GetValueOrDefault(s.Source,ushort.MaxValue)); w.Write(s.Remaining); }
                    w.Write(p.Y.HasValue);if(p.Y.HasValue)w.Write(p.Y.Value);
                    w.Write(p.ClassJobId);
                    var changed=!snapshots.TryGetValue(p.Player,out var prior) || prior.StatusTimestamp!=p.StatusTimestamp || (p.Statuses.Count>0&&!ReferenceEquals(prior.Statuses,p.Statuses)) || prior.Statuses.Count!=p.Statuses.Count;
                    w.Write(changed);
                    if(changed) {
                        if(p.Statuses.Count>60)throw new InvalidDataException("Too many replay statuses.");
                        w.Write(Utc(p.StatusTimestamp).ToBinary()); w.Write((byte)p.Statuses.Count);
                        foreach(var status in p.Statuses) { w.Write(status.Id); w.Write(status.Param); w.Write(status.Remaining); }
                        snapshots[p.Player]=p;
                    }
                }
            }
        }
        if(raw.Length>RawBudget)throw new InvalidDataException("Movement payload is too large.");
        using var output=new MemoryStream(); using(var zip=new GZipStream(output,CompressionLevel.Optimal,true)) raw.WriteTo(zip);
        return output.ToArray();
    }
    public static List<MovementFrame> Decode(byte[] packed) {
        using var input=new MemoryStream(packed); using var zip=new GZipStream(input,CompressionMode.Decompress);
        using var raw=new MemoryStream(); var buffer=new byte[8192]; int read;
        while((read=zip.Read(buffer))>0) { if(raw.Length+read>RawBudget)throw new InvalidDataException("Movement payload is too large."); raw.Write(buffer,0,read); }
        raw.Position=0; using var r=new BinaryReader(raw,Encoding.UTF8);
        var magic=r.ReadInt32();
        if(magic!=0x43434D31&&magic!=0x43434D32&&magic!=0x43434D33)throw new InvalidDataException("Unsupported movement payload.");
        var extended=magic>=0x43434D32;
        var height=magic==0x43434D33;
        var snapshots=new Dictionary<string,PlayerPosition>();
        var count=r.ReadInt32(); if(count<0||count>MaxFrames)throw new InvalidDataException("Invalid frame count.");
        var namesCount=r.ReadUInt16(); if(namesCount>512)throw new InvalidDataException("Invalid roster count.");
        var names=new string[namesCount]; for(int i=0;i<names.Length;i++) { names[i]=r.ReadString(); if(names[i].Length>256)throw new InvalidDataException("Identity too long."); }
        string Name(ushort index)=>index<names.Length?names[index]:index==ushort.MaxValue?"":throw new InvalidDataException("Invalid roster index.");
        var origin=Utc(DateTime.FromBinary(r.ReadInt64())); var frames=new List<MovementFrame>(count);
        for(int i=0;i<count;i++) {
            var f=new MovementFrame { Timestamp=origin.AddTicks(r.ReadInt64()),CrystalProgress=r.ReadInt32(),AstraProgress=r.ReadInt32(),UmbraProgress=r.ReadInt32() };
            if(r.ReadBoolean()) { f.Crystal=new() { X=r.ReadSingle(),Z=r.ReadSingle() };if(height&&r.ReadBoolean())f.Crystal.Y=r.ReadSingle(); }
            var players=r.ReadByte(); if(players>10)throw new InvalidDataException("Invalid player count.");
            for(int j=0;j<players;j++) {
                var p=new PlayerPosition { Player=Name(r.ReadUInt16()),Team=r.ReadByte(),X=r.ReadSingle(),Z=r.ReadSingle(),Heading=r.ReadSingle(),HP=r.ReadUInt32(),MaxHP=r.ReadUInt32() };
                var controls=r.ReadByte(); if(controls>32)throw new InvalidDataException("Invalid status count.");
                for(int k=0;k<controls;k++)p.Controls.Add(new() { StatusId=r.ReadUInt32(),Source=Name(r.ReadUInt16()),Remaining=r.ReadSingle() });
                if(extended) {
                    if(height&&r.ReadBoolean())p.Y=r.ReadSingle();
                    p.ClassJobId=r.ReadUInt32();
                    if(r.ReadBoolean()) {
                        p.StatusTimestamp=Utc(DateTime.FromBinary(r.ReadInt64())); var statuses=r.ReadByte();
                        if(statuses>60)throw new InvalidDataException("Invalid replay status count.");
                        for(int k=0;k<statuses;k++)p.Statuses.Add(new() { Id=r.ReadUInt32(),Param=r.ReadUInt16(),Remaining=r.ReadSingle() });
                        snapshots[p.Player]=p;
                    } else if(snapshots.TryGetValue(p.Player,out var previous)) {
                        p.StatusTimestamp=previous.StatusTimestamp; p.Statuses=previous.Statuses;
                    } else throw new InvalidDataException("Missing replay status snapshot.");
                }
                f.Players.Add(p);
            }
            if(frames.Count>0 && f.Timestamp<frames[^1].Timestamp)throw new InvalidDataException("Unordered movement samples.");
            frames.Add(f);
        }
        if(raw.Position!=raw.Length)throw new InvalidDataException("Unexpected movement data.");
        return frames;
    }
}
