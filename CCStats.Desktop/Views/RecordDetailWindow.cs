using CCStats.Core.Library;
using PvpStats.Analysis;
using PvpStats.Localization;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace CCStats.Desktop.Views;
internal sealed class RecordDetailWindow : Window {
    private readonly ReplayBundle _bundle;
    public event Action? Changed;
    public RecordDetailWindow(ReplayBundle bundle,RecordLibrary library) {
        _bundle=bundle;Title="对局详情 · "+bundle.Match.Map;Width=1180;Height=800;MinWidth=850;MinHeight=600;Background=(Brush)Application.Current.Resources["BackgroundBrush"];
        var root=new DockPanel {Margin=new Thickness(18)};var header=new StackPanel();
        header.Children.Add(new TextBlock {Text=bundle.Match.Map+"　"+bundle.Match.StartLabel+"　"+bundle.Match.Result+"　"+bundle.Match.Duration,Foreground=Brushes.White,FontSize=21,Margin=new Thickness(0,0,0,14)});
        var tools=new WrapPanel();var favorite=new CheckBox {Content="收藏",IsChecked=bundle.Match.Favorite,Foreground=Brushes.White,Margin=new Thickness(0,8,12,0)};var tags=new TextBox {Text=bundle.Match.Tags,MinWidth=220};tools.Children.Add(favorite);tools.Children.Add(tags);
        Button Button(string title,RoutedEventHandler action) {var b=new Button {Content=title};b.Click+=action;return b;}
        tools.Children.Add(Button("保存标签",(_,_)=>{library.Annotate(bundle.Match.Id,favorite.IsChecked==true,tags.Text);bundle.Match.Tags=tags.Text;bundle.Match.Favorite=favorite.IsChecked==true;Changed?.Invoke();}));
        tools.Children.Add(Button("地图回放",(_,_)=>new ReplayWindow(bundle){Owner=this}.Show()));
        tools.Children.Add(Button("导出 JSON",async (_,_)=>{var save=new SaveFileDialog {Filter="JSON (*.json)|*.json",FileName=bundle.Match.Id+".json"};if(save.ShowDialog(this)==true)try {await ReplayImport.ExportAsync(bundle,save.FileName);}catch(Exception ex){MessageBox.Show(this,ex.Message,"导出失败");}}));
        header.Children.Add(tools);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var tabs=new TabControl();void Tab(string title,UIElement content)=>tabs.Items.Add(new TabItem {Header=title,Content=content});
        var settle=new StackPanel();settle.Children.Add(TableFactory.Create(bundle.Match.Players,("完整玩家名","Name"),("职业","Job"),("队伍","Team"),("击杀","Kills"),("死亡","Deaths"),("助攻","Assists"),("造成伤害","Damage"),("受到伤害","Taken"),("治疗量","Healing"),("推车秒数","CrystalSeconds")));
        var totals=bundle.Match.Players.GroupBy(p=>p.Team).Select(g=>new {Team=g.Key,Damage=g.Any(p=>p.Damage.HasValue)?g.Sum(p=>p.Damage??0):(long?)null,Taken=g.Any(p=>p.Taken.HasValue)?g.Sum(p=>p.Taken??0):(long?)null,Healing=g.Any(p=>p.Healing.HasValue)?g.Sum(p=>p.Healing??0):(long?)null,Crystal=g.Any(p=>p.CrystalSeconds.HasValue)?g.Sum(p=>p.CrystalSeconds??0):(double?)null});
        var teamGrid=TableFactory.Create(totals,("队伍合计","Team"),("伤害","Damage"),("承伤","Taken"),("治疗","Healing"),("推车秒数","Crystal"));teamGrid.Height=150;settle.Children.Add(teamGrid);Tab("结算数据",new ScrollViewer {Content=settle});
        var analysis=new StackPanel();var analysisStatus=new TextBlock {Text="点击后才计算防御推算、控制和恢复数据。",Foreground=Brushes.LightSteelBlue,Margin=new Thickness(10)};var analysisHost=new ContentControl();
        var calculate=Button("计算高级数据",async (s,_)=> {
            var button=(Button)s;button.IsEnabled=false;analysisStatus.Text="正在计算…";
            try {
                var result=await Task.Run(()=>{var estimates=MitigationEstimator.Calculate(bundle.Data);var stats=AdvancedCombatAnalysis.Calculate(bundle.Data,estimates);return (estimates,stats);});
                var rows=result.stats.Values.Select(p=>new {p.Player,Damage=p.DamageDealt,Taken=p.DamageReceived,Healing=p.HealingDealt,Drain=p.DrainHealing,GuardDamage=p.GuardedDamage,GuardPrevented=p.EstimatedHits>0?(double?)p.EstimatedGuardPrevented:null,GuardHits=p.GuardedHits,Controls=p.ControlApplications,ControlSeconds=p.ControlSeconds,BlockedControls=p.ControlResistsUnderGuard,Pending=p.PendingHits});
                analysisHost.Content=TableFactory.Create(rows,("玩家","Player"),("观测伤害","Damage"),("观测承伤","Taken"),("观测治疗","Healing"),("吸血","Drain"),("防御期间承伤","GuardDamage"),("推算防御减伤","GuardPrevented"),("防御命中数","GuardHits"),("控制应用","Controls"),("控制秒数","ControlSeconds"),("防御期间抵抗","BlockedControls"),("待定记录","Pending"));
                analysisStatus.Text=$"已完成；可推算 {result.estimates.Count(e=>e.Supported)} 条，条件不足 {result.estimates.Count(e=>!e.Supported)} 条。观测统计不等同完整结算。";
            } catch(Exception ex){analysisStatus.Text=ex.Message;}finally{button.IsEnabled=true;}
        });analysis.Children.Add(calculate);analysis.Children.Add(analysisStatus);analysis.Children.Add(analysisHost);Tab("高级分析",analysis);
        var events=bundle.Timeline.Concat(bundle.Data.ReplaySkills.Select(e=>new TimelineRow(e.Timestamp,"技能",e.Actor,e.TargetPlayer,ChineseText.ActionName(e.ActionId,$"技能 {e.ActionId}")))).Concat(bundle.Data.MapMechanics.Select(e=>new TimelineRow(e.Timestamp,"地图事件","","",e.Label))).Distinct().OrderBy(e=>e.Timestamp).ToArray();
        Tab("事件时间线",TableFactory.Create(events,("时间","Timestamp"),("类型","Kind"),("来源","Actor"),("目标","Target"),("事件","Description")));
        Tab("推进曲线",new TrendView(bundle.Data.Frames.Select(f=>(f.Timestamp,(double)f.CrystalProgress)).Where((_,i)=>i%15==0).ToArray()){Height=360});
        var skills=bundle.Data.Effects.GroupBy(e=>new {e.Actor,e.ActionId}).Select(g=>new {Player=g.Key.Actor,Name=ChineseText.ActionName(g.Key.ActionId,$"技能 {g.Key.ActionId}"),Hits=g.Count(),Damage=g.Where(e=>DetailedAnalysis.IsDamage(e.Type)).Sum(e=>e.Amount),Healing=g.Where(e=>e.Type==4).Sum(e=>e.Amount)});
        Tab("技能分析",TableFactory.Create(skills,("玩家","Player"),("技能","Name"),("效果条数","Hits"),("伤害","Damage"),("治疗","Healing")));
        root.Children.Add(tabs);Content=root;
    }
}
