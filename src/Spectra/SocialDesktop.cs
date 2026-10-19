using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
namespace Spectra;
// Device-local secret store. The account ID and endpoint scope prevent account mixing.
public sealed class SocialVault(Store store)
{
 string PathFor(string scope){if(scope.Length!=64||!scope.All(Uri.IsHexDigit))throw new IOException("Неверная область ключей");return Path.Combine(store.Root,"social",scope+".dat");}
 public JsonNode Load(string scope){var path=PathFor(scope);if(!File.Exists(path))return new JsonObject();if(new FileInfo(path).Length>8*1024*1024)throw new IOException("Слишком большое хранилище чата");return JsonNode.Parse(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser)))??new JsonObject();}
 public void Save(string scope,JsonNode value){var raw=Encoding.UTF8.GetBytes(value.ToJsonString());if(raw.Length>8*1024*1024)throw new IOException("История чатов переполнена");var path=PathFor(scope);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path+".tmp",ProtectedData.Protect(raw,null,DataProtectionScope.CurrentUser));File.Move(path+".tmp",path,true);}
}
public sealed class SocialDesktop:IDisposable
{
 readonly Func<string,string> localize;readonly Window main;readonly Action<JsonNode> open;readonly System.Windows.Forms.NotifyIcon tray;readonly MediaPlayer sound=new();readonly Queue<(string Title,string Text,JsonNode Action)> queue=new();Window? popup;bool quitting,disposed;
 public SocialDesktop(Window main,Action<JsonNode> open,Func<string,string>? localize=null){this.localize=localize??(value=>value);this.main=main;this.open=open;var menu=new System.Windows.Forms.ContextMenuStrip();menu.Items.Add("Открыть Spectra",null,(_,_)=>Show());menu.Items.Add("Выйти полностью",null,(_,_)=>{quitting=true;Dispose();Application.Current.Shutdown();});tray=new(){Text=this.localize("Spectra · работает в фоне"),Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??System.Drawing.SystemIcons.Application,Visible=true,ContextMenuStrip=menu};menu.Opening+=(_,_)=>RefreshLanguage();tray.DoubleClick+=(_,_)=>Show();main.Closing+=(_,e)=>{if(!quitting){e.Cancel=true;main.Hide();}};Application.Current.SessionEnding+=(_,_)=>quitting=true;}
 public void RefreshLanguage(){if(disposed)return;tray.Text=localize("Spectra · работает в фоне");var menu=tray.ContextMenuStrip;if(menu?.Items.Count>=2){menu.Items[0].Text=localize("Открыть Spectra");menu.Items[1].Text=localize("Выйти полностью");}}
 public void Show(){main.Show();if(main.WindowState==WindowState.Minimized)main.WindowState=WindowState.Normal;main.Activate();}
 public void Notify(string title,string text,JsonNode action){if(disposed)return;if(queue.Count>=20)queue.Dequeue();queue.Enqueue((title[..Math.Min(title.Length,1000)],text[..Math.Min(text.Length,8000)],action.DeepClone()));Next();}
 static SolidColorBrush Ink(byte r,byte g,byte b)=>new(Color.FromRgb(r,g,b));
 static Button PixelButton(string label,bool primary=false)
 {
  var frame=new FrameworkElementFactory(typeof(Border));frame.SetValue(Border.BackgroundProperty,primary?Ink(62,111,72):Ink(37,47,61));frame.SetValue(Border.BorderBrushProperty,primary?Ink(135,191,115):Ink(101,116,135));frame.SetValue(Border.BorderThicknessProperty,new Thickness(2));frame.SetValue(Border.PaddingProperty,new Thickness(12,7,12,7));
  var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);frame.AppendChild(content);
  var button=new Button{Content=label,Foreground=Brushes.White,Cursor=System.Windows.Input.Cursors.Hand,FontSize=12,Template=new ControlTemplate(typeof(Button)){VisualTree=frame}};
  button.MouseEnter+=(_,_)=>button.Opacity=.8;button.MouseLeave+=(_,_)=>button.Opacity=1;return button;
 }
 void Next()
 {
  if(popup!=null||queue.Count==0||disposed)return;var n=queue.Dequeue();
  var titleText=n.Action["type"]?.ToString() is "chat" or "friend"?n.Title:localize(n.Title);var bodyText=n.Action["type"]?.ToString()=="chat"&&n.Action["systemMessage"]?.GetValue<bool>()!=true?n.Text:localize(n.Text);titleText=titleText[..Math.Min(titleText.Length,100)];bodyText=bodyText[..Math.Min(bodyText.Length,240)];
  var font=new FontFamily("Consolas");var fontPath=Path.Combine(AppContext.BaseDirectory,"Web","assets","minecraft.ttf");
  try{if(File.Exists(fontPath))font=new FontFamily(new Uri(Path.GetDirectoryName(fontPath)!+Path.DirectorySeparatorChar),"./#Minecraft Rus");}catch{}
  var grid=new Grid{Margin=new Thickness(18,13,18,16)};for(int i=0;i<4;i++)grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
  var brand=new TextBlock{Text="■ SPECTRA NETWORK",Foreground=Ink(153,197,123),FontSize=11,FontFamily=font,Margin=new Thickness(0,5,32,14)};grid.Children.Add(brand);
  var close=PixelButton("×");close.HorizontalAlignment=HorizontalAlignment.Right;close.ToolTip=localize("Закрыть уведомление");grid.Children.Add(close);
  var title=new TextBlock{Text=titleText,Foreground=Brushes.White,FontFamily=font,FontSize=16,LineHeight=23,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,9)};Grid.SetRow(title,1);grid.Children.Add(title);
  var text=new TextBlock{Text=bodyText,Foreground=Ink(190,201,214),FontFamily=font,FontSize=13,LineHeight=19,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)};Grid.SetRow(text,2);grid.Children.Add(text);
  var link=PixelButton(localize("Открыть в Spectra →"),true);link.FontFamily=font;link.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetRow(link,3);grid.Children.Add(link);
  var root=new Grid{Background=Ink(19,25,34)};root.RowDefinitions.Add(new(){Height=new GridLength(3)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(3)});
  root.Children.Add(new Border{Background=Ink(153,197,123)});Grid.SetRow(grid,1);root.Children.Add(grid);var lifetime=new Border{Background=Ink(95,140,85),RenderTransformOrigin=new Point(0,0),RenderTransform=new ScaleTransform(1,1)};Grid.SetRow(lifetime,2);root.Children.Add(lifetime);
  var border=new Border{Child=root,BorderBrush=Ink(82,98,116),BorderThickness=new Thickness(2),SnapsToDevicePixels=true};
  var w=new Window{Width=Math.Min(390,SystemParameters.WorkArea.Width-24),SizeToContent=SizeToContent.Height,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Topmost=true,Background=Ink(19,25,34),Content=border,Opacity=0,UseLayoutRounding=true};popup=w;
  var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(8)};bool closing=false;
  void StartTimer(){if(closing)return;timer.Start();if(SystemParameters.ClientAreaAnimation)((ScaleTransform)lifetime.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,0,TimeSpan.FromSeconds(8)));}
  void Dismiss(){if(closing)return;closing=true;timer.Stop();if(SystemParameters.ClientAreaAnimation){w.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(160)));var animation=new DoubleAnimation(w.Left+24,TimeSpan.FromMilliseconds(160));animation.Completed+=(_,_)=>w.Close();w.BeginAnimation(Window.LeftProperty,animation);}else w.Close();}
  void Open(){Show();open(n.Action);Dismiss();}
  close.Click+=(_,_)=>Dismiss();link.Click+=(_,_)=>Open();title.MouseLeftButtonUp+=(_,_)=>Open();text.MouseLeftButtonUp+=(_,_)=>Open();
  w.MouseEnter+=(_,_)=>{timer.Stop();((ScaleTransform)lifetime.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,null);};w.MouseLeave+=(_,_)=>StartTimer();
  w.Closed+=(_,_)=>{timer.Stop();popup=null;Next();};
  w.Loaded+=(_,_)=>{var area=SystemParameters.WorkArea;w.Left=area.Right-w.Width-16;w.Top=area.Bottom-w.ActualHeight-16;if(SystemParameters.ClientAreaAnimation){w.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(220)));w.BeginAnimation(Window.LeftProperty,new DoubleAnimation(w.Left+30,w.Left,TimeSpan.FromMilliseconds(220)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});}else w.Opacity=1;StartTimer();};timer.Tick+=(_,_)=>Dismiss();w.Show();
  var path=Path.Combine(AppContext.BaseDirectory,"Web","assets","notification.mp3");try{sound.Open(new Uri(path));sound.Volume=.7;sound.Play();}catch{}
 }
 public void Clear(){queue.Clear();popup?.Close();sound.Stop();}
 public void Dispose(){if(disposed)return;quitting=true;disposed=true;queue.Clear();popup?.Close();sound.Close();tray.Visible=false;tray.Dispose();}
}
