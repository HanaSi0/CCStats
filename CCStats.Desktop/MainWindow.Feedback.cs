using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CCStats.Desktop.Views;
namespace CCStats.Desktop;
public partial class MainWindow {
    private readonly Dictionary<string,string> _notices=new();
    private readonly Dictionary<string,Border> _noticeViews=new();
    private Border Notice(string scope) {
        var text=Text(_notices.GetValueOrDefault(scope)??"",true);
        text.Margin=new Thickness(0);
        AutomationProperties.SetLiveSetting(text,AutomationLiveSetting.Polite);
        var panel=new Border {Child=text,Padding=new Thickness(12,10,12,10),Margin=new Thickness(0,0,0,12),CornerRadius=new CornerRadius(7),BorderThickness=new Thickness(3,0,0,0),Visibility=text.Text.Length==0?Visibility.Collapsed:Visibility.Visible};
        panel.SetResourceReference(Border.BackgroundProperty,"InputBrush");panel.SetResourceReference(Border.BorderBrushProperty,"AccentBrush");
        _noticeViews[scope]=panel;return panel;
    }
    private void Notify(string scope,string message) {
        if(_notices.GetValueOrDefault(scope)==message)return;
        _notices[scope]=message;
        if(!_noticeViews.TryGetValue(scope,out var panel))return;
        ((TextBlock)panel.Child).Text=message;panel.Visibility=message.Length==0?Visibility.Collapsed:Visibility.Visible;
        if(message.Length>0&&!_smoke)UiMotion.Enter(panel);
    }
    private async Task VerifyFeatureFeedbackAsync() {
        ShowPage(3);
        var tools=PageContent.Children.OfType<WrapPanel>().Single();
        tools.Children.OfType<Button>().Single(b=>Equals(b.Content,"定位本人")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(_noticeViews["rankings"].Visibility!=Visibility.Visible||!((TextBlock)_noticeViews["rankings"].Child).Text.Contains("登录"))throw new InvalidOperationException("Ranking action feedback is missing");
        ShowPage(1);
        if(_noticeViews.Count!=0||FindName("StatusText")!=null)throw new InvalidOperationException("Feature feedback leaked into overview");
        ShowPage(0);UpdateLayout();
        var expander=PageContent.Children.OfType<Expander>().Single();
        var toggle=(System.Windows.Controls.Primitives.ToggleButton)expander.Template.FindName("HeaderToggle",expander);
        toggle.Focus();
        for(int i=0;i<3;i++){expander.IsExpanded=true;UpdateLayout();expander.IsExpanded=false;UpdateLayout();}
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var header=(Border)toggle.Template.FindName("HeaderFrame",toggle);
        if(toggle.IsChecked==true||expander.IsExpanded||header.Background is not System.Windows.Media.SolidColorBrush brush||brush.Color.A!=0)throw new InvalidOperationException("Collapsed expander retains a focus fill");
        if(!Equals(_navigation[0].Tag,"Selected")||_navigation.Skip(1).Any(b=>Equals(b.Tag,"Selected")))throw new InvalidOperationException("Navigation selection is ambiguous");
        _notices.Clear();ShowPage(0);
    }
}
