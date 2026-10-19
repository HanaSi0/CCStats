using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace CCStats.Desktop.Views;
internal static class UiMotion {
    private sealed class State {
        public readonly TranslateTransform Move=new();public readonly ScaleTransform Scale=new(1,1);
        public State(FrameworkElement element){var group=new TransformGroup();group.Children.Add(element.RenderTransform);group.Children.Add(Scale);group.Children.Add(Move);element.RenderTransform=group;}
        public void Stop(FrameworkElement e){e.BeginAnimation(UIElement.OpacityProperty,null);Move.BeginAnimation(TranslateTransform.YProperty,null);Scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);Scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);}
    }
    private static bool _enabled=true;private static readonly ConditionalWeakTable<FrameworkElement,State> States=new();private static readonly List<WeakReference<FrameworkElement>> Active=new();
    public static bool Enabled {get=>_enabled;set{_enabled=value;if(!value)foreach(var reference in Active)if(reference.TryGetTarget(out var element)&&States.TryGetValue(element,out var state))state.Stop(element);}}
    public static void Enter(FrameworkElement element) {
        if(!Enabled||!SystemParameters.ClientAreaAnimation)return;
        Animate(element,0,6,.996);
        if(element is Panel panel){int i=0;foreach(var child in panel.Children.OfType<FrameworkElement>().Where(c=>c.Visibility==Visibility.Visible).Take(10))Animate(child,Math.Min(90,i++*14),8,1);}
    }
    private static void Animate(FrameworkElement element,int delay,double distance,double scale){
        if(!States.TryGetValue(element,out var state)){state=new(element);States.Add(element,state);Active.RemoveAll(w=>!w.TryGetTarget(out _));Active.Add(new(element));}state.Stop(element);
        DoubleAnimation Animation(double from,double to)=>new(from,to,TimeSpan.FromMilliseconds(220)){BeginTime=TimeSpan.FromMilliseconds(delay),EasingFunction=new QuinticEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};
        element.BeginAnimation(UIElement.OpacityProperty,Animation(.45,element.Opacity));state.Move.BeginAnimation(TranslateTransform.YProperty,Animation(distance,0));if(scale!=1){state.Scale.BeginAnimation(ScaleTransform.ScaleXProperty,Animation(scale,1));state.Scale.BeginAnimation(ScaleTransform.ScaleYProperty,Animation(scale,1));}
    }
    internal static async Task VerifyAsync(){var enabled=Enabled;var element=new Border();try{Enabled=true;Enter(element);if(SystemParameters.ClientAreaAnimation){await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);await Task.Delay(40);if(element.Opacity>=1)throw new InvalidOperationException("Entrance motion is missing");}Enabled=false;if(element.Opacity!=1)throw new InvalidOperationException("Reduced motion left a faded panel");}finally{Enabled=enabled;}}
}
