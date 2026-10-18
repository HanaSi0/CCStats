using System.Windows;
using System.Windows.Media;
using System.Globalization;
namespace CCStats.Desktop.Views;
internal sealed class TrendView : FrameworkElement {
    private readonly (DateTime Time,double Value)[] _points;
    public TrendView((DateTime,double)[] points)=>_points=points;
    protected override void OnRender(DrawingContext dc) {
        base.OnRender(dc);dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(17,25,43)),null,new Rect(0,0,ActualWidth,ActualHeight));if(_points.Length==0)return;
        var min=_points.Min(p=>p.Value);var max=_points.Max(p=>p.Value);var span=Math.Max(1,max-min);
        var pen=new Pen(Brushes.DeepSkyBlue,2);Point? last=null;
        for(int i=0;i<_points.Length;i++) {
            var p=new Point(35+Math.Max(1,ActualWidth-70)*i/Math.Max(1,_points.Length-1),30+Math.Max(1,ActualHeight-60)*(1-(_points[i].Value-min)/span));
            if(last.HasValue)dc.DrawLine(pen,last.Value,p);dc.DrawEllipse(Brushes.Gold,null,p,3,3);last=p;
            if(i==0||i==_points.Length-1||_points.Length<8)dc.DrawText(new FormattedText(_points[i].Value.ToString("N0"),CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),12,Brushes.White,1),p+new Vector(4,-17));
        }
    }
}
