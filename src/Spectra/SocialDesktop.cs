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
public sealed partial class SocialDesktop:IDisposable
{
 readonly Func<string,string> localize;readonly Window main;readonly Action<JsonNode> open;readonly System.Windows.Forms.NotifyIcon tray;readonly MediaPlayer sound=new();readonly Queue<(string Title,string Text,JsonNode Action)> queue=new();Window? popup;bool quitting,disposed;
 readonly Func<string> appearance;
 bool LightMode(){try{var config=JsonNode.Parse(appearance());var theme=config.Str("theme");var mode=config?["themeModes"].Str(theme);return theme=="light"||mode=="light"||mode==""&&(theme=="aero"||theme=="liquid"&&config.Str("liquidMode")!="dark");}catch{return false;}}
 SolidColorBrush Surface(string role){var light=LightMode();string theme;try{theme=JsonNode.Parse(appearance()).Str("theme");}catch{theme="dark";}var palette=(theme,light) switch{
  ("steam",true)=>new[]{"#eef2f5","#ffffff","#243746","#1673ab"},("steam",false)=>new[]{"#1b2028","#2a2e35","#dcdedf","#1a9fff"},
  ("minecraft",true)=>new[]{"#e9e8df","#faf9f0","#242a21","#397c2b"},("minecraft",false)=>new[]{"#171717","#292929","#ffffff","#52a535"},
  ("aero",true)=>new[]{"#e6f3fa","#f5fcff","#153e57","#1898c6"},("aero",false)=>new[]{"#102733","#1b3946","#e8faff","#68cdea"},
  ("windows",true)=>new[]{"#f3f3f3","#ffffff","#202020","#0067c0"},("windows",false)=>new[]{"#171717","#242424","#f2f2f2","#60cdff"},
  ("liquid",true)=>new[]{"#e7edf7","#ffffff","#172339","#007aff"},("liquid",false)=>new[]{"#101723","#1b2638","#edf3ff","#64acff"},
  (_,true)=>new[]{"#edf3fa","#ffffff","#15243b","#286ac8"},_=>new[]{"#090e18","#172333","#edf3fc","#9fc8f3"}};
  var index=role switch{"bg"=>0,"panel"=>1,"text"=>2,"accent"=>3,_=>-1};if(index>=0)return new((Color)ColorConverter.ConvertFromString(palette[index]));return role=="muted"?(light?Ink(87,104,125):Ink(165,183,204)):(light?Ink(169,185,204):Ink(66,86,110));}
 public SocialDesktop(Window main,Action<JsonNode> open,Func<string,string>? localize=null,Action<string>? onCommand=null,Func<string>? appearance=null,Func<DesktopIdentity>? identity=null){this.localize=localize??(value=>value);this.appearance=appearance??(()=>"{}");this.main=main;this.open=open;
  this.identity=identity??(()=>new DesktopIdentity("Spectra","","offline","Не в сети",false,false,""));this.command=onCommand;
  tray=new(){Text=this.localize("Spectra · работает в фоне"),Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??System.Drawing.SystemIcons.Application,Visible=true};tray.DoubleClick+=(_,_)=>Show();tray.MouseUp+=(_,e)=>{if(e.Button==System.Windows.Forms.MouseButtons.Right)main.Dispatcher.Invoke(OpenTray);};
  main.Closing+=(_,e)=>{if(!quitting){e.Cancel=true;main.Hide();}};Application.Current.SessionEnding+=(_,_)=>quitting=true;
 }
 public void RefreshLanguage(){if(disposed)return;tray.Text=localize("Spectra · работает в фоне");if(trayMenu?.IsOpen==true)PopulateTray(trayMenu);}
 public void Show(){main.Show();if(main.WindowState==WindowState.Minimized)main.WindowState=WindowState.Normal;main.Activate();}
 public void Notify(string title,string text,JsonNode action){if(disposed)return;if(queue.Count>=20)queue.Dequeue();queue.Enqueue((title[..Math.Min(title.Length,1000)],text[..Math.Min(text.Length,8000)],action.DeepClone()));Next();}
 static SolidColorBrush Ink(byte r,byte g,byte b)=>new(Color.FromRgb(r,g,b));
 Button PixelButton(string label,bool primary=false)
 {
  var frame=new FrameworkElementFactory(typeof(Border));frame.SetValue(Border.BackgroundProperty,primary?Surface("accent"):Surface("panel"));frame.SetValue(Border.BorderBrushProperty,Surface("border"));frame.SetValue(Border.BorderThicknessProperty,new Thickness(2));frame.SetValue(Border.PaddingProperty,new Thickness(12,7,12,7));
  var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);frame.AppendChild(content);
  var button=new Button{Content=label,Foreground=primary?Surface("bg"):Surface("text"),Cursor=System.Windows.Input.Cursors.Hand,FontSize=12,Template=new ControlTemplate(typeof(Button)){VisualTree=frame}};
  button.MouseEnter+=(_,_)=>button.Opacity=.8;button.MouseLeave+=(_,_)=>button.Opacity=1;return button;
 }
 void Next()
 {
  if(popup!=null||queue.Count==0||disposed)return;var n=queue.Dequeue();
  var titleText=n.Action["type"]?.ToString() is "chat" or "friend"?n.Title:localize(n.Title);var bodyText=n.Action["type"]?.ToString()=="chat"&&n.Action["systemMessage"]?.GetValue<bool>()!=true?n.Text:localize(n.Text);titleText=titleText[..Math.Min(titleText.Length,100)];bodyText=bodyText[..Math.Min(bodyText.Length,130)];
  var font=new FontFamily("Consolas");var fontPath=Path.Combine(AppContext.BaseDirectory,"Web","assets","minecraft.ttf");
  try{if(File.Exists(fontPath))font=new FontFamily(new Uri(Path.GetDirectoryName(fontPath)!+Path.DirectorySeparatorChar),"./#Minecraft Rus");}catch{}
  var grid=new Grid{Margin=new Thickness(12,8,12,10)};for(int i=0;i<4;i++)grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
  var brand=new TextBlock{Text="■ SPECTRA NETWORK",Foreground=Surface("accent"),FontSize=11,FontFamily=font,Margin=new Thickness(0,2,28,7)};grid.Children.Add(brand);
  var close=PixelButton("×");close.FontFamily=font;close.HorizontalAlignment=HorizontalAlignment.Right;close.ToolTip=localize("Закрыть уведомление");grid.Children.Add(close);
  var title=new TextBlock{Text=titleText,Foreground=Surface("text"),FontFamily=font,FontSize=13,LineHeight=18,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,9)};Grid.SetRow(title,1);grid.Children.Add(title);
  var text=new TextBlock{Text=bodyText,Foreground=Surface("muted"),FontFamily=font,FontSize=11,LineHeight=16,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};Grid.SetRow(text,2);grid.Children.Add(text);
  var link=PixelButton(localize("Открыть в Spectra →"),true);link.FontFamily=font;link.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetRow(link,3);grid.Children.Add(link);
  var root=new Grid{Background=Surface("panel")};root.RowDefinitions.Add(new(){Height=new GridLength(3)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(3)});
  root.Children.Add(new Border{Background=Surface("accent")});Grid.SetRow(grid,1);root.Children.Add(grid);var lifetime=new Border{Background=Surface("accent"),RenderTransformOrigin=new Point(0,0),RenderTransform=new ScaleTransform(1,1)};Grid.SetRow(lifetime,2);root.Children.Add(lifetime);
  var border=new Border{Child=root,BorderBrush=Surface("border"),BorderThickness=new Thickness(1),SnapsToDevicePixels=true};
  var w=new Window{Width=Math.Min(320,SystemParameters.WorkArea.Width-24),SizeToContent=SizeToContent.Height,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Topmost=true,Background=Surface("panel"),Content=border,Opacity=0,UseLayoutRounding=true};popup=w;
  var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(6)};bool closing=false;
  void StartTimer(){if(closing)return;timer.Start();if(SystemParameters.ClientAreaAnimation)((ScaleTransform)lifetime.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,0,TimeSpan.FromSeconds(6)));}
  void Dismiss(){if(closing)return;closing=true;timer.Stop();if(SystemParameters.ClientAreaAnimation){w.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(160)));var animation=new DoubleAnimation(w.Left+24,TimeSpan.FromMilliseconds(160));animation.Completed+=(_,_)=>w.Close();w.BeginAnimation(Window.LeftProperty,animation);}else w.Close();}
  void Open(){Show();open(n.Action);Dismiss();}
  close.Click+=(_,_)=>Dismiss();link.Click+=(_,_)=>Open();title.MouseLeftButtonUp+=(_,_)=>Open();text.MouseLeftButtonUp+=(_,_)=>Open();
  w.MouseEnter+=(_,_)=>{timer.Stop();((ScaleTransform)lifetime.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,null);};w.MouseLeave+=(_,_)=>StartTimer();
  w.Closed+=(_,_)=>{timer.Stop();popup=null;Next();};
  w.Loaded+=(_,_)=>{var area=SystemParameters.WorkArea;w.Left=area.Right-w.Width-16;w.Top=area.Bottom-w.ActualHeight-16;if(SystemParameters.ClientAreaAnimation){w.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(220)));w.BeginAnimation(Window.LeftProperty,new DoubleAnimation(w.Left+30,w.Left,TimeSpan.FromMilliseconds(220)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});}else w.Opacity=1;StartTimer();};timer.Tick+=(_,_)=>Dismiss();w.Show();
  var path=Path.Combine(AppContext.BaseDirectory,"Web","assets","notification.mp3");try{sound.Open(new Uri(path));sound.Volume=.7;sound.Play();}catch{}
 }
 public void Clear(){queue.Clear();popup?.Close();sound.Stop();}
 public void Dispose(){if(disposed)return;quitting=true;disposed=true;queue.Clear();popup?.Close();sound.Close();if(trayMenu!=null)trayMenu.IsOpen=false;tray.Visible=false;tray.Dispose();}
}
