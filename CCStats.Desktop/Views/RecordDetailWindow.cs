using CCStats.Core.Library;
using PvpStats.Analysis;
using PvpStats.Localization;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace CCStats.Desktop.Views;
internal sealed class RecordDetailWindow : Window {
    private sealed record TimedEvent(string Elapsed,DateTime Timestamp,string Kind,string Actor,string Target,string Description);
    private readonly ReplayBundle _bundle;
    private readonly TextBlock _matchHeading=new();
    private readonly TabControl _tabs=new();
    public void RefreshTimeZone() {_matchHeading.Text=_bundle.Match.Map+"　"+_bundle.Match.StartLabel+"　"+_bundle.Match.Result+"　"+_bundle.Match.Duration;RefreshGrids(Content as DependencyObject);}
    private static void RefreshGrids(DependencyObject? root){if(root==null)return;if(root is DataGrid grid)grid.Items.Refresh();foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())RefreshGrids(child);}
    internal async Task VerifyViewsAsync(string screenshot) {
        if(_tabs.Items.Cast<TabItem>().Any(t=>Equals(t.Header,"玩家数据"))||!Equals(((TabItem)_tabs.Items[0]).Header,"玩家战绩"))throw new InvalidOperationException("Coordinate diagnostics still replace the player scoreboard");
        foreach(var label in new[]{"事件时间线","推进曲线","技能分析"}) {
            _tabs.SelectedItem=_tabs.Items.Cast<TabItem>().Single(t=>Equals(t.Header,label));await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Loaded);UpdateLayout();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var file=System.IO.File.Create(System.IO.Path.ChangeExtension(screenshot,label+".png"));encoder.Save(file);
        }
        _tabs.SelectedIndex=0;UpdateLayout();
    }
    public event Action? Changed;
    public RecordDetailWindow(ReplayBundle bundle,RecordLibrary library) {
        WindowCaption.Attach(this);
        _bundle=bundle;Title="CCstats";Width=1180;Height=800;MinWidth=850;MinHeight=600;Background=(Brush)Application.Current.Resources["BackgroundBrush"];
        var root=new DockPanel {Margin=new Thickness(18)};var header=new StackPanel();
        _matchHeading.Foreground=DesktopTheme.Brush("TextBrush");_matchHeading.FontSize=21;_matchHeading.Margin=new Thickness(0,0,0,14);RefreshTimeZone();header.Children.Add(_matchHeading);
        var tools=new WrapPanel();var favorite=new CheckBox {Content="收藏",IsChecked=bundle.Match.Favorite,Foreground=CCStats.Desktop.Views.DesktopTheme.Brush("TextBrush"),Margin=new Thickness(0,8,12,0)};var tags=new TextBox {Text=bundle.Match.Tags,MinWidth=220};tools.Children.Add(favorite);tools.Children.Add(tags);
        Button Button(string title,RoutedEventHandler action) {var b=new Button {Content=title};b.Click+=action;return b;}
        tools.Children.Add(Button("保存标签",(_,_)=>{library.Annotate(bundle.Match.Id,favorite.IsChecked==true,tags.Text);bundle.Match.Tags=tags.Text;bundle.Match.Favorite=favorite.IsChecked==true;Changed?.Invoke();}));
        tools.Children.Add(Button("地图回放",(_,_)=>new ReplayWindow(bundle){Owner=this}.Show()));
        tools.Children.Add(Button("导出 JSON",async (_,_)=>{var save=new SaveFileDialog {Filter="JSON (*.json)|*.json",FileName=bundle.Match.Id+".json"};if(save.ShowDialog(this)==true)try {await ReplayImport.ExportAsync(bundle,save.FileName);}catch(Exception ex){MessageBox.Show(this,ex.Message,"导出失败");}}));
        tools.Children.Add(Button("复制结算表",(_,_)=> {
            try {var csv="玩家,职业,击杀,死亡,助攻,造成伤害,受到伤害,治疗量,推车秒数\n"+string.Join('\n',bundle.Match.Players.Select(p=>string.Join(',',new[]{p.Name,p.Job,p.Kills?.ToString()??"",p.Deaths?.ToString()??"",p.Assists?.ToString()??"",p.Damage?.ToString()??"",p.Taken?.ToString()??"",p.Healing?.ToString()??"",p.CrystalSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture)??""}.Select(s=>"\""+s.Replace("\"","\"\"")+"\""))));Clipboard.SetText(csv);}catch(Exception ex){MessageBox.Show(this,ex.Message,"复制失败");}
        }));
        tools.Children.Add(Button("删除记录",(_,_)=>{if(MessageBox.Show(this,"仅删除资料库中的此记录，原始文件保留。是否继续？","删除记录",MessageBoxButton.YesNo)==MessageBoxResult.Yes){library.Delete(bundle.Match.Id);Changed?.Invoke();Close();}}));
        header.Children.Add(tools);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var tabs=_tabs;void Tab(string title,UIElement content)=>tabs.Items.Add(new TabItem {Header=title,Content=content});
        var playerRows=ReplayPlayerData.Build(bundle);
        var settle=new StackPanel();
        if(!bundle.Match.ScoreboardComplete)settle.Children.Add(new TextBlock {Text="此局尚未读取到完整的结算战绩。",Foreground=DesktopTheme.Brush("MutedBrush"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(10)});settle.Children.Add(TableFactory.Create(bundle.Match.Players,("完整玩家名","Name"),("职业","Job"),("队伍","Team"),("击杀","Kills"),("死亡","Deaths"),("助攻","Assists"),("造成伤害","Damage"),("受到伤害","Taken"),("治疗量","Healing"),("推车秒数","CrystalSeconds")));
        var totals=bundle.Match.Players.GroupBy(p=>p.Team).Select(g=>new {Team=g.Key,Damage=g.All(p=>p.Damage.HasValue)?g.Sum(p=>p.Damage??0):(long?)null,Taken=g.All(p=>p.Taken.HasValue)?g.Sum(p=>p.Taken??0):(long?)null,Healing=g.All(p=>p.Healing.HasValue)?g.Sum(p=>p.Healing??0):(long?)null,Crystal=g.All(p=>p.CrystalSeconds.HasValue)?g.Sum(p=>p.CrystalSeconds??0):(double?)null});
        var teamGrid=TableFactory.Create(totals,("队伍合计","Team"),("伤害","Damage"),("承伤","Taken"),("治疗","Healing"),("推车秒数","Crystal"));teamGrid.Height=150;settle.Children.Add(teamGrid);Tab("玩家战绩",new ScrollViewer {Content=settle});
        if(bundle.Data.CaptureMethod.Length>0) {
            var capture=new StackPanel {Margin=new Thickness(12)};
            capture.Children.Add(new TextBlock {Text=$"时间帧 {bundle.Data.Frames.Count:N0} · 角色位置 {playerRows.Sum(p=>p.PositionSamples):N0} 条 · 动作效果 {bundle.Data.Effects.Count:N0} 条 · 场地事件 {bundle.Data.MapMechanics.Count:N0} 条 · 场景状态 {bundle.Data.ArenaStates.Count:N0} 条",Foreground=CCStats.Desktop.Views.DesktopTheme.Brush("TextBrush"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
            capture.Children.Add(new TextBlock {Text=$"结算窗口快照 {bundle.ResultPanels.Count} 份 · 比赛起点 {bundle.Match.StartUtc.ToUniversalTime():yyyy-MM-dd HH:mm:ss.fff} UTC · 首个位置帧 {(bundle.Data.Frames.FirstOrDefault(f=>f.Players.Count>0)?.Timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fff")??"无")} UTC",Foreground=DesktopTheme.Brush("MutedBrush"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
            foreach(var warning in bundle.Data.CaptureWarnings)capture.Children.Add(new TextBlock {Text=warning.Replace("观"+"测","记录").Replace("采"+"集","记录").Replace("观察","读取"),Foreground=CCStats.Desktop.Views.DesktopTheme.Brush("MutedBrush"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
            capture.Children.Add(TableFactory.Create(bundle.Data.ArenaStates,("记录时间","Timestamp"),("场景资源","LayoutId"),("状态","State"),("标志","Flags")));
            Tab("记录状态",new ScrollViewer {Content=capture});
        }

        var analysis=new StackPanel();var analysisStatus=new TextBlock {Text="点击后才计算防御推算、控制和恢复数据。",Foreground=CCStats.Desktop.Views.DesktopTheme.Brush("MutedBrush"),Margin=new Thickness(10)};var analysisHost=new ContentControl();
        var calculate=Button("计算高级数据",async (s,_)=> {
            var button=(Button)s;button.IsEnabled=false;analysisStatus.Text="正在计算…";
            try {
                var result=await Task.Run(()=>{var estimates=MitigationEstimator.Calculate(bundle.Data);var stats=AdvancedCombatAnalysis.Calculate(bundle.Data,estimates);return (estimates,stats);});
                bool exactSnapshots=bundle.Data.CaptureMethod!="ReadOnlyMemoryPolling";
                var rows=result.stats.Values.Select(p=>new {p.Player,Damage=p.DamageDealt,Taken=p.DamageReceived,Healing=p.HealingDealt,Drain=p.DrainHealing,SelfReturn=p.SelfReturnHealing,GuardDamage=exactSnapshots?(long?)p.GuardedDamage:null,GuardPrevented=p.EstimatedHits>0?(double?)p.EstimatedGuardPrevented:null,GuardHits=exactSnapshots?(int?)p.GuardedHits:null,Controls=p.ControlApplications,ControlSeconds=p.ControlSeconds,BlockedControls=exactSnapshots?(int?)p.ControlResistsUnderGuard:null,Pending=p.PendingHits});
                analysisHost.Content=TableFactory.Create(rows,("玩家","Player"),("造成伤害","Damage"),("受到伤害","Taken"),("治疗量","Healing"),("已识别吸血","Drain"),("自身回返治疗","SelfReturn"),("防御期间承伤","GuardDamage"),("推算防御减伤","GuardPrevented"),("防御命中数","GuardHits"),("控制应用","Controls"),("控制时长","ControlSeconds"),("防御期间抵抗","BlockedControls"),("待定记录","Pending"));
                analysisStatus.Text=(exactSnapshots?"":"事后轮询缺少命中瞬间状态，防御相关数值显示为 —。")+$"已完成；可推算 {result.estimates.Count(e=>e.Supported)} 条，条件不足 {result.estimates.Count(e=>!e.Supported)} 条。记录统计不等同完整结算。";
            } catch(Exception ex){analysisStatus.Text=ex.Message;}finally{button.IsEnabled=true;}
        });calculate.IsEnabled=bundle.Data.Effects.Count>0;
        if(!calculate.IsEnabled)analysisStatus.Text="此记录没有技能效果记录，不能推算伤害、防御减伤或技能控制数据。";
        analysis.Children.Add(calculate);analysis.Children.Add(analysisStatus);analysis.Children.Add(analysisHost);Tab("高级分析",analysis);
        var events=ReplayEventReport.Build(bundle);
        var eventPanel=new DockPanel();var eventFilters=new WrapPanel();var eventKind=new ComboBox {ItemsSource=new[]{"全部事件","技能","地图事件","减员"},SelectedIndex=0,MinWidth=140,Margin=new Thickness(0,0,8,10)};eventFilters.Children.Add(eventKind);eventFilters.Children.Add(new TextBlock {Text="Ctrl + 滚轮缩放 · 拖动平移 · 双击回放；蓝色技能 / 金色场地 / 红色减员",Foreground=DesktopTheme.Brush("MutedBrush"),Margin=new Thickness(8)});DockPanel.SetDock(eventFilters,Dock.Top);eventPanel.Children.Add(eventFilters);var timelineHost=new ContentControl {VerticalContentAlignment=VerticalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};eventPanel.Children.Add(timelineHost);
        void DrawEvents(){var timeline=new EventTimelineView(events.Where(e=>eventKind.SelectedIndex==0||e.Kind==eventKind.SelectedItem?.ToString()),bundle.Match.StartUtc,bundle.Match.DurationSeconds);timeline.SeekRequested+=time=>new ReplayWindow(bundle,(time-bundle.Match.StartUtc).TotalSeconds){Owner=this}.Show();timelineHost.Content=new ScrollViewer {Content=timeline,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};}eventKind.SelectionChanged+=(_,_)=>DrawEvents();DrawEvents();Tab("事件时间线",eventPanel);
        var curve=new StackPanel();var teams=bundle.Progress.Where(p=>(p.Series.StartsWith("星极队")||p.Series.StartsWith("灵极队"))&&!p.Series.Contains("检查点")).ToArray();
        if(teams.Length>0){var lines=new[]{new ChartSeries("星极队",Colors.DeepSkyBlue,teams.Where(p=>p.Series.StartsWith("星极队")).Select(p=>(p.Timestamp,p.Points/10d)).ToArray(),true),new ChartSeries("灵极队",Colors.Coral,teams.Where(p=>p.Series.StartsWith("灵极队")).Select(p=>(p.Timestamp,p.Points/10d)).ToArray(),true)};var chart=new TrendView(lines,bundle.Match.StartUtc,"%",0,100){Height=280};chart.SeekRequested+=time=>new ReplayWindow(bundle,(time-bundle.Match.StartUtc).TotalSeconds){Owner=this}.Show();curve.Children.Add(chart);}
        var checkpoints=bundle.Progress.Where(p=>p.Series.Contains("检查点")).ToArray();
        if(checkpoints.Length>0){var lines=new[]{new ChartSeries("星极队检查点",Colors.DeepSkyBlue,checkpoints.Where(p=>p.Series.StartsWith("星极队")).Select(p=>(p.Timestamp,p.Points/10d)).OrderBy(p=>p.Timestamp).ToArray(),true),new ChartSeries("灵极队检查点",Colors.Coral,checkpoints.Where(p=>p.Series.StartsWith("灵极队")).Select(p=>(p.Timestamp,p.Points/10d)).OrderBy(p=>p.Timestamp).ToArray(),true)};var chart=new TrendView(lines,bundle.Match.StartUtc,"%",0,100){Height=280};chart.SeekRequested+=time=>new ReplayWindow(bundle,(time-bundle.Match.StartUtc).TotalSeconds){Owner=this}.Show();curve.Children.Add(chart);}
        var points=bundle.Progress.Where(p=>p.Series is "水晶位置" or "战术水晶位置").Select(p=>(p.Timestamp,(double)p.Points)).ToArray();
        if(points.Length>0){var chart=new TrendView(new[]{new ChartSeries("战术水晶位置",Colors.Gold,points)},bundle.Match.StartUtc){Height=260};chart.SeekRequested+=time=>new ReplayWindow(bundle,(time-bundle.Match.StartUtc).TotalSeconds){Owner=this}.Show();curve.Children.Add(chart);}if(points.Length==0&&teams.Length==0&&checkpoints.Length==0)curve.Children.Add(new TextBlock {Text="此局尚无推进数据。",Margin=new Thickness(12)});Tab("推进曲线",new ScrollViewer {Content=curve,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var skillPanel=new DockPanel();var skillTools=new WrapPanel();var skillRows=SkillReport.Build(bundle);var actorChoice=new ComboBox {ItemsSource=new[]{"全部来源"}.Concat(skillRows.Select(r=>r.Player).Distinct()).ToArray(),SelectedIndex=0,MinWidth=220,Margin=new Thickness(0,0,8,10)};var kindChoice=new ComboBox {ItemsSource=new[]{"全部类型","玩家","召唤兽","场地机制","状态"},SelectedIndex=0,MinWidth=130,Margin=new Thickness(0,0,8,10)};var skillSearch=new TextBox {Width=200,ToolTip="搜索技能或编号"};skillTools.Children.Add(actorChoice);skillTools.Children.Add(kindChoice);skillTools.Children.Add(skillSearch);DockPanel.SetDock(skillTools,Dock.Top);skillPanel.Children.Add(skillTools);var skillHost=new ContentControl {VerticalContentAlignment=VerticalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};skillPanel.Children.Add(skillHost);
        void DrawSkills(){var rows=skillRows.Where(r=>(actorChoice.SelectedIndex==0||r.Player==actorChoice.SelectedItem?.ToString())&&(kindChoice.SelectedIndex==0||r.Kind==kindChoice.SelectedItem?.ToString())&&(r.Name.Contains(skillSearch.Text,StringComparison.OrdinalIgnoreCase)||r.ActionId.ToString().Contains(skillSearch.Text))).ToArray();var grid=TableFactory.Create(rows,("来源","Player"),("类型","Kind"),("技能 / 状态","Name"),("使用次数","Casts"),("目标数","Targets"),("效果条数","Hits"),("总伤害","Damage"),("每次使用均伤","AverageDamage"),("总治疗","Healing"),("每次使用均疗","AverageHealing"),("状态命中","StatusHits"),("状态抵抗","StatusMisses"));grid.Height=double.NaN;TableFactory.OnRowActivated(grid,item=>{if(item is SkillReportRow row){var hits=bundle.Data.Effects.Where(e=>SkillReport.Source(e.Actor,e.ActionId)==row.Player&&e.ActionId==row.ActionId).Select(e=>new {e.Timestamp,Actor=SkillReport.Source(e.Actor,e.ActionId),e.Target,Type=DetailedAnalysis.IsDamage(e.Type)?"伤害":e.Type==4?"治疗":e.Type is 14 or 15?"状态命中":"状态抵抗",Amount=DetailedAnalysis.IsDamage(e.Type)||e.Type==4?e.Amount.ToString("N0"):ChineseText.StatusName(e.StatusId,$"状态 {e.StatusId}")}).ToArray();var detail=new Window {Title="CCstats",Owner=this,Width=900,Height=580,Background=DesktopTheme.Brush("BackgroundBrush"),Content=hits.Length>0?TableFactory.Create(hits,("时间","Timestamp"),("来源","Actor"),("目标","Target"),("效果类型","Type"),("数值","Amount")):TableFactory.Create(bundle.Data.ReplaySkills.Where(e=>SkillReport.Source(e.Actor,e.ActionId)==row.Player&&e.ActionId==row.ActionId).Select(e=>new {e.Timestamp,Actor=SkillReport.Source(e.Actor,e.ActionId),Target=e.TargetPlayer,Skill=SkillReport.Name(e.ActionId),e.Sequence,Phase=e.Resolved?"已读取效果阶段":"已读取使用阶段"}),("时间","Timestamp"),("来源","Actor"),("目标","Target"),("技能","Skill"),("动作序列","Sequence"),("记录阶段","Phase"))};WindowCaption.Attach(detail);detail.Show();}});skillHost.Content=grid;}actorChoice.SelectionChanged+=(_,_)=>DrawSkills();kindChoice.SelectionChanged+=(_,_)=>DrawSkills();skillSearch.TextChanged+=(_,_)=>DrawSkills();DrawSkills();Tab("技能分析",skillPanel);
        root.Children.Add(tabs);Content=root;
    }
}
