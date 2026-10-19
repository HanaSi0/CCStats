using System.Buffers.Binary;
using CCStats.Core.Capture;
using PvpStats.Analysis;
internal static class ReplayUpgradeChecks {
    internal static void Run(Action<bool,string> check) {
        check(CCStats.Core.Library.StatusCatalog.Find(2872) is {Icon:212952},"status metadata maps real state IDs to game icon resources");
        var now=DateTime.UtcNow;var p=new PlayerPosition {Player="Mana Test",MP=4200,MaxMP=10000,HP=100,MaxHP=100};
        var frames=new List<MovementFrame>{new(){Timestamp=now,Players=new(){p}},new(){Timestamp=now.AddMilliseconds(100),Players=new(){new(){Player=p.Player,HP=100,MP=3800,MaxMP=10000}}}};
        var read=MovementCodec.Decode(MovementCodec.Encode(frames));check(read[0].Players[0].MP==4200&&read[0].Players[0].MaxMP==10000,"new packed records retain mana without truncating positions");
        check(ReplayInterpolation.Sample(new(){Frames=read},now.AddMilliseconds(50))!.Players[0].MP==4200,"mana keeps its actual sampled value during position interpolation");
        using(var zip=new System.IO.Compression.GZipStream(new MemoryStream(MovementCodec.Encode(frames)),System.IO.Compression.CompressionMode.Decompress)){using var reader=new BinaryReader(zip);check(reader.ReadInt32()==0x43434D34,"mana uses a versioned movement payload rather than corrupting old records");}
        p.MP=null;p.MaxMP=null;var old=MovementCodec.Decode(MovementCodec.Encode(frames.Take(1).ToArray()));check(old[0].Players[0].MP==null,"old records without mana remain unknown rather than becoming zero");
        var header=new byte[ClientLayout.HeaderBytes];header[0x90]=1;header[0x30]=(byte)'A';header[0x1CA]=19;
        void U(int offset,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(offset),value);
        U(0x78,1);U(0x1AC,100);U(0x1B0,100);U(0x1B4,2500);U(0x1B8,10000);
        check(ClientLayout.DecodePlayer(header,1,ReadOnlySpan<byte>.Empty,now) is {MP:2500,MaxMP:10000},"native mana fields decode from the documented CharacterData layout");
        U(0x1B4,11000);check(ClientLayout.DecodePlayer(header,1,ReadOnlySpan<byte>.Empty,now) is {MP:null},"invalid mana cannot suppress otherwise valid character coordinates");
    }
}
