using System.Buffers.Binary;
using System.Text;
namespace CCStats.Core.Capture;
public sealed record ClientUiText(string Path,uint NodeId,float X,float Y,ushort Flags,string Text,byte[] EncodedText);
public static class SettlementTextProbe {
    public static bool IsResultPanel(string name)=>name=="ColosseumRecord"||name.StartsWith("PvPResult",StringComparison.Ordinal)||name.StartsWith("PvPColosseumResult",StringComparison.Ordinal)||name=="PvPTeamResult";
    /// <summary>Bounded text-only traversal of a verified open result panel. Does not issue UI commands.</summary>
    public static List<ClientUiText> Read(long unit,Func<long,int,byte[]?> read) {
        var texts=new List<ClientUiText>();var visited=new HashSet<long>();var managers=new Queue<(long Address,string Path,int Depth)>();
        managers.Enqueue((unit+0x28,"root",0));int budget=2048;
        while(managers.TryDequeue(out var manager)&&budget>0&&texts.Count<512) {
            if(manager.Depth>8)continue;var uld=read(manager.Address,0x58);if(uld==null||uld.Length<0x58)continue;
            int count=BinaryPrimitives.ReadUInt16LittleEndian(uld.AsSpan(0x42));long list=BinaryPrimitives.ReadInt64LittleEndian(uld.AsSpan(0x50));
            if(count is <1 or >1024||list<0x10000)continue;var pointers=read(list,count*8);if(pointers==null||pointers.Length<count*8)continue;
            for(int i=0;i<count&&budget>0&&texts.Count<512;i++) {
                long address=BinaryPrimitives.ReadInt64LittleEndian(pointers.AsSpan(i*8));if(address<0x10000||!visited.Add(address))continue;budget--;
                var node=read(address,0xD8);if(node==null||node.Length<0xD8)continue;
                uint id=BinaryPrimitives.ReadUInt32LittleEndian(node.AsSpan(8));ushort type=BinaryPrimitives.ReadUInt16LittleEndian(node.AsSpan(0x40));string path=manager.Path+"/"+id+"-"+i;
                if(type>=1000) {
                    long component=BinaryPrimitives.ReadInt64LittleEndian(node.AsSpan(0xC0));if(component>=0x10000)managers.Enqueue((component+8,path,manager.Depth+1));
                }
                if(type!=3)continue;
                float x=F(node,0x70),y=F(node,0x74);if(!float.IsFinite(x)||!float.IsFinite(y))continue;
                long text=BinaryPrimitives.ReadInt64LittleEndian(node.AsSpan(0xD0));if(text<0x10000)continue;var bytes=read(text,512);if(bytes==null)continue;
                int end=Array.IndexOf(bytes,(byte)0);if(end<0)end=Math.Min(512,bytes.Length);if(end==0)continue;
                var encoded=bytes.AsSpan(0,end).ToArray();texts.Add(new(path,id,x,y,BinaryPrimitives.ReadUInt16LittleEndian(node.AsSpan(0xAE)),Encoding.UTF8.GetString(encoded),encoded));
            }
        }
        return texts;
    }
    private static float F(ReadOnlySpan<byte> bytes,int at)=>BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes[at..]));
}
