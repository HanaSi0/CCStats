using PvpStats.Analysis;
using System.Windows.Media.Media3D;
namespace CCStats.Desktop.Views;
internal static class ReplayMapCache {
    // Frozen geometry can be shared by viewports; keep at most two map loads in the cache.
    private static readonly Dictionary<uint,Task<(ReplayMapMesh Mesh,Model3D Model)>> Maps=new();
    private static readonly LinkedList<uint> Order=new();
    public static Task<(ReplayMapMesh Mesh,Model3D Model)> LoadAsync(uint territory) {
        lock(Maps) {
            if(Maps.TryGetValue(territory,out var cached)&&!cached.IsFaulted&&!cached.IsCanceled){Order.Remove(territory);Order.AddLast(territory);return cached;}
            var pending=Task.Run(()=>{var mesh=ReplayMapMesh.LoadEmbedded(territory);return (mesh,MeshVisuals.StaticMap(mesh));});
            Maps[territory]=pending;Order.Remove(territory);Order.AddLast(territory);
            while(Order.Count>2){Maps.Remove(Order.First!.Value);Order.RemoveFirst();}
            return pending;
        }
    }
}
