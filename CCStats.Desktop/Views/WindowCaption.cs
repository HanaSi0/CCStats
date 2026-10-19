using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
namespace CCStats.Desktop.Views;
internal static class WindowCaption {
    public static void Attach(Window window) {window.SourceInitialized+=(_,_)=>Apply(window);Apply(window);}
    public static void Apply(Window window) {
        if(!OperatingSystem.IsWindows())return;
        var handle=new WindowInteropHelper(window).Handle;if(handle==0)return;
        var background=(window.Background as SolidColorBrush)?.Color??((SolidColorBrush)DesktopTheme.Brush("BackgroundBrush")).Color;
        uint dark=background.R*.2126+background.G*.7152+background.B*.0722<128?1u:0u;
        if(DwmSetWindowAttribute(handle,20,ref dark,4)<0)DwmSetWindowAttribute(handle,19,ref dark,4);
        uint caption=ColorRef(background),text=ColorRef(((SolidColorBrush)DesktopTheme.Brush("TextBrush")).Color),border=caption;
        DwmSetWindowAttribute(handle,35,ref caption,4);DwmSetWindowAttribute(handle,36,ref text,4);DwmSetWindowAttribute(handle,34,ref border,4);
    }
    internal static void Verify(Window window) {
        Apply(window);var handle=new WindowInteropHelper(window).Handle;
        var result=DwmGetWindowAttribute(handle,35,out var actual,4);
        if(OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)&&result<0)throw new InvalidOperationException("Window caption colour is unavailable on this Windows build");
        if(result>=0&&window.Background is SolidColorBrush background&&actual!=ColorRef(background.Color))throw new InvalidOperationException("Title-bar colour does not match the window theme");
    }
    private static uint ColorRef(Color color)=>(uint)(color.R|color.G<<8|color.B<<16);
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(nint window,int attribute,ref uint value,int size);
    [DllImport("dwmapi.dll")]private static extern int DwmGetWindowAttribute(nint window,int attribute,out uint value,int size);
}
