using LiteDB;
using System.Text.Json;
using PvpStats.Analysis;

namespace CCStats.Core.Library;

public sealed class RecordLibrary : IDisposable {
    private readonly LiteDatabase _db;
    private readonly object _gate=new();
    public RecordLibrary(string directory) {
        Directory.CreateDirectory(directory);_db=new LiteDatabase(Path.Combine(directory,"library.db"));
        // Packed movement timestamps are UTC. LiteDB otherwise returns local dates, shifting replay by the machine timezone.
        _db.UtcDate=true;
        _db.GetCollection<MatchRecord>("records").EnsureIndex(r=>r.StartUtc);
    }
    public List<MatchRecord> All() {lock(_gate)return _db.GetCollection<MatchRecord>("records").FindAll().OrderByDescending(r=>r.StartUtc).ToList();}
    public void Save(ReplayBundle bundle) {
        // Store movement in the existing compressed binary format, not expanded JSON in the database.
        if(bundle.Data.Frames.Count>0)bundle.Data.Version=Math.Max(3,bundle.Data.Version);
        bundle.Data.PrepareForStorage();
        bundle.Match.FrameCount=bundle.Data.Frames.Count;
        lock(_gate) {
            var records=_db.GetCollection<MatchRecord>("records");var old=records.FindById(bundle.Match.Id);
            if(old!=null) {bundle.Match.Favorite=old.Favorite;bundle.Match.Tags=old.Tags;}
            _db.BeginTrans();
            try {
                records.Upsert(bundle.Match);
                _db.GetCollection<BsonDocument>("replays").Upsert(new BsonDocument { ["_id"]=bundle.Match.Id,["ClientProfile"]=System.Text.Json.JsonSerializer.Serialize(bundle.ClientProfile,RecordTime.Json),["ResultPanels"]=System.Text.Json.JsonSerializer.Serialize(bundle.ResultPanels,RecordTime.Json),["Data"]=BsonMapper.Global.ToDocument(bundle.Data),["Actions"]=System.Text.Json.JsonSerializer.Serialize(bundle.Actions,RecordTime.Json),["Progress"]=System.Text.Json.JsonSerializer.Serialize(bundle.Progress,RecordTime.Json),["Timeline"]=System.Text.Json.JsonSerializer.Serialize(bundle.Timeline,RecordTime.Json)});
                if(bundle.ClientProfile!=null)SaveProfile(bundle.ClientProfile);
                _db.Commit();
            } catch {_db.Rollback();throw;}
        }
    }
    public ReplayBundle Load(string id) {lock(_gate) {
        var match=_db.GetCollection<MatchRecord>("records").FindById(id)??throw new InvalidDataException("记录不存在。");
        var replay=_db.GetCollection<BsonDocument>("replays").FindById(id);
        List<T> ReadList<T>(string key)=>replay!=null&&replay[key].IsString?System.Text.Json.JsonSerializer.Deserialize<List<T>>(replay[key].AsString,RecordTime.Json)??new():new();
        var bundle=new ReplayBundle {Match=match,ClientProfile=replay!=null&&replay["ClientProfile"].IsString?System.Text.Json.JsonSerializer.Deserialize<Capture.PlayerPvpProfile>(replay["ClientProfile"].AsString,RecordTime.Json):null,ResultPanels=ReadList<Capture.SettlementUiSnapshot>("ResultPanels"),Actions=ReadList<ActionSummary>("Actions"),Progress=ReadList<ProgressSample>("Progress"),Data=replay==null?new():BsonMapper.Global.ToObject<DetailedTelemetry>(replay["Data"].AsDocument),Timeline=replay==null?new():System.Text.Json.JsonSerializer.Deserialize<List<TimelineRow>>(replay["Timeline"].AsString,RecordTime.Json)??new()};
        ReplayRoster.Reconcile(bundle);bool wasComplete=match.ScoreboardComplete,wasObservation=match.Observation;
        if(Capture.SettlementDecoder.Apply(bundle)&&(!wasComplete||wasObservation!=match.Observation))Save(bundle);
        return bundle;
    }}
    public void SaveProfile(Capture.PlayerPvpProfile profile) {lock(_gate) {if(Profile(profile.Player) is {} current&&current.ObservedAt>profile.ObservedAt)return;_db.GetCollection<BsonDocument>("profiles").Upsert(new BsonDocument {["_id"]=profile.Player,["Data"]=System.Text.Json.JsonSerializer.Serialize(profile,RecordTime.Json)});}}
    public Capture.PlayerPvpProfile? Profile(string player) {lock(_gate) {var row=_db.GetCollection<BsonDocument>("profiles").FindById(player);return row==null?null:System.Text.Json.JsonSerializer.Deserialize<Capture.PlayerPvpProfile>(row["Data"].AsString,RecordTime.Json);}}
    public void Annotate(string id,bool favorite,string tags) {lock(_gate) {var c=_db.GetCollection<MatchRecord>("records");var row=c.FindById(id);if(row==null)return;row.Favorite=favorite;row.Tags=tags;c.Update(row);}}
    public void Delete(string id) {lock(_gate) {_db.BeginTrans();try {_db.GetCollection<MatchRecord>("records").Delete(id);_db.GetCollection<BsonDocument>("replays").Delete(id);_db.Commit();}catch {_db.Rollback();throw;}}}
    public void Dispose() {lock(_gate)_db.Dispose();}
}
