using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
namespace Spectra;
public sealed record DesktopIdentity(string Name,string MinecraftId,string State,string Detail,bool Connected,bool Hidden,string SelectedGame);
public sealed partial class SocialDesktop
{
 readonly Func<DesktopIdentity> identity;readonly Action<string>? command;ContextMenu? trayMenu;string avatarKey="";ImageSource? avatar;
 SolidColorBrush StatusColor(string state)=>state switch{"playing"=>Ink(143,190,98),"online"=>Surface("accent"),_=>Surface("muted")};
 FontFamily MenuFont(){var path=Path.Combine(AppContext.BaseDirectory,"Web","assets","minecraft.ttf");return File.Exists(path)?new FontFamily(new Uri(Path.GetDirectoryName(path)!+Path.DirectorySeparatorChar),"./#Minecraft Rus"):new FontFamily("Consolas");}
 Style TrayItemStyle()
 {
  var frame=new FrameworkElementFactory(typeof(Border));frame.Name="row";frame.SetValue(Border.PaddingProperty,new Thickness(10,7,10,7));frame.SetBinding(Border.BackgroundProperty,new Binding("Background"){RelativeSource=RelativeSource.TemplatedParent});var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(ContentPresenter.ContentSourceProperty,"Header");frame.AppendChild(content);
  var template=new ControlTemplate(typeof(MenuItem)){VisualTree=frame};var hover=new Trigger{Property=MenuItem.IsHighlightedProperty,Value=true};hover.Setters.Add(new Setter(Border.BackgroundProperty,Surface("bg"),"row"));template.Triggers.Add(hover);
  var style=new Style(typeof(MenuItem));style.Setters.Add(new Setter(Control.TemplateProperty,template));style.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));style.Setters.Add(new Setter(Control.ForegroundProperty,Surface("text")));style.Setters.Add(new Setter(Control.FontFamilyProperty,MenuFont()));style.Setters.Add(new Setter(Control.FontSizeProperty,12d));style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));return style;
 }
 void OpenTray()
 {
  if(disposed)return;if(trayMenu?.IsOpen==true){trayMenu.IsOpen=false;return;}
  trayMenu=CreateTrayMenu();trayMenu.IsOpen=true;
 }
 ContextMenu CreateTrayMenu()
 {
  var frame=new FrameworkElementFactory(typeof(Border));frame.SetValue(Border.BackgroundProperty,Surface("panel"));frame.SetValue(Border.BorderBrushProperty,Surface("border"));frame.SetValue(Border.BorderThicknessProperty,new Thickness(1));frame.SetValue(Border.CornerRadiusProperty,new CornerRadius(3));frame.SetValue(Border.PaddingProperty,new Thickness(5));var scroll=new FrameworkElementFactory(typeof(ScrollViewer));scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty,ScrollBarVisibility.Auto);scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty,ScrollBarVisibility.Disabled);scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));frame.AppendChild(scroll);
  var menu=new ContextMenu{Width=Math.Min(300,SystemParameters.WorkArea.Width-20),MaxHeight=SystemParameters.WorkArea.Height-20,Placement=PlacementMode.MousePoint,StaysOpen=false,HasDropShadow=false,Background=Surface("panel"),Foreground=Surface("text"),FontFamily=MenuFont(),Template=new ControlTemplate(typeof(ContextMenu)){VisualTree=frame},Effect=new DropShadowEffect{BlurRadius=16,ShadowDepth=4,Opacity=.25}};PopulateTray(menu);return menu;
 }
 void PopulateTray(ContextMenu menu)
 {
  menu.Items.Clear();var current=identity();var color=StatusColor(current.State);
  MenuItem Item(object header,string action,bool enabled=true){var item=new MenuItem{Header=header,IsEnabled=enabled,Style=TrayItemStyle()};item.Click+=(_,_)=>{menu.IsOpen=false;if(action=="show")Show();else if(action=="quit"){quitting=true;Dispose();Application.Current.Shutdown();}else command?.Invoke(action);};menu.Items.Add(item);return item;}
  void Divider(){var separator=new Separator{Margin=new Thickness(8,5,8,5)};var line=new FrameworkElementFactory(typeof(Border));line.SetValue(Border.HeightProperty,1d);line.SetValue(Border.BackgroundProperty,Surface("border"));separator.Template=new ControlTemplate(typeof(Separator)){VisualTree=line};menu.Items.Add(separator);}
  var header=new Grid{Margin=new Thickness(2,6,2,8)};header.ColumnDefinitions.Add(new(){Width=new GridLength(42)});header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  var face=new Grid{Width=35,Height=35,VerticalAlignment=VerticalAlignment.Top,Background=Surface("bg")};face.Children.Add(new TextBlock{Text=current.Name.Length>0?current.Name[..1]:"S",Foreground=color,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FontFamily=MenuFont(),FontSize=19});
  if(System.Text.RegularExpressions.Regex.IsMatch(current.MinecraftId,@"\A[a-fA-F0-9]{32}\z")){if(avatarKey!=current.MinecraftId){avatarKey=current.MinecraftId;avatar=new BitmapImage(new Uri("https://mc-heads.net/avatar/"+current.MinecraftId));}var picture=new Image{Source=avatar,Stretch=Stretch.UniformToFill};RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.NearestNeighbor);face.Children.Add(picture);}
  var edge=new Border{BorderBrush=color,BorderThickness=new Thickness(0,0,2,0),Child=face,HorizontalAlignment=HorizontalAlignment.Left};header.Children.Add(edge);
  var info=new StackPanel{Margin=new Thickness(6,0,0,0)};info.Children.Add(new TextBlock{Text=current.Name,Foreground=color,FontFamily=MenuFont(),FontSize=14,TextTrimming=TextTrimming.CharacterEllipsis});info.Children.Add(new TextBlock{Text="■ "+localize(current.State switch{"playing"=>"В игре","online"=>"В сети","invisible"=>"Невидимый",_=>"Не в сети"}),Foreground=color,FontFamily=MenuFont(),FontSize=10,Margin=new Thickness(0,6,0,0)});info.Children.Add(new TextBlock{Text=localize(current.Detail),Foreground=Surface("muted"),FontFamily=MenuFont(),FontSize=10,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)});Grid.SetColumn(info,1);header.Children.Add(info);var identityItem=Item(header,"show");identityItem.IsHitTestVisible=false;identityItem.Focusable=false;
  foreach(var (label,action,selected) in new[]{("В сети","online",!current.Hidden),("Невидимый","invisible",current.Hidden)}){var choice=new TextBlock{Text=(selected?"✓ ":"　 ")+localize(label),Foreground=StatusColor(action=="online"?"online":"invisible"),FontFamily=MenuFont(),FontSize=11};Item(choice,action,current.Connected);}
  Divider();Item(localize("Открыть Spectra"),"show");Item(localize(current.SelectedGame==""?"Выбрать сборку":current.SelectedGame),current.SelectedGame==""?"instances":"launch");Item(localize("Друзья и чат"),"friends");Divider();Item(localize("Библиотека сборок"),"instances");Item(localize("Версии Minecraft"),"versions");Item(localize("Настройки"),"settings");Item(localize("Открыть папку Spectra"),"folder");Item(localize("Переключить уведомления"),"mute");Divider();Item(localize("Выйти полностью"),"quit");
 }
}
