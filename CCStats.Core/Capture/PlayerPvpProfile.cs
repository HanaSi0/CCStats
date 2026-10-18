using System.Buffers.Binary;
namespace CCStats.Core.Capture;
public sealed record PlayerPvpProfile(string Player,DateTime ObservedAt,byte Season,ushort CasualMatches,ushort CasualWins,ushort RankedMatches,ushort RankedWins,ushort Credit,ushort HighestCredit,byte Rank,byte HighestRank,byte Riser,byte Stars) {
    public string RankName=>Rank<=8?new[]{"无","青铜","白银","黄金","白金","钻石","水晶","欧米茄","究极"}[Rank]:"未知";
    public static PlayerPvpProfile? Decode(ReadOnlySpan<byte> bytes,string player,DateTime now) {
        if(bytes.Length<0x8C||bytes[0]!=1||string.IsNullOrWhiteSpace(player))return null;
        ushort casual=U(bytes,0x4E),casualWins=U(bytes,0x50),ranked=U(bytes,0x52),rankedWins=U(bytes,0x54);
        if(casualWins>casual||rankedWins>ranked||bytes[0x62]>8||bytes[0x63]>8||bytes[0x64]>5||bytes[0x66]>3)return null;
        return new(player,now,bytes[0x4D],casual,casualWins,ranked,rankedWins,U(bytes,0x5A),U(bytes,0x5C),bytes[0x62],bytes[0x63],bytes[0x64],bytes[0x66]);
    }
    private static ushort U(ReadOnlySpan<byte> b,int at)=>BinaryPrimitives.ReadUInt16LittleEndian(b[at..]);
}
