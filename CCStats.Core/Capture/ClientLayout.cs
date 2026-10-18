using System.Buffers.Binary;
using System.Text;
using PvpStats.Analysis;

namespace CCStats.Core.Capture;

// Experimental layout facts from FFXIVClientStructs d6397a76b3a63c2b247025dc296653182b718ca6.
// Signature uniqueness and field sanity are mandatory, but are not live-client validation.
public static class ClientLayout {
    public const string Profile = "FFXIVClientStructs-d6397a7-experimental";
    public const string ObjectsSignature = "48 8D 35 ?? ?? ?? ?? 81 FA";
    public const string AtkStageSignature = "48 8B 05 ?? ?? ?? ?? 4C 8B 40 18 45 8B 40 18";
    public const string PvpProfileSignature = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 3C ?? 75 ?? B8 ?? ?? ?? ?? EB ?? B8";
    public const string EventFrameworkSignature = "48 8B 05 ?? ?? ?? ?? 48 85 C0 74 ?? 83 B8 ?? ?? ?? ?? ?? 7C";
    public const int ActiveDirectorOffset=0x158, DirectorTypeOffset=0xDCE;
    public const int DirectorPlayerPointerOffset=0x1FE8, DirectorProgressOffset=0x2138, DirectorProgressBytes=28;
    public const int DirectorPlayerBytes=0x138, CastOffset=0x2790, CastBytes=0x170, EffectOffset=0x2900, EffectBytes=0xF00;
    public const string GameMainSignature = "48 8D 0D ?? ?? ?? ?? 0F 28 F2 48 89 44 24 ??";
    public const int ObjectArrayOffset = 0x20, ObjectSlots = 819, HeaderBytes = 0x1F0;
    public const int WorldOffset = 0x2362, StatusOffset = 0x23B0, StatusBytes = 0x3E0;
    public const int TerritoryOffset = 0x4108;
    public static int FindUnique(ReadOnlySpan<byte> image,string signature) {
        var pattern=signature.Split(' ').Select(s=>s=="??"?(byte?)null:Convert.ToByte(s,16)).ToArray();
        int found=-1, offset=0;
        while(offset<=image.Length-pattern.Length) {
            int next=image[offset..].IndexOf(pattern[0]!.Value);if(next<0)break;offset+=next;
            if(offset+pattern.Length>image.Length)break;
            bool match=true;
            for(int i=1;i<pattern.Length;i++)if(pattern[i].HasValue&&pattern[i]!=image[offset+i]) {match=false;break;}
            if(match) {if(found>=0)throw new InvalidDataException("客户端签名不唯一，未启动采集。");found=offset;}
            offset++;
        }
        if(found<0)throw new InvalidDataException("当前客户端签名尚未适配，未启动采集。");return found;
    }
    private static float Float(ReadOnlySpan<byte> data,int at)=>BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data[at..]));
    public static PlayerPosition? DecodePlayer(ReadOnlySpan<byte> header,ushort world,ReadOnlySpan<byte> status,DateTime now) {
        if(header.Length<HeaderBytes || header[0x90]!=1)return null; // PC only; never interpret NPC memory as BattleChara.
        var id=BinaryPrimitives.ReadUInt32LittleEndian(header[0x78..]);if(id is 0 or 0xE0000000)return null;
        var nameBytes=header.Slice(0x30,64);var end=nameBytes.IndexOf((byte)0);if(end<=0)return null;
        string name;
        try {name=new UTF8Encoding(false,true).GetString(nameBytes[..end]);}catch(DecoderFallbackException) {return null;}
        if(name.Any(char.IsControl))return null;
        var x=Float(header,0xB0);var y=Float(header,0xB4);var z=Float(header,0xB8);var heading=Float(header,0xC0);
        var hp=BinaryPrimitives.ReadUInt32LittleEndian(header[0x1AC..]);var max=BinaryPrimitives.ReadUInt32LittleEndian(header[0x1B0..]);
        var job=header[0x1CA];
        if(!float.IsFinite(x)||!float.IsFinite(y)||!float.IsFinite(z)||!float.IsFinite(heading)||MathF.Abs(x)>100000||MathF.Abs(y)>100000||MathF.Abs(z)>100000||max==0||max>10000000||hp>max||job is 0 or >100)return null;
        var result=new PlayerPosition {Player=ClientCatalog.PlayerName(name,world),X=x,Y=y,Z=z,Heading=heading,HP=hp,MaxHP=max,ClassJobId=job,StatusTimestamp=now};
        // Team is deliberately unknown. Relative hostility is not an Astra/Umbra identity.
        if(status.Length>=StatusBytes)for(int i=0;i<60;i++) {
            var row=status.Slice(8+i*16,16);var statusId=BinaryPrimitives.ReadUInt16LittleEndian(row);if(statusId==0)continue;
            var remaining=BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(row[4..]));
            if(!float.IsFinite(remaining)||MathF.Abs(remaining)>1000000)continue;
            result.Statuses.Add(new ReplayBuff {Id=statusId,Param=BinaryPrimitives.ReadUInt16LittleEndian(row[2..]),Remaining=remaining});
        }
        return result;
    }
}
