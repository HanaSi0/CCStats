using System.IO;
using CCStats.Core.Capture;
using PvpStats.Analysis;
using CCStats.Core.Library;
namespace CCStats.Desktop.Capture;
internal sealed record CaptureState(bool Connected,bool Recording,string Message,uint Territory=0,int Frames=0,string[]? Players=null,string? SavedPath=null,PlayerPvpProfile? Profile=null,string? DiagnosticError=null,string GameVersion="",IReadOnlyDictionary<string,string>? ChannelStatus=null,string LocalPlayer="");
internal sealed class IndependentCapture {
    private readonly Func<IObservedGameSource?> _factory;
    private TaskCompletionSource? _flush;
    private string? _summaryError;
    public async Task FlushAsync() {var worker=_worker;if(worker==null||worker.IsCompleted)return;var request=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var existing=Interlocked.CompareExchange(ref _flush,request,null);await Task.WhenAny((existing??request).Task,worker);}
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _completed=new();
    public bool TryDequeueCompleted(out string path)=>_completed.TryDequeue(out path!);
    private string Finish(ObservationWriter writer) {writer.Dispose();_summaryError=writer.SummaryError;if(!File.Exists(writer.Destination))throw new IOException("记录尚未完成，保留临时文件。");_completed.Enqueue(writer.Destination);return writer.Destination;}
    public async Task ReconnectAsync(string directory) {await StopAsync();Connect(directory);}
    public IndependentCapture(Func<IObservedGameSource?> factory)=>_factory=factory;
    private CancellationTokenSource? _lifetime;
    private Task? _worker;
    private int _record;
    private DateTime _nextAttempt;
    private bool _autoRecord=true,_manualPaused;
    public volatile CaptureState State=new(false,false,"正在等待游戏启动并登录角色…");
    public void Configure(bool autoRecord)=>_autoRecord=autoRecord;
    public void Poll(string directory) {if(DateTime.UtcNow>=_nextAttempt)Connect(directory,true);}
    public void Connect(string directory,bool automatic=false) {
        if(_worker is {IsCompleted:false})return;
        if(!automatic)_nextAttempt=DateTime.MinValue;
        _lifetime?.Dispose();_lifetime=new();var token=_lifetime.Token;
        _worker=Task.Run(async ()=> {
            ObservationWriter? writer=null;string? saved=null;var lastPublished=DateTime.MinValue;var lastProbe=DateTime.MinValue;uint lastTerritory=0;string? diagnosticError=null;var activity=new MatchActivityGate();var preroll=new Queue<(uint Territory,MovementFrame Frame,ObservationBatch? Batch)>();
            try {
                using var game=_factory();
                if(game==null) {State=new(false,false,"等待游戏启动：会自动识别并连接，无需手动选择进程。");return;}
                State=new(false,false,"已识别游戏，正在检查客户端地址…");
                using var timer=new PeriodicTimer(TimeSpan.FromTicks(TimeSpan.TicksPerSecond/30));
                do {
                    token.ThrowIfCancellationRequested();var (territory,frame)=game.Sample();
                    if(game.Telemetry is {} diagnostic&&diagnostic.SettlementPanels.Count>0) {
                        try {CaptureDiagnostics.SavePanels(directory,territory,diagnostic);diagnosticError=null;}
                        catch(Exception error) when(error is IOException or UnauthorizedAccessException) {diagnosticError=error.Message;}
                    }
                    if(game.Telemetry is {} probe&&ReplayMapMesh.MapKey(territory)!=null&&probe.ChannelStatus.ContainsKey("sceneIdentities")&&frame.Timestamp-lastProbe>=TimeSpan.FromSeconds(5)) {
                        lastProbe=frame.Timestamp;
                        try {CaptureDiagnostics.SaveProbe(directory,territory,probe);}catch(Exception error) when(error is IOException or UnauthorizedAccessException) {diagnosticError=error.Message;}
                    }
                    if(territory!=lastTerritory&&territory!=0) {_manualPaused=false;lastTerritory=territory;}
                    // A loading screen has no frames to append; wait for a nonzero territory or process exit, rather than splitting on an arbitrary timeout.
                    bool loadingGrace=territory==0&&writer!=null;
                    writer?.ObserveSettlement(game.Telemetry);
                    bool active=activity.Update(territory,frame,game.Telemetry);
                    if(territory>0&&ReplayMapMesh.MapKey(territory)!=null&&frame.Players.Count>0){preroll.Enqueue((territory,frame,game.Telemetry));while(preroll.Count>60||preroll.TryPeek(out var oldest)&&(oldest.Territory!=territory||frame.Timestamp-oldest.Frame.Timestamp>TimeSpan.FromSeconds(2)))preroll.Dequeue();}
                    else if(!loadingGrace)preroll.Clear();
                    bool request=Volatile.Read(ref _record)!=0||(_autoRecord&&!_manualPaused&&(active||loadingGrace));
                    var flush=Volatile.Read(ref _flush);
                    if(writer!=null&&(!request||(!loadingGrace&&territory!=writer.Territory)||writer.ShouldRollOver)) {saved=Finish(writer);writer=null;}
                    if(flush!=null) {writer?.Flush();Interlocked.CompareExchange(ref _flush,null,flush);flush.TrySetResult();}
                    if(request&&frame.Players.Count>0&&territory>0) {
                        if(writer==null) {
                            writer=new ObservationWriter(directory,territory,preroll.TryPeek(out var first)?first.Frame.Timestamp:frame.Timestamp);
                            foreach(var sample in preroll)writer.Append(sample.Territory,sample.Frame,sample.Batch);
                            if(preroll.Count==0)writer.Append(territory,frame,game.Telemetry);
                        } else writer.Append(territory,frame,game.Telemetry);
                        preroll.Clear();
                    }
                    if(frame.Timestamp-lastPublished>=TimeSpan.FromMilliseconds(200)) {
                        lastPublished=frame.Timestamp;
                        State=new(true,writer!=null,$"PID {game.ProcessId} · 目标采样 30 Hz · 状态 5 Hz"+(game.Telemetry is {} telemetry?$" · 队伍 {(frame.Players.Any(p=>p.Team is 1 or 2)?"已读取":"未确认")} · 水晶 {(frame.Crystal!=null?"已读取":"未读取")} · 事件槽 {(telemetry.EffectsPolled?"读取中":"未确认")}":""),territory,writer?.Frames??0,frame.Players.Select(p=>$"{p.Player}　职业 {JobNames.Name(p.ClassJobId)}　HP {p.HP:N0}/{p.MaxHP:N0}").ToArray(),saved,game.Telemetry?.Profile,diagnosticError??_summaryError,game.Telemetry?.GameVersion??"",game.Telemetry?.ChannelStatus,game.Telemetry?.LocalPlayer??"");
                    }
                } while(await timer.WaitForNextTickAsync(token));
            } catch(OperationCanceledException) when(token.IsCancellationRequested) {State=State with {Message="已暂停记录。"};}
            catch(Exception error) {_nextAttempt=DateTime.UtcNow.AddSeconds(3);State=State with {Message="暂未连接："+error.Message+" 将延后重试。"};}
            finally {
                try {if(writer!=null) {saved=Finish(writer);}}
                catch(Exception error) {State=State with {Message="记录收尾失败，保留临时文件："+error.Message};}
                Interlocked.Exchange(ref _flush,null)?.TrySetResult();
                Interlocked.Exchange(ref _record,0);State=State with {Connected=false,Recording=false,SavedPath=saved,LocalPlayer="",Profile=null};
            }
        });
    }
    public void Record(bool enabled) {_manualPaused=!enabled;Interlocked.Exchange(ref _record,enabled?1:0);}
    public async Task StopAsync() {Interlocked.Exchange(ref _record,0);_lifetime?.Cancel();if(_worker!=null)await _worker;}
}
