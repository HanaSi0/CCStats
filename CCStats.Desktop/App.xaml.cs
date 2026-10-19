using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace CCStats.Desktop;

public partial class App : Application {
    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        var smoke=Array.IndexOf(e.Args,"--smoke-test");
        var output=smoke>=0 && e.Args.Length>smoke+1?Path.GetFullPath(e.Args[smoke+1]):null;
        if(smoke>=0 && output==null) {Shutdown(2);return;}
        var temp=output!=null?Path.Combine(Path.GetTempPath(),"CCStats-Desktop-Smoke-"+Guid.NewGuid().ToString("N")):null;
        var window=new MainWindow(temp==null?null:Path.Combine(temp,"settings.json"));MainWindow=window;window.Show();
        if(output==null) {
            if(e.Args.Length==1 && !e.Args[0].StartsWith("--"))_ = window.ImportAsync(e.Args[0]);
            return;
        }
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async ()=> {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);Directory.CreateDirectory(temp!);
                var fixture=Path.Combine(temp!,"smoke-replay.json");
                await File.WriteAllTextAsync(fixture,"""
                {"RecordKind":"IndependentObservation","LocalPlayer":"Example One Ixion","MatchId":"507f1f77bcf86cd799439011","TerritoryId":1034,"StartUtc":"2026-10-18T00:00:00Z","MatchEndTime":"2026-10-18T00:04:00Z","Telemetry":{"CaptureMethod":"ReadOnlyMemoryPolling","DirectorObserved":true,"CaptureWarnings":["内部记录夹具：未保存官方结算。"],"MapSizeFactor":400,"Frames":[{"Timestamp":"2026-10-18T00:00:00Z","AstraProgress":500,"UmbraProgress":200,"CrystalProgress":100,"Crystal":{"X":-60,"Y":3,"Z":0},"Players":[{"Player":"Example One Ixion","Team":1,"ClassJobId":19,"HP":10000,"MaxHP":10000,"X":-60,"Y":3,"Z":0,"Heading":1.57,"StatusTimestamp":"2026-10-18T00:00:00Z","Statuses":[{"Id":3054,"Remaining":5}]},{"Player":"Example Two Titan","Team":2,"ClassJobId":25,"HP":8000,"MaxHP":10000,"X":-55,"Y":3,"Z":0}]},{"Timestamp":"2026-10-18T00:00:00.1Z","Players":[]}],"Effects":[],"ReplaySkills":[{"Timestamp":"2026-10-18T00:00:00Z","ActionId":29230,"Actor":"Example One Ixion","TargetPlayer":"Example Two Titan","X":-60,"Y":3,"Z":0,"Heading":1.57}],"MapMechanics":[]}}
                """);
                await window.RunSmokeChecksAsync(fixture,Path.ChangeExtension(output,"png"));
                await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new {success=true,os=Environment.OSVersion.ToString(),framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,navigation=9,import=true,persistence=true,tray=true,closeChoice=true,replay=true,width=window.ActualWidth,height=window.ActualHeight}));
                Shutdown(0);
            } catch(Exception error) {
                await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new {success=false,error=error.ToString()}));Shutdown(1);
            } finally {if(Directory.Exists(temp))Directory.Delete(temp,true);}
        }));
    }
}
