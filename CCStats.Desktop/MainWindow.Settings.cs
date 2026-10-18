using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace CCStats.Desktop;
public partial class MainWindow {
    private void ShowCapture() {
        var actions=new WrapPanel();actions.Children.Add(ActionButton("重新识别",(_,_)=>_capture.Connect(Path.Combine(_directory,"observations"))));
        actions.Children.Add(ActionButton("开始记录",(_,_)=>_capture.Record(true)));actions.Children.Add(ActionButton("结束本段",(_,_)=>_capture.Record(false)));
        actions.Children.Add(ActionButton("导出采集诊断",async (_,_)=> {
            var save=new Microsoft.Win32.SaveFileDialog {Filter="ZIP (*.zip)|*.zip",FileName="CCStats-采集诊断.zip"};if(save.ShowDialog(this)!=true)return;
            var temp=save.FileName+"."+Guid.NewGuid().ToString("N")+".partial";
            try {await CCStats.Core.Capture.CaptureDiagnostics.ExportAsync(_directory,temp,new {CapturedAt=DateTime.UtcNow,State=_capture.State,Layout=CCStats.Core.Capture.ClientLayout.Profile,Notes="包含采集状态与结算界面字段，不包含完整回放或任意内存转储。"});File.Move(temp,save.FileName,true);StatusText.Text="采集诊断已导出。";}catch(Exception error) {StatusText.Text="导出失败："+error.Message;}finally {if(File.Exists(temp))File.Delete(temp);}
        }));
        _captureStatus=Text("",true);_capturePlayers=new ListBox {MaxHeight=330};Card("游戏连接",actions,_captureStatus,_capturePlayers);
        Card("后台运行",Text("游戏启动后自动识别。进入水晶冲突后按设置自动记录，关闭窗口时可继续在托盘运行。",true),Text("读取已加载角色的位置、朝向、体力、职业、状态，并尝试读取队伍、推进、水晶、召唤物及保留的动作效果。完整结算与命中瞬间快照仍需适配，缺失数据不会补造。",true));UpdateCaptureStatus();
    }
    private void UpdateCaptureStatus() {
        if(_tray!=null) {var text=_capture.State.Recording?"CCStats · 后台记录中":_capture.State.Connected?"CCStats · 游戏已连接":"CCStats · 等待游戏";if(_tray.Text!=text)_tray.Text=text;}
        if(!IsVisible||_page!=9||_captureStatus==null||_capturePlayers==null)return;var state=_capture.State;
        _captureStatus.Text=$"{state.Message}\n区域 {state.Territory} · {state.Players?.Length??0} 名已加载玩家 · {state.Frames:N0} 帧"+(state.DiagnosticError==null?"":"\n诊断暂未写入："+state.DiagnosticError)+(state.SavedPath==null?"":"\n已保存："+state.SavedPath);
        var players=state.Players??Array.Empty<string>();if(!_capturePlayers.Items.Cast<string>().SequenceEqual(players))_capturePlayers.ItemsSource=players;
    }
    private void ShowSettings() {
        var player=new TextBox {Text=_preferences.LocalPlayer,MinWidth=300,ToolTip="完整角色名和服务器，用于统计默认本人和榜单定位"};
        var auto=new CheckBox {Content="自动识别游戏并重连",IsChecked=_preferences.AutoDetectGame,Foreground=Brushes.White,Margin=new Thickness(0,0,0,10)};
        var record=new CheckBox {Content="进入水晶冲突时自动记录",IsChecked=_preferences.AutoRecord,Foreground=Brushes.White,Margin=new Thickness(0,0,0,10)};
        var close=new ComboBox {ItemsSource=new[]{"每次询问","立即退出","最小化到托盘"},SelectedIndex=_preferences.CloseBehavior switch {"Exit"=>1,"Tray"=>2,_=>0},MinWidth=240,Margin=new Thickness(0,0,0,12)};
        var theme=new ComboBox {ItemsSource=new[]{"深海蓝","石墨灰","森林绿","紫罗兰"},SelectedItem=_preferences.Theme,MinWidth=240,Margin=new Thickness(0,0,0,12)};
        Card("个人与采集",Text("默认角色（完整姓名与服务器）",true),player,auto,record);
        Card("关闭窗口时",close,Text("最小化到托盘后仍会读取游戏并写入记录；右下角托盘图标可重新打开窗口或退出。",true));
        Card("颜色主题",theme);
        var colors=new WrapPanel();
        foreach(var (label,key) in new[]{("背景","BackgroundBrush"),("面板","SurfaceBrush"),("按钮","ButtonBrush"),("强调","AccentBrush"),("正文","TextBrush"),("次要文字","MutedBrush")})colors.Children.Add(ActionButton(label+"颜色",(_,_)=> {
            var current=((SolidColorBrush)FindResource(key)).Color;using var dialog=new System.Windows.Forms.ColorDialog {FullOpen=true,Color=System.Drawing.Color.FromArgb(current.R,current.G,current.B)};
            if(dialog.ShowDialog()==System.Windows.Forms.DialogResult.OK) {_preferences.CustomColors[key]=$"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";SavePreferences();ApplyTheme();ShowPage(10);}
        }));
        colors.Children.Add(ActionButton("重置配色",(_,_)=>{_preferences.CustomColors.Clear();SavePreferences();ApplyTheme();ShowPage(10);}));Card("自定义颜色",colors);
        PageContent.Children.Add(ActionButton("保存设置",(_,_)=> {_preferences.LocalPlayer=player.Text.Trim();_preferences.AutoDetectGame=auto.IsChecked==true;_preferences.AutoRecord=record.IsChecked==true;_preferences.CloseBehavior=close.SelectedIndex switch {1=>"Exit",2=>"Tray",_=>"Ask"};_preferences.Theme=theme.SelectedItem as string??"深海蓝";_capture.Configure(_preferences.AutoRecord);SavePreferences();ApplyTheme();ShowPage(10);StatusText.Text="设置已保存。";}));
        var alias=new TextBox {ToolTip="别名角色的完整姓名",MinWidth=260};var main=new TextBox {ToolTip="主角色的完整姓名",MinWidth=260};
        Card("角色关联",Text("将不同角色归入同一身份，原始记录保持不变。",true),Text("别名角色",true),alias,Text("主角色",true),main,ActionButton("添加关联",(_,_)=>{try {_preferences.Links.Link(alias.Text,main.Text);SavePreferences();ShowPage(10);}catch(Exception ex) {StatusText.Text=ex.Message;}}));
        foreach(var link in _preferences.Links.Aliases.ToArray())PageContent.Children.Add(ActionButton(link.Key+" → "+link.Value+"　移除",(_,_)=>{_preferences.Links.Aliases.Remove(link.Key);SavePreferences();ShowPage(10);}));
        Card("数据目录",Text(_directory,true),ActionButton("打开数据目录",(_,_)=>{try {Process.Start(new ProcessStartInfo {FileName=_directory,UseShellExecute=true});}catch(Exception ex){StatusText.Text=ex.Message;}}));
    }
    private void ApplyTheme() {
        var palette=_preferences.Theme switch {
            "石墨灰"=>new[]{"#141619","#20242A","#343B46","#566D87"},
            "森林绿"=>new[]{"#0E1917","#182E29","#244D42","#287A62"},
            "紫罗兰"=>new[]{"#151020","#291D3C","#483263","#794FAD"},
            _=>new[]{"#0D1220","#171F33","#29395D","#245F9F"}
        };
        var keys=new[]{"BackgroundBrush","SurfaceBrush","ButtonBrush","AccentBrush"};
        for(int i=0;i<keys.Length;i++) {var color=(Color)ColorConverter.ConvertFromString(palette[i]);var brush=(SolidColorBrush)Application.Current.Resources[keys[i]];if(brush.IsFrozen)Application.Current.Resources[keys[i]]=new SolidColorBrush(color);else brush.Color=color;}
        foreach(var pair in _preferences.CustomColors) {
            if(pair.Key is not ("BackgroundBrush" or "SurfaceBrush" or "ButtonBrush" or "AccentBrush" or "TextBrush" or "MutedBrush"))continue;
            try {Application.Current.Resources[pair.Key]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));}catch(FormatException) { }
        }
        Background=(Brush)FindResource("BackgroundBrush");
    }
}
