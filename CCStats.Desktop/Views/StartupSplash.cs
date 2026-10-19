using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace CCStats.Desktop.Views;
internal sealed class StartupSplash : Window {
    private readonly Stopwatch _clock=Stopwatch.StartNew();
    private readonly Image _logo;
    public StartupSplash() {
        Width=244;Height=264;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
        ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;Topmost=true;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var content=new StackPanel {Margin=new Thickness(28,25,28,25),HorizontalAlignment=HorizontalAlignment.Center};
        _logo=new Image {Source=new BitmapImage(new Uri("pack://application:,,,/Assets/icon.png")),Width=112,Height=112,Margin=new Thickness(0,0,0,14)};content.Children.Add(_logo);
        content.Children.Add(new TextBlock {Text="CCStats",FontSize=22,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center});
        var label=new TextBlock {Text="正在打开",FontSize=12,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,6,0,12)};label.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");content.Children.Add(label);
        var track=new Grid {Width=164,Height=3,ClipToBounds=true};track.SetResourceReference(Panel.BackgroundProperty,"ButtonBrush");var pulse=new Border {Width=52,HorizontalAlignment=HorizontalAlignment.Left,CornerRadius=new CornerRadius(2)};pulse.SetResourceReference(Border.BackgroundProperty,"AccentBrush");track.Children.Add(pulse);content.Children.Add(track);
        var frame=new Border {CornerRadius=new CornerRadius(24),BorderThickness=new Thickness(1),Child=content};frame.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");frame.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");Content=frame;
        if(UiMotion.Enabled&&SystemParameters.ClientAreaAnimation) {
            Opacity=0;Loaded+=(_,_)=>BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)));
            var move=new TranslateTransform();pulse.RenderTransform=move;move.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-52,164,TimeSpan.FromMilliseconds(1050)){RepeatBehavior=RepeatBehavior.Forever});
            _logo.RenderTransformOrigin=new Point(.5,.5);var scale=new ScaleTransform(1,1);_logo.RenderTransform=scale;
            var breathe=new DoubleAnimation(1,1.035,TimeSpan.FromMilliseconds(800)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever};scale.BeginAnimation(ScaleTransform.ScaleXProperty,breathe);scale.BeginAnimation(ScaleTransform.ScaleYProperty,breathe);
        }
    }
    public async Task DismissAsync() {
        var wait=450-(int)_clock.ElapsedMilliseconds;if(wait>0)await Task.Delay(wait);
        if(UiMotion.Enabled&&SystemParameters.ClientAreaAnimation) {BeginAnimation(OpacityProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(150)));await Task.Delay(160);}
        Close();
    }
    internal static async Task VerifyAsync() {
        var splash=new StartupSplash();splash.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        try {if(splash._logo.Source is not BitmapSource {PixelWidth:>0}||!splash.IsVisible||splash.ShowInTaskbar)throw new InvalidOperationException("Startup logo did not load correctly");}
        finally {await splash.DismissAsync();}
    }
}
