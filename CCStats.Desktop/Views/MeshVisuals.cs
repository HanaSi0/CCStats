using PvpStats.Analysis;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
namespace CCStats.Desktop.Views;
internal static class MeshVisuals {
    private static Material Palette(bool opaque) {
        var pixels=new byte[256*256*4];
        for(int i=0;i<65536;i++) {pixels[i*4]=(byte)(((i>>8)&15)*17);pixels[i*4+1]=(byte)(((i>>4)&15)*17);pixels[i*4+2]=(byte)((i&15)*17);pixels[i*4+3]=opaque?(byte)255:(byte)(((i>>12)&15)*17);}
        var bitmap=BitmapSource.Create(256,256,96,96,PixelFormats.Bgra32,null,pixels,1024);bitmap.Freeze();
        var brush=new ImageBrush(bitmap) {Stretch=Stretch.Fill,TileMode=TileMode.None,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,1,1)};RenderOptions.SetBitmapScalingMode(brush,BitmapScalingMode.NearestNeighbor);brush.Freeze();
        var material=new DiffuseMaterial(brush);material.Freeze();return material;
    }
    private static readonly Material Opaque=Palette(true),Alpha=Palette(false);
    internal static async Task VerifyPaletteAsync(Window owner) {
        var viewport=new System.Windows.Controls.Viewport3D {Width=128,Height=128,Camera=new PerspectiveCamera(new Point3D(0,0,4),new Vector3D(0,0,-1),new Vector3D(0,1,0),45)};
        var color=ReplaySceneGeometry.Rgba(.25f,.5f,.75f);
        var vertices=new[]{new ReplayVertex(new(-2,-2,0),color),new ReplayVertex(new(2,-2,0),color),new ReplayVertex(new(0,2,0),color),new ReplayVertex(new(-2,-2,-.5f),color),new ReplayVertex(new(2,-2,-.5f),color),new ReplayVertex(new(0,2,-.5f),color)};
        viewport.Children.Add(new ModelVisual3D {Content=new AmbientLight(Colors.White)});
        viewport.Children.Add(new ModelVisual3D {Content=Dynamic(vertices)});
        var window=new Window {Owner=owner,Content=viewport,SizeToContent=SizeToContent.WidthAndHeight,ShowInTaskbar=false,WindowStyle=WindowStyle.None};
        window.Show();await Task.Delay(150);viewport.UpdateLayout();
        var bitmap=new RenderTargetBitmap(128,128,96,96,PixelFormats.Pbgra32);bitmap.Render(viewport);var pixels=new byte[128*128*4];bitmap.CopyPixels(pixels,512,0);window.Close();
        int p=(64*128+64)*4;
        if(Math.Abs(pixels[p+2]-68)>40||Math.Abs(pixels[p+1]-136)>40||Math.Abs(pixels[p]-204)>40)throw new InvalidOperationException($"Palette render mismatch: {pixels[p+2]}, {pixels[p+1]}, {pixels[p]}");
    }
    public static Model3D StaticMap(ReplayMapMesh mesh) {
        var group=new Model3DGroup();group.Children.Add(Build(mesh.Vertices,mesh.Indices,0,mesh.OpaqueIndexCount,Opaque));
        if(mesh.OpaqueIndexCount<mesh.Indices.Length)group.Children.Add(Build(mesh.Vertices,mesh.Indices,mesh.OpaqueIndexCount,mesh.Indices.Length-mesh.OpaqueIndexCount,Alpha));
        group.Freeze();return group;
    }
    public static Model3D Dynamic(ReplayVertex[] vertices) {
        var opaque=new List<uint>();var alpha=new List<uint>();
        for(int i=0;i+2<vertices.Length;i+=3) {var target=(vertices[i].Color>>24)==255?opaque:alpha;target.Add((uint)i);target.Add((uint)(i+1));target.Add((uint)(i+2));}
        var group=new Model3DGroup();if(opaque.Count>0)group.Children.Add(Build(vertices,opaque.ToArray(),0,opaque.Count,Opaque));if(alpha.Count>0)group.Children.Add(Build(vertices,alpha.ToArray(),0,alpha.Count,Alpha));group.Freeze();return group;
    }
    private static GeometryModel3D Build(ReplayVertex[] vertices,uint[] indices,int start,int count,Material material) {
        var points=new Point3DCollection(count);var uv=new PointCollection(count);var triangles=new Int32Collection(count);
        void Add(ReplayVertex vertex,Point tex) {points.Add(new(vertex.Position.X,vertex.Position.Y,vertex.Position.Z));uv.Add(tex);triangles.Add(triangles.Count);}
        for(int i=start;i<start+count;i+=3) {
            var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];
            uint average=0;for(int channel=0;channel<4;channel++) {int shift=channel*8;var value=((a.Color>>shift)&255)+((b.Color>>shift)&255)+((c.Color>>shift)&255);average|=(value/3)<<shift;}
            int key=(int)(((average&255)>>4)|(((average>>8)&255)>>4)<<4|(((average>>16)&255)>>4)<<8|(((average>>24)&255)>>4)<<12);
            var tex=new Point(((key&255)+.5)/256,((key>>8)+.5)/256);
            Add(a,tex);Add(b,tex);Add(c,tex);
        }
        // WPF derives brush-space bounds from UVs. Degenerate anchors preserve the full atlas
        // even when the visible geometry uses a single colour or one narrow palette row.
        if(points.Count>0) {
            var anchor=points[0];int n=points.Count;
            foreach(var tex in new[]{new Point(0,0),new Point(1,0),new Point(1,1),new Point(0,1)}) {points.Add(anchor);uv.Add(tex);}
            triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);triangles.Add(n);triangles.Add(n+2);triangles.Add(n+3);
        }
        var geometry=new MeshGeometry3D {Positions=points,TextureCoordinates=uv,TriangleIndices=triangles};geometry.Freeze();var model=new GeometryModel3D(geometry,material) {BackMaterial=material};model.Freeze();return model;
    }
}
