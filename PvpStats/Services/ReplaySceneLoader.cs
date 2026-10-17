using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Models.Models;
using PvpStats.Analysis;
namespace PvpStats.Services;
// Reads local client resources only. Never packages or exports game mesh assets.
internal sealed class ReplaySceneLoader {
    private readonly Plugin _plugin;
    private readonly CancellationToken _cancel;
    private readonly ReplayScene3D _scene=new();
    private readonly Dictionary<string,List<SceneTriangle>> _models=new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pathStack=new(StringComparer.OrdinalIgnoreCase);
    private readonly Vector2 _min,_max;
    private int _nodes;
    private readonly Action<string>? _progress;
    internal ReplaySceneLoader(Plugin plugin,Vector2 min,Vector2 max,CancellationToken cancel,Action<string>? progress=null) { _progress=progress;_plugin=plugin;_min=min-new Vector2(25);_max=max+new Vector2(25);_cancel=cancel; }
    internal ReplayScene3D Load(string background) {
        var path=background.Replace('\\','/').Trim('/');
        if(!path.StartsWith("bg/",StringComparison.OrdinalIgnoreCase))path="bg/"+path;
        var level=path.LastIndexOf("/level/",StringComparison.OrdinalIgnoreCase);
        var root=level>=0?path[..level]:Path.GetDirectoryName(path)?.Replace('\\','/');
        if(string.IsNullOrWhiteSpace(root)) { _scene.Status="地图资源入口不可用";return _scene; }
        int layouts=0;
        foreach(var name in new[]{"bg.lgb","planmap.lgb","planevent.lgb"}) {
            _cancel.ThrowIfCancellationRequested();
            try {
                var file=_plugin.DataManager.GetFile<LgbFile>(root+"/level/"+name);
                if(file==null)continue;
                layouts++;Visit(file.Layers,Matrix4x4.Identity,0);
            } catch(OperationCanceledException) { throw; }
            catch(Exception e) { _plugin.Log.Debug(e,"Replay scene layout unavailable: "+name); }
        }
        try {
            var terrain=_plugin.DataManager.GetFile<TeraFile>(root+"/bgplate/terrain.tera");
            if(terrain!=null) { _scene.TerrainPlates=(int)Math.Min(terrain.PlateCount,10000u);if(_scene.TerrainPlates>0)_scene.Limited=true; }
        } catch { }
        _scene.Status=_scene.Triangles.Count>0?$"纯色地图模型 {_scene.Models} · 三角面 {_scene.Triangles.Count:N0}":$"未读取到地图模型（布局 {layouts}）；仅显示观测棋子与坐标网格";
        return _scene;
    }
    private void Visit(LayerCommon.Layer[]? layers,Matrix4x4 parent,int depth) {
        if(layers==null||depth>6)return;
        foreach(var layer in layers)foreach(var instance in layer.InstanceObjects??[]) {
            _cancel.ThrowIfCancellationRequested();
            if(++_nodes>2000||_scene.Triangles.Count>=12000) { _scene.Limited=true;return; }
            var t=instance.Transform;
            Vector3 V(Lumina.Data.Parsing.Common.Vector3 v)=>new(v.X,v.Y,v.Z);
            var transform=ReplayScene3D.Transform(V(t.Translation),V(t.Rotation),V(t.Scale))*parent;
            if(instance.Object is LayerCommon.BGInstanceObject bg && bg.AssetPath?.EndsWith(".mdl",StringComparison.OrdinalIgnoreCase)==true)AddModel(bg.AssetPath,transform);
            if(instance.Object is LayerCommon.SharedGroupInstanceObject group && group.AssetPath?.EndsWith(".sgb",StringComparison.OrdinalIgnoreCase)==true && _pathStack.Add(group.AssetPath)) {
                try {
                    var shared=_plugin.DataManager.GetFile<SgbFile>(group.AssetPath);
                    if(shared?.LayerGroups!=null)foreach(var child in shared.LayerGroups)Visit(child.Layers,transform,depth+1);
                } catch(OperationCanceledException) { throw; }
                catch { _scene.MissingModels++; }
                finally { _pathStack.Remove(group.AssetPath); }
            }
        }
    }
    private void AddModel(string path,Matrix4x4 transform) {
        if(!_models.TryGetValue(path,out var source)) {
            if(_models.Count>=128) { _scene.Limited=true;return; }
            source=new();_models[path]=source;_scene.AssetPaths.Add(path);
            try {
                var file=_plugin.DataManager.GetFile<MdlFile>(path);
                if(file==null) { _scene.MissingModels++;return; }
                Model model;
                try { model=new(file,Model.ModelLod.Low,0);if(model.Meshes.Length==0)model=new(file,Model.ModelLod.High,0); }
                catch { model=new(file,Model.ModelLod.High,0); }
                foreach(var mesh in model.Meshes??[]) {
                    if(!mesh.Types.Contains(Mesh.MeshType.Main))continue;
                    for(int i=0;i+2<mesh.Indices.Length;i+=3) {
                        if(source.Count>=60000) { _scene.Limited=true;break; }
                        var ia=mesh.Indices[i];var ib=mesh.Indices[i+1];var ic=mesh.Indices[i+2];
                        if(ia>=mesh.Vertices.Length||ib>=mesh.Vertices.Length||ic>=mesh.Vertices.Length)continue;
                        if(mesh.Vertices[ia].Position is not {} a||mesh.Vertices[ib].Position is not {} b||mesh.Vertices[ic].Position is not {} c)continue;
                        source.Add(new(new(a.X,a.Y,a.Z),new(b.X,b.Y,b.Z),new(c.X,c.Y,c.Z),new(.32f,.4f,.48f,1)));
                    }
                }
            } catch { _scene.MissingModels++;return; }
        }
        bool added=false;var countBefore=_scene.Triangles.Count;
        var worldMin=new Vector3(float.PositiveInfinity);var worldMax=new Vector3(float.NegativeInfinity);
        foreach(var tri in source) {
            if(_scene.Triangles.Count>=12000) { _scene.Limited=true;break; }
            var a=Vector3.Transform(tri.A,transform);var b=Vector3.Transform(tri.B,transform);var c=Vector3.Transform(tri.C,transform);
            if(!ReplayScene3D.Finite(a)||!ReplayScene3D.Finite(b)||!ReplayScene3D.Finite(c))continue;
            var min=Vector3.Min(a,Vector3.Min(b,c));var max=Vector3.Max(a,Vector3.Max(b,c));
            if(max.X<_min.X||min.X>_max.X||max.Z<_min.Y||min.Z>_max.Y)continue;
            if(Vector3.Cross(b-a,c-a).LengthSquared()<.000001f)continue;
            _scene.Triangles.Add(new(a,b,c,tri.Color));added=true;worldMin=Vector3.Min(worldMin,min);worldMax=Vector3.Max(worldMax,max);
        }
        if(added) {
            if(_scene.ModelDiagnostics.Count<256)_scene.ModelDiagnostics.Add(new(path,_scene.Triangles.Count-countBefore,worldMin.X,worldMin.Y,worldMin.Z,worldMax.X,worldMax.Y,worldMax.Z,
                new[]{transform.M11,transform.M12,transform.M13,transform.M14,transform.M21,transform.M22,transform.M23,transform.M24,transform.M31,transform.M32,transform.M33,transform.M34,transform.M41,transform.M42,transform.M43,transform.M44}));
            _scene.Models++;_progress?.Invoke($"已读取 {_scene.Models} 个模型，{_scene.Triangles.Count:N0} 个三角面"); }
    }
}
