using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
namespace Spectra;
public sealed partial class SocialDesktop
{
 sealed record Notice(string Title,string Text,JsonNode Action);
 sealed record ToastCard(Border Root,DispatcherTimer Timer);
 readonly NotificationStack<Notice> notices=new();readonly Dictionary<Notice,ToastCard> toastCards=new();
 Window? toastHost;StackPanel? toastList;Button? dismissAll;
 public void Notify(string title,string text,JsonNode action)
 {
  if(disposed)return;var item=new Notice(title[..Math.Min(title.Length,1000)],text[..Math.Min(text.Length,8000)],action.DeepClone());
  if(notices.Push(item))PresentToast(item,true);
 }
 void EnsureToastHost()
 {
  if(toastHost!=null)return;
  var root=new StackPanel();dismissAll=PixelButton(localize("Скрыть все уведомления"));dismissAll.FontFamily=MenuFont();dismissAll.FontSize=11;dismissAll.HorizontalAlignment=HorizontalAlignment.Right;dismissAll.Margin=new Thickness(0,0,0,8);dismissAll.Visibility=Visibility.Collapsed;dismissAll.Click+=(_,_)=>Clear();root.Children.Add(dismissAll);
  toastList=new StackPanel();root.Children.Add(toastList);
  var w=new Window{Width=Math.Min(340,SystemParameters.WorkArea.Width-24),SizeToContent=SizeToContent.Height,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Topmost=true,AllowsTransparency=true,Background=Brushes.Transparent,Content=root,UseLayoutRounding=true};toastHost=w;
  w.SizeChanged+=(_,_)=>PositionToasts();w.Closed+=(_,_)=>{if(ReferenceEquals(toastHost,w)){toastHost=null;Clear();}};
 }
 void PositionToasts(){if(toastHost==null)return;var area=SystemParameters.WorkArea;toastHost.Left=area.Right-toastHost.Width-16;toastHost.Top=Math.Max(area.Top+8,area.Bottom-toastHost.ActualHeight-16);}
 void PresentToast(Notice item,bool audible)
 {
  if(disposed||!notices.Visible.Contains(item))return;EnsureToastHost();
  var title=item.Action.Str("type") is "chat" or "friend"?item.Title:localize(item.Title);var body=item.Action.Str("type")=="chat"&&item.Action["systemMessage"]?.GetValue<bool>()!=true?item.Text:localize(item.Text);
  var content=NotificationContent(title[..Math.Min(title.Length,100)],body[..Math.Min(body.Length,160)],item.Action);var card=content.Root;card.Margin=new Thickness(0,0,0,8);
  var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(6)};
  void Start(){timer.Start();if(SystemParameters.ClientAreaAnimation)((ScaleTransform)content.Lifetime.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,0,TimeSpan.FromSeconds(6)));}
  timer.Tick+=(_,_)=>DismissToast(item);card.MouseEnter+=(_,_)=>{timer.Stop();((ScaleTransform)content.Lifetime.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,null);};card.MouseLeave+=(_,_)=>{if(toastCards.ContainsKey(item))Start();};
  content.Close.Click+=(_,_)=>DismissToast(item);card.MouseLeftButtonUp+=(_,_)=>{if(content.Close.IsMouseOver)return;var action=item.Action;Clear();Show();open(action);};
  toastCards.Add(item,new(card,timer));toastList!.Children.Add(card);RefreshToastHeader();
  if(!toastHost!.IsVisible)toastHost.Show();PositionToasts();
  if(SystemParameters.ClientAreaAnimation){card.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)));card.RenderTransform=new TranslateTransform();card.RenderTransform.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(12,0,TimeSpan.FromMilliseconds(180)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});}
  Start();if(audible){var path=Path.Combine(AppContext.BaseDirectory,"Web","assets","notification.mp3");try{sound.Open(new Uri(path));sound.Volume=.7;sound.Play();}catch{}}
 }
 void DismissToast(Notice item)
 {
  if(!toastCards.Remove(item,out var card))return;card.Timer.Stop();toastList?.Children.Remove(card.Root);var next=notices.Dismiss(item);
  if(next!=null)PresentToast(next,false);else RefreshToastHeader();
  if(toastCards.Count==0){var host=toastHost;toastHost=null;toastList=null;dismissAll=null;host?.Close();}
 }
 void RefreshToastHeader(){if(dismissAll!=null)dismissAll.Visibility=toastCards.Count>=2?Visibility.Visible:Visibility.Collapsed;PositionToasts();}
 public void Clear()
 {
  notices.Clear();foreach(var card in toastCards.Values)card.Timer.Stop();toastCards.Clear();toastList?.Children.Clear();var host=toastHost;toastHost=null;toastList=null;dismissAll=null;host?.Close();sound.Stop();
 }
}
