using System.Buffers.Binary;
using System.Text;
namespace CCStats.Core.Capture;
public static class DirectorDecoder {
    public static DirectorSnapshot? Progress(ReadOnlySpan<byte> data) {
        if(data.Length<ClientLayout.DirectorProgressBytes)return null;
        var a=new int[7];for(int i=0;i<a.Length;i++)a[i]=BinaryPrimitives.ReadInt32LittleEndian(data[(i*4)..]);
        if(a[0] is < -1000 or >1000||a[1] is <0 or >1000||a[2] is <0 or >1000||a[3] is <0 or >5||a[4] is <0 or >5||a[5] is <0 or >1000||a[6] is <0 or >1000)return null;
        return new(a[0],a[1],a[2],a[3],a[4],a[5],a[6]);
    }
    public static List<DirectorPlayer>? Roster(ReadOnlySpan<byte> data) {
        if(data.Length<ClientLayout.DirectorPlayerBytes*10)return null;
        var result=new List<DirectorPlayer>();var ids=new HashSet<uint>();
        for(int i=0;i<10;i++) {
            var row=data.Slice(i*ClientLayout.DirectorPlayerBytes,ClientLayout.DirectorPlayerBytes);
            uint id=BinaryPrimitives.ReadUInt32LittleEndian(row[0xE0..]);if(id is 0 or 0xE0000000)continue;
            byte team=row[0xE8],job=row[0xE9];ushort world=BinaryPrimitives.ReadUInt16LittleEndian(row[0x128..]);
            var bytes=row.Slice(0x8A,64);int end=bytes.IndexOf((byte)0);if(end<=0||team>1||job is <19 or >42||world==0||!ids.Add(id))return null;
            string name;try{name=new UTF8Encoding(false,true).GetString(bytes[..end]);}catch(DecoderFallbackException){return null;}
            if(name.Any(char.IsControl))return null;
            result.Add(new(id,team+1,job,world,name));
        }
        // Empty loading data is not evidence of an active match.
        return result.Count is >0 and <=10?result:null;
    }
}
