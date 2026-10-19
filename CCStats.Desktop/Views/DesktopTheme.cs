using System.Windows;
using System.Windows.Media;
namespace CCStats.Desktop.Views;
internal static class DesktopTheme {
    public static readonly string[] Names={"深海蓝","石墨灰","森林绿","紫罗兰","暮色酒红","青瓷墨色","暖砂","午夜蓝","北极蓝","雾海青","翡翠绿","竹影绿","橄榄绿","薰衣草","星云紫","莓果紫","樱桃红","赤铜红","琥珀金","焦糖棕","咖啡棕","玄武黑","雾银灰","深空灰"};
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
            "午夜蓝"=>new[]{"#080D1B","#131D34","#233754","#345F9E"},
            "北极蓝"=>new[]{"#0D1823","#1A2D40","#294C68","#286B92"},
            "雾海青"=>new[]{"#0E1D20","#1D3338","#2D5057","#267380"},
            "翡翠绿"=>new[]{"#0B1A17","#14372C","#225A47","#237C59"},
            "竹影绿"=>new[]{"#141D14","#273626","#3F5537","#4D713B"},
            "橄榄绿"=>new[]{"#1B1D12","#303723","#4F5735","#647139"},
            "薰衣草"=>new[]{"#191624","#302840","#4C4065","#725597"},
            "星云紫"=>new[]{"#110F24","#241D43","#3C3269","#5C4A9D"},
            "莓果紫"=>new[]{"#211322","#3A233A","#5B365B","#874E80"},
            "樱桃红"=>new[]{"#231117","#40212B","#653443","#A23F58"},
            "赤铜红"=>new[]{"#211613","#392722","#5F3B30","#98543B"},
            "琥珀金"=>new[]{"#211B0F","#372E1C","#574A2C","#896925"},
            "焦糖棕"=>new[]{"#211A15","#3B2D22","#5D4530","#8E633A"},
            "咖啡棕"=>new[]{"#1B1514","#302421","#4E3731","#7D5545"},
            "玄武黑"=>new[]{"#0C0D0F","#191B1F","#2C3038","#4E5B6B"},
            "雾银灰"=>new[]{"#1A1D22","#2A3038","#414B57","#586D83"},
            "深空灰"=>new[]{"#11161D","#1D2833","#2C4252","#3B677E"},
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
        foreach(Window window in Application.Current.Windows)WindowCaption.Apply(window);
    }
}
