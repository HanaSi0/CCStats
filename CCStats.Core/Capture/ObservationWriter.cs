using System.Text.Json;
using PvpStats.Analysis;

namespace CCStats.Core.Capture;

/// <summary>Bounded streaming observation recording, not a fabricated match result.</summary>
public sealed class ObservationWriter : IDisposable {
    private readonly FileStream _file;
    private readonly Utf8JsonWriter _json;
    private readonly uint _territory;
    private readonly DateTime _start=DateTime.UtcNow;
    private bool _finished;
    public long BytesWritten=>_file.Position+_json.BytesPending;
    public bool ShouldRollOver=>BytesWritten>=60L*1024*1024;
    public uint Territory=>_territory;
    public string Destination {get;}
    public int Frames {get;private set;}
    public ObservationWriter(string directory,uint territory) {
        Directory.CreateDirectory(directory);_territory=territory;
        Destination=Path.Combine(directory,"Observation-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]+".json");
        _file=new FileStream(Destination+".partial",FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536);
        _json=new Utf8JsonWriter(_file);
        _json.WriteStartObject();_json.WriteString("RecordKind","IndependentObservation");_json.WriteString("MatchId",Guid.NewGuid().ToString("N")[..24]);
        _json.WriteNumber("TerritoryId",territory);_json.WriteString("StartUtc",_start);_json.WriteString("CaptureProfile",ClientLayout.Profile);
        _json.WriteString("MissingData","未采集结算、队伍归属、水晶、技能效果包；不能用于完整伤害或控制统计。");
        _json.WriteStartObject("Telemetry");_json.WriteNumber("Version",2);_json.WriteNumber("SampleIntervalSeconds",1.0/30);_json.WriteStartArray("Frames");
    }
    public void Append(uint territory,MovementFrame frame) {
        if(_finished)throw new InvalidOperationException("记录已经结束。");
        if(territory!=_territory)throw new InvalidOperationException("地图已切换，本段观测已结束。");
        if(_file.Position+_json.BytesPending>64*1024*1024)throw new InvalidOperationException("本段记录已达到 64 MiB，已停止以限制资源占用。");
        JsonSerializer.Serialize(_json,frame);Frames++;
        if(Frames%30==0)_json.Flush();
    }
    public void Dispose() {
        if(_finished)return;_finished=true;
        try {
            _json.WriteEndArray();_json.WriteEndObject();_json.WriteString("MatchEndTime",DateTime.UtcNow);_json.WriteEndObject();_json.Flush();
        } finally {_json.Dispose();_file.Dispose();}
        File.Move(Destination+".partial",Destination);
    }
}
