using System.Buffers.Binary;
using System.Globalization;
namespace CCStats.Core.Capture;
public sealed record ClientUiValue(int Index,int Type,string Value);
public sealed record SettlementUiSnapshot(DateTime ObservedAt,string Panel,List<ClientUiValue> Values);
public static class ClientUiDecoder {
    // No pointers, vectors or arbitrary memory are exported. Only bounded primitive values in result panels.
    public static List<ClientUiValue> Decode(ReadOnlySpan<byte> bytes,int count,Func<long,bool,string> readString) {
        if(count is <0 or >2048||bytes.Length<count*16)throw new InvalidDataException("结算界面字段数量异常。");
        var values=new List<ClientUiValue>();
        for(int i=0;i<count;i++) {
            var row=bytes.Slice(i*16,16);int type=BinaryPrimitives.ReadInt32LittleEndian(row)&0xF;var data=row[8..];
            string? value=type switch {
                2=>data[0]==0?"false":"true",
                3=>BinaryPrimitives.ReadInt32LittleEndian(data).ToString(CultureInfo.InvariantCulture),
                4=>BinaryPrimitives.ReadInt64LittleEndian(data).ToString(CultureInfo.InvariantCulture),
                5=>BinaryPrimitives.ReadUInt32LittleEndian(data).ToString(CultureInfo.InvariantCulture),
                6=>BinaryPrimitives.ReadUInt64LittleEndian(data).ToString(CultureInfo.InvariantCulture),
                7=>BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data)).ToString(CultureInfo.InvariantCulture),
                8 or 9 or 10=>readString(BinaryPrimitives.ReadInt64LittleEndian(data),type==9),_=>null
            };
            if(value!=null)values.Add(new(i,type,value.Length>256?value[..256]:value));
        }
        return values;
    }
}
