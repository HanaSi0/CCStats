using System.Diagnostics;
using CCStats.Desktop.Views;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace CCStats.Desktop;
public partial class MainWindow {
    private Button DiagnosticExportButton() {
        return ActionButton("导出诊断",async (_,_)=> {
            var save=new Microsoft.Win32.SaveFileDialog {Filter="ZIP (*.zip)|*.zip",FileName="CCStats-诊断.zip"};if(save.ShowDialog(this)!=true)return;
            var temp=save.FileName+"."+Guid.NewGuid().ToString("N")+".partial";
            try {await _capture.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));await CCStats.Core.Capture.CaptureDiagnostics.ExportAsync(_directory,temp,new {CapturedAt=DateTime.UtcNow,SoftwareVersion=typeof(MainWindow).Assembly.GetName().Version?.ToString(),SoftwareBuild=typeof(MainWindow).Module.ModuleVersionId,State=_capture.State,Layout=CCStats.Core.Capture.ClientLayout.Profile,Notes="包含记录状态、结算界面字段和记录汇总，不包含完整回放或任意内存转储。"});File.Move(temp,save.FileName,true);StatusText.Text="诊断已导出。";}catch(Exception error) {StatusText.Text="导出失败："+error.Message;}finally {if(File.Exists(temp))File.Delete(temp);}
        });
    }
    private void UpdateCaptureStatus() {
        if(_tray!=null) {var text=_capture.State.Recording?"CCStats · 后台记录中":_capture.State.Connected?"CCStats · 游戏已连接":"CCStats · 等待游戏";if(_tray.Text!=text)_tray.Text=text;}
    }
    private bool _customColorsExpanded;
    private void ShowSettings() {
        var close=new ComboBox {ItemsSource=new[]{"每次询问","立即退出","最小化到托盘"},SelectedIndex=_preferences.CloseBehavior switch {"Exit"=>1,"Tray"=>2,_=>0},MinWidth=240,Margin=new Thickness(0,0,0,12)};
        var theme=new ComboBox {ItemsSource=DesktopTheme.Names,SelectedItem=_preferences.Theme,MinWidth=240,Margin=new Thickness(0,0,0,12)};
        Card("关闭窗口时",close,Text("最小化到托盘后仍会读取游戏并写入记录；右下角托盘图标可重新打开窗口或退出。",true));
        theme.SelectionChanged+=(_,_)=>{if(theme.SelectedItem is string selected){_preferences.Theme=selected;SavePreferences();ApplyTheme();}};
        Card("颜色主题",theme);
        var colors=new WrapPanel();
        foreach(var (label,key) in new[]{("背景","BackgroundBrush"),("面板","SurfaceBrush"),("按钮","ButtonBrush"),("强调","AccentBrush"),("正文","TextBrush"),("次要文字","MutedBrush"),("悬停","HoverBrush"),("选中","SelectionBrush"),("边框","BorderBrush"),("输入区","InputBrush")})colors.Children.Add(ActionButton(label+"颜色",(_,_)=> {
            var current=((SolidColorBrush)FindResource(key)).Color;using var dialog=new System.Windows.Forms.ColorDialog {FullOpen=true,Color=System.Drawing.Color.FromArgb(current.R,current.G,current.B)};
            if(dialog.ShowDialog()==System.Windows.Forms.DialogResult.OK) {_preferences.CustomColors[key]=$"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";SavePreferences();ApplyTheme();ShowPage(7);}
        }));
        colors.Children.Add(ActionButton("重置配色",(_,_)=>{_preferences.CustomColors.Clear();SavePreferences();ApplyTheme();ShowPage(7);}));var custom=new Expander {Header="自定义颜色",Content=colors,IsExpanded=_customColorsExpanded,Margin=new Thickness(0,0,0,14)};
        custom.Expanded+=(_,_)=>_customColorsExpanded=true;custom.Collapsed+=(_,_)=>_customColorsExpanded=false;PageContent.Children.Add(custom);
        PageContent.Children.Add(ActionButton("保存设置",(_,_)=> {_preferences.CloseBehavior=close.SelectedIndex switch {1=>"Exit",2=>"Tray",_=>"Ask"};_preferences.Theme=theme.SelectedItem as string??"深海蓝";SavePreferences();ApplyTheme();ShowPage(7);StatusText.Text="设置已保存。";}));
        var alias=new TextBox {ToolTip="别名角色的完整姓名",MinWidth=260};var main=new TextBox {ToolTip="主角色的完整姓名",MinWidth=260};
        Card("角色关联",Text("将不同角色归入同一身份，原始记录保持不变。",true),Text("别名角色",true),alias,Text("主角色",true),main,ActionButton("添加关联",(_,_)=>{try {_preferences.Links.Link(alias.Text,main.Text);SavePreferences();ShowPage(7);}catch(Exception ex) {StatusText.Text=ex.Message;}}));
        foreach(var link in _preferences.Links.Aliases.ToArray())PageContent.Children.Add(ActionButton(link.Key+" → "+link.Value+"　移除",(_,_)=>{_preferences.Links.Aliases.Remove(link.Key);SavePreferences();ShowPage(7);}));
        Card("支持与诊断",DiagnosticExportButton());
        Card("数据目录",Text(_directory,true),ActionButton("打开数据目录",(_,_)=>{try {Process.Start(new ProcessStartInfo {FileName=_directory,UseShellExecute=true});}catch(Exception ex){StatusText.Text=ex.Message;}}));
    }
    private void ApplyTheme() {
        DesktopTheme.Apply(_preferences.Theme,_preferences.CustomColors);
        SetResourceReference(BackgroundProperty,"BackgroundBrush");
    }
}
