using System.Windows.Controls.Primitives;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace CCStats.Desktop.Views;
internal static class TableFactory {
    public static DataGrid Create(System.Collections.IEnumerable rows,params (string Header,string Path)[] columns) {
        var grid=new DataGrid {ItemsSource=rows,AutoGenerateColumns=false,IsReadOnly=true,Height=430,EnableRowVirtualization=true,EnableColumnVirtualization=true,CanUserReorderColumns=true,CanUserResizeColumns=true,CanUserSortColumns=true,SelectionMode=DataGridSelectionMode.Single,Background=new SolidColorBrush(Color.FromRgb(17,25,43)),Foreground=Brushes.White,RowBackground=new SolidColorBrush(Color.FromRgb(23,31,51)),AlternatingRowBackground=new SolidColorBrush(Color.FromRgb(28,39,62)),GridLinesVisibility=DataGridGridLinesVisibility.Horizontal,BorderThickness=new Thickness(0)};
        var header=new Style(typeof(DataGridColumnHeader));header.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(38,55,84))));header.Setters.Add(new Setter(Control.ForegroundProperty,Brushes.White));header.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10)));grid.ColumnHeaderStyle=header;
        foreach(var (text,path) in columns)grid.Columns.Add(new DataGridTextColumn {Header=text,Binding=new Binding(path){TargetNullValue="—"},Width=new DataGridLength(1,DataGridLengthUnitType.SizeToCells),MinWidth=80});
        return grid;
    }
}
