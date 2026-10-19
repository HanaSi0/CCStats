using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lumina;
using Lumina.Data.Files;
namespace CCStats.Desktop.Views;
// Only local game resources are read. Every texture is cached, frozen and loaded off the UI thread.
internal static class StatusIcons {
    private static readonly SemaphoreSlim Gate=new(1);
    private static readonly ConcurrentDictionary<uint,Task<BitmapSource?>> Images=new();
    private static GameData? _game;private static string _folder="",_cache="";
    public static int Generation {get;private set;}
    public static string Error {get;private set;}="等待本机游戏资源";
    public static async Task ConfigureAsync(string directory,string? folder) {
        if(string.IsNullOrWhiteSpace(folder)||!Directory.Exists(Path.Combine(folder,"sqpack")))return;
        await Gate.WaitAsync();try {
            var versionPath=Path.Combine(folder,"ffxivgame.ver");var version=File.Exists(versionPath)?await File.ReadAllTextAsync(versionPath):"";
            var identity=folder+"|"+version;if(identity==_folder)return;
            var game=await Task.Run(()=>new GameData(Path.Combine(folder,"sqpack")));
            _game?.Dispose();_game=game;_folder=identity;
            _cache=Path.Combine(directory,"status-icons",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16]);Directory.CreateDirectory(_cache);
            Images.Clear();Generation++;Error="";
        }catch(Exception ex) when(ex is not OutOfMemoryException){Error="状态图标暂不可用："+ex.Message;}finally{Gate.Release();}
    }
    public static Task<BitmapSource?> LoadAsync(uint icon) {
        if(icon==0||_game==null)return Task.FromResult<BitmapSource?>(null);
        // A bounded cache; previously returned frozen images remain valid for active cards.
        if(Images.Count>768)Images.Clear();return Images.GetOrAdd(icon,id=>Task.Run(()=>LoadCoreAsync(id)));
    }
    private static async Task<BitmapSource?> LoadCoreAsync(uint icon) {
        await Gate.WaitAsync();try {
            var path=Path.Combine(_cache,icon+".png");if(File.Exists(path)){try{var cached=new BitmapImage();cached.BeginInit();cached.CacheOption=BitmapCacheOption.OnLoad;cached.UriSource=new Uri(path);cached.EndInit();cached.Freeze();return cached;}catch(Exception ex) when(ex is IOException or NotSupportedException or System.IO.FileFormatException){File.Delete(path);}}
            var texture=_game?.GetFile<TexFile>($"ui/icon/{icon/1000*1000:000000}/{icon:000000}.tex");if(texture==null)return null;
            if(texture.Header.Width is 0 or >512||texture.Header.Height is 0 or >512)return null;
            var bitmap=BitmapSource.Create(texture.Header.Width,texture.Header.Height,96,96,PixelFormats.Bgra32,null,texture.ImageData,texture.Header.Width*4);bitmap.Freeze();
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);return bitmap;
        }catch(Exception ex) when(ex is not OutOfMemoryException){Error="状态图标暂不可用："+ex.Message;return null;}finally{Gate.Release();}
    }
}
