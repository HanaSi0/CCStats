using System.IO;
using CCStats.Core.Capture;
using PvpStats.Analysis;
using CCStats.Core.Library;
namespace CCStats.Desktop.Capture;
internal sealed record CaptureState(bool Connected,bool Recording,string Message,uint Territory=0,int Frames=0,string[]? Players=null,string? SavedPath=null,PlayerPvpProfile? Profile=null,string? DiagnosticError=null);
internal sealed class IndependentCapture {
    private readonly Func<IObservedGameSource?> _factory;
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
            ObservationWriter? writer=null;string? saved=null;var lastPublished=DateTime.MinValue;uint lastTerritory=0;string? diagnosticError=null;
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
                    if(territory!=lastTerritory&&territory!=0) {_manualPaused=false;lastTerritory=territory;}
                    bool request=Volatile.Read(ref _record)!=0||(_autoRecord&&!_manualPaused&&ReplayMapMesh.MapKey(territory)!=null);
                    if(writer!=null&&(!request||territory!=writer.Territory||writer.ShouldRollOver)) {writer.Dispose();saved=writer.Destination;writer=null;}
                    if(request&&frame.Players.Count>0&&territory>0) {writer??=new ObservationWriter(directory,territory);writer.Append(territory,frame,game.Telemetry);}
                    if(frame.Timestamp-lastPublished>=TimeSpan.FromMilliseconds(200)) {
                        lastPublished=frame.Timestamp;
                        State=new(true,writer!=null,$"PID {game.ProcessId} · 目标采样 30 Hz · 状态 5 Hz"+(game.Telemetry is {} telemetry?$" · 队伍 {(telemetry.DirectorAvailable?"已读取":"未确认")} · 水晶 {(frame.Crystal!=null?"已读取":"未观察")} · 事件槽 {(telemetry.EffectsPolled?"读取中":"未确认")}":""),territory,writer?.Frames??0,frame.Players.Select(p=>$"{p.Player}　职业 {JobNames.Name(p.ClassJobId)}　HP {p.HP:N0}/{p.MaxHP:N0}").ToArray(),saved,game.Telemetry?.Profile,diagnosticError);
                    }
                } while(await timer.WaitForNextTickAsync(token));
            } catch(OperationCanceledException) when(token.IsCancellationRequested) {State=State with {Message="已暂停采集。"};}
            catch(Exception error) {_nextAttempt=DateTime.UtcNow.AddSeconds(30);State=State with {Message="暂未连接："+error.Message+" 将延后重试。"};}
            finally {
                try {if(writer!=null) {writer.Dispose();saved=writer.Destination;}}
                catch(Exception error) {State=State with {Message="记录收尾失败，保留临时文件："+error.Message};}
                Interlocked.Exchange(ref _record,0);State=State with {Connected=false,Recording=false,SavedPath=saved};
            }
        });
    }
    public void Record(bool enabled) {_manualPaused=!enabled;Interlocked.Exchange(ref _record,enabled?1:0);}
    public async Task StopAsync() {Interlocked.Exchange(ref _record,0);_lifetime?.Cancel();if(_worker!=null)await _worker;}
}
