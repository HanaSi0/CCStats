using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace CCStats.Desktop.Views;
internal sealed class CloseChoiceDialog : Window {
    public string Choice {get;private set;}="Cancel";
    public bool Remember=>_remember.IsChecked==true;
    private readonly Dictionary<string,Button> _buttons=new();
    internal void SelectForCheck(string choice,bool remember) {_remember.IsChecked=remember;_buttons[choice].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
    private readonly CheckBox _remember=new() {Content="记住我的选择",Foreground=CCStats.Desktop.Views.DesktopTheme.Brush("TextBrush"),Margin=new Thickness(0,14,0,0)};
    public CloseChoiceDialog(Window owner) {
        Owner=owner;Title="关闭 CCStats";Width=450;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(17,25,43));ShowInTaskbar=false;
        var panel=new StackPanel {Margin=new Thickness(24)};
        panel.Children.Add(new TextBlock {Text="退出软件，还是继续在后台记录？",Foreground=CCStats.Desktop.Views.DesktopTheme.Brush("TextBrush"),FontSize=18,Margin=new Thickness(0,0,0,18)});
        var buttons=new WrapPanel();
        foreach(var (label,choice) in new[]{("立即退出","Exit"),("最小化到托盘","Tray")}) {
            var button=new Button {Content=label,Padding=new Thickness(12,9,12,9),Margin=new Thickness(0,0,10,0)};
            button.Click+=(_,_)=>{Choice=choice;DialogResult=true;};_buttons[choice]=button;buttons.Children.Add(button);
        }
        panel.Children.Add(buttons);panel.Children.Add(_remember);panel.Children.Add(new TextBlock {Text="可在“设置 → 关闭窗口时”随时修改。",Foreground=CCStats.Desktop.Views.DesktopTheme.Brush("MutedBrush"),Margin=new Thickness(0,10,0,0)});Content=panel;
    }
}
