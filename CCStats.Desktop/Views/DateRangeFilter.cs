using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace CCStats.Desktop.Views;

internal sealed class DateRangeFilter : StackPanel {
    private readonly Button _fromButton,_untilButton;
    private DateTime? _from,_until;
    private Popup? _popup;
    public event Action<DateTime?,DateTime?>? Changed;
    public DateRangeFilter(DateTime? from,DateTime? until) {
        Orientation=Orientation.Vertical;Margin=new Thickness(0,0,0,8);_from=from?.Date;_until=until?.Date;
        var row=new WrapPanel();_fromButton=DateButton("开始日期",true);_untilButton=DateButton("结束日期",false);row.Children.Add(_fromButton);row.Children.Add(_untilButton);Children.Add(row);
        var quick=new WrapPanel();
        foreach(var (label,days) in new[]{("今天",1),("近 7 天",7),("近 30 天",30),("清空日期",0)}) {
            var button=SmallButton(label);button.Click+=(_,_)=>Set(days==0?null:DateTime.Today.AddDays(1-days),days==0?null:DateTime.Today);quick.Children.Add(button);
        }
        Children.Add(quick);Refresh();
    }
    private static Brush Brush(string key)=>(Brush)Application.Current.Resources[key];
    private static Button SmallButton(string text)=>new(){Content=text,Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,0,8,4),MinHeight=30};
    private Button DateButton(string title,bool start) {
        var button=new Button {MinWidth=176,MinHeight=42,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,8),HorizontalContentAlignment=HorizontalAlignment.Left,ToolTip=title};
        button.Click+=(_,_)=>OpenCalendar(button,start);return button;
    }
    private void Refresh() {
        _fromButton.Content="开始  "+(_from?.ToString("yyyy-MM-dd")??"不限日期")+"  ▾";
        _untilButton.Content="结束  "+(_until?.ToString("yyyy-MM-dd")??"不限日期")+"  ▾";
    }
    private void Set(DateTime? from,DateTime? until) {_from=from;_until=until;Refresh();Changed?.Invoke(_from,_until);}
    private void OpenCalendar(Button anchor,bool start) {
        if(_popup!=null)_popup.IsOpen=false;
        var month=(start?_from:_until)??DateTime.Today;month=new DateTime(month.Year,month.Month,1);
        var content=new StackPanel();var header=new DockPanel();var prev=SmallButton("‹");var next=SmallButton("›");var caption=new TextBlock {FontSize=15,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        DockPanel.SetDock(prev,Dock.Left);DockPanel.SetDock(next,Dock.Right);header.Children.Add(prev);header.Children.Add(next);header.Children.Add(caption);content.Children.Add(header);
        var days=new UniformGrid {Columns=7};content.Children.Add(days);
        _popup=new Popup {PlacementTarget=anchor,Placement=PlacementMode.Bottom,StaysOpen=false,AllowsTransparency=true,Child=new Border {Background=Brush("SurfaceBrush"),BorderBrush=Brush("AccentBrush"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(12),Width=300,Child=content}};
        void Draw() {
            caption.Text=month.ToString("yyyy 年 M 月",CultureInfo.InvariantCulture);days.Children.Clear();
            foreach(var day in new[]{"一","二","三","四","五","六","日"})days.Children.Add(new TextBlock {Text=day,Foreground=Brush("MutedBrush"),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,8,0,8)});
            int offset=((int)month.DayOfWeek+6)%7;
            for(int i=0;i<42;i++) {
                var date=month.AddDays(i-offset);var b=SmallButton(date.Day.ToString());b.Margin=new Thickness(1);b.Padding=new Thickness(0);b.MinWidth=32;b.Height=32;b.Foreground=Brush(date.Month==month.Month?"TextBrush":"MutedBrush");
                if(date==(start?_from:_until)?.Date)b.Background=Brush("AccentBrush");
                if(date==DateTime.Today)b.FontWeight=FontWeights.Bold;
                b.ToolTip=date.ToString("yyyy-MM-dd");b.Click+=(_,_)=> {
                    if(start)Set(date,_until.HasValue&&_until<date?date:_until);else Set(_from.HasValue&&_from>date?date:_from,date);
                    _popup.IsOpen=false;
                };days.Children.Add(b);
            }
        }
        prev.Click+=(_,_)=>{if(month.Year>1||month.Month>1){month=month.AddMonths(-1);Draw();}};
        next.Click+=(_,_)=>{if(month.Year<9998){month=month.AddMonths(1);Draw();}};
        Draw();_popup.IsOpen=true;
    }
    internal void Verify() {
        var quick=(WrapPanel)Children[1];((Button)quick.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(_from!=DateTime.Today.AddDays(-6)||_until!=DateTime.Today)throw new InvalidOperationException("Date quick range failed");
        _fromButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(_popup?.IsOpen!=true)throw new InvalidOperationException("Date calendar popup failed");_popup.IsOpen=false;
        ((Button)quick.Children[3]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(_from!=null||_until!=null)throw new InvalidOperationException("Date range clear failed");
    }
}
