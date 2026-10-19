using CCStats.Core.Library;
using CCStats.Desktop.Views;
using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace CCStats.Desktop;
public partial class MainWindow {
    private string LocalIdentity=>_preferences.Links.Canonical(_capture.State.LocalPlayer);
    private List<MatchRecord> Filtered()=>RecordStatistics.Filter(_records,_search,_map,_result,_favorites,_mode).Where(r=>(_rank=="全部"||r.Rank==_rank)&&(!_fromDate.HasValue||r.StartUtc.ToLocalTime().Date>=_fromDate.Value.Date)&&(!_untilDate.HasValue||r.StartUtc.ToLocalTime().Date<=_untilDate.Value.Date)).ToList();
    private bool _advancedExpanded;
    private void ShowMatches() {
        var advancedContent=new StackPanel();
        var advanced=new Expander {Header="高级",IsExpanded=_advancedExpanded,Content=advancedContent,Margin=new Thickness(0,0,0,12)};
        advanced.Expanded+=(_,_)=>_advancedExpanded=true;advanced.Collapsed+=(_,_)=>_advancedExpanded=false;
        PageContent.Children.Add(advanced);PageContent.Children.Add(Notice("records"));
        var tools=new WrapPanel();tools.Children.Add(ActionButton("读取文件",OpenFile));
        tools.Children.Add(ActionButton("导出 CSV",async (_,_)=> {
            var save=new SaveFileDialog {Filter="CSV (*.csv)|*.csv",FileName="CCStats-战绩.csv"};if(save.ShowDialog(this)!=true)return;
            string Csv(string s)=>"\""+s.Replace("\"","\"\"")+"\"";
            try {var text="时间,地图,类型,结果,时长,标签\n"+string.Join('\n',Filtered().Where(r=>!_observationsView||r.FrameCount>0).Select(r=>string.Join(',',new[]{r.StartLabel,r.Map,r.Mode,r.Result,r.Duration,r.Tags}.Select(Csv))));await File.WriteAllTextAsync(save.FileName,text,new UTF8Encoding(true));Notify("records","已导出当前筛选的全部记录。");}catch(Exception ex) {Notify("records",ex.Message);}
        }));
        tools.Children.Add(ActionButton("刷新",async (_,_)=>{_records=await Task.Run(_library.All);ShowPage(0);}));advancedContent.Children.Add(tools);
        var filters=new WrapPanel();var search=new TextBox {Text=_search,Width=250,ToolTip="搜索玩家、职业、地图、标签；空格同时匹配，减号排除"};
        filters.Children.Add(search);
        ComboBox Choice(string[] values,string selected,Action<string> set) {var c=new ComboBox {ItemsSource=values,SelectedItem=selected,MinWidth=115,Margin=new Thickness(0,0,8,8)};c.SelectionChanged+=(_,_)=>{if(c.SelectedItem is string s)set(s);};return c;}
        filters.Children.Add(Choice(new[]{"全部"}.Concat(_records.Select(r=>r.Map).Distinct()).ToArray(),_map,s=>_map=s));
        filters.Children.Add(Choice(new[]{"全部","胜利","失败","未记录"},_result,s=>_result=s));
        filters.Children.Add(Choice(new[]{"全部","练习赛","段位赛","自定赛"},_mode,s=>_mode=s));
        filters.Children.Add(Choice(new[]{"全部"}.Concat(_records.Select(r=>r.Rank).Where(r=>!string.IsNullOrEmpty(r)).Distinct()).ToArray(),_rank,s=>_rank=s));
        var dateRange=new DateRangeFilter(_fromDate,_untilDate);dateRange.Changed+=(from,until)=>{_fromDate=from;_untilDate=until;};
        var favorite=new CheckBox {Content="仅收藏",IsChecked=_favorites,Foreground=CCStats.Desktop.Views.DesktopTheme.Brush("TextBrush"),Margin=new Thickness(4,5,8,0)};filters.Children.Add(favorite);
        filters.Children.Add(ActionButton("应用筛选",(_,_)=>{_search=search.Text;_favorites=favorite.IsChecked==true;ShowPage(0);Notify("records","筛选已应用。");}));advancedContent.Children.Add(filters);advancedContent.Children.Add(dateRange);
        var scopes=new WrapPanel();
        foreach(var (label,scope) in new[]{("比赛记录",false),("回放记录",true)}) {
            var count=_records.Count(r=>!scope||r.FrameCount>0);var button=ActionButton($"{label} ({count:N0})",(_,_)=>{_observationsView=scope;ShowPage(0);});
            button.Tag=_observationsView==scope?"Selected":null;scopes.Children.Add(button);
        }
        PageContent.Children.Add(scopes);
        var rows=Filtered().Where(r=>!_observationsView||r.FrameCount>0).ToList();Card($"{rows.Count:N0} 条"+(_observationsView?"回放记录":"比赛记录"),Text("双击查看玩家、事件和回放。尚未取得结算的对局也会显示在此处，缺失数值显示为 —。",true));
        var grid=TableFactory.Create(rows,("时间","StartLabel"),("地图","Map"),("记录类型","Kind"),("比赛类型","Mode"),("结果","Result"),("时长","Duration"),("收藏","Favorite"),("标签","Tags"));
        TableFactory.OnRowActivated(grid,async row=>{if(row is MatchRecord record)await OpenRecordAsync(record);});PageContent.Children.Add(grid);
        if(_observationsView)Card("记录状态",Text("此处显示已有位置帧的记录。未确认结算的记录仍可回放，不计入有效比赛场次与胜率。",true));
        if(rows.Count==0)Card("暂无记录",Text("软件会自动识别正在运行的游戏，也可以读取已有 JSON 或历史数据文件。",true));
    }
    private async Task OpenRecordAsync(MatchRecord record) {
        try {_bundle=await Task.Run(()=>_library.Load(record.Id));_records=await Task.Run(_library.All);if(_page==0)ShowPage(0);var window=new RecordDetailWindow(_bundle,_library);window.Owner=this;window.Changed+=async ()=>{_records=await Task.Run(_library.All);if(_page==0)ShowPage(0);};window.Show();}
        catch(Exception ex) {Notify("records","无法打开记录："+ex.Message);}
    }
    private void ShowOverview() {
        var rows=Filtered();var matches=rows.Where(r=>!r.Observation&&r.Completed&&!r.Quarantined).ToArray();int wins=matches.Count(r=>r.Result=="胜利"),losses=matches.Count(r=>r.Result=="失败");
        Card("总览",Text($"{matches.Length:N0} 场已结算比赛 · {rows.Count(r=>r.Observation):N0} 份回放"),Text($"有效胜率　{RecordStatistics.Rate(wins,losses)}"),Text($"平均时长　{(matches.Length==0?"—":TimeSpan.FromSeconds(matches.Average(r=>r.DurationSeconds)).ToString(@"mm\:ss"))}",true));
        var groups=matches.GroupBy(r=>r.Map).Select(g=>new {Map=g.Key,Matches=g.Count(),Wins=g.Count(r=>r.Result=="胜利"),Losses=g.Count(r=>r.Result=="失败"),Rate=RecordStatistics.Rate(g.Count(r=>r.Result=="胜利"),g.Count(r=>r.Result=="失败"))});
        PageContent.Children.Add(TableFactory.Create(groups,("地图","Map"),("场数","Matches"),("胜场","Wins"),("败场","Losses"),("胜率与样本","Rate")));
    }
    private int _analysisTab;
    private void ShowAnalysis() {
        var tabs=new TabControl();var jobs=new StackPanel();var players=new StackPanel();
        ShowAggregate(true,jobs);ShowAggregate(false,players);
        tabs.Items.Add(new TabItem {Header="职业分析",Content=jobs});tabs.Items.Add(new TabItem {Header="玩家分析",Content=players});
        tabs.SelectedIndex=_analysisTab;
        tabs.SelectionChanged+=(_,e)=>{if(e.Source==tabs)_analysisTab=tabs.SelectedIndex;};PageContent.Children.Add(tabs);
    }
    private void ShowAggregate(bool jobs,Panel host) {
        var players=_records.Select(r=>r.LocalPlayer).Concat(_records.SelectMany(r=>r.Players.Select(p=>p.Name))).Concat(new[]{LocalIdentity}).Where(n=>n.Length>0).Select(_preferences.Links.Canonical).Distinct().Order().ToArray();
        var selected=LocalIdentity.Length>0?LocalIdentity:null;
        var choice=new ComboBox {ItemsSource=new[]{"全部玩家"}.Concat(players),SelectedItem=selected??"全部玩家",MinWidth=280,Margin=new Thickness(0,0,0,12)};
        var metric=new ComboBox {ItemsSource=new[]{"场均","每分钟","队伍贡献中位数"},SelectedIndex=0,MinWidth=200,Margin=new Thickness(0,0,0,12)};
        var content=new ContentControl();void Render() {
            var player=choice.SelectedItem as string;var rows=RecordStatistics.Aggregate(Filtered(),jobs?"职业":"玩家",player=="全部玩家"?null:player,_preferences.Links.Canonical,metric.SelectedItem as string??"场均");
            content.Content=TableFactory.Create(rows,(jobs?"职业":"玩家","Name"),("出场","Appearances"),("胜场","Wins"),("败场","Losses"),("有效胜率","WinRate"),("击杀","Kills"),("死亡","Deaths"),("助攻","Assists"),("伤害","Damage"),("承伤","Taken"),("治疗","Healing"),("推车时间","Crystal"));
        }
        choice.SelectionChanged+=(_,_)=>Render();metric.SelectionChanged+=(_,_)=>Render();host.Children.Add(choice);host.Children.Add(metric);host.Children.Add(content);Render();
    }
    private void ShowRankTrend() {
        var rows=Filtered().Where(r=>!r.Observation&&r.Completed&&!r.Quarantined&&r.RankValue.HasValue).OrderBy(r=>r.StartUtc).ToArray();
        if(rows.Length==0) {Card("暂无段位记录",Text("已保存记录尚无段位或积分，记录到有效结算后会显示。",true));return;}
        PageContent.Children.Add(new TrendView(rows.Select(r=>(r.StartUtc,(double)r.RankValue!.Value)).ToArray()) {Height=280});
        PageContent.Children.Add(TableFactory.Create(rows.Reverse(),("日期","StartLabel"),("段位","Rank"),("积分","Credit"),("结果","Result")));
    }
    private void ShowRecords() {
        var rows=Filtered().Where(r=>!r.Observation&&r.Completed&&!r.Quarantined).SelectMany(r=>r.Players.Where(p=>_preferences.Links.Canonical(p.Name)==LocalIdentity).Select(p=>(Match:r,Player:p))).ToArray();
        if(rows.Length==0) {Card("暂无个人纪录",Text("登录游戏后自动识别角色；有结算数据时显示个人纪录。",true));return;}
        var records=new[] {("单场伤害",rows.Where(x=>x.Player.Damage.HasValue).OrderByDescending(x=>x.Player.Damage).Select(x=>(x.Match,x.Player.Damage?.ToString("N0")))),("单场承伤",rows.Where(x=>x.Player.Taken.HasValue).OrderByDescending(x=>x.Player.Taken).Select(x=>(x.Match,x.Player.Taken?.ToString("N0")))),("单场治疗",rows.Where(x=>x.Player.Healing.HasValue).OrderByDescending(x=>x.Player.Healing).Select(x=>(x.Match,x.Player.Healing?.ToString("N0"))))};
        foreach(var (name,values) in records) {var first=values.FirstOrDefault();if(first.Match!=null)Card(name,Text(first.Item2??"—",false,24),Text(first.Match.StartLabel+" · "+first.Match.Map,true),ActionButton("查看对局",async (_,_)=>await OpenRecordAsync(first.Match)));}
    }
    private TextBlock? _loggedPlayer,_connectionState;
    private void ShowProfile() {
        PageContent.Children.Add(Notice("profile"));
        _loggedPlayer=Text("",false,20);
        _connectionState=Text("",true);Card("本机已登录角色",_loggedPlayer,_connectionState);UpdateLoggedPlayer();
    }
    private void UpdateLoggedPlayer() {
        if(_page!=5||_loggedPlayer==null)return;
        // Display the actual login identity, without alias merging or historical fallbacks.
        var player=_capture.State.Connected?_capture.State.LocalPlayer:"";
        _loggedPlayer.Text=string.IsNullOrWhiteSpace(player)?"尚未确认登录角色":player;
        if(_connectionState!=null) {
            var state=_capture.State;
            _connectionState.Text=!state.Connected?state.Message:state.LocalPlayer.Length==0?(state.ChannelStatus?.GetValueOrDefault("login")??"等待角色加载"):state.Recording?"游戏已连接 · 正在记录对局":"游戏已连接 · 等待对局开始";
        }
    }
    private void ShowReplay() {
        PageContent.Children.Add(ActionButton("读取回放或战绩",OpenFile));PageContent.Children.Add(Notice("files"));
        if(_bundle==null) {Card("尚未选择回放",Text("双击战绩记录或读取 JSON 后，可以播放时间轴、查看事件与地图。",true));return;}
        Card(_bundle.Match.Map,Text(_bundle.Match.StartLabel+" · "+_bundle.Match.Duration),ActionButton("打开完整回放",(_,_)=>new ReplayWindow(_bundle) {Owner=this}.Show()));
        PageContent.Children.Add(TableFactory.Create(_bundle.Match.Players,("玩家","Name"),("职业","Job"),("击杀","Kills"),("死亡","Deaths"),("助攻","Assists"),("伤害","Damage"),("承伤","Taken"),("治疗","Healing"),("推车秒数","CrystalSeconds")));
    }
}
