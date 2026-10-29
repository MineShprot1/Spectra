using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
namespace Spectra;
public sealed partial class SocialDesktop
{
 internal sealed record ToastContent(Border Root,Button Close,Border Lifetime);
 internal ToastContent NotificationContent(string title,string body,JsonNode action)
 {
  var kind=action.Str("type");var color=kind=="friend"?StatusColor("playing"):Surface("accent");
  string theme;try{theme=JsonNode.Parse(appearance()).Str("theme");}catch{theme="dark";}
  var font=new[]{"steam","windows","aero","modern","liquid"}.Contains(theme)?new FontFamily("Segoe UI"):MenuFont();
  var layout=new Grid{Margin=new Thickness(12,11,10,11)};layout.ColumnDefinitions.Add(new(){Width=new GridLength(42)});layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});layout.ColumnDefinitions.Add(new(){Width=new GridLength(20)});
  var face=new Grid{Width=34,Height=34,VerticalAlignment=VerticalAlignment.Top,Background=Surface("bg"),Margin=new Thickness(0,2,0,0)};face.Children.Add(new TextBlock{Text=kind=="chat"?"✉":kind=="friend"?"▶":"S",FontFamily=new FontFamily("Segoe UI"),FontSize=19,Foreground=color,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
  if(action["avatar"] is JsonNode avatarInfo)_=LoadTrayFace(face,new DesktopIdentity(avatarInfo.Str("name"),avatarInfo.Str("minecraftId"),"online","",false,false,"",avatarInfo.Str("skin")));
  var avatarBorder=new Border{Child=face,BorderBrush=color,BorderThickness=new Thickness(0,0,2,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};layout.Children.Add(avatarBorder);
  var copy=new StackPanel{Margin=new Thickness(8,0,8,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(copy,1);layout.Children.Add(copy);
  copy.Children.Add(new TextBlock{Text=title,Foreground=color,FontFamily=font,FontSize=12,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=19});
  copy.Children.Add(new TextBlock{Text=body,Foreground=Surface("text"),FontFamily=font,FontSize=11,LineHeight=16,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=32,Margin=new Thickness(0,5,0,0)});
  var close=PixelButton("×");close.Padding=new Thickness(0);close.Width=20;close.Height=20;close.FontSize=12;close.VerticalAlignment=VerticalAlignment.Top;close.ToolTip=localize("Закрыть уведомление");AutomationProperties.SetName(close,localize("Закрыть уведомление"));close.Template=ToastCloseTemplate();Grid.SetColumn(close,2);layout.Children.Add(close);
  var root=new Grid{Background=Surface("panel")};root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=new GridLength(2)});root.Children.Add(layout);
  var lifetime=new Border{Background=color,RenderTransformOrigin=new Point(0,0),RenderTransform=new ScaleTransform(1,1),Opacity=.65};Grid.SetRow(lifetime,1);root.Children.Add(lifetime);
  var border=new Border{Child=root,MinHeight=76,MaxHeight=106,BorderBrush=Surface("border"),BorderThickness=new Thickness(1),SnapsToDevicePixels=true,Cursor=System.Windows.Input.Cursors.Hand};AutomationProperties.SetName(border,title+". "+body);border.ToolTip=localize("Открыть в Spectra");return new(border,close,lifetime);
 }
 static ControlTemplate ToastCloseTemplate(){var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);presenter.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);return new(typeof(Button)){VisualTree=presenter};}
}
