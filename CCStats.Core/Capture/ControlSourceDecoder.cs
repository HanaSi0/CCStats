using System.Buffers.Binary;
using PvpStats.Analysis;
namespace CCStats.Core.Capture;
public sealed record ControlSource(uint StatusId,uint EntityId,float Remaining);
public static class ControlSourceDecoder {
    public static List<ControlSource> Decode(ReadOnlySpan<byte> statuses) {
        var result=new List<ControlSource>();if(statuses.Length<ClientLayout.StatusBytes)return result;
        for(int i=0;i<60;i++) {
            var row=statuses.Slice(8+i*16,16);uint id=BinaryPrimitives.ReadUInt16LittleEndian(row);
            if(!DetailedAnalysis.Controls.ContainsKey(id)||BinaryPrimitives.ReadUInt32LittleEndian(row[12..])!=0)continue;
            uint source=BinaryPrimitives.ReadUInt32LittleEndian(row[8..]);float remaining=BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(row[4..]));
            if(source is 0 or 0xE0000000||!float.IsFinite(remaining)||remaining is <=0 or >1000000)continue;
            result.Add(new(id,source,remaining));
        }
        return result;
    }
    public static List<ObservedControl> Resolve(IReadOnlyList<ControlSource> controls,DateTime sampledAt,DateTime now,IReadOnlyDictionary<uint,ObservedActor> actors) {
        var result=new List<ObservedControl>();
        foreach(var control in controls) {
            float remaining=control.Remaining-(float)Math.Max(0,(now-sampledAt).TotalSeconds);if(remaining<=0)continue;
            actors.TryGetValue(control.EntityId,out var actor);var owner=actor!=null?actors.GetValueOrDefault(actor.OwnerId):null;
            var name=actor?.Player?.Player??owner?.Player?.Player??"";
            result.Add(new(){StatusId=control.StatusId,Source=name,Remaining=remaining});
        }
        return result;
    }
}
