using System.IO.Compression;
using System.Text;
using LiteDB;
using PvpStats.Analysis;
internal static class ReplayUpgradeChecks {
    internal static void Run(Action<bool,string> check) {
        var start=new DateTime(2026,10,17,0,0,0,DateTimeKind.Utc);
        var buffs=new List<ReplayBuff> { new() { Id=3054,Param=2,Remaining=5 } };
        var frames=new List<MovementFrame> {
            new() { Timestamp=start,Crystal=new() { X=0,Z=0 },Players=new() { new() { Player="Player World",ClassJobId=19,Team=1,HP=60000,MaxHP=60000,StatusTimestamp=start,Statuses=buffs } } },
            new() { Timestamp=start.AddMilliseconds(33),Crystal=new() { X=.1f,Z=.1f },Players=new() { new() { Player="Player World",ClassJobId=19,Team=1,HP=42000,MaxHP=60000,StatusTimestamp=start,Statuses=buffs } } },
            new() { Timestamp=start.AddMilliseconds(66),Players=new() { new() { Player="Player World",ClassJobId=19,Team=1,HP=42000,MaxHP=60000,StatusTimestamp=start.AddMilliseconds(66),Statuses=new() } } },
        };
        var stepping=new DetailedTelemetry { Frames=frames };
        check(ReplayObservation.Step(stepping,start,1)==frames[1].Timestamp&&ReplayObservation.Step(stepping,frames[1].Timestamp,-1)==start,"frame stepping selects actual adjacent observations");
        check(ReplayObservation.Step(stepping,start,-1)==null&&ReplayObservation.Step(stepping,frames[^1].Timestamp,1)==null,"frame stepping never invents samples beyond recording boundaries");
        var loaded=MovementCodec.Decode(MovementCodec.Encode(frames));
        check(loaded[1].Players[0].HP==42000&&loaded[1].Players[0].ClassJobId==19,"replay stores observed HP and job with each exact frame");
        check(loaded[1].Players[0].Statuses[0].Id==3054&&loaded[1].Players[0].Statuses[0].Param==2,"delta status snapshots retain IDs and parameters");
        check(ReferenceEquals(loaded[0].Players[0].Statuses,loaded[1].Players[0].Statuses)&&loaded[2].Players[0].Statuses.Count==0,"shared buff snapshots decode and removal clears the complete state");
        check(Math.Abs(ReplayObservation.Remaining(buffs[0],start,start.AddSeconds(2))-3)<.001,"buff countdown follows playback time rather than wall clock");
        check(ReplayObservation.Remaining(buffs[0],start,start.AddSeconds(10))==0,"expired statuses have no negative countdown");
        var refreshed=new PlayerPosition { StatusTimestamp=start.AddMilliseconds(33),Statuses=new() { new() { Id=3054,Param=2,Remaining=4.967f } } };
        check(ReplayObservation.ReuseStatuses(frames[0].Players[0],refreshed,start.AddMilliseconds(33)),"unchanged status countdown reuses its snapshot without loss of state");
        refreshed.Statuses[0].Remaining=5;
        check(!ReplayObservation.ReuseStatuses(frames[0].Players[0],refreshed,start.AddSeconds(1)),"refreshed buff duration forces a new snapshot");
        refreshed.Statuses[0].Remaining=4.967f; refreshed.Statuses[0].Param=3;
        check(!ReplayObservation.ReuseStatuses(frames[0].Players[0],refreshed,start.AddMilliseconds(33)),"status parameter changes force a new snapshot");
        check(ReplayObservation.CrystalSegment(frames[0],frames[1],.15),"crystal path connects adjacent observed positions");
        check(!ReplayObservation.CrystalSegment(frames[1],frames[2],.15),"crystal path stops when an observation is missing");
        var gap=new MovementFrame { Timestamp=start.AddSeconds(2),Crystal=new() { X=1,Z=1 } };
        check(!ReplayObservation.CrystalSegment(frames[1],gap,.15),"crystal path never bridges long observation gaps");
        gap.Timestamp=start.AddMilliseconds(66);gap.Crystal.X=100;
        check(!ReplayObservation.CrystalSegment(frames[1],gap,.15),"crystal discontinuities do not draw a false cross-map path");
        // A hand-written v1 payload proves compatibility with already-published recordings.
        using var raw=new MemoryStream();
        using(var w=new BinaryWriter(raw,Encoding.UTF8,true)) {
            w.Write(0x43434D31);w.Write(1);w.Write((ushort)1);w.Write("Legacy World");w.Write(start.ToBinary());
            w.Write(0L);w.Write(0);w.Write(0);w.Write(0);w.Write(false);w.Write((byte)1);
            w.Write((ushort)0);w.Write((byte)1);w.Write(1f);w.Write(2f);w.Write(0f);w.Write(20000u);w.Write(60000u);w.Write((byte)0);
        }
        using var old=new MemoryStream();using(var gzip=new GZipStream(old,CompressionLevel.Fastest,true))raw.WriteTo(gzip);
        var legacy=MovementCodec.Decode(old.ToArray());
        check(legacy[0].Players[0].HP==20000&&legacy[0].Players[0].Statuses.Count==0,"previous binary format remains readable without inventing full statuses");
        var data=new DetailedTelemetry { Version=4,SampleIntervalSeconds=1d/30,Frames=frames,ReplayStatuses=new() { [3054]=new() { Name="防御",Icon=123 } },
            MapMechanics=new() { new() { Timestamp=start,Label="地图计时器",MapWide=true },new() { Timestamp=start,ActionId=123,X=12,Z=34 } } };
        data.PrepareForStorage();
        var restored=BsonMapper.Global.ToObject<DetailedTelemetry>(BsonMapper.Global.ToDocument(data));
        check(restored.ReplayStatuses[3054].Name=="防御"&&restored.MapMechanics[0].MapWide&&restored.MapMechanics[0].X==null&&restored.MapMechanics[1].X==12,"mechanic scope and real coordinates plus status names survive database roundtrip");
        check(restored.Frames[1].Players[0].Statuses[0].Remaining==5,"full statuses survive compressed database storage");
        var longData=new DetailedTelemetry { Version=4,SampleIntervalSeconds=1d/30 };
        var names=Enumerable.Range(0,10).Select(i=>"Player "+i+" World").ToArray();
        var snapshots=Enumerable.Range(0,10).Select(_=>new List<ReplayBuff>()).ToArray();
        for(int i=0;i<36000;i++) {
            var time=start.AddSeconds(i/30d);var f=new MovementFrame { Timestamp=time,Crystal=new() { X=i*.001f,Z=MathF.Sin(i*.001f) } };
            if(i%30==0) snapshots=Enumerable.Range(0,10).Select(j=>Enumerable.Range(0,12).Select(k=>new ReplayBuff { Id=(uint)(3000+k),Param=(ushort)(j%3),Remaining=30-i%900/30f }).ToList()).ToArray();
            for(int j=0;j<10;j++)f.Players.Add(new() { Player=names[j],ClassJobId=19,Team=j<5?1:2,X=MathF.Sin(i*.01f+j)*50,Z=MathF.Cos(i*.01f+j)*50,Heading=j*.2f,HP=(uint)(60000-i%2000),MaxHP=60000,StatusTimestamp=start.AddSeconds(i/30),Statuses=snapshots[j] });
            longData.Frames.Add(f);
        }
        longData.PrepareForStorage();
        check(longData.PackedFrames.Length<=4*1024*1024&&longData.Frames[0].Timestamp==start&&longData.Frames[^1].Timestamp==start.AddSeconds(35999/30d),"20-minute 30 Hz ten-player buff recording fits budget while preserving full time range");
        var longRestored=MovementCodec.Decode(longData.PackedFrames);
        check(longRestored[^1].Players.Count==10&&longRestored[^1].Players[0].Statuses.Count==12,"long-match downsampling retains both teams and complete status snapshots");
        Console.WriteLine($"30 Hz full-buff fixture: {longData.Frames.Count:N0} stored frames, {longData.PackedFrames.Length:N0} bytes, interval {longData.SampleIntervalSeconds:F3}s.");
    }
}
