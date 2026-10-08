using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Web.WebView2.Core;
namespace Spectra;
public partial class MainWindow : Window
{
 [DllImport("user32.dll")] static extern bool ReleaseCapture();
 [DllImport("user32.dll")] static extern nint SendMessage(nint hwnd,uint message,nint wParam,nint lParam);
 [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
 bool librarySized;
 void ExpandLibrary()
 {
  if(librarySized)return;librarySized=true;
  var screen=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
  var transform=PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice??System.Windows.Media.Matrix.Identity;
  var area=transform.Transform(new Point(screen.Left,screen.Top));
  var size=transform.Transform(new Point(screen.Width,screen.Height));
  MinWidth=Math.Min(900,size.X);MinHeight=Math.Min(580,size.Y);
  Width=Math.Min(1100,Math.Max(MinWidth,size.X*.85));Height=Math.Min(720,Math.Max(MinHeight,size.Y*.85));
  Left=area.X+(size.X-Width)/2;Top=area.Y+(size.Y-Height)/2;
 }
 readonly Store store=new();
 readonly Authentication auth;
 readonly GameService game;
 readonly CatalogService catalog;
 readonly HashSet<string> activeRequests=[];
 public MainWindow()
 {
  InitializeComponent();auth=new(store,Emit);game=new(store,auth,Emit);catalog=new(store,game,Emit);Loaded+=async(_,_)=>await Initialize();
 }
 async Task Initialize()
 {
  try
  {
   var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(store.Root,"webview"));await Browser.EnsureCoreWebView2Async(env);
   var core=Browser.CoreWebView2;
   core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreDevToolsEnabled=false;core.Settings.IsStatusBarEnabled=false;
   core.SetVirtualHostNameToFolderMapping("app.spectra.local",Path.Combine(AppContext.BaseDirectory,"Web"),CoreWebView2HostResourceAccessKind.DenyCors);
   core.SetVirtualHostNameToFolderMapping("data.spectra.local",store.Root,CoreWebView2HostResourceAccessKind.DenyCors);
   core.NavigationStarting+=(_,e)=>{if(!e.Uri.StartsWith("https://app.spectra.local/",StringComparison.OrdinalIgnoreCase))e.Cancel=true;};
   core.NewWindowRequested+=(_,e)=>e.Handled=true;
   core.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
   core.WebMessageReceived+=Receive;
   core.Navigate("https://app.spectra.local/index.html");
  }
  catch(Exception ex){MessageBox.Show("Для Spectra нужен Microsoft Edge WebView2 Runtime.\n"+ex.Message,"Spectra");Close();}
 }
 void Emit(object data)
 {
  Dispatcher.InvokeAsync(()=>
  {
   if(Browser.CoreWebView2==null)return;
   Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(data,Store.Json));
   var n=JsonSerializer.SerializeToNode(data,Store.Json);if(n.Str("type")=="started"&&n?["hide"]?.GetValue<bool>()==true)WindowState=WindowState.Minimized;
   if(n.Str("type")=="exited"){WindowState=WindowState.Normal;Activate();}
  });
 }
 async void Receive(object? sender,CoreWebView2WebMessageReceivedEventArgs e)
 {
  if(!e.Source.StartsWith("https://app.spectra.local/",StringComparison.OrdinalIgnoreCase))return;
  string requestId="";
  try
  {
   var message=JsonNode.Parse(e.WebMessageAsJson)!;requestId=message.Str("id");if(!activeRequests.Add(requestId))return;
   var action=message.Str("action");var d=message["data"]??new JsonObject();var result=await Handle(action,d);
   Emit(new{type="reply",id=requestId,ok=true,result});
  }
  catch(Exception ex){Emit(new{type="reply",id=requestId,ok=false,error=ex.Message});}
  finally{activeRequests.Remove(requestId);}
 }
 object State()=>new{instances=store.Config.Instances,defaults=store.Config.Defaults,profile=auth.Profile,hasAccount=auth.HasSavedAccount,curseForgeConfigured=!string.IsNullOrEmpty(store.Config.CurseForgeKey),craftyConfigured=!string.IsNullOrEmpty(store.Config.CraftyKey),running=game.Running.Keys};
 async Task<object?> Handle(string action,JsonNode d)
 {
  switch(action)
  {
   case "state":return State();
   case "bootstrap":return new{update="Проверка обновлений пока отключена",version="0.2.1"};
   case "login":await auth.Login(d["interactive"]?.GetValue<bool>()??true);ExpandLibrary();return State();
   case "expand":ExpandLibrary();return State();
   case "logout":await auth.Logout();return State();
   case "window":switch(d.Str("command")){case "close":Close();break;case "minimize":WindowState=WindowState.Minimized;break;case "maximize":WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;break;case "drag":if((GetAsyncKeyState(0x01)&0x8000)!=0){ReleaseCapture();SendMessage(new WindowInteropHelper(this).Handle,0x00A1,2,0);}break;}return null;
   case "versions":return await game.Versions();
   case "artwork":return await game.Artwork();
   case "components":return await game.Components(d.Str("instanceId"));
   case "loaders":return await game.Loaders(d.Str("version"),d.Str("loader"));
   case "saveInstance":
    var incoming=d.Deserialize<Instance>(Store.Json)??throw new IOException("Нет сборки");if(incoming.Name.Trim().Length is <1 or >80)throw new IOException("Название: от 1 до 80 символов");await game.Metadata(incoming.Version);Store.Validate(incoming.Settings);
    if(!new[]{"vanilla","fabric","forge","quilt","neoforge"}.Contains(incoming.Loader))throw new IOException("Неизвестный загрузчик");
    var current=store.Config.Instances.Find(x=>x.Id==incoming.Id);if(current==null){incoming.Id=Guid.NewGuid().ToString("N");store.Config.Instances.Add(incoming);}else{if(game.Running.ContainsKey(current.Id))throw new IOException("Закройте игру перед изменением сборки");store.Config.Instances[store.Config.Instances.IndexOf(current)]=incoming;}store.Folder(incoming);store.Save();return State();
   case "settings":
    var settings=d["defaults"]!.Deserialize<GameSettings>(Store.Json)!;Store.Validate(settings);store.Config.Defaults=settings;
    if(d.Str("curseForgeKey")!="")store.Config.CurseForgeKey=d.Str("curseForgeKey");if(d.Str("craftyKey")!="")store.Config.CraftyKey=d.Str("craftyKey");store.Save();return State();
   case "launch":await game.Launch(d.Str("instanceId"));return State();
   case "stop":game.Stop(d.Str("instanceId"));return null;
   case "files":return await Task.Run(()=>game.Files(d.Str("instanceId"),d.Str("kind")));
   case "toggle":game.Toggle(d.Str("instanceId"),d.Str("path"));return null;
   case "readLog":return await game.ReadLog(d.Str("instanceId"),d.Str("path"));
   case "openFolder":
    var inst=store.Get(d.Str("instanceId"));var root=store.Folder(inst);var kind=d.Str("kind");var path=kind=="instance"?Path.GetDirectoryName(root)!:kind=="minecraft"?root:Path.Combine(root,GameService.KindFolder(kind));Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo(path){UseShellExecute=true});return null;
   case "pickJava":var java=new Microsoft.Win32.OpenFileDialog{Filter="Java|javaw.exe;java.exe"};return java.ShowDialog()==true?java.FileName:null;
   case "pickImage":
    var img=new Microsoft.Win32.OpenFileDialog{Filter="Изображения|*.png;*.jpg;*.jpeg;*.webp"};if(img.ShowDialog()!=true)return null;
    if(new FileInfo(img.FileName).Length>20*1024*1024)throw new IOException("Изображение больше 20 МБ");var asset=Path.Combine(store.Root,"artwork",Guid.NewGuid()+Path.GetExtension(img.FileName));Directory.CreateDirectory(Path.GetDirectoryName(asset)!);File.Copy(img.FileName,asset);return game.Asset(asset);
   case "search":return await catalog.Search(d.Str("query"),d.Str("kind"),d.Str("instanceId"),d["offset"]?.GetValue<int>()??0);
   case "install":await catalog.Install(d.Str("source"),d.Str("projectId"),d.Str("kind"),d.Str("instanceId"));return null;
   case "installPack":await catalog.InstallPack(d.Str("source"),d.Str("projectId"));return State();
   case "importPack":var pack=new Microsoft.Win32.OpenFileDialog{Filter="Модпаки|*.mrpack;*.zip"};if(pack.ShowDialog()==true){if(pack.FileName.EndsWith(".mrpack",StringComparison.OrdinalIgnoreCase))await catalog.ImportMrpack(pack.FileName);else await catalog.ImportCursePack(pack.FileName);}return State();
   case "profileAction":await auth.ProfileAction(d.Str("operation"),d);return State();
   case "crafty":
    if(string.IsNullOrWhiteSpace(store.Config.CraftyKey))throw new IOException("Укажите Crafty API-токен в настройках");using(var req=new HttpRequestMessage(HttpMethod.Get,"https://api.crafty.gg/api/v2/players/"+Uri.EscapeDataString(d.Str("name")))){req.Headers.Authorization=new("Bearer",store.Config.CraftyKey);return await Net.Send(req);}
   case "external":
    var url=d.Str("url");var uri=new Uri(url);if(uri.Scheme!="https"||!new[]{"github.com","crafty.gg","www.minecraft.net","www.curseforge.com","modrinth.com","microsoft.com","www.microsoft.com","login.microsoftonline.com"}.Contains(uri.Host))throw new IOException("Недопустимая ссылка");Process.Start(new ProcessStartInfo(url){UseShellExecute=true});return null;
   case "gpus":return await Task.Run(()=>GetGpus());
   default:throw new IOException("Неизвестная команда");
  }
 }
 static string[] GetGpus()
 {
  using var p=Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -Command \"Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true});if(p==null)return [];var output=p.StandardOutput.ReadToEnd();p.WaitForExit();return output.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
 }
}
