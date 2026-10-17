using System.IO.Compression;
using System.Text;
using LiteDB;
using PvpStats.Analysis;
internal static class MovementCompatibilityChecks {
    internal static void Run(Action<bool,string> check) {
        var start=new DateTime(2026,10,17,0,0,0,DateTimeKind.Utc);
        var capture=new DetailedTelemetry { Version=5,Frames=new() { new() { Timestamp=start,Crystal=new() { X=2,Y=3.5f,Z=4 },Players=new() { new() { Player="Example World",X=1,Y=7.25f,Z=3,StatusTimestamp=start } } } } };
        capture.PrepareForStorage();
        var restored=BsonMapper.Global.ToObject<DetailedTelemetry>(BsonMapper.Global.ToDocument(capture));
        check(restored.Frames[0].Players[0].Y==7.25f&&restored.Frames[0].Crystal?.Y==3.5f,"removing experimental UI preserves existing height-aware replay records");
        using var raw=new MemoryStream();
        using(var w=new BinaryWriter(raw,Encoding.UTF8,true)) {
            w.Write(0x43434D32);w.Write(1);w.Write((ushort)1);w.Write("Legacy World");w.Write(start.ToBinary());
            w.Write(0L);w.Write(0);w.Write(0);w.Write(0);w.Write(true);w.Write(3f);w.Write(4f);w.Write((byte)1);
            w.Write((ushort)0);w.Write((byte)1);w.Write(1f);w.Write(2f);w.Write(0f);w.Write(20000u);w.Write(60000u);w.Write((byte)0);
            w.Write(19u);w.Write(true);w.Write(start.ToBinary());w.Write((byte)1);w.Write(3054u);w.Write((ushort)0);w.Write(5f);
        }
        using var packed=new MemoryStream();using(var zip=new GZipStream(packed,CompressionLevel.Fastest,true))raw.WriteTo(zip);
        var prior=MovementCodec.Decode(packed.ToArray());
        check(prior[0].Players[0].Y==null&&prior[0].Crystal?.Y==null&&prior[0].Players[0].Statuses[0].Id==3054,"previous full-status recordings remain readable after experimental code removal");
    }
}
