using System.Buffers.Binary;
using PvpStats.Analysis;
namespace CCStats.Core.Capture;
public static class ArenaObjectDecoder {
    public static ReplayArenaObject? Decode(ReadOnlySpan<byte> header,uint nameId,uint territory,int slot,DateTime now) {
        if(header.Length<ClientLayout.HeaderBytes||slot is <0 or >=ClientLayout.ObjectSlots)return null;
        byte kind=header[0x90];if(kind is not (2 or 7)||kind==2&&nameId==11350)return null;
        uint entity=U(header,0x78),data=U(header,0x84);bool validEntity=entity is not (0 or 0xE0000000);
        var mechanism=ClientCatalog.Mechanic(kind,nameId,data,territory);if(kind==2&&(!validEntity||mechanism==ArenaMechanicKind.Unknown))return null;
        float x=F(header,0xB0),y=F(header,0xB4),z=F(header,0xB8),heading=F(header,0xC0);
        if(!float.IsFinite(x)||!float.IsFinite(y)||!float.IsFinite(z)||!float.IsFinite(heading)||Math.Abs(x)>100000||Math.Abs(y)>100000||Math.Abs(z)>100000)return null;
        // Event objects commonly have no network entity ID. This is a recording-local key,
        // made from observed layout identity and slot; it is not presented as a native entity ID.
        ulong key=validEntity?entity:0x8000000000000000UL|((ulong)U(header,0x7C)<<16)|(uint)slot;
        return new(){Timestamp=now,ObservedUntil=now,ObjectId=key,EntityId=validEntity?entity:0,NameId=nameId,DataId=data,ObjectKind=kind,EventState=header[0x70],RenderFlags=BinaryPrimitives.ReadUInt64LittleEndian(header[0x118..]),Kind=mechanism,X=x,Y=y,Z=z,Heading=heading};
    }
    private static uint U(ReadOnlySpan<byte> data,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
    private static float F(ReadOnlySpan<byte> data,int at)=>BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data[at..]));
}
