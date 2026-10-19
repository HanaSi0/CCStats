using PvpStats.Leaderboards;
using CCStats.Desktop.Views;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace CCStats.Desktop;
public partial class MainWindow {
    private LeaderboardService[]? _rankings;
    private int _rankingRegion;
    private TextBlock? _rankingText;
    private DataGrid? _rankingGrid;
    private Style? _rankingRowBase;
    private RankingSnapshot? _shownRanking;
    private string? _rankingIdentity;
    private async Task StartRankingsAsync() {
        _rankings=await Task.Run(()=>RankingRegion.All.Select(r=>new LeaderboardService(Path.Combine(_directory,"leaderboards",r.Key))).ToArray());
        if(_exiting) {foreach(var service in _rankings)service.Dispose();return;}
        foreach(var (region,index) in RankingRegion.All.Select((r,i)=>(r,i))) {
            if(_exiting)return;
            try {await Task.Run(()=>_rankings[index].CheckDateAsync(region with {Site=0}));}catch(Exception ex) {Notify("rankings","榜单暂未更新："+ex.Message);}
        }
    }
    private void ShowRankings() {
        var tools=new WrapPanel();var regions=new ComboBox {ItemsSource=RankingRegion.All.Select(r=>r.Name),SelectedIndex=_rankingRegion,MinWidth=150,Margin=new Thickness(0,0,10,10)};
        regions.SelectionChanged+=(_,_)=>{Notify("rankings","");_rankingRegion=regions.SelectedIndex;_shownRanking=null;UpdateRankings();};tools.Children.Add(regions);
        tools.Children.Add(ActionButton("手动更新",async (_,_)=> {
            if(_rankings==null){Notify("rankings","榜单服务正在启动，请稍后重试。");return;}var index=_rankingRegion;Notify("rankings","正在更新当前榜单…");try {await Task.Run(()=>_rankings[index].CheckDateAsync(RankingRegion.All[index] with {Site=0},true));if(index==_rankingRegion){UpdateRankings();Notify("rankings","榜单检查已完成。");}}catch(Exception ex){if(index==_rankingRegion)Notify("rankings",ex.Message);}
        }));
        tools.Children.Add(ActionButton("定位本人",(_,_)=> {
            if(_rankingGrid==null)return;var name=_capture.State.LocalPlayer;
            var row=_rankingGrid.Items.Cast<RankingEntry>().FirstOrDefault(e=>RankingIdentity.MatchesFullIdentity(e,name));
            if(row==null) {Notify("rankings",name.Length==0?"请先登录游戏角色，再定位本人。":"未在当前榜单找到当前登录角色。");return;}_rankingGrid.SelectedItem=row;_rankingGrid.ScrollIntoView(row);Notify("rankings","已定位当前登录角色。");
        }));PageContent.Children.Add(tools);PageContent.Children.Add(Notice("rankings"));
        _rankingText=Text("正在读取本机榜单…",true);PageContent.Children.Add(_rankingText);
        _rankingGrid=TableFactory.Create(Array.Empty<RankingEntry>(),("名次","Rank"),("完整玩家名","CharacterName"),("服务器","World"),("段位","Tier"),("阶位","Division"),("积分","Rating"),("胜场","Victories"),("名次变化","RankChange"));
        _rankingRowBase=_rankingGrid.RowStyle;_rankingIdentity=null;
        PageContent.Children.Add(_rankingGrid);_shownRanking=null;UpdateRankings();
    }
    private void UpdateRankings() {
        if(!IsVisible||_page!=3||_rankingText==null||_rankingGrid==null)return;
        if(_rankingIdentity!=_capture.State.LocalPlayer) {
            _rankingIdentity=_capture.State.LocalPlayer;
            var style=new Style(typeof(DataGridRow),_rankingRowBase);var trigger=new DataTrigger {Binding=new System.Windows.Data.MultiBinding {Converter=new IdentityConverter(_capture.State.LocalPlayer),Bindings={new System.Windows.Data.Binding("CharacterName"),new System.Windows.Data.Binding("World")}},Value=true};trigger.Setters.Add(new Setter(Control.BorderBrushProperty,new DynamicResourceExtension("AccentBrush")));trigger.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(3,0,0,0)));trigger.Setters.Add(new Setter(Control.FontWeightProperty,FontWeights.SemiBold));style.Triggers.Add(trigger);_rankingGrid.RowStyle=style;
        }
        var state=_rankings?[_rankingRegion].State;
        _rankingText.Text=state==null?"尚未启动联网检查。":$"来源：官方榜单　{state.Snapshot?.OfficialDate:yyyy-MM-dd}　{state.Snapshot?.Entries.Length??0} 人 · {state.Message}";
        if(state?.Snapshot is {} snapshot&&!ReferenceEquals(snapshot,_shownRanking)) {_shownRanking=snapshot;_rankingGrid.ItemsSource=snapshot.Entries;}
    }
    private sealed class IdentityConverter(string identity):System.Windows.Data.IMultiValueConverter {
        public object Convert(object[] values,Type type,object parameter,System.Globalization.CultureInfo culture)=>values.Length==2&&identity.Length>0&&RankingIdentity.MatchesFullIdentity(new RankingEntry {CharacterName=values[0]?.ToString()??"",World=values[1]?.ToString()??""},identity);
        public object[] ConvertBack(object value,Type[] types,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
    }
}
