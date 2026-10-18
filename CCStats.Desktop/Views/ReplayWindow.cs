using CCStats.Core.Library;
using PvpStats.Analysis;
using PvpStats.Localization;
using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.Globalization;
using NVector=System.Numerics.Vector3;
namespace CCStats.Desktop.Views;
internal sealed class ReplayWindow : Window {
    private readonly TaskCompletionSource _ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ReplayBundle _bundle;
    private readonly DateTime _origin;
    private readonly double _duration;
    private readonly Viewport3D _viewport=new();
    private readonly ModelVisual3D _mapVisual=new(),_dynamic=new();
    private readonly Overlay _overlay;
    private readonly ReplayOrbitCamera _camera=new();
    private readonly Slider _time=new() {Minimum=0,Margin=new Thickness(8)};
    private readonly TextBlock _info=new() {Foreground=Brushes.LightSteelBlue};
    private readonly TextBlock _progress=new() {Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,8,0,8)};
    private readonly StackPanel _left=new(),_right=new();
    private readonly Grid _canvas=new() {Background=new SolidColorBrush(Color.FromRgb(7,13,23)),ClipToBounds=true};
    private readonly DispatcherTimer _timer=new() {Interval=TimeSpan.FromMilliseconds(33)};
    private readonly Stopwatch _clock=Stopwatch.StartNew();
    private ReplayMapMesh? _mesh;
    private bool _playing,_busy,_closed,_threeD=true,_effects=true,_trail=true,_clip=true;
    private double _seconds,_speed=1,_lastClock,_rendered=-1;
    private Point _lastMouse;
    private BitmapImage? _mapImage;
    public ReplayWindow(ReplayBundle bundle,double startSeconds=0) {
        _bundle=bundle;_origin=bundle.Match.StartUtc;
        _duration=Math.Max(1,bundle.Match.DurationSeconds>0?bundle.Match.DurationSeconds:bundle.Data.Frames.LastOrDefault() is {} last?(last.Timestamp-_origin).TotalSeconds:1);
        _seconds=Math.Clamp(startSeconds,0,_duration);
        Title="地图回放 · "+bundle.Match.Map;Width=1450;Height=920;MinWidth=1000;MinHeight=680;Background=(Brush)Application.Current.Resources["BackgroundBrush"];
        var root=new DockPanel {Margin=new Thickness(12)};var top=new StackPanel();var actions=new WrapPanel();
        Button Button(string text,Action click) {var b=new Button {Content=text};b.Click+=(_,_)=>click();return b;}
        actions.Children.Add(Button("播放 / 暂停",()=>_playing=!_playing));actions.Children.Add(Button("回到开始",()=>{_seconds=0;_playing=false;_rendered=-1;}));
        actions.Children.Add(Button("上一帧",()=>Step(-1)));actions.Children.Add(Button("下一帧",()=>Step(1)));
        var speed=new ComboBox {ItemsSource=new[]{"0.5×","1×","2×","4×"},SelectedIndex=1,Width=85,Margin=new Thickness(0,0,8,8)};speed.SelectionChanged+=(_,_)=>_speed=new[]{.5,1,2,4}[speed.SelectedIndex];actions.Children.Add(speed);
        actions.Children.Add(Button("2D / 3D",()=>{_threeD=!_threeD;_viewport.Visibility=_threeD?Visibility.Visible:Visibility.Hidden;_overlay?.InvalidateVisual();_rendered=-1;}));
        actions.Children.Add(Button("重置视角",ResetCamera));
        void Toggle(string label,bool initial,Action<bool> set) {var check=new CheckBox {Content=label,IsChecked=initial,Foreground=Brushes.White,Margin=new Thickness(4,7,12,0)};check.Click+=(_,_)=>{set(check.IsChecked==true);_rendered=-1;};actions.Children.Add(check);}
        Toggle("水晶轨迹",true,v=>_trail=v);Toggle("技能与地图事件",true,v=>_effects=v);Toggle("范围裁剪",true,v=>_clip=v);
        top.Children.Add(actions);_time.Maximum=_duration;_time.ValueChanged+=(_,_)=>{if(Math.Abs(_time.Value-_seconds)>.0001){_seconds=_time.Value;_playing=false;_rendered=-1;}};top.Children.Add(_time);top.Children.Add(_info);top.Children.Add(_progress);DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(220)});body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});body.ColumnDefinitions.Add(new(){Width=new GridLength(220)});
        body.Children.Add(new ScrollViewer {Content=_left,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Grid.SetColumn(_canvas,1);body.Children.Add(_canvas);var right=new ScrollViewer {Content=_right,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(right,2);body.Children.Add(right);
        _viewport.Children.Add(new ModelVisual3D {Content=new AmbientLight(Colors.White)});
        _viewport.Children.Add(_mapVisual);_viewport.Children.Add(_dynamic);_canvas.Children.Add(_viewport);_overlay=new Overlay(this);_canvas.Children.Add(_overlay);
        _canvas.MouseDown+=(_,e)=>{_lastMouse=e.GetPosition(_canvas);_canvas.CaptureMouse();_camera.BeginDrag(e.ChangedButton==MouseButton.Right,new((float)_lastMouse.X,(float)_lastMouse.Y),SizeVector());};
        _canvas.MouseMove+=(_,e)=>{if(_canvas.IsMouseCaptured){var p=e.GetPosition(_canvas);_camera.Drag(new((float)p.X,(float)p.Y),SizeVector());_lastMouse=p;ApplyCamera();}};
        _canvas.MouseUp+=(_,_)=>{_camera.EndDrag();_canvas.ReleaseMouseCapture();};_canvas.MouseWheel+=(_,e)=>{var p=e.GetPosition(_canvas);_camera.Zoom(e.Delta/120f,new((float)p.X,(float)p.Y),SizeVector());};_canvas.SizeChanged+=(_,_)=>ApplyCamera();
        IsVisibleChanged+=(_,_)=>{if(_closed)return;if(IsVisible&&_ready.Task.IsCompletedSuccessfully){_lastClock=_clock.Elapsed.TotalSeconds;_timer.Start();}else _timer.Stop();};
        root.Children.Add(body);Content=root;_timer.Tick+=async (_,_)=>await TickAsync();Closed+=(_,_)=>{_closed=true;_timer.Stop();_mapVisual.Content=null;_dynamic.Content=null;};Loaded+=async (_,_)=>await LoadAsync();
    }
    internal async Task VerifyAsync(string screenshot) {
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(40));
        if(_bundle.Data.DirectorObserved&&_progress.Text.StartsWith("暂无",StringComparison.Ordinal))throw new InvalidOperationException("Observed director progress is incorrectly hidden");
        if(_mapVisual.Content==null||_viewport.Camera==null)throw new InvalidOperationException("3D map or camera missing");
        _threeD=false;_viewport.Visibility=Visibility.Hidden;_overlay.InvalidateVisual();await Task.Delay(100);
        if(_mapImage==null)throw new InvalidOperationException("Official 2D map missing");
        _threeD=true;_viewport.Visibility=Visibility.Visible;_overlay.InvalidateVisual();UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=System.IO.File.Create(screenshot);encoder.Save(file);
    }
    private Vector2 SizeVector()=>new((float)Math.Max(1,_canvas.ActualWidth),(float)Math.Max(1,_canvas.ActualHeight));
    private async Task LoadAsync() {
        try {
            if(ReplayMapMesh.MapKey(_bundle.Match.Territory) is {} key) {
                _mesh=await Task.Run(()=>ReplayMapMesh.LoadEmbedded(_bundle.Match.Territory));var model=await Task.Run(()=>MeshVisuals.StaticMap(_mesh));if(_closed)return;_mapVisual.Content=model;
                _mapImage=new BitmapImage(new Uri("pack://application:,,,/Assets/Maps/"+key+".png"));
            }
            ResetCamera();_lastClock=_clock.Elapsed.TotalSeconds;if(IsVisible)_timer.Start();await TickAsync();_ready.TrySetResult();
        } catch(Exception ex){_info.Text="地图读取失败："+ex.Message;_ready.TrySetException(ex);}
    }
    private void ResetCamera() {
        var target=_mesh==null?NVector.Zero:(_mesh.Min+_mesh.Max)/2;target.Y=_mesh?.EstimatedHeight(target.X,target.Z)??0;
        var extent=_mesh==null?150:Math.Max(_mesh.Max.X-_mesh.Min.X,_mesh.Max.Z-_mesh.Min.Z);float distance=extent;
        for(int pass=0;pass<8;pass++) {
            _camera.Reset(target,distance);if(_mesh==null)break;float needed=0;
            int step=Math.Max(1,_mesh.Vertices.Length/8192);
            for(int i=0;i<_mesh.Vertices.Length;i+=step) {
                var point=_mesh.Vertices[i].Position;var projected=Vector4.Transform(new Vector4(point,1),_camera.ViewProjection(SizeVector()));
                if(projected.W<=.01f){needed=Math.Max(needed,2);continue;}
                needed=Math.Max(needed,Math.Max(Math.Abs(projected.X/projected.W),Math.Abs(projected.Y/projected.W)));
            }
            if(needed<=.90f)break;distance=Math.Min(600,distance*Math.Max(1.05f,needed/.90f));
        }
        ApplyCamera();
    }
    private static Matrix3D Matrix(Matrix4x4 m)=>new(m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44);
    private void ApplyCamera() {
        var eye=_camera.Target+new NVector(MathF.Sin(_camera.Yaw)*MathF.Cos(_camera.Pitch),MathF.Sin(_camera.Pitch),MathF.Cos(_camera.Yaw)*MathF.Cos(_camera.Pitch))*_camera.Distance;
        var size=SizeVector();_viewport.Camera=new MatrixCamera(Matrix(Matrix4x4.CreateLookAt(eye,_camera.Target,NVector.UnitY)),Matrix(Matrix4x4.CreatePerspectiveFieldOfView(.82f,size.X/size.Y,.2f,3000)));_overlay.InvalidateVisual();
    }
    private void Step(int direction) {var time=ReplayObservation.Step(_bundle.Data,_origin.AddSeconds(_seconds),direction);if(time.HasValue)_seconds=Math.Clamp((time.Value-_origin).TotalSeconds,0,_duration);_playing=false;_rendered=-1;}
    private async Task TickAsync() {
        double clock=_clock.Elapsed.TotalSeconds;float delta=(float)Math.Min(.1,clock-_lastClock);_lastClock=clock;
        if(_playing)_seconds=Math.Min(_duration,_seconds+delta*_speed);if(_seconds>=_duration)_playing=false;
        if(_camera.Update(delta,SizeVector()))ApplyCamera();_time.Value=_seconds;
        _info.Text=$"{TimeSpan.FromSeconds(_seconds):mm\\:ss} / {TimeSpan.FromSeconds(_duration):mm\\:ss}　{_bundle.Data.Frames.Count:N0} 帧　左键平移 · 右键旋转 · 滚轮缩放";
        if(_busy||Math.Abs(_rendered-_seconds)<.06)return;_busy=true;var seconds=_seconds;_rendered=seconds;
        try {
            var now=_origin.AddSeconds(seconds);var frame=ReplayInterpolation.Sample(_bundle.Data,now);
            _progress.Text=frame==null||frame.AstraProgress<0||frame.UmbraProgress<0||(_bundle.Match.Observation&&!_bundle.Data.DirectorObserved)?"暂无该时刻的完整观测":$"星极队 {frame.AstraProgress/10f:F1}%　　◆　　灵极队 {frame.UmbraProgress/10f:F1}%";
            DrawTeam(_left,frame,1,now);DrawTeam(_right,frame,2,now);
            if(_threeD) {var model=await Task.Run(()=>MeshVisuals.Dynamic(ReplaySceneBuilder.Build(_bundle,_mesh,now,_effects,_trail,_clip)));if(!_closed)_dynamic.Content=model;}
            _overlay.InvalidateVisual();
        } catch(Exception ex){_info.Text="本帧未显示："+ex.Message;}finally{_busy=false;}
    }
    private static void DrawTeam(StackPanel panel,MovementFrame? frame,int team,DateTime now) {
        panel.Children.Clear();panel.Children.Add(new TextBlock {Text=team==1?"星极队":"灵极队",Foreground=team==1?Brushes.DeepSkyBlue:Brushes.Coral,FontSize=18,Margin=new Thickness(8)});
        var players=frame?.Players.Where(p=>p.Team==team||(team==1&&p.Team==0)).ToArray()??Array.Empty<PlayerPosition>();
        foreach(var p in players) {
            var card=new StackPanel {Margin=new Thickness(6,5,6,5)};
            card.Children.Add(new TextBlock {Text=JobNames.Name(p.ClassJobId)+" · "+p.Player,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap});
            card.Children.Add(new ProgressBar {Minimum=0,Maximum=Math.Max(1,p.MaxHP),Value=p.HP,Height=10,Margin=new Thickness(0,5,0,2)});
            card.Children.Add(new TextBlock {Text=$"{p.HP:N0}/{p.MaxHP:N0}"+(p.Team==0?" · 队伍未知":""),Foreground=Brushes.LightSteelBlue,FontSize=11});
            var buffs=p.Statuses.Select(s=>(Status:s,Remaining:s.Remaining-(now-p.StatusTimestamp).TotalSeconds)).Where(x=>x.Status.Remaining<=0||x.Remaining>0).Select(x=>ChineseText.StatusName(x.Status.Id,$"状态 {x.Status.Id}")+(x.Status.Remaining>0?$" {Math.Ceiling(x.Remaining):0}":""));
            card.Children.Add(new TextBlock {Text=string.Join(" · ",buffs),Foreground=Brushes.LightSteelBlue,FontSize=11,TextWrapping=TextWrapping.Wrap});panel.Children.Add(card);
        }
    }
    private sealed class Overlay(ReplayWindow owner):FrameworkElement {
        protected override void OnRender(DrawingContext dc) {
            base.OnRender(dc);var now=owner._origin.AddSeconds(owner._seconds);var frame=ReplayInterpolation.Sample(owner._bundle.Data,now);var size=owner.SizeVector();
            var data=owner._bundle.Data;
            Point At(PlayerPosition p) {
                if(owner._threeD) {
                    var v=Vector4.Transform(new Vector4(p.X,(p.Y??owner._mesh?.EstimatedHeight(p.X,p.Z)??0)+3.4f,p.Z,1),owner._camera.ViewProjection(size));if(v.W<=0)return new(-100,-100);return new((v.X/v.W+1)*ActualWidth/2,(1-v.Y/v.W)*ActualHeight/2);
                }
                var metadata=MapMetadata.For(owner._bundle.Match.Territory);int factor=data.MapSizeFactor>0?data.MapSizeFactor:metadata?.Factor??400;var uv=DetailedAnalysis.WorldToUv(p.X,p.Z,factor,data.MapSizeFactor>0?data.MapOffsetX:metadata?.OffsetX??0,data.MapSizeFactor>0?data.MapOffsetY:metadata?.OffsetY??0);
                double side=Math.Min(ActualWidth,ActualHeight);return new((ActualWidth-side)/2+uv.X*side,(ActualHeight-side)/2+uv.Y*side);
            }
            if(!owner._threeD) {double side=Math.Min(ActualWidth,ActualHeight);if(owner._mapImage!=null)dc.DrawImage(owner._mapImage,new Rect((ActualWidth-side)/2,(ActualHeight-side)/2,side,side));
                if(frame?.Crystal is {} crystal)dc.DrawEllipse(Brushes.Gold,new Pen(Brushes.White,1),At(new PlayerPosition {X=crystal.X,Z=crystal.Z}),7,7);
            }
            foreach(var p in frame?.Players??Enumerable.Empty<PlayerPosition>()) {
                var at=At(p);if(at.X<0||at.Y<0||at.X>ActualWidth||at.Y>ActualHeight)continue;var brush=p.Team==1?Brushes.DeepSkyBlue:p.Team==2?Brushes.Coral:Brushes.LightGray;
                if(!owner._threeD){dc.DrawEllipse(brush,null,at,6,6);dc.DrawLine(new Pen(brush,2),at,at+new System.Windows.Vector(Math.Sin(p.Heading)*18,Math.Cos(p.Heading)*18));}
                dc.DrawText(new FormattedText(JobNames.Initial(p.ClassJobId),CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),14,brush,1),at+new System.Windows.Vector(-7,-18));
            }
        }
    }
}
