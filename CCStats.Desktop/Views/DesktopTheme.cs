using System.Windows;
using System.Windows.Media;
namespace CCStats.Desktop.Views;
internal static class DesktopTheme {
    public static readonly string[] Names={"深海蓝","石墨灰","森林绿","紫罗兰","暮色酒红","青瓷墨色","暖砂"};
    public static readonly string[] Keys={"BackgroundBrush","SurfaceBrush","ButtonBrush","AccentBrush","TextBrush","MutedBrush","HoverBrush","SelectionBrush","BorderBrush","InputBrush","AlternateBrush","OnAccentBrush"};
    public static Brush Brush(string key)=>(Brush)Application.Current.Resources[key];
    public static void Apply(string theme,IReadOnlyDictionary<string,string> custom) {
        var palette=theme switch {
            "石墨灰"=>new[]{"#141619","#20242A","#343B46","#526985"},
            "森林绿"=>new[]{"#0E1917","#182E29","#244D42","#216D58"},
            "紫罗兰"=>new[]{"#151020","#291D3C","#483263","#71469E"},
            "暮色酒红"=>new[]{"#1B1118","#30212B","#573345","#91445F"},
            "青瓷墨色"=>new[]{"#0B191E","#142A31","#214650","#1D6878"},
            "暖砂"=>new[]{"#1C1814","#2F2821","#514334","#85602E"},
            _=>new[]{"#0D1220","#171F33","#29395D","#245F9F"}
        };
        Color Parse(string value)=>(Color)ColorConverter.ConvertFromString(value);
        var colors=Keys.Take(4).Select((k,i)=>(k,v:Parse(palette[i]))).ToDictionary(p=>p.k,p=>p.v);
        colors["TextBrush"]=Parse("#ECF2FF");colors["MutedBrush"]=Parse("#AAB8CE");
        foreach(var pair in custom.Where(p=>colors.ContainsKey(p.Key)))try{colors[pair.Key]=Parse(pair.Value);}catch(FormatException) { }
        Color Mix(Color a,Color b,double t)=>Color.FromRgb((byte)(a.R+(b.R-a.R)*t),(byte)(a.G+(b.G-a.G)*t),(byte)(a.B+(b.B-a.B)*t));
        colors["HoverBrush"]=Mix(colors["ButtonBrush"],colors["AccentBrush"],.4);
        colors["SelectionBrush"]=colors["AccentBrush"];colors["BorderBrush"]=Mix(colors["SurfaceBrush"],colors["TextBrush"],.25);
        colors["InputBrush"]=Mix(colors["BackgroundBrush"],colors["SurfaceBrush"],.6);colors["AlternateBrush"]=Mix(colors["SurfaceBrush"],colors["ButtonBrush"],.25);
        foreach(var pair in custom.Where(p=>Keys.Contains(p.Key)&&!p.Key.Equals("OnAccentBrush")))try{colors[pair.Key]=Parse(pair.Value);}catch(FormatException) { }
        double Linear(byte b) {double v=b/255d;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);}
        var selected=colors["SelectionBrush"];double lum=.2126*Linear(selected.R)+.7152*Linear(selected.G)+.0722*Linear(selected.B);
        colors["OnAccentBrush"]=lum>.179?Colors.Black:Colors.White;
        foreach(var pair in colors) {
            if(Application.Current.Resources[pair.Key] is SolidColorBrush existing&&!existing.IsFrozen)existing.Color=pair.Value;
            else Application.Current.Resources[pair.Key]=new SolidColorBrush(pair.Value);
        }
    }
}
