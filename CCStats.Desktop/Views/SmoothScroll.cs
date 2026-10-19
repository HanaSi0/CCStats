using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace CCStats.Desktop.Views;
internal static class SmoothScroll {
    private sealed class State {public double Target;public bool Moving,Pending;public int Generation;}
    private static readonly ConditionalWeakTable<ScrollViewer,State> States=new();
    private static readonly DependencyProperty OffsetProperty=DependencyProperty.RegisterAttached("Offset",typeof(double),typeof(SmoothScroll),new PropertyMetadata(0d,(sender,args)=>((ScrollViewer)sender).ScrollToVerticalOffset((double)args.NewValue)));
    public static void Attach(ScrollViewer viewer) {
        if(States.TryGetValue(viewer,out _))return;States.Add(viewer,new());
        viewer.PreviewMouseWheel+=(_,e)=> {
            if(viewer.CanContentScroll||e.Handled)return;
            DependencyObject? child=e.OriginalSource as DependencyObject;
            while(child!=null&&child is not ScrollViewer)child=child is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(child):LogicalTreeHelper.GetParent(child);
            if(child!=viewer)return; // Leave nested lists, menus and replay camera zoom with their owner.
            double step=SystemParameters.WheelScrollLines<0?viewer.ViewportHeight*.9:Math.Max(0,SystemParameters.WheelScrollLines)*18;
            if(ScrollBy(viewer,-e.Delta/120d*step))e.Handled=true;
        };
        viewer.PreviewMouseDown+=(_,_)=>Stop(viewer);viewer.PreviewKeyDown+=(_,_)=>Stop(viewer);viewer.Unloaded+=(_,_)=>Stop(viewer);
    }
    internal static bool ScrollBy(ScrollViewer viewer,double amount) {
        if(viewer.ScrollableHeight<=0||amount==0)return false;
        var state=States.GetValue(viewer,_=>new State());double current=viewer.VerticalOffset;
        var target=Math.Clamp((state.Moving||state.Pending?state.Target:current)+amount,0,viewer.ScrollableHeight);
        if(Math.Abs(target-current)<.1&&!state.Moving&&!state.Pending)return false;
        Stop(viewer);state.Target=target;
        if(!UiMotion.Enabled||!SystemParameters.ClientAreaAnimation){QueueOffset(viewer,state,target);return true;}
        state.Moving=true;int generation=++state.Generation;viewer.SetValue(OffsetProperty,current);
        var animation=new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(190)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};
        animation.Completed+=(_,_)=>{if(generation!=state.Generation)return;state.Moving=false;viewer.SetValue(OffsetProperty,target);viewer.BeginAnimation(OffsetProperty,null);QueueOffset(viewer,state,target);};
        viewer.BeginAnimation(OffsetProperty,animation,HandoffBehavior.SnapshotAndReplace);return true;
    }
    private static void QueueOffset(ScrollViewer viewer,State state,double target) {
        state.Pending=true;state.Target=target;int generation=++state.Generation;
        viewer.ScrollToVerticalOffset(target);
        // ScrollViewer applies commands during layout, even with animations disabled.
        _=viewer.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>{if(generation==state.Generation)state.Pending=false;}));
    }
    private static void Stop(ScrollViewer viewer) {
        if(!States.TryGetValue(viewer,out var state)||(!state.Moving&&!state.Pending))return;
        double current=viewer.VerticalOffset;++state.Generation;state.Moving=false;state.Pending=false;viewer.SetValue(OffsetProperty,current);viewer.BeginAnimation(OffsetProperty,null);state.Target=current;
    }
    internal static async Task VerifyAsync(Window owner) {
        var viewer=new ScrollViewer {Content=new Border{Height=2000},VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var window=new Window {Owner=owner,Content=viewer,Width=240,Height=180,ShowInTaskbar=false,ShowActivated=false,WindowStyle=WindowStyle.None};
        bool enabled=UiMotion.Enabled;
        try {
            window.Show();window.UpdateLayout();Attach(viewer);UiMotion.Enabled=true;
            ScrollBy(viewer,140);ScrollBy(viewer,60);ScrollBy(viewer,-50);
            await Task.Delay(35);
            if(SystemParameters.ClientAreaAnimation&&(viewer.VerticalOffset<=0||viewer.VerticalOffset>=150))throw new InvalidOperationException("Wheel scrolling does not ease between positions");
            await Task.Delay(250);
            if(Math.Abs(viewer.VerticalOffset-150)>1)throw new InvalidOperationException($"Accumulated wheel input lost distance or direction: actual {viewer.VerticalOffset}, target {States.GetValue(viewer,_=>new State()).Target}, moving {States.GetValue(viewer,_=>new State()).Moving}");
            UiMotion.Enabled=false;ScrollBy(viewer,50);await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Loaded);
            if(Math.Abs(viewer.VerticalOffset-200)>1)throw new InvalidOperationException("Reduced motion scrolling is not immediate");
        } finally {UiMotion.Enabled=enabled;window.Close();}
    }
}
