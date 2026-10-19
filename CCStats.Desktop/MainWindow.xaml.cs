using CCStats.Core;
using CCStats.Core.Import;
using CCStats.Core.Library;
using CCStats.Desktop.Capture;
using CCStats.Desktop.Views;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CCStats.Desktop;
public partial class MainWindow : Window {
    private static readonly string[] Pages={"战绩查询","数据概览","段位趋势","榜单","数据分析","个人中心","回放文件","设置","关于"};
    private readonly IndependentCapture _capture=new(()=>ExternalGameReader.FindGameProcess() is var pid && pid>0?new ExternalGameReader(pid):null);
    private readonly DispatcherTimer _captureTimer=new() {Interval=TimeSpan.FromMilliseconds(200)};
    private readonly DispatcherTimer _processTimer=new() {Interval=TimeSpan.FromSeconds(2)};
    private readonly string _settingsPath,_directory;
    private readonly DesktopPreferences _preferences;
    private readonly CancellationTokenSource _operations=new();
    private readonly RecordLibrary _library;
    private readonly List<Button> _navigation=new();
    private List<MatchRecord> _records=new();
    private ReplayBundle? _bundle;
    private int _page;
    private bool _busy,_exiting,_closed,_smoke;
    private System.Windows.Forms.NotifyIcon? _tray;
    private readonly List<Window> _hiddenOwned=new();
    private readonly Queue<string> _pendingObservations=new();
    private CCStats.Core.Capture.PlayerPvpProfile? _lastStoredProfile;
    private string? _smokeCloseChoice;
    private string? _lastSaved;
    private string _shownLogin="";
    private string _search="",_map="全部",_result="全部";
    private bool _favorites,_observationsView;
    private string _mode="全部",_rank="全部";
    private DateTime? _fromDate,_untilDate;
    public MainWindow(string? settingsPath=null) {
        InitializeComponent();_smoke=settingsPath!=null;
        VersionLabel.Text="v"+ProductVersion;
        WindowCaption.Attach(this);
        Icon=new BitmapImage(new Uri("pack://application:,,,/Assets/icon.png"));
        _settingsPath=settingsPath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CCStatsDesktop","settings.json");
        _directory=Path.GetDirectoryName(_settingsPath)!;_preferences=DesktopPreferences.Load(_settingsPath);
        // Retired manual settings must not disable the always-on background workflow.
        _preferences.LocalPlayer="";_preferences.AutoDetectGame=true;_preferences.AutoRecord=true;
        _library=new RecordLibrary(_directory);_records=_library.All();ApplyTheme();
        if(!_smoke&&Directory.Exists(Path.Combine(_directory,"observations"))) {
            var known=_records.Select(r=>r.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var path in Directory.EnumerateFiles(Path.Combine(_directory,"observations"),"*.json"))if(!known.Contains(path))_pendingObservations.Enqueue(path);
        }
        for(int i=0;i<Pages.Length;i++) {var index=i;var button=new Button {Content=Pages[i],HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,5),Padding=new Thickness(12,8,12,8)};button.Click+=(_,_)=>ShowPage(index);Navigation.Children.Add(button);_navigation.Add(button);}
        if(!_smoke)SetupTray();
        _capture.Configure(true);
        _captureTimer.Tick+=async (_,_)=> {
            UpdateCaptureStatus();UpdateRankings();
            if(!_exiting&&_shownLogin!=_capture.State.LocalPlayer){_shownLogin=_capture.State.LocalPlayer;if(_page is 4 or 5)ShowPage(_page);}
            UpdateLoggedPlayer();
            if(!_exiting&&_capture.State.Profile is {} profile&&profile!=_lastStoredProfile) {
                try {_library.SaveProfile(profile);_lastStoredProfile=profile;}catch(Exception error) when(error is IOException or UnauthorizedAccessException or LiteDB.LiteException) {Notify("profile","个人资料暂未保存："+error.Message);}
            }
            while(_capture.TryDequeueCompleted(out var saved))if(saved!=_lastSaved) {_lastSaved=saved;_pendingObservations.Enqueue(saved);}
            if(!_busy&&_pendingObservations.TryDequeue(out var pending))await ImportAsync(pending,false);
        };
        _captureTimer.Start();
        _processTimer.Tick+=(_,_)=>{if(!_smoke&&!_exiting)_capture.Poll(Path.Combine(_directory,"observations"));};
        _processTimer.Start();Closing+=OnWindowClosing;
        Loaded+=async (_,_)=>{if(!_smoke)await StartRankingsAsync();};
        ShowPage(0);
    }
    private static string ProductVersion=>typeof(MainWindow).Assembly.GetName().Version?.ToString(3)??"0.1.0";
    private TextBlock Text(string value,bool muted=false,double size=14) {
        var text=new TextBlock {Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};
        text.SetResourceReference(TextBlock.ForegroundProperty,muted?"MutedBrush":"TextBrush");return text;
    }
    private void Card(string title,params UIElement[] children) {
        var content=new StackPanel();content.Children.Add(Text(title,false,18));foreach(var child in children)content.Children.Add(child);
        var border=new Border {CornerRadius=new CornerRadius(12),Padding=new Thickness(18),Margin=new Thickness(0,0,0,14),Child=content};border.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");PageContent.Children.Add(border);
    }
    private Button ActionButton(string title,RoutedEventHandler handler) {var b=new Button {Content=title,HorizontalAlignment=HorizontalAlignment.Left};b.Click+=handler;return b;}
    private bool SavePreferences() {try {_preferences.Save(_settingsPath);return true;}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {Notify("settings","设置未能保存："+ex.Message);return false;}}
    private async void OpenFile(object sender,RoutedEventArgs e) {
        var dialog=new OpenFileDialog {Filter="CCStats 数据 (*.json;*.db)|*.json;*.db|回放 JSON (*.json)|*.json|历史数据库 (*.db)|*.db",Title="打开战绩或回放数据",Multiselect=false};
        if(dialog.ShowDialog(this)==true)await ImportAsync(dialog.FileName);
    }
    public async Task ImportAsync(string path,bool navigate=true) {
        if(_busy||_exiting)return;_busy=true;var scope=Path.GetExtension(path).Equals(".db",StringComparison.OrdinalIgnoreCase)?"records":"files";if(navigate){ShowPage(scope=="records"?0:6);Notify(scope,"正在读取数据…");}
        try {
            if(Path.GetExtension(path).Equals(".db",StringComparison.OrdinalIgnoreCase)) {
                var imported=await Task.Run(()=>DatabaseImport.Read(path,_library,token:_operations.Token));if(navigate)Notify(scope,$"已读取 {imported.Imported} 场，跳过 {imported.Skipped} 条不完整记录。");
            } else {
                var bundle=await Task.Run(()=>ReplayImport.ReadAsync(path,_operations.Token));await Task.Run(()=>_library.Save(bundle));_bundle=bundle;if(navigate)Notify(scope,"记录已加入本机资料库，原文件保持不变。");
            }
            _preferences.Remember(path);SavePreferences();_records=await Task.Run(_library.All);
            if(navigate)ShowPage(scope=="records"?0:6);
        } catch(Exception ex) {Notify(scope,"读取失败："+ex.Message);}
        finally {_busy=false;}
    }
    private void ShowPage(int page) {
        _page=page;_noticeViews.Clear();PageContent.Children.Clear();PageScroll.ScrollToTop();
        for(int i=0;i<_navigation.Count;i++){_navigation[i].Tag=i==page?"Selected":null;}
        PageTitle.Text=Pages[page];
        switch(page) {
            case 0:ShowMatches();break;
            case 1:ShowOverview();break;
            case 2:ShowRankTrend();break;
            case 3:ShowRankings();break;
            case 4:ShowAnalysis();break;
            case 5:ShowProfile();if(LocalIdentity.Length>0)ShowRecords();break;
            case 6:ShowReplay();break;
            case 7:ShowSettings();break;
            case 8:
                Card("CCStats Desktop",Text("水晶冲突战绩记录与复盘工具",false,20),Text("版本 "+ProductVersion,true),Text("作者：HanaSi0"),Text("自动识别游戏进程，在后台记录对局数据，提供战绩管理、统计分析与二维、三维回放。",true));
                Card("数据与隐私",Text("游戏数据以只读方式访问，记录保存在本机。软件不会修改游戏文件或游戏内状态。",true),Text("部分数据仍在验证中；缺少结算的记录不计入比赛场次和胜率。",true));
                Card("开源许可",Text("基于 SaMo / wrath16 的 PvPStats 开源项目开发，遵循 MIT 许可。完整许可与第三方声明随软件提供。",true));break;
        }
        if(!_smoke)UiMotion.Enter(PageContent);
    }
    private void Window_DragOver(object sender,DragEventArgs e) {e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
    private async void Window_Drop(object sender,DragEventArgs e) {if(e.Data.GetData(DataFormats.FileDrop) is string[] {Length:1} paths)await ImportAsync(paths[0]);else {ShowPage(6);Notify("files","请一次拖入一个数据文件。");}}
    private void SetupTray() {
        _tray=new System.Windows.Forms.NotifyIcon {Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),Text="CCStats · 后台记录",Visible=true};
        var menu=new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开主窗口",null,(_,_)=>Dispatcher.Invoke(RestoreFromTray));
        menu.Items.Add("退出软件",null,async (_,_)=>await Dispatcher.InvokeAsync(ExitAsync).Task.Unwrap());
        _tray.ContextMenuStrip=menu;_tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(RestoreFromTray);
    }
    private void MinimizeToTray() {
        _hiddenOwned.Clear();
        foreach(var window in Application.Current.Windows.Cast<Window>().Where(w=>w!=this&&w.IsVisible&&(w is ReplayWindow||w is RecordDetailWindow)).ToArray()) {_hiddenOwned.Add(window);window.Hide();}
        ShowInTaskbar=false;Hide();
    }
    private void RestoreFromTray() {
        Show();ShowInTaskbar=true;WindowState=WindowState.Normal;
        foreach(var window in _hiddenOwned)try {window.Show();}catch(InvalidOperationException) { }
        _hiddenOwned.Clear();Activate();
    }
    private void OnWindowClosing(object? sender,System.ComponentModel.CancelEventArgs e) {
        if(_closed)return;e.Cancel=true;if(_exiting)return;
        var choice=_preferences.CloseBehavior;
        if(choice is not ("Tray" or "Exit")) {
            var dialog=new CloseChoiceDialog(this);if(_smoke&&_smokeCloseChoice is {} forced)dialog.Loaded+=(_,_)=>dialog.SelectForCheck(forced,true);if(dialog.ShowDialog()!=true)return;choice=dialog.Choice;
            if(dialog.Remember) {_preferences.CloseBehavior=choice;SavePreferences();}
        }
        if(choice=="Tray")_ = Dispatcher.BeginInvoke(new Action(MinimizeToTray));else _=ExitAsync();
    }
    private async Task ExitAsync() {
        if(_exiting)return;_exiting=true;_operations.Cancel();_processTimer.Stop();IsEnabled=false;
        await _capture.StopAsync();
        while(_busy)await Task.Delay(50);
        _captureTimer.Stop();if(_rankings!=null)foreach(var service in _rankings)service.Dispose();_library.Dispose();_tray?.Dispose();_closed=true;
        _=Dispatcher.BeginInvoke(new Action(Close));
    }
    internal async Task RunSmokeChecksAsync(string fixture,string screenshot) {
        await StartupSplash.VerifyAsync();
        await VerifyFeatureFeedbackAsync();
        if(_navigation.Count!=9||VersionLabel.Text!="v"+ProductVersion||Pages.Contains("自动记录"))throw new InvalidOperationException("Simplified navigation or product version failed");
        ShowPage(7);
        if(PageContent.Children.OfType<Border>().Select(b=>b.Child).OfType<StackPanel>().SelectMany(p=>p.Children.OfType<TextBox>()).Any(t=>t.ToolTip?.ToString()?.Contains("统计默认本人")==true))throw new InvalidOperationException("Manual default character setting remains");
        if(!_preferences.AutoDetectGame||!_preferences.AutoRecord||_preferences.LocalPlayer.Length!=0)throw new InvalidOperationException("Automatic capture defaults failed");
        for(int i=0;i<_navigation.Count;i++) {_navigation[i].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(_page!=i||PageContent.Children.Count==0)throw new InvalidOperationException("Navigation failed: "+i);}
        ShowPage(4);var analysisTabs=PageContent.Children.OfType<TabControl>().Single();
        if(analysisTabs.Items.Count!=2)throw new InvalidOperationException("Combined analysis tabs missing");analysisTabs.SelectedIndex=1;
        ShowPage(7);if(PageContent.Children.OfType<Expander>().Single().IsExpanded)throw new InvalidOperationException("Custom colours must start collapsed");
        await ImportAsync(fixture);if(_bundle?.Match.Players.Count!=2||_bundle.Data.Frames.Count!=2)throw new InvalidOperationException("Record import failed: "+_notices.GetValueOrDefault("files"));
        if(_library.All().Count!=1||DesktopPreferences.Load(_settingsPath).RecentFiles.Count!=1)throw new InvalidOperationException("Persistence failed");
        for(int i=0;i<_navigation.Count;i++)ShowPage(i);
        SetupTray();
        foreach(var choice in new[]{"Tray","Exit"}) {
            var dialog=new CloseChoiceDialog(this);dialog.Loaded+=(_,_)=>dialog.SelectForCheck(choice,true);
            if(dialog.ShowDialog()!=true||dialog.Choice!=choice||!dialog.Remember)throw new InvalidOperationException("Close choice dialog failed");
        }
        MinimizeToTray();if(IsVisible||ShowInTaskbar)throw new InvalidOperationException("Background hide failed");((System.Windows.Forms.ToolStripMenuItem)_tray!.ContextMenuStrip!.Items[0]).PerformClick();if(!IsVisible||!ShowInTaskbar)throw new InvalidOperationException("Tray restore failed");
        _preferences.CloseBehavior="Ask";_smokeCloseChoice="Tray";Close();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        if(IsVisible||_preferences.CloseBehavior!="Tray"||DesktopPreferences.Load(_settingsPath).CloseBehavior!="Tray")throw new InvalidOperationException("Native close dialog, remember and tray transition failed");
        _smokeCloseChoice=null;RestoreFromTray();
        TableFactory.CheckSorting();
        var originalTheme=_preferences.Theme;
        foreach(var theme in DesktopTheme.Names) {DesktopTheme.Apply(theme,new Dictionary<string,string>());WindowCaption.Verify(this);if(DesktopTheme.Keys.Any(k=>Application.Current.Resources[k] is not SolidColorBrush))throw new InvalidOperationException("Incomplete theme: "+theme);}
        DesktopTheme.Apply("深海蓝",new Dictionary<string,string>{{"TextBrush","#123456"}});DesktopTheme.Apply("深海蓝",new Dictionary<string,string>());
        if(((SolidColorBrush)DesktopTheme.Brush("TextBrush")).Color!=Color.FromRgb(236,242,255))throw new InvalidOperationException("Theme reset leaves stale text colours");
        DesktopTheme.Apply(originalTheme,_preferences.CustomColors);
        _observationsView=true;ShowPage(0);
        var advanced=PageContent.Children.OfType<Expander>().Single();
        advanced.IsExpanded=true;UpdateLayout();((StackPanel)advanced.Content).Children.OfType<DateRangeFilter>().Single().Verify();
        advanced.IsExpanded=false;UpdateLayout();
        PageScroll.ScrollToVerticalOffset(Math.Min(40,PageScroll.ScrollableHeight));await Dispatcher.Yield(DispatcherPriority.Loaded);
        if(PageScroll.ScrollableHeight>1&&PageScroll.VerticalOffset<1)throw new InvalidOperationException("Themed scrolling failed");
        PageScroll.ScrollToTop();
        UpdateLayout();await TableFactory.VerifySelectionAsync(PageContent.Children.OfType<DataGrid>().Single());
        await MeshVisuals.VerifyPaletteAsync(this);
        var replay=new ReplayWindow(_bundle){Owner=this};replay.Show();await replay.VerifyAsync(Path.ChangeExtension(screenshot,"replay.png"));replay.Close();
        var detail=new RecordDetailWindow(_bundle,_library){Owner=this};detail.Show();detail.Close();
        ShowPage(0);UpdateLayout();var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(screenshot))encoder.Save(file);
        _preferences.CloseBehavior="Exit";_closed=true;_processTimer.Stop();_captureTimer.Stop();_tray?.Dispose();_library.Dispose();
    }
}
