using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CCStats.Desktop.Views;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace CCStats.Desktop;

public partial class App : Application {
    private void RefreshTimeZone(object? sender,EventArgs args) {
        if(Dispatcher.HasShutdownStarted)return;
        _=Dispatcher.BeginInvoke(new Action(()=>{TimeZoneInfo.ClearCachedData();if(MainWindow is MainWindow main)main.RefreshTimeZone();}));
    }
    private void RefreshTimeZonePreference(object sender,Microsoft.Win32.UserPreferenceChangedEventArgs args) {if(args.Category is Microsoft.Win32.UserPreferenceCategory.Locale or Microsoft.Win32.UserPreferenceCategory.General)RefreshTimeZone(sender,args);}
    protected override void OnExit(ExitEventArgs e) {Microsoft.Win32.SystemEvents.TimeChanged-=RefreshTimeZone;Microsoft.Win32.SystemEvents.UserPreferenceChanged-=RefreshTimeZonePreference;base.OnExit(e);}

    protected override async void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        var smoke=Array.IndexOf(e.Args,"--smoke-test");
        var output=smoke>=0 && e.Args.Length>smoke+1?Path.GetFullPath(e.Args[smoke+1]):null;
        if(smoke>=0 && output==null) {Shutdown(2);return;}
        var temp=output!=null?Path.Combine(Path.GetTempPath(),"CCStats-Desktop-Smoke-"+Guid.NewGuid().ToString("N")):null;
        UiMotion.Enabled=output==null;
        EventManager.RegisterClassHandler(typeof(TabControl),Selector.SelectionChangedEvent,new SelectionChangedEventHandler((sender,args)=>{if(sender is TabControl tabs&&args.Source==tabs&&tabs.SelectedContent is FrameworkElement content)UiMotion.Enter(content);}));
        EventManager.RegisterClassHandler(typeof(ScrollViewer),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>SmoothScroll.Attach((ScrollViewer)sender)));
        StartupSplash? splash=null;MainWindow window;
        try {
            if(output==null){splash=new StartupSplash();splash.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);}
            window=new MainWindow(temp==null?null:Path.Combine(temp,"settings.json"));MainWindow=window;window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);if(splash!=null)await splash.DismissAsync();
        } catch(Exception error) {
            splash?.Hide();
            if(output!=null){Directory.CreateDirectory(Path.GetDirectoryName(output)!);await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new {success=false,error=error.ToString()}));}
            else MessageBox.Show(error.Message,"CCStats 无法打开",MessageBoxButton.OK,MessageBoxImage.Error);
            splash?.Close();Shutdown(1);return;
        }
        if(output==null) {
            Microsoft.Win32.SystemEvents.TimeChanged+=RefreshTimeZone;Microsoft.Win32.SystemEvents.UserPreferenceChanged+=RefreshTimeZonePreference;
            if(e.Args.Length==1 && !e.Args[0].StartsWith("--"))_ = window.ImportAsync(e.Args[0]);
            return;
        }
        _ = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async ()=> {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);Directory.CreateDirectory(temp!);
                var fixture=Path.Combine(temp!,"smoke-replay.json");
                await File.WriteAllTextAsync(fixture,"""
                {"RecordKind":"IndependentObservation","LocalPlayer":"Example One Ixion","MatchId":"507f1f77bcf86cd799439011","TerritoryId":1034,"StartUtc":"2026-10-18T00:00:00Z","MatchEndTime":"2026-10-18T00:04:00Z","Telemetry":{"CaptureMethod":"ReadOnlyMemoryPolling","DirectorObserved":true,"CaptureWarnings":["内部记录夹具：未保存官方结算。"],"MapSizeFactor":400,"Frames":[{"Timestamp":"2026-10-18T00:00:00Z","AstraProgress":500,"UmbraProgress":200,"CrystalProgress":100,"Crystal":{"X":-60,"Y":3,"Z":0},"Players":[{"Player":"Example One Ixion","Team":1,"ClassJobId":19,"HP":10000,"MaxHP":10000,"X":-60,"Y":3,"Z":0,"Heading":1.57,"StatusTimestamp":"2026-10-18T00:00:00Z","Statuses":[{"Id":3054,"Remaining":5}]},{"Player":"Example Two Titan","Team":2,"ClassJobId":25,"HP":8000,"MaxHP":10000,"X":-55,"Y":3,"Z":0}]},{"Timestamp":"2026-10-18T00:00:00.1Z","Players":[]}],"Effects":[{"Timestamp":"2026-10-18T00:01:00Z","Sequence":1,"Actor":"场地实体 123","Target":"Example Two Titan","ActionId":28726,"Type":3,"Amount":54000},{"Timestamp":"2026-10-18T00:02:00Z","Sequence":2,"Actor":"Example One Ixion","Target":"Example Two Titan","ActionId":29230,"Type":3,"Amount":18000}],"ReplaySkills":[{"Timestamp":"2026-10-18T00:00:00Z","ActionId":29230,"Actor":"Example One Ixion","TargetPlayer":"Example Two Titan","X":-60,"Y":3,"Z":0,"Heading":1.57}],"MapMechanics":[]},"Progress":[{"Timestamp":"2026-10-18T00:00:00Z","Series":"星极队推进进度","Points":500},{"Timestamp":"2026-10-18T00:01:00Z","Series":"星极队推进进度","Points":600},{"Timestamp":"2026-10-18T00:02:00Z","Series":"星极队推进进度","Points":650},{"Timestamp":"2026-10-18T00:03:00Z","Series":"星极队推进进度","Points":800},{"Timestamp":"2026-10-18T00:04:00Z","Series":"星极队推进进度","Points":1000},{"Timestamp":"2026-10-18T00:00:00Z","Series":"灵极队推进进度","Points":200},{"Timestamp":"2026-10-18T00:01:00Z","Series":"灵极队推进进度","Points":250},{"Timestamp":"2026-10-18T00:02:00Z","Series":"灵极队推进进度","Points":300},{"Timestamp":"2026-10-18T00:03:00Z","Series":"灵极队推进进度","Points":300},{"Timestamp":"2026-10-18T00:04:00Z","Series":"灵极队推进进度","Points":350},{"Timestamp":"2026-10-18T00:00:00Z","Series":"战术水晶位置","Points":100},{"Timestamp":"2026-10-18T00:01:00Z","Series":"战术水晶位置","Points":250},{"Timestamp":"2026-10-18T00:02:00Z","Series":"战术水晶位置","Points":-100},{"Timestamp":"2026-10-18T00:03:00Z","Series":"战术水晶位置","Points":500},{"Timestamp":"2026-10-18T00:04:00Z","Series":"战术水晶位置","Points":900}],"Timeline":[{"Timestamp":"2026-10-18T00:03:00Z","Kind":"减员","Actor":"Example One Ixion","Target":"Example Two Titan","Description":"内部验证事件"}]}
                """);
                await window.RunSmokeChecksAsync(fixture,Path.ChangeExtension(output,"png"));
                await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new {success=true,os=Environment.OSVersion.ToString(),framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,timeZone=TimeZoneInfo.Local.Id,utcOffsetMinutes=TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalMinutes,navigation=window.NavigationCount,edition=CCStats.Core.Distribution.Edition,rankingsEnabled=!CCStats.Core.Distribution.IsChina,import=true,persistence=true,tray=true,closeChoice=true,replay=true,width=window.ActualWidth,height=window.ActualHeight}));
                Shutdown(0);
            } catch(Exception error) {
                await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new {success=false,error=error.ToString()}));Shutdown(1);
            } finally {if(Directory.Exists(temp))Directory.Delete(temp,true);}
        }));
    }
}
