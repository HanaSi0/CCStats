using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using PvpStats.Localization;
namespace CCStats.Desktop.Views;
internal static class TableFactory {
    public static DataGrid Create(IEnumerable rows,params (string Header,string Path)[] columns) {
        var grid=new DataGrid {ItemsSource=rows.Cast<object>().ToList(),AutoGenerateColumns=false,IsReadOnly=true,Height=430,EnableRowVirtualization=true,EnableColumnVirtualization=true,CanUserReorderColumns=true,CanUserResizeColumns=true,CanUserSortColumns=true,SelectionMode=DataGridSelectionMode.Single,SelectionUnit=DataGridSelectionUnit.FullRow,HeadersVisibility=DataGridHeadersVisibility.Column,RowHeight=40,GridLinesVisibility=DataGridGridLinesVisibility.None,BorderThickness=new Thickness(0),FocusVisualStyle=null};
        grid.SetResourceReference(Control.BackgroundProperty,"InputBrush");grid.SetResourceReference(Control.ForegroundProperty,"TextBrush");grid.SetResourceReference(DataGrid.RowBackgroundProperty,"SurfaceBrush");grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty,"AlternateBrush");
        var header=new Style(typeof(DataGridColumnHeader));header.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("ButtonBrush")));header.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("TextBrush")));header.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(12,10,12,10)));header.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));header.Setters.Add(new Setter(Control.FocusVisualStyleProperty,null));grid.ColumnHeaderStyle=header;
        var rowStyle=new Style(typeof(DataGridRow));rowStyle.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("TextBrush")));rowStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty,null));
        var hover=new Trigger {Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("HoverBrush")));rowStyle.Triggers.Add(hover);
        var selected=new Trigger {Property=DataGridRow.IsSelectedProperty,Value=true};selected.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("SelectionBrush")));selected.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("OnAccentBrush")));rowStyle.Triggers.Add(selected);grid.RowStyle=rowStyle;
        var cell=new Style(typeof(DataGridCell));cell.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));cell.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));cell.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(12,6,12,6)));cell.Setters.Add(new Setter(Control.FocusVisualStyleProperty,null));cell.Setters.Add(new Setter(Control.ForegroundProperty,new Binding("Foreground"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(DataGridRow),1)}));
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.BackgroundProperty,Brushes.Transparent);border.SetBinding(Border.PaddingProperty,new Binding("Padding"){RelativeSource=RelativeSource.TemplatedParent});var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(presenter);cell.Setters.Add(new Setter(Control.TemplateProperty,new ControlTemplate(typeof(DataGridCell)){VisualTree=border}));grid.CellStyle=cell;
        var textStyle=new Style(typeof(TextBlock));textStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty,new Binding("Foreground"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(DataGridCell),1)}));
        foreach(var (text,path) in columns)grid.Columns.Add(new DataGridTextColumn {ElementStyle=textStyle,Header=text,SortMemberPath=path,Binding=new Binding(path){TargetNullValue="—",Converter=new Display(path)},Width=new DataGridLength(1,DataGridLengthUnitType.SizeToCells),MinWidth=80,MaxWidth=360});
        grid.Sorting+=(_,e)=> {
            if(CollectionViewSource.GetDefaultView(grid.ItemsSource) is not ListCollectionView view)return;
            e.Handled=true;var direction=e.Column.SortDirection==ListSortDirection.Ascending?ListSortDirection.Descending:ListSortDirection.Ascending;
            foreach(var column in grid.Columns)column.SortDirection=null;e.Column.SortDirection=direction;view.CustomSort=new RowComparer(e.Column.SortMemberPath,direction==ListSortDirection.Ascending?1:-1);
        };
        return grid;
    }
    public static void OnRowActivated(DataGrid grid,Action<object> open) {
        grid.PreviewMouseDoubleClick+=(_,e)=> {
            if(e.ChangedButton!=MouseButton.Left||e.OriginalSource is not DependencyObject source||ItemsControl.ContainerFromElement(grid,source) is not DataGridRow row)return;
            if(row.Item!=null&&row.Item!=CollectionView.NewItemPlaceholder) {e.Handled=true;open(row.Item);}
        };
        grid.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter&&grid.SelectedItem is {} row) {e.Handled=true;open(row);}};
    }
    internal static void VerifySelection(DataGrid grid) {
        if(grid.SelectionUnit!=DataGridSelectionUnit.FullRow)throw new InvalidOperationException("Table selection must cover the full row");
        if(grid.Items.Count==0)return;
        grid.SelectedIndex=0;grid.ScrollIntoView(grid.Items[0]);grid.UpdateLayout();
        if(grid.ItemContainerGenerator.ContainerFromIndex(0) is not DataGridRow row||!row.IsSelected||row.Background!=DesktopTheme.Brush("SelectionBrush"))throw new InvalidOperationException("Selected row styling failed");
        if(grid.CellStyle==null)throw new InvalidOperationException("Cell selection styling missing");
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
