using CCStats.Core.Capture;

namespace CCStats.Desktop.Capture;

internal sealed record CaptureState(bool Connected,bool Recording,string Message,uint Territory=0,int Frames=0,string[]? Players=null,string? SavedPath=null);
internal sealed class IndependentCapture {
    private CancellationTokenSource? _lifetime;
    private Task? _worker;
    private int _record;
    public volatile CaptureState State=new(false,false,"请先打开纯净游戏并登录角色，然后连接。采集布局仍需实机验证。");
    public void Connect(string directory) {
        if(_worker is {IsCompleted:false})return;
        _lifetime?.Dispose();_lifetime=new();var token=_lifetime.Token;
        State=new(false,false,"正在识别客户端与解析地址…");
        _worker=Task.Run(async ()=> {
            ObservationWriter? writer=null;
            string? saved=null;
            var lastPublished=DateTime.MinValue;
            try {
                using var game=new ExternalGameReader();
                using var timer=new PeriodicTimer(TimeSpan.FromTicks(TimeSpan.TicksPerSecond/30));
                do {
                    token.ThrowIfCancellationRequested();
                    var (territory,frame)=game.Sample();
                    if(Volatile.Read(ref _record)==0 && writer!=null) {writer.Dispose();saved=writer.Destination;writer=null;}
                    if(Volatile.Read(ref _record)!=0 && frame.Players.Count>0 && territory>0) {
                        writer??=new ObservationWriter(directory,territory);
                        writer.Append(territory,frame);
                    }
                    if(frame.Timestamp-lastPublished>=TimeSpan.FromMilliseconds(200)) {
                    lastPublished=frame.Timestamp;
                    State=new(true,writer!=null,frame.Players.Count>0?$"PID {game.ProcessId} · 只读观测 · 目标 30 Hz；状态读取 5 Hz":"地址已解析，尚无有效角色观测，请登录后等待；未验证成功前不写入数据。",territory,writer?.Frames??0,frame.Players.Select(p=>$"{p.Player}　职业 {p.ClassJobId}　HP {p.HP:N0}/{p.MaxHP:N0}").ToArray(),saved);
                    }
                } while(await timer.WaitForNextTickAsync(token));
            } catch(OperationCanceledException) when(token.IsCancellationRequested) {State=State with {Message="已断开采集连接。"};}
            catch(Exception error) {State=State with {Message="采集已停止："+error.Message};}
            finally {
                try {if(writer!=null) {writer.Dispose();saved=writer.Destination;}}
                catch(Exception error) {State=State with {Message="记录收尾失败，保留 .partial 文件："+error.Message};}
                Interlocked.Exchange(ref _record,0);
                State=State with {Connected=false,Recording=false,SavedPath=saved};
            }
        });
    }
    public void Record(bool enabled) => Interlocked.Exchange(ref _record,enabled?1:0);
    public async Task StopAsync() {
        Interlocked.Exchange(ref _record,0);_lifetime?.Cancel();
        if(_worker!=null)await _worker;
    }
}
