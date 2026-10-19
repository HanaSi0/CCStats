using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CCStats.Core.Library;
using PvpStats.Analysis;
namespace CCStats.Desktop.Views;
internal sealed class ReplayTeamPanel:StackPanel {
    private readonly Dictionary<string,PlayerCard> _cards=new();private readonly int _team;
    public ReplayTeamPanel(int team){_team=team;Children.Add(new TextBlock {Text=team==1?"星极队":"灵极队",Foreground=team==1?Brushes.DeepSkyBlue:Brushes.Coral,FontSize=18,Margin=new Thickness(8)});}
    public void Update(MovementFrame? frame,DateTime now){var players=frame?.Players.Where(p=>p.Team==_team||(_team==1&&p.Team==0)).ToArray()??Array.Empty<PlayerPosition>();
        var names=players.Select(p=>p.Player).ToHashSet();foreach(var key in _cards.Keys.Where(k=>!names.Contains(k)).ToArray()){Children.Remove(_cards[key]);_cards.Remove(key);}
        foreach(var p in players){if(!_cards.TryGetValue(p.Player,out var card)){card=new();_cards.Add(p.Player,card);Children.Add(card);}card.Update(p,now);}
    }
    internal static void Verify(){var p=new PlayerPosition {Player="HUD Test",Team=1,HP=500,MaxHP=1000,MP=4200,MaxMP=10000};var frame=new MovementFrame {Players=new(){p}};var panel=new ReplayTeamPanel(1);panel.Update(frame,DateTime.UtcNow);var card=panel.Children[1];panel.Update(frame,DateTime.UtcNow);if(!ReferenceEquals(card,panel.Children[1]))throw new InvalidOperationException("Replay cards are rebuilt each tick");var texts=((StackPanel)((Border)card).Child).Children.OfType<TextBlock>().Select(t=>t.Text).ToArray();if(!texts.Any(t=>t.StartsWith("MP ")&&t.Contains(4200.ToString("N0"))))throw new InvalidOperationException("Mana HUD is missing");p.MP=null;p.MaxMP=null;panel.Update(frame,DateTime.UtcNow);if(!((StackPanel)((Border)card).Child).Children.OfType<TextBlock>().Any(t=>t.Text=="MP · 未记录"))throw new InvalidOperationException("Missing old mana became a fake value");}
    private sealed class PlayerCard:Border {
        private readonly TextBlock _name=new(){TextWrapping=TextWrapping.Wrap,FontSize=12},_hpText=new(){FontSize=10},_mpText=new(){FontSize=10};
        private readonly ProgressBar _hp=new(){Height=5,Foreground=Brushes.MediumSeaGreen},_mp=new(){Height=4,Foreground=Brushes.DeepSkyBlue};
        private readonly WrapPanel _statuses=new();private string _signature="";
        public PlayerCard(){Margin=new Thickness(4,4,4,6);Padding=new Thickness(9);CornerRadius=new CornerRadius(9);SetResourceReference(BackgroundProperty,"SurfaceBrush");var content=new StackPanel();_name.SetResourceReference(TextBlock.ForegroundProperty,"TextBrush");_hpText.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");_mpText.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");content.Children.Add(_name);_hp.Margin=new Thickness(0,5,0,2);content.Children.Add(_hp);content.Children.Add(_hpText);_mp.Margin=new Thickness(0,4,0,2);content.Children.Add(_mp);content.Children.Add(_mpText);_statuses.Margin=new Thickness(0,5,0,0);content.Children.Add(_statuses);Child=content;}
        public void Update(PlayerPosition p,DateTime now){_name.Text=JobNames.Name(p.ClassJobId)+" · "+p.Player;_hp.Maximum=Math.Max(1,p.MaxHP);_hp.Value=p.HP;_hpText.Text=$"HP {p.HP:N0} / {p.MaxHP:N0}";
            bool known=p.MP.HasValue&&p.MaxMP is >0;_mp.Visibility=known?Visibility.Visible:Visibility.Collapsed;_mp.Maximum=known?p.MaxMP!.Value:1;_mp.Value=known?p.MP!.Value:0;_mpText.Text=known?$"MP {p.MP:N0} / {p.MaxMP:N0}":"MP · 未记录";
            var statuses=p.Statuses.Select(s=>(s,remaining:s.Remaining-(now-p.StatusTimestamp).TotalSeconds)).Where(x=>x.s.Remaining<=0||x.remaining>0).ToArray();var signature=StatusIcons.Generation+"|"+string.Join(';',statuses.Select(x=>$"{x.s.Id}:{x.s.Param}:{(x.s.Remaining>0?Math.Ceiling(x.remaining):-1)}"));if(signature==_signature)return;_signature=signature;_statuses.Children.Clear();
            foreach(var (s,remaining) in statuses){var entry=StatusCatalog.Find(s.Id);var chip=new Grid {Width=28,Height=35,Margin=new Thickness(0,0,4,3),ToolTip=StatusCatalog.Name(s.Id)+(s.Remaining>0?$" · 剩余 {Math.Max(0,remaining):0.#} 秒":"")};var image=new Image {Width=24,Height=28,VerticalAlignment=VerticalAlignment.Top,Stretch=Stretch.Uniform};var placeholder=new TextBlock {Text="?",FontSize=14,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};placeholder.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");chip.Children.Add(placeholder);chip.Children.Add(image);_ = Load(image,placeholder,entry?.Icon??0);
                if(s.Remaining>0)chip.Children.Add(new TextBlock {Text=Math.Ceiling(Math.Max(0,remaining)).ToString("0"),FontSize=10,Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromArgb(210,10,14,24)),VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Center});
                if(entry?.MaxStacks>1&&s.Param>1)chip.Children.Add(new TextBlock {Text=s.Param.ToString(),FontSize=10,FontWeight=FontWeights.Bold,Foreground=Brushes.White,Background=Brushes.Black,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top});_statuses.Children.Add(chip);
            }
        }
        private static async Task Load(Image image,TextBlock placeholder,uint icon){var bitmap=await StatusIcons.LoadAsync(icon);if(bitmap!=null){image.Source=bitmap;placeholder.Visibility=Visibility.Collapsed;}else placeholder.ToolTip=StatusIcons.Error.Length>0?StatusIcons.Error:"本机资源中未找到此状态图标";}
    }
}
