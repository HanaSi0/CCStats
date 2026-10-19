using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace CCStats.Desktop.Views;
internal static class UiMotion {
    public static bool Enabled {get;set;}=true;
    public static void Enter(FrameworkElement element) {
        if(!Enabled||!SystemParameters.ClientAreaAnimation)return;
        var duration=TimeSpan.FromMilliseconds(170);var easing=new CubicEase {EasingMode=EasingMode.EaseOut};
        element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.55,1,duration){EasingFunction=easing});
        var translate=new TranslateTransform();element.RenderTransform=translate;translate.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(7,0,duration){EasingFunction=easing});
    }
}
