using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using PvpStats.Localization;
namespace CCStats.Desktop.Views;
internal static class TableFactory {
    public static DataGrid Create(IEnumerable rows,params (string Header,string Path)[] columns) {
        var grid=new DataGrid {ItemsSource=rows.Cast<object>().ToList(),AutoGenerateColumns=false,IsReadOnly=true,Height=430,EnableRowVirtualization=true,EnableColumnVirtualization=true,CanUserReorderColumns=true,CanUserResizeColumns=true,CanUserSortColumns=true,SelectionMode=DataGridSelectionMode.Single,Background=new SolidColorBrush(Color.FromRgb(17,25,43)),Foreground=Brushes.White,RowBackground=new SolidColorBrush(Color.FromRgb(23,31,51)),AlternatingRowBackground=new SolidColorBrush(Color.FromRgb(28,39,62)),GridLinesVisibility=DataGridGridLinesVisibility.Horizontal,BorderThickness=new Thickness(0)};
        var header=new Style(typeof(DataGridColumnHeader));header.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(38,55,84))));header.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));header.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10)));grid.ColumnHeaderStyle=header;
        foreach(var (text,path) in columns)grid.Columns.Add(new DataGridTextColumn {Header=text,SortMemberPath=path,Binding=new Binding(path){TargetNullValue="—",Converter=new Display(path)},Width=new DataGridLength(1,DataGridLengthUnitType.SizeToCells),MinWidth=80});
        grid.Sorting+=(_,e)=> {
            if(CollectionViewSource.GetDefaultView(grid.ItemsSource) is not ListCollectionView view)return;
            e.Handled=true;var direction=e.Column.SortDirection==ListSortDirection.Ascending?ListSortDirection.Descending:ListSortDirection.Ascending;
            foreach(var column in grid.Columns)column.SortDirection=null;e.Column.SortDirection=direction;view.CustomSort=new RowComparer(e.Column.SortMemberPath,direction==ListSortDirection.Ascending?1:-1);
        };
        return grid;
    }
    internal static void CheckSorting() {
        var rows=new[]{new {StartLabel="2026-10-19 10:00",Damage="10"},new {StartLabel="2026-10-18 10:00",Damage="2"}};
        if(new RowComparer("Damage",1).Compare(rows[0],rows[1])<=0)throw new InvalidOperationException("Numeric sorting failed");
        if(new RowComparer("StartLabel",1).Compare(rows[0],rows[1])<=0)throw new InvalidOperationException("Date sorting failed");
    }
    private sealed class RowComparer(string path,int direction):IComparer {
        private static object? Value(object? row,string key)=>row?.GetType().GetProperty(key)?.GetValue(row);
        private static bool Numeric(string text,out double number) {var token=Regex.Match(text,@"^[-+]?[\d,.]+").Value;return double.TryParse(token,NumberStyles.Number,CultureInfo.CurrentCulture,out number);}
        public int Compare(object? x,object? y) {
            var a=Value(x,path);var b=Value(y,path);
            if(a==null||b==null)return direction*(a==null?(b==null?0:-1):1);
            if(a is string sa&&b is string sb) {if(path=="Duration"&&TimeSpan.TryParseExact("0:"+sa,@"h\:m\:ss",CultureInfo.InvariantCulture,out var ta)&&TimeSpan.TryParseExact("0:"+sb,@"h\:m\:ss",CultureInfo.InvariantCulture,out var tb))return direction*ta.CompareTo(tb);if(path is "WinRate" or "Damage" or "Taken" or "Healing" or "Crystal" or "Kills" or "Deaths" or "Assists" or "Rating" or "Rank" or "Victories" or "RankChange" && Numeric(sa,out var na)&&Numeric(sb,out var nb))return direction*na.CompareTo(nb);return direction*StringComparer.CurrentCultureIgnoreCase.Compare(sa,sb);}
            return direction*Comparer.DefaultInvariant.Compare(a,b);
        }
    }
    private sealed class Display(string path):IValueConverter {
        public object Convert(object value,Type type,object parameter,CultureInfo culture) {
            if(value==null)return "—";
            if(path=="Team"&&value is int team)return team switch {1=>"星极队",2=>"灵极队",_=>"未知"};
            if(path=="Favorite"&&value is bool favorite)return favorite?"★":"—";
            if(path=="Tier"&&value is string tier)return ChineseText.Text(tier);
            if(value is DateTime time)return time.ToLocalTime().ToString("HH:mm:ss.fff",culture);
            if(value is double d)return double.IsFinite(d)?d.ToString(path=="CrystalSeconds"||path=="ControlSeconds"?"N1":"N0",culture):"—";
            if(value is int i)return i.ToString("N0",culture);if(value is long l)return l.ToString("N0",culture);return value;
        }
        public object ConvertBack(object value,Type type,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
}
