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
        var brush=new ImageBrush(bitmap) {Stretch=Stretch.Fill,TileMode=TileMode.None};RenderOptions.SetBitmapScalingMode(brush,BitmapScalingMode.NearestNeighbor);brush.Freeze();
        var material=new EmissiveMaterial(brush);material.Freeze();return material;
    }
    private static readonly Material Opaque=Palette(true),Alpha=Palette(false);
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
        var geometry=new MeshGeometry3D {Positions=points,TextureCoordinates=uv,TriangleIndices=triangles};geometry.Freeze();var model=new GeometryModel3D(geometry,material) {BackMaterial=material};model.Freeze();return model;
    }
}
