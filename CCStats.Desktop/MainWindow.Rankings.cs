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
    private RankingSnapshot? _shownRanking;
    private async Task StartRankingsAsync() {
        _rankings=RankingRegion.All.Select(r=>new LeaderboardService(Path.Combine(_directory,"leaderboards",r.Key))).ToArray();
        foreach(var (region,index) in RankingRegion.All.Select((r,i)=>(r,i))) {
            if(_exiting)return;
            try {await _rankings[index].CheckDateAsync(region with {Site=0});}catch(Exception ex) {StatusText.Text="榜单暂未更新："+ex.Message;}
        }
    }
    private void ShowRankings() {
        var tools=new WrapPanel();var regions=new ComboBox {ItemsSource=RankingRegion.All.Select(r=>r.Name),SelectedIndex=_rankingRegion,MinWidth=150,Margin=new Thickness(0,0,10,10)};
        regions.SelectionChanged+=(_,_)=>{_rankingRegion=regions.SelectedIndex;_shownRanking=null;UpdateRankings();};tools.Children.Add(regions);
        tools.Children.Add(ActionButton("手动更新",async (_,_)=> {
            if(_rankings==null)return;try {await _rankings[_rankingRegion].CheckDateAsync(RankingRegion.All[_rankingRegion] with {Site=0},true);UpdateRankings();}catch(Exception ex){StatusText.Text=ex.Message;}
        }));
        tools.Children.Add(ActionButton("定位本人",(_,_)=> {
            if(_rankingGrid==null)return;var name=LocalIdentity;
            var row=_rankingGrid.Items.Cast<RankingEntry>().FirstOrDefault(e=>name.Equals(e.CharacterName+" "+e.World,StringComparison.OrdinalIgnoreCase));
            if(row==null) {StatusText.Text="未在当前榜单找到已设置的角色。";return;}_rankingGrid.SelectedItem=row;_rankingGrid.ScrollIntoView(row);
        }));PageContent.Children.Add(tools);
        _rankingText=Text("正在读取本机榜单…",true);PageContent.Children.Add(_rankingText);
        _rankingGrid=TableFactory.Create(Array.Empty<RankingEntry>(),("名次","Rank"),("完整玩家名","CharacterName"),("服务器","World"),("段位","Tier"),("阶位","Division"),("积分","Rating"),("胜场","Victories"),("名次变化","RankChange"));
        var style=new Style(typeof(DataGridRow));var trigger=new DataTrigger {Binding=new System.Windows.Data.MultiBinding {Converter=new IdentityConverter(LocalIdentity),Bindings={new System.Windows.Data.Binding("CharacterName"),new System.Windows.Data.Binding("World")}},Value=true};trigger.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(29,99,113))));style.Triggers.Add(trigger);_rankingGrid.RowStyle=style;
        PageContent.Children.Add(_rankingGrid);_shownRanking=null;UpdateRankings();
    }
    private void UpdateRankings() {
        if(_page!=3||_rankingText==null||_rankingGrid==null)return;
        var state=_rankings?[_rankingRegion].State;
        _rankingText.Text=state==null?"尚未启动联网检查。":$"来源：官方榜单　{state.Snapshot?.OfficialDate:yyyy-MM-dd}　{state.Snapshot?.Entries.Length??0} 人 · {state.Message}";
        if(state?.Snapshot is {} snapshot&&!ReferenceEquals(snapshot,_shownRanking)) {_shownRanking=snapshot;_rankingGrid.ItemsSource=snapshot.Entries;}
    }
    private sealed class IdentityConverter(string identity):System.Windows.Data.IMultiValueConverter {
        public object Convert(object[] values,Type type,object parameter,System.Globalization.CultureInfo culture)=>values.Length==2&&identity.Length>0&&identity.Equals(values[0]+" "+values[1],StringComparison.OrdinalIgnoreCase);
        public object[] ConvertBack(object value,Type[] types,object parameter,System.Globalization.CultureInfo culture)=>throw new NotSupportedException();
    }
}
