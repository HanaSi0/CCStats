using CCStats.Core;
using CCStats.Desktop.Capture;
using System.Windows.Threading;
using CCStats.Core.Import;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CCStats.Desktop;

public partial class MainWindow : Window {
    private readonly IndependentCapture _capture = new();
    private readonly DispatcherTimer _captureTimer = new() { Interval=TimeSpan.FromMilliseconds(200) };
    private TextBlock? _captureStatus;
    private ListBox? _capturePlayers;
    private bool _closingComplete;
    private readonly string _settingsPath;
    private readonly DesktopPreferences _preferences;
    private readonly List<Button> _navigation = new();
    private ReplaySummary? _replay;
    private int _page;
    private bool _busy;
    public MainWindow(string? settingsPath = null) {
        InitializeComponent();
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/icon.png"));
        _settingsPath = settingsPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CCStatsDesktop","settings.json");
        _preferences = DesktopPreferences.Load(_settingsPath);
        foreach(var (label,index) in new[]{"开始","回放文件","设置","关于","独立采集"}.Select((label,index)=>(label,index))) {
            var button = new Button { Content=label, HorizontalContentAlignment=HorizontalAlignment.Left, Margin=new Thickness(0,0,0,8) };
            button.Click += (_,_)=>ShowPage(index);
            Navigation.Children.Add(button); _navigation.Add(button);
        }
        _captureTimer.Tick+=(_,_)=>UpdateCaptureStatus();_captureTimer.Start();
        Closing+=async (_,e)=> {
            if(_closingComplete)return;
            e.Cancel=true;IsEnabled=false;
            await _capture.StopAsync();_captureTimer.Stop();_closingComplete=true;_ = Dispatcher.BeginInvoke(new Action(Close));
        };
        ShowPage(0);
    }
    private static TextBlock Text(string value, bool muted=false, double size=14) => new() {
        Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10),
        Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(muted?"#A6B6D0":"#ECF2FF"))
    };
    private void Card(string title, params UIElement[] children) {
        var content=new StackPanel();content.Children.Add(Text(title,false,18));
        foreach(var child in children)content.Children.Add(child);
        PageContent.Children.Add(new Border {Background=(Brush)FindResource("SurfaceBrush"),CornerRadius=new CornerRadius(12),Padding=new Thickness(20),Margin=new Thickness(0,0,0,16),Child=content});
    }
    private Button ActionButton(string title, RoutedEventHandler handler) {
        var button=new Button {Content=title,HorizontalAlignment=HorizontalAlignment.Left};button.Click+=handler;return button;
    }
    private async void OpenFile(object sender,RoutedEventArgs e) {
        var dialog=new OpenFileDialog {Filter="CCStats 回放 JSON (*.json)|*.json",Title="打开插件导出的回放记录",Multiselect=false};
        if(dialog.ShowDialog(this)==true)await ImportAsync(dialog.FileName);
    }
    public async Task ImportAsync(string path) {
        if(_busy)return;
        _busy=true;StatusText.Text="正在读取回放…";
        try {
            var replay=await ReplayFileReader.ReadAsync(path);
            _preferences.Remember(replay.FilePath);
            _replay=replay;
            try { _preferences.Save(_settingsPath);StatusText.Text="已读取回放；原始文件保持不变。"; }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException) { StatusText.Text="回放已读取，但最近文件列表未能保存："+error.Message; }
            ShowPage(1);
        } catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or FormatException or ArgumentException) {
            StatusText.Text="无法导入："+error.Message;
        } finally { _busy=false; }
    }
    private void ShowPage(int page) {
        _page=page;PageContent.Children.Clear();
        for(int i=0;i<_navigation.Count;i++) _navigation[i].Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(i==page?"#245F9F":"#29395D"));
        PageTitle.Text=new[]{"开始","回放文件","设置","关于","独立采集"}[page];
        PageSubtitle.Text=new[]{"水晶冲突桌面工作台","读取插件已导出的观测记录","桌面版数据与插件配置分别保存","独立桌面版 · 构建预览","无需 Dalamud · 客户端只读观测（试验）"}[page];
        switch(page) {
            case 0:
                Card("从一份回放开始",Text("打开插件导出的 JSON，查看地图、玩家和观测记录概况。也可以把文件直接拖入窗口。",true),ActionButton("打开回放文件",OpenFile));
                Card("当前版本",Text("窗口框架、回放文件检查、记录概况与最近文件列表已可使用。"),Text("独立只读观测采集可在侧栏试验。战绩数据库、官方榜单页面和 2D / 3D 回放播放将在后续接入。",true));
                ShowRecent();break;
            case 1:
                PageContent.Children.Add(ActionButton("打开其他回放",OpenFile));
                if(_replay is not {} replay) {
                    Card("尚未导入回放",Text("在插件地图回放中点击“导出观测记录”，选择导出的 JSON 文件。此版本暂不读取 data.db 或 ZIP。",true));ShowRecent();break;
                }
                Card(replay.MapName,Text($"{(replay.IsObservation?"独立观测片段":"比赛")}编号  {replay.MatchId}",true),Text($"开始时间  {replay.StartUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}　·　时长  {replay.DurationLabel}"),Text($"走位帧  {replay.Frames:N0}　·　效果记录  {replay.Effects:N0}　·　技能事件  {replay.Skills:N0}　·　地图事件  {replay.MapEvents:N0}"));
                Card($"已记录玩家 · {replay.Players.Count}",Text(replay.Players.Count>0?string.Join(Environment.NewLine,replay.Players):"此记录没有玩家走位观测。",replay.Players.Count==0));
                Card("来源",Text(replay.FilePath,true),Text("当前显示文件内的记录概况，尚未提供地图播放；缺失的历史观测不会补造。",true));break;
            case 2:
                Card("本机数据",Text(Path.GetDirectoryName(_settingsPath)!,true),Text("仅保存最近打开的文件路径，不复制回放，不修改插件战绩库。",true),ActionButton("打开数据文件夹",(_,_)=> {
                    try {Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);Process.Start(new ProcessStartInfo {FileName=Path.GetDirectoryName(_settingsPath)!,UseShellExecute=true});}
                    catch(Exception ex) {StatusText.Text="无法打开文件夹："+ex.Message;}
                }),ActionButton("清空最近文件",(_,_)=> {
                    try {_preferences.RecentFiles.Clear();_preferences.Save(_settingsPath);StatusText.Text="最近文件列表已清空；原文件未删除。";ShowPage(2);}
                    catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {StatusText.Text="保存失败："+ex.Message;}
                }));break;
            case 3:
                Card("CCStats Desktop 0.1.0",Text("作者：HanaSi0"),Text("独立 Windows x64 程序，内置 .NET 运行时。无需安装 Dalamud 或单独安装 .NET。",true),Text("基于 SaMo / wrath16 的 PvPStats 开源代码继续开发，遵循 MIT 许可。许可证随安装包提供。",true));
                Card("采集与回放",Text("共用分析核心已接入独立工程；独立采集目前仅提供玩家观测，尚无结算和技能效果包，不等于完整比赛记录。现有 Dalamud 插件继续单独维护。",true));break;
            case 4:
                var actions=new WrapPanel();
                actions.Children.Add(ActionButton("连接游戏",(_,_)=>_capture.Connect(Path.Combine(Path.GetDirectoryName(_settingsPath)!,"observations"))));
                actions.Children.Add(ActionButton("开始记录",(_,_)=>_capture.Record(true)));
                actions.Children.Add(ActionButton("结束记录",(_,_)=>_capture.Record(false)));
                actions.Children.Add(ActionButton("断开",async (_,_)=>await _capture.StopAsync()));
                _captureStatus=Text("",true);_capturePlayers=new ListBox {MaxHeight=300};
                Card("独立观测",actions,_captureStatus,_capturePlayers);
                Card("当前采集范围",Text("无需 Dalamud。实验性读取已加载玩家的姓名、世界编号、职业、位置、朝向、血量与状态。",true),Text("客户端布局尚未经本机游戏验证；签名或字段校验失败会停止。队伍、水晶、结算、技能效果包尚未接入，不能由血量变化推造伤害统计。",true),Text("记录以观测片段保存为 JSON，不冒充已完成比赛；切图或达到 64 MiB 会收尾停止。",true));
                UpdateCaptureStatus();break;
        }
    }
    private void UpdateCaptureStatus() {
        if(_page!=4||_captureStatus==null||_capturePlayers==null)return;
        var state=_capture.State;
        _captureStatus.Text=$"{state.Message}\n区域 {state.Territory} · {state.Players?.Length??0} 名已加载玩家 · {state.Frames:N0} 帧"+(state.SavedPath==null?"":"\n已保存："+state.SavedPath);
        var players=state.Players??Array.Empty<string>();
        if(!_capturePlayers.Items.Cast<string>().SequenceEqual(players))_capturePlayers.ItemsSource=players;
    }
    private void ShowRecent() {
        var list=new StackPanel();
        foreach(var path in _preferences.RecentFiles) {
            var item=ActionButton(Path.GetFileName(path),async (_,_)=>await ImportAsync(path));item.ToolTip=path;list.Children.Add(item);
        }
        if(list.Children.Count==0)list.Children.Add(Text("还没有最近打开的文件。",true));
        Card("最近文件",list);
    }
    private void Window_DragOver(object sender,DragEventArgs e) {e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
    private async void Window_Drop(object sender,DragEventArgs e) {
        if(e.Data.GetData(DataFormats.FileDrop) is string[] {Length:1} paths)await ImportAsync(paths[0]);
        else StatusText.Text="请一次拖入一个回放 JSON 文件。";
    }
    internal async Task RunSmokeChecksAsync(string fixture,string screenshot) {
        for(int i=0;i<_navigation.Count;i++) {
            _navigation[i].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(_page!=i || PageContent.Children.Count==0)throw new InvalidOperationException("Navigation failed: "+i);
        }
        await ImportAsync(fixture);
        if(_replay?.Players.Count!=2 || _replay.Frames!=2 || _page!=1)throw new InvalidOperationException("Replay UI import failed: "+StatusText.Text);
        var saved=DesktopPreferences.Load(_settingsPath);
        if(saved.RecentFiles.Count!=1)throw new InvalidOperationException("Recent file persistence failed");
        UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output=File.Create(screenshot);encoder.Save(output);
    }
}
