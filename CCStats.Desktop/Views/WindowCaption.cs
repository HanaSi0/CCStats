using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
namespace CCStats.Desktop.Views;
internal static class WindowCaption {
    private sealed record CustomCaption(Grid Header,Button Close);
    private static readonly ConditionalWeakTable<Window,CustomCaption> Custom=new();
    public static void Attach(Window window) {window.SourceInitialized+=(_,_)=>Apply(window);Apply(window);}
    public static void Apply(Window window) {
        if(!OperatingSystem.IsWindows()||Custom.TryGetValue(window,out _))return;
        var handle=new WindowInteropHelper(window).Handle;if(handle==0)return;
        var background=(window.Background as SolidColorBrush)?.Color??((SolidColorBrush)DesktopTheme.Brush("BackgroundBrush")).Color;
        uint dark=background.R*.2126+background.G*.7152+background.B*.0722<128?1u:0u;
        if(DwmSetWindowAttribute(handle,20,ref dark,4)<0)DwmSetWindowAttribute(handle,19,ref dark,4);
        uint caption=ColorRef(background),text=ColorRef(((SolidColorBrush)DesktopTheme.Brush("TextBrush")).Color),border=caption;
        if(DwmSetWindowAttribute(handle,35,ref caption,4)<0) {UseCustomCaption(window);return;}
        DwmSetWindowAttribute(handle,36,ref text,4);DwmSetWindowAttribute(handle,34,ref border,4);
    }
    private static void UseCustomCaption(Window window) {
        if(Custom.TryGetValue(window,out _))return;
        var root=new Grid();root.RowDefinitions.Add(new(){Height=new GridLength(36)});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var header=new Grid {Height=36};header.SetBinding(Panel.BackgroundProperty,new Binding("Background"){Source=window});
        header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var label=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0)};
        var icon=new Image {Width=16,Height=16,Margin=new Thickness(0,0,8,0)};icon.SetBinding(Image.SourceProperty,new Binding("Icon"){Source=window});label.Children.Add(icon);
        var title=new TextBlock {FontSize=12,TextWrapping=TextWrapping.NoWrap,VerticalAlignment=VerticalAlignment.Center};title.SetBinding(TextBlock.TextProperty,new Binding("Title"){Source=window});title.SetResourceReference(TextBlock.ForegroundProperty,"TextBrush");label.Children.Add(title);header.Children.Add(label);
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,2,4,2)};Grid.SetColumn(buttons,1);header.Children.Add(buttons);
        Button Button(string text,string tooltip,Action action) {
            var button=new Button {Content=text,ToolTip=tooltip,Width=42,Height=32,FontSize=16,Padding=new Thickness(0),Margin=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Center};
            button.SetBinding(Control.BackgroundProperty,new Binding("Background"){Source=window});button.SetResourceReference(Control.ForegroundProperty,"TextBrush");WindowChrome.SetIsHitTestVisibleInChrome(button,true);button.Click+=(_,_)=>action();buttons.Children.Add(button);return button;
        }
        if(window.ResizeMode!=ResizeMode.NoResize)Button("—","最小化",()=>window.WindowState=WindowState.Minimized);
        if(window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)Button("□","最大化 / 还原",()=>window.WindowState=window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized);
        var close=Button("×","关闭",window.Close);root.Children.Add(header);
        var content=window.Content;window.Content=null;var host=new ContentPresenter {Content=content};Grid.SetRow(host,1);root.Children.Add(host);
        Custom.Add(window,new(header,close));window.WindowStyle=WindowStyle.None;
        WindowChrome.SetWindowChrome(window,new WindowChrome {CaptionHeight=36,ResizeBorderThickness=window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip?new Thickness(6):new Thickness(0),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(8),UseAeroCaptionButtons=false});window.Content=root;
    }
    internal static void Verify(Window window) {
        Apply(window);
        var expected=((SolidColorBrush)window.Background).Color;
        if(Custom.TryGetValue(window,out var custom)) {
            window.UpdateLayout();
            if(custom.Header.Background is not SolidColorBrush brush||brush.Color!=expected||WindowChrome.GetWindowChrome(window)?.CaptionHeight!=36||!WindowChrome.GetIsHitTestVisibleInChrome(custom.Close))throw new InvalidOperationException("Custom title-bar theme or window controls are incomplete");
            return;
        }
        var handle=new WindowInteropHelper(window).Handle;var result=DwmGetWindowAttribute(handle,35,out var actual,4);
        if(result<0||actual!=ColorRef(expected))throw new InvalidOperationException("Title-bar colour does not match the window theme");
    }
    private static uint ColorRef(Color color)=>(uint)(color.R|color.G<<8|color.B<<16);
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(nint window,int attribute,ref uint value,int size);
    [DllImport("dwmapi.dll")]private static extern int DwmGetWindowAttribute(nint window,int attribute,out uint value,int size);
}
