using System.Diagnostics;
using System.Collections.Concurrent;
using System.Windows.Threading;
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
 [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd,int attribute,ref int value,int size);
 void ApplyWindowsCorners()
 {
  if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,22000))return;
  var hwnd=new WindowInteropHelper(this).Handle;if(hwnd==0)return;
  int preference=WindowState==WindowState.Maximized?1:2;
  _=DwmSetWindowAttribute(hwnd,33,ref preference,sizeof(int));
 }
 bool fullscreen;
 Rect restoredBounds;
 WindowState restoredState;
 void ToggleFullscreen()
 {
  if(!fullscreen){restoredBounds=new Rect(Left,Top,Width,Height);restoredState=WindowState;WindowState=WindowState.Normal;fullscreen=true;
   var screen=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).Bounds;
   var matrix=PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice??System.Windows.Media.Matrix.Identity;
   var origin=matrix.Transform(new Point(screen.Left,screen.Top));var size=matrix.Transform(new Point(screen.Width,screen.Height));
   MaxWidth=double.PositiveInfinity;MaxHeight=double.PositiveInfinity;Left=origin.X;Top=origin.Y;Width=size.X;Height=size.Y;
  }else{fullscreen=false;WindowState=WindowState.Normal;Left=restoredBounds.Left;Top=restoredBounds.Top;Width=restoredBounds.Width;Height=restoredBounds.Height;WindowState=restoredState;}
  store.Config.LauncherFullscreen=fullscreen;store.Save();Emit(new{type="windowState",maximized=WindowState==WindowState.Maximized,fullscreen});
 }
 nint WindowHook(nint hwnd,int message,nint wParam,nint lParam,ref bool handled)
 {
  if(message!=0x24||fullscreen)return 0;
  var screen=System.Windows.Forms.Screen.FromHandle(hwnd);var work=screen.WorkingArea;var bounds=screen.Bounds;
  var info=Marshal.PtrToStructure<MinMaxInfo>(lParam);info.MaxPosition=new NativePoint{X=work.Left-bounds.Left,Y=work.Top-bounds.Top};info.MaxSize=new NativePoint{X=work.Width,Y=work.Height};Marshal.StructureToPtr(info,lParam,false);handled=true;return 0;
 }
 [StructLayout(LayoutKind.Sequential)] struct NativePoint{public int X,Y;}
 [StructLayout(LayoutKind.Sequential)] struct MinMaxInfo{public NativePoint Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
 bool librarySized;
 void ExpandLibrary()
 {
  if(librarySized||fullscreen)return;librarySized=true;
  var screen=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
  var transform=PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice??System.Windows.Media.Matrix.Identity;
  var area=transform.Transform(new Point(screen.Left,screen.Top));
  var size=transform.Transform(new Point(screen.Width,screen.Height));
  MinWidth=Math.Min(900,size.X);MinHeight=Math.Min(580,size.Y);
  Width=Math.Min(1100,Math.Max(MinWidth,size.X*.85));Height=Math.Min(720,Math.Max(MinHeight,size.Y*.85));
  Left=area.X+(size.X-Width)/2;Top=area.Y+(size.Y-Height)/2;
 }
 readonly ConcurrentQueue<string> logQueue=new();
 readonly DispatcherTimer logTimer=new(){Interval=TimeSpan.FromMilliseconds(100)};
 void FlushLogs()
 {
  if(Browser.CoreWebView2==null)return;var batch=new List<string>();while(batch.Count<1000&&logQueue.TryDequeue(out var line))batch.Add(line);
  if(batch.Count>0)Browser.CoreWebView2.PostWebMessageAsJson("{\"type\":\"logBatch\",\"items\":["+string.Join(",",batch)+"]}");
 }
 readonly Store store=new();
 readonly Authentication auth;
 readonly GameService game;
 readonly CatalogService catalog;
 readonly BedrockService bedrock;
 readonly FriendsService friends;
 readonly FriendPacks friendPacks;
 readonly DispatcherTimer friendsTimer=new(){Interval=TimeSpan.FromSeconds(10)};
 bool sendingPresence;
 readonly InstanceArchive archives;
 readonly HashSet<string> activeRequests=[];
 readonly SemaphoreSlim libraryMutation=new(1,1);
 public MainWindow()
 {
  InitializeComponent();logTimer.Tick+=(_,_)=>FlushLogs();logTimer.Start();Closed+=(_,_)=>logTimer.Stop();SourceInitialized+=(_,_)=>{ApplyWindowsCorners();HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowHook);};StateChanged+=(_,_)=>{ApplyWindowsCorners();if(Browser.CoreWebView2!=null)Emit(new{type="windowState",maximized=WindowState==WindowState.Maximized});};auth=new(store,Emit);game=new(store,auth,Emit);catalog=new(store,game,Emit);bedrock=new(store);friends=new(store,auth,game);friendPacks=new(store,game,friends,Emit);friends.PackPublisher=friendPacks.Publish;friendsTimer.Tick+=(_,_)=>RefreshPresence();friendsTimer.Start();Closed+=(_,_)=>friendsTimer.Stop();archives=new(store,game,Emit);Loaded+=async(_,_)=>await Initialize();
 }
 async void RefreshPresence(){if(sendingPresence)return;sendingPresence=true;try{await friends.Heartbeat();}catch{}finally{sendingPresence=false;}}
 async Task Initialize()
 {
  try
  {
   var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(store.Root,"webview"));await Browser.EnsureCoreWebView2Async(env);
   var core=Browser.CoreWebView2;
   core.Settings.AreBrowserAcceleratorKeysEnabled=false;core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreDevToolsEnabled=false;core.Settings.IsStatusBarEnabled=false;
   core.SetVirtualHostNameToFolderMapping("app.spectra.local",Path.Combine(AppContext.BaseDirectory,"Web"),CoreWebView2HostResourceAccessKind.DenyCors);
   core.SetVirtualHostNameToFolderMapping("data.spectra.local",store.Root,CoreWebView2HostResourceAccessKind.DenyCors);
   var skinFolder=Path.Combine(store.Root,"skins");Directory.CreateDirectory(skinFolder);
   core.SetVirtualHostNameToFolderMapping("skins.spectra.local",skinFolder,CoreWebView2HostResourceAccessKind.Allow);
   var fontFolder=Path.Combine(store.Root,"fonts");Directory.CreateDirectory(fontFolder);
   core.SetVirtualHostNameToFolderMapping("fonts.spectra.local",fontFolder,CoreWebView2HostResourceAccessKind.Allow);
   core.NavigationStarting+=(_,e)=>{if(!e.Uri.StartsWith("https://app.spectra.local/",StringComparison.OrdinalIgnoreCase))e.Cancel=true;};
   core.NewWindowRequested+=(_,e)=>e.Handled=true;
   core.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
   core.WebMessageReceived+=Receive;
   // Clear only cached assets, preserving cookies and appearance storage.
   try{await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);}catch{ /* Cache cleanup must not prevent startup. */ }
   if(store.Config.LauncherFullscreen)ToggleFullscreen();
   core.Navigate("https://app.spectra.local/index.html");
  }
  catch(Exception ex){MessageBox.Show("Для Spectra нужен Microsoft Edge WebView2 Runtime.\n"+ex.Message,"Spectra");Close();}
 }
 void Emit(object data)
 {
  var json=JsonSerializer.Serialize(data,Store.Json);
  var kind=data.GetType().GetProperty("type")?.GetValue(data)?.ToString();
  if(kind=="log"){logQueue.Enqueue(json);while(logQueue.Count>5000)logQueue.TryDequeue(out _);return;}
  Dispatcher.InvokeAsync(()=>
  {
   if(Browser.CoreWebView2==null)return;
   Browser.CoreWebView2.PostWebMessageAsJson(json);
   var n=kind is "started" or "exited" or "activity"?JsonNode.Parse(json):null;if(n.Str("type")=="started"&&n?["hide"]?.GetValue<bool>()==true)WindowState=WindowState.Minimized;
   if(n.Str("type")=="activity"){if(n?["clearLan"]?.GetValue<bool>()==true)friends.ShareLan("");RefreshPresence();}
   if(n.Str("type")=="started")RefreshPresence();
   if(n.Str("type")=="exited"){RefreshPresence();WindowState=WindowState.Normal;Activate();}
  });
 }
 async void Receive(object? sender,CoreWebView2WebMessageReceivedEventArgs e)
 {
  if(!e.Source.StartsWith("https://app.spectra.local/",StringComparison.OrdinalIgnoreCase))return;
  string requestId="";bool transfer=false,mutating=false;
  try
  {
   var message=JsonNode.Parse(e.WebMessageAsJson)!;requestId=message.Str("id");if(!activeRequests.Add(requestId))return;
   var action=message.Str("action");var d=message["data"]??new JsonObject();transfer=action is "contentDownload" or "contentImport" or "installVersion" or "packContents" or "bedrockLaunch" or "bedrockDownload" or "launch" or "install" or "installPack" or "importPack" or "importInstance" or "exportInstance" or "installFriendPack" or "joinFriend";Net.ProgressSink.Value=Emit;object? result;
   if(action is "contentDownload" or "contentImport" or "bedrockContentDelete" or "installVersion" or "deleteVersion" or "launch" or "launchTarget" or "joinFriend" or "install" or "installPack" or "installFriendPack" or "importPack" or "importInstance" or "dropPack" or "exportInstance" or "saveInstance" or "deleteInstance" or "enableFiles" or "toggle" or "serverAction" or "worldAction"){await libraryMutation.WaitAsync();mutating=true;}
   if(action=="dropPack")
   {
    transfer=true;var files=e.AdditionalObjects.OfType<CoreWebView2File>().ToArray();if(files.Length!=1||!files[0].Path.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))throw new IOException("Перенесите один ZIP-архив сборки");
    await Task.Run(()=>archives.Import(files[0].Path));result=State();Emit(new{type="transferComplete"});
   }
   else result=await Handle(action,d);
   if(action is "contentDownload" or "contentImport" or "installVersion" or "packContents" or "bedrockLaunch" or "bedrockDownload" or "launch" or "install" or "installPack" or "importPack" or "importInstance" or "exportInstance" or "installFriendPack" or "joinFriend")Emit(new{type="transferComplete"});
   Emit(new{type="reply",id=requestId,ok=true,result});
  }
  catch(Exception ex){if(transfer)Emit(new{type="transferComplete"});Emit(new{type="reply",id=requestId,ok=false,error=ex.Message});}
  finally{Net.ProgressSink.Value=null;if(mutating)libraryMutation.Release();activeRequests.Remove(requestId);}
 }
 object State()=>new{launcherFullscreen=fullscreen,bedrockSkinFolder=store.Config.BedrockSkinFolder,networkAccount=friends.Account,friendsEndpoint=store.Config.FriendsEndpoint,shareGameActivity=store.Config.ShareGameActivity,hideOnlineStatus=store.Config.HideOnlineStatus,friendsSharingError=friends.LastSharingError,installedCommitName=File.Exists(Path.Combine(AppContext.BaseDirectory,"build-commit-name.txt"))?File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"build-commit-name.txt")).Trim():"",installedCommit=File.Exists(Path.Combine(AppContext.BaseDirectory,"build-commit.txt"))?File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"build-commit.txt")).Trim():"",updateReport=File.Exists(Path.Combine(store.Root,"updates","last-apply.json"))?JsonNode.Parse(File.ReadAllText(Path.Combine(store.Root,"updates","last-apply.json"))):null,appearance=JsonNode.Parse(store.Config.AppearanceJson),instances=store.Config.Instances,defaults=store.Config.Defaults,profile=auth.Profile,hasAccount=auth.HasSavedAccount,curseForgeConfigured=!string.IsNullOrEmpty(store.Config.CurseForgeKey),craftyConfigured=!string.IsNullOrEmpty(store.Config.CraftyKey),running=game.Running.Keys,selection=new{edition=store.Config.SelectedEdition,bedrockId=store.Config.SelectedBedrock,bedrockLabel=store.Config.SelectedBedrockLabel,version=store.Config.SelectedVersion,instanceId=store.Config.SelectedInstance},views=new{versions=store.Config.VersionsView,instances=store.Config.InstancesView},skins=store.Config.Skins.Where(x=>x.Owner==auth.Profile.Str("id")).Select(x=>new{x.Id,x.Name,x.Variant,x.Added,image="https://skins.spectra.local/"+x.Id+".png"})};
 async Task<object?> Handle(string action,JsonNode d)
 {
  switch(action)
  {
   case "state":
   {
    return State();
   }
   case "networkBegin":
   {
    await friends.ConnectMinecraft();return new{status="connected",state=State()};
   }
   case "networkPoll":
   {
    return new{status="connected",state=State()};
   }
   case "networkLogout":
   {
    await friends.Disconnect();return State();
   }
   case "networkAccount":
   {
    await friends.UpdateAccount(d.Str("name"));return State();
   }
   case "friendsSearch":
   {
    return await friends.Call("/players?q="+Uri.EscapeDataString(d.Str("query")));
   }
   case "friendsSettings":
   {
    var endpoint=FriendsService.ValidateEndpoint(d.Str("endpoint"));if(endpoint!=store.Config.FriendsEndpoint)await friends.Disconnect();store.Config.FriendsEndpoint=endpoint;store.Config.ShareGameActivity=d["shareActivity"]?.GetValue<bool>()??store.Config.ShareGameActivity;store.Config.HideOnlineStatus=d["hideOnline"]?.GetValue<bool>()??store.Config.HideOnlineStatus;store.Save();await friends.Heartbeat();return State();
   }
   case "friends":
   {
    var result=await friends.Call("/friends");return new{items=result["items"],sharingError=friends.LastSharingError};
   }
   case "friendAction":
   {
    return await friends.Call("/relationship",new{id=d.Str("id"),operation=d.Str("operation")});
   }
   case "shareLan":
   {
    friends.ShareLan(d.Str("address"));await friends.Heartbeat();return new{ok=true};
   }
   case "friendPackPlan":
   {
    return await friendPacks.Plan(d.Str("id"));
   }
   case "installFriendPack":
   {
    var instance=await friendPacks.Install(d.Str("id"),d.Str("packId"),d.Str("overwrite"),d["selected"]?.AsArray()??throw new IOException("Выберите файлы"),d["categories"]?.AsArray()??throw new IOException("Выберите категории"));return new{instanceId=instance.Instance.Id,presence=instance.Presence,state=State()};
   }
   case "joinFriend":
   {
    if(!game.Running.IsEmpty)throw new IOException("Закройте Minecraft перед подключением к другу");
    var presence=await friends.JoinInfo(d.Str("id"));Instance matching;
    if(presence.Str("loader")=="vanilla")matching=new Instance{Id="vanilla",Version=presence.Str("version"),Settings=store.Config.Defaults with {}};
    else{var match=await friendPacks.Match(d.Str("id"),d.Str("instanceId"),d["allowCustomized"]?.GetValue<bool>()==true);matching=match.Instance;presence=match.Presence;}
    var version=presence.Str("version");await game.Metadata(version);
    if(presence.Str("targetKind")=="lan"){
     var host=presence.Str("target").Split(':')[0];if(!System.Net.IPAddress.TryParse(host,out var remoteIp))throw new IOException("Неверный LAN-адрес");
     var bytes=remoteIp.GetAddressBytes();var sameNetwork=System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==System.Net.NetworkInformation.OperationalStatus.Up).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Any(a=>a.Address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork&&bytes.Length==4&&a.IPv4Mask!=null&&a.Address.GetAddressBytes().Zip(bytes,(local,remote)=>local^remote).Zip(a.IPv4Mask.GetAddressBytes(),(difference,mask)=>(difference&mask)==0).All(x=>x));
     if(!sameNetwork)throw new IOException("LAN-подключение доступно только в одной локальной сети с другом");
    }
    if(!game.Running.IsEmpty)throw new IOException("Закройте Minecraft перед подключением к другу");
    store.Config.SelectedInstance=matching.Id=="vanilla"?"":matching.Id;store.Config.SelectedVersion=matching.Version;store.Save();
    await game.Launch(matching.Id,"servers",presence.Str("target"));return State();
   }
   case "fonts":
   {
    var fontDir=Path.Combine(store.Root,"fonts");Directory.CreateDirectory(fontDir);
    return Directory.EnumerateFiles(fontDir,"*.json").Select(path=>JsonNode.Parse(File.ReadAllText(path))).ToArray();
   }
   case "importFont":
   {
    var fontPicker=new Microsoft.Win32.OpenFileDialog{Filter="Шрифты|*.ttf;*.otf;*.woff;*.woff2",Title="Выберите шрифт"};if(fontPicker.ShowDialog(this)!=true)return null;
    if(new FileInfo(fontPicker.FileName).Length>10*1024*1024)throw new IOException("Шрифт должен быть не больше 10 МиБ");
    var fontBytes=await File.ReadAllBytesAsync(fontPicker.FileName);if(fontBytes.Length<12||fontBytes.Length>10*1024*1024)throw new IOException("Шрифт должен быть не больше 10 МиБ");
    var signature=Convert.ToHexString(fontBytes.AsSpan(0,4));if(signature is not ("00010000" or "4F54544F" or "774F4646" or "774F4632"))throw new IOException("Неподдерживаемый формат шрифта");
    var fontId=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fontBytes)).ToLowerInvariant()+Path.GetExtension(fontPicker.FileName).ToLowerInvariant();var fontDir=Path.Combine(store.Root,"fonts");Directory.CreateDirectory(fontDir);
    await File.WriteAllBytesAsync(Path.Combine(fontDir,fontId),fontBytes);var fontEntry=new{id=fontId,name=Path.GetFileNameWithoutExtension(fontPicker.FileName)};await File.WriteAllTextAsync(Path.Combine(fontDir,fontId+".json"),JsonSerializer.Serialize(fontEntry,Store.Json));return fontEntry;
   }
   case "appearance":
   {
    var appearance=d.ToJsonString();if(appearance.Length>131072)throw new IOException("Тема слишком большая");
    store.Config.AppearanceJson=appearance;store.Save();return State();
   }
   case "bootstrap":
   {
    return await SourceUpdater.Check();
   }
   case "updateSource":
   {
    if(game.Running.Count>0)throw new IOException("Закройте Minecraft перед обновлением лаунчера");
    Browser.IsEnabled=false;
    try{await SourceUpdater.Prepare(d.Str("commit"),store,Emit);Application.Current.Shutdown();return new{restarting=true};}
    catch{if(SourceUpdater.ValidCommit(d.Str("commit"))){Directory.CreateDirectory(Path.Combine(store.Root,"updates"));File.WriteAllText(Path.Combine(store.Root,"updates","failed-commit.txt"),d.Str("commit"));}throw;}
    finally{Browser.IsEnabled=true;}
   }
   case "login":
   {
    await auth.Login(d["interactive"]?.GetValue<bool>()??true);try{await friends.ConnectMinecraft();}catch(Exception ex){Emit(new{type="networkError",message=ex.Message});}ExpandLibrary();return State();
   }
   case "expand":
   {
    ExpandLibrary();return State();
   }
   case "logout":
   {
    await friends.Disconnect();await auth.Logout();return State();
   }
   case "window":
   {
    switch(d.Str("command")){case "close":Close();break;case "minimize":WindowState=WindowState.Minimized;break;case "fullscreen":ToggleFullscreen();break;case "maximize":if(fullscreen)ToggleFullscreen();WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;Emit(new{type="windowState",maximized=WindowState==WindowState.Maximized});break;case "drag":if((GetAsyncKeyState(0x01)&0x8000)!=0){ReleaseCapture();SendMessage(new WindowInteropHelper(this).Handle,0x00A1,2,0);}break;}return null;
   }
   case "select":
   {
    store.Config.SelectedEdition="java";
    if(!string.IsNullOrEmpty(d.Str("instanceId")))store.Get(d.Str("instanceId"));
    else await game.Metadata(d.Str("version"));
    store.Config.SelectedInstance=d.Str("instanceId");store.Config.SelectedVersion=d.Str("version");store.Save();return State();
   }
   case "view":
   {
    var mode=d.Str("mode");if(mode is not ("cards" or "tiles" or "list"))throw new IOException("Неизвестный вид");
    if(d.Str("kind")=="versions")store.Config.VersionsView=mode;else store.Config.InstancesView=mode;store.Save();return State();
   }
   case "addSkin":
   {
    if(auth.Session==null)throw new IOException("Сначала войдите в аккаунт");
    var picker=new Microsoft.Win32.OpenFileDialog{Filter="Minecraft PNG|*.png"};if(picker.ShowDialog()!=true)return State();
    if(new FileInfo(picker.FileName).Length>1024*1024)throw new IOException("Скин больше 1 МБ");
    var bitmap=new System.Windows.Media.Imaging.BitmapImage();bitmap.BeginInit();bitmap.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(picker.FileName);bitmap.EndInit();
    if(bitmap.PixelWidth!=64||bitmap.PixelHeight is not (32 or 64))throw new IOException("Нужен PNG размером 64×64 или 64×32");
    var skin=new SavedSkin{Name=Path.GetFileNameWithoutExtension(picker.FileName),Owner=auth.Profile.Str("id"),Variant=d.Str("variant")=="slim"?"slim":"classic"};
    Directory.CreateDirectory(Path.Combine(store.Root,"skins"));File.Copy(picker.FileName,Store.SafePath(store.Root,"skins/"+skin.Id+".png"));store.Config.Skins.Add(skin);store.Save();return State();
   }
   case "removeSkin":
   {
    var removed=store.Config.Skins.Single(x=>x.Id==d.Str("id")&&x.Owner==auth.Profile.Str("id"));store.Config.Skins.Remove(removed);File.Delete(Store.SafePath(store.Root,"skins/"+removed.Id+".png"));store.Save();return State();
   }
   case "applySkin":
   {
    var saved=store.Config.Skins.Single(x=>x.Id==d.Str("id")&&x.Owner==auth.Profile.Str("id"));
    await auth.ApplySavedSkin(Store.SafePath(store.Root,"skins/"+saved.Id+".png"),saved.Variant);return State();
   }
   case "skinTexture":
   {
    var textureUri=new Uri(d.Str("url"));if(textureUri.Scheme!="https"||textureUri.Host!="textures.minecraft.net")throw new IOException("Неизвестный источник текстуры");
    using(var response=await Net.Http.GetAsync(textureUri,HttpCompletionOption.ResponseHeadersRead))
    {
     response.EnsureSuccessStatusCode();await using var input=await response.Content.ReadAsStreamAsync();using var output=new MemoryStream();var buffer=new byte[8192];int count;
     while((count=await input.ReadAsync(buffer))>0){if(output.Length+count>1024*1024)throw new IOException("Слишком большая текстура");output.Write(buffer,0,count);}
     return "data:image/png;base64,"+Convert.ToBase64String(output.ToArray());
    }
   }
   case "player":
   {
    return await Player(d.Str("name"));
   }
   case "installVersion":await game.InstallVersion(d.Str("version"));return State();
   case "deleteVersion":game.DeleteVersion(d.Str("version"));return State();
   case "installedJava":return await Task.Run(()=>game.InstalledVanilla());
   case "versions":
   {
    return await game.Versions();
   }
   case "artwork":
   {
    return await game.Artwork();
   }
   case "components":
   {
    return await game.Components(d.Str("instanceId"));
   }
   case "loaders":
   {
    return await game.Loaders(d.Str("version"),d.Str("loader"));
   }
   case "saveInstance":
   {
    var incoming=d.Deserialize<Instance>(Store.Json)??throw new IOException("Нет сборки");if(incoming.Name.Trim().Length is <1 or >80)throw new IOException("Название: от 1 до 80 символов");await game.Metadata(incoming.Version);Store.Validate(incoming.Settings);
    if(!new[]{"vanilla","fabric","forge","quilt","neoforge"}.Contains(incoming.Loader))throw new IOException("Неизвестный загрузчик");
    var current=store.Config.Instances.Find(x=>x.Id==incoming.Id);if(current==null){incoming.Id=Guid.NewGuid().ToString("N");store.Config.Instances.Add(incoming);}else{if(game.Running.ContainsKey(current.Id))throw new IOException("Закройте игру перед изменением сборки");store.Config.Instances[store.Config.Instances.IndexOf(current)]=incoming;}store.Folder(incoming);store.Save();return State();
   }
   case "settings":
   {
    var settings=d["defaults"]!.Deserialize<GameSettings>(Store.Json)!;Store.Validate(settings);store.Config.Defaults=settings;
    if(d.Str("curseForgeKey")!="")store.Config.CurseForgeKey=d.Str("curseForgeKey");if(d.Str("craftyKey")!="")store.Config.CraftyKey=d.Str("craftyKey");store.Save();return State();
   }
   case "launch":
   {
    await game.Launch(d.Str("instanceId"));return State();
   }
   case "stop":
   {
    game.Stop(d.Str("instanceId"));return null;
   }
   case "files":
   {
    return await Task.Run(()=>game.Files(d.Str("instanceId"),d.Str("kind")));
   }
   case "deleteInstance":
   {
    new LibraryActions(store,game).DeleteInstance(d.Str("instanceId"));return State();
   }
   case "enableFiles":
   {
    new LibraryActions(store,game).Enable(d.Str("instanceId"),d.Str("kind"),d["paths"]!.Deserialize<string[]>(Store.Json)!,d["enabled"]!.GetValue<bool>());return null;
   }
   case "serverAction":
   {
    new LibraryActions(store,game).Server(d.Str("instanceId"),d.Str("operation"),d["index"]?.GetValue<int>()??-1,d.Str("name"),d.Str("address"));return null;
   }
   case "worldAction":
   {
    var library=new LibraryActions(store,game);
    if(d.Str("operation")=="rename")library.RenameWorld(d.Str("instanceId"),d.Str("path"),d.Str("name"));
    else if(d.Str("operation")=="delete")library.DeleteWorld(d.Str("instanceId"),d.Str("path"));
    else if(d.Str("operation")=="import"){using var worldPicker=new System.Windows.Forms.FolderBrowserDialog{Description="Папка мира с level.dat"};if(worldPicker.ShowDialog()==System.Windows.Forms.DialogResult.OK)await Task.Run(()=>library.ImportWorld(d.Str("instanceId"),worldPicker.SelectedPath));}return null;
   }
   case "renameScreenshot":
   {
    new LibraryActions(store,game).RenameScreenshot(d.Str("instanceId"),d.Str("path"),d.Str("name"));return null;
   }
   case "copyScreenshots":
   {
    var screenshots=d["paths"]!.Deserialize<string[]>(Store.Json)!;var screenshotPaths=screenshots.Select(p=>new LibraryActions(store,game).PathFor(d.Str("instanceId"),"screenshots",p)).ToArray();
    if(screenshotPaths.Length==1){var screenshotBitmap=new System.Windows.Media.Imaging.BitmapImage();screenshotBitmap.BeginInit();screenshotBitmap.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;screenshotBitmap.UriSource=new Uri(screenshotPaths[0]);screenshotBitmap.EndInit();screenshotBitmap.Freeze();Clipboard.SetImage(screenshotBitmap);}
    else if(screenshotPaths.Length>1){var collection=new System.Collections.Specialized.StringCollection();collection.AddRange(screenshotPaths);Clipboard.SetFileDropList(collection);}return null;
   }
   case "launchTarget":
   {
    await game.Launch(d.Str("instanceId"),d.Str("kind"),d.Str("target"));return State();
   }
   case "toggle":
   {
    game.Toggle(d.Str("instanceId"),d.Str("path"));return null;
   }
   case "readLog":
   {
    return await game.ReadLog(d.Str("instanceId"),d.Str("path"));
   }
   case "openFolder":
   {
    var inst=store.Get(d.Str("instanceId"));var root=store.Folder(inst);var kind=d.Str("kind");var path=kind=="instance"?Path.GetDirectoryName(root)!:kind=="minecraft"?root:Path.Combine(root,GameService.KindFolder(kind));Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo(path){UseShellExecute=true});return null;
   }
   case "pickJava":
   {
    var java=new Microsoft.Win32.OpenFileDialog{Filter="Java|javaw.exe;java.exe"};return java.ShowDialog()==true?java.FileName:null;
   }
   case "pickImage":
   {
    var img=new Microsoft.Win32.OpenFileDialog{Filter="Изображения|*.png;*.jpg;*.jpeg;*.webp"};if(img.ShowDialog()!=true)return null;
    if(new FileInfo(img.FileName).Length>20*1024*1024)throw new IOException("Изображение больше 20 МБ");var asset=Path.Combine(store.Root,"artwork",Guid.NewGuid()+Path.GetExtension(img.FileName));Directory.CreateDirectory(Path.GetDirectoryName(asset)!);File.Copy(img.FileName,asset);return game.Asset(asset);
   }
   case "contentBrowserSearch":return await new ContentCatalog(store,uri=>CatalogBrowser.Read(this,store,uri)).Search(d,true);
   case "contentSearch":return await new ContentCatalog(store).Search(d);
   case "contentDownload":return await new ContentCatalog(store).Download(d,new ContentService(store,game,auth));
   case "contentSources":return ContentService.Sources(d.Str("edition"),d.Str("kind"),d.Str("query"));
   case "contentImport":return await new ContentService(store,game,auth).Import(d,this);
   case "bedrockContent":return new ContentService(store,game,auth).BedrockItems();
   case "bedrockContentDelete":new ContentService(store,game,auth).DeleteBedrock(d.Str("id"));return null;
   case "bedrockSkinFolder":using(var folder=new System.Windows.Forms.FolderBrowserDialog()){if(folder.ShowDialog()==System.Windows.Forms.DialogResult.OK){store.Config.BedrockSkinFolder=folder.SelectedPath;store.Save();}}return State();
   case "bedrockVersions":return await bedrock.Versions(d["refresh"]?.GetValue<bool>()??false);
   case "bedrockImport":
   {
    var bedrockPicker=new Microsoft.Win32.OpenFileDialog{Filter="Minecraft Windows package|*.appx;*.msix"};if(bedrockPicker.ShowDialog(this)!=true)return null;return await bedrock.Import(bedrockPicker.FileName);
   }
   case "bedrockSelect":
   {
    store.Config.SelectedEdition="bedrock";store.Config.SelectedBedrock=d.Str("id");store.Config.SelectedBedrockLabel=d.Str("label");store.Save();return State();
   }
   case "bedrockLaunch":return await bedrock.Launch(d.Str("id"),d["install"]?.GetValue<bool>()??false,false,d["replace"]?.GetValue<bool>()??false,d["installOnly"]?.GetValue<bool>()??false);
   case "bedrockDownload":return await bedrock.Download(d.Str("id"));
   case "bedrockOfficialLauncher":await bedrock.OpenOfficialLauncher();return null;
   case "bedrockDeleteVersion":await bedrock.DeleteVersion(d.Str("id"));return State();
   case "bedrockUnregister":return await bedrock.Unregister();
   case "bedrockLaunchCurrent":return await bedrock.Launch("latest",false,true);
   case "bedrockStore":BedrockService.OpenStore();return null;
   case "packContents":return await catalog.PackContents(d.Str("source"),d.Str("projectId"),d.Str("versionId"));
   case "projectDetails":return await catalog.Details(d.Str("source"),d.Str("projectId"),d.Str("kind"),d.Str("instanceId"));
   case "openLink":
   {
    if(!Uri.TryCreate(d.Str("url"),UriKind.Absolute,out var projectLink)||projectLink.Scheme!="https"||!string.IsNullOrEmpty(projectLink.UserInfo))throw new IOException("Разрешены только HTTPS-ссылки");Process.Start(new ProcessStartInfo(projectLink.AbsoluteUri){UseShellExecute=true});return null;
   }
   case "search":
   {
    return await catalog.Search(d.Str("query"),d.Str("kind"),d.Str("instanceId"),d["offset"]?.GetValue<int>()??0);
   }
   case "install":
   {
    await catalog.Install(d.Str("source"),d.Str("projectId"),d.Str("kind"),d.Str("instanceId"));return null;
   }
   case "installPack":
   {
    await catalog.InstallPack(d.Str("source"),d.Str("projectId"),d.Str("versionId"));return State();
   }
   case "importInstance":
   {
    var instanceZip=new Microsoft.Win32.OpenFileDialog{Filter="Prism / MultiMC ZIP|*.zip"};if(instanceZip.ShowDialog()==true)await Task.Run(()=>archives.Import(instanceZip.FileName));return State();
   }
   case "exportInstance":
   {
    var exportInstance=store.Get(d.Str("instanceId"));
    var exportDialog=new Microsoft.Win32.SaveFileDialog{Filter="Prism / MultiMC ZIP|*.zip",DefaultExt=".zip",FileName=string.Concat(exportInstance.Name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c))+".zip"};
    if(exportDialog.ShowDialog()!=true)return new{cancelled=true};await archives.Export(exportInstance.Id,exportDialog.FileName,d["includeWorlds"]?.GetValue<bool>()??false,d.Str("target"));return new{cancelled=false};
   }
   case "importPack":
   {
    var pack=new Microsoft.Win32.OpenFileDialog{Filter="Модпаки|*.mrpack;*.zip"};if(pack.ShowDialog()==true){if(pack.FileName.EndsWith(".mrpack",StringComparison.OrdinalIgnoreCase))await catalog.ImportMrpack(pack.FileName);else await catalog.ImportCursePack(pack.FileName);}return State();
   }
   case "profileAction":
   {
    await auth.ProfileAction(d.Str("operation"),d);return State();
   }
   case "crafty":
   {
    if(string.IsNullOrWhiteSpace(store.Config.CraftyKey))throw new IOException("Укажите Crafty API-токен в настройках");using(var req=new HttpRequestMessage(HttpMethod.Get,"https://api.crafty.gg/api/v2/players/"+Uri.EscapeDataString(d.Str("name")))){req.Headers.Authorization=new("Bearer",store.Config.CraftyKey);return await Net.Send(req);}
   }
   case "external":
   {
    var url=d.Str("url");var uri=new Uri(url);if(uri.Scheme!="https"||!new[]{"github.com","crafty.gg","www.minecraft.net","www.curseforge.com","modrinth.com","microsoft.com","www.microsoft.com","login.microsoftonline.com"}.Contains(uri.Host))throw new IOException("Недопустимая ссылка");Process.Start(new ProcessStartInfo(url){UseShellExecute=true});return null;
   }
   case "gpus":
   {
    return await Task.Run(()=>GetGpus());
   }
   default:
   {
    throw new IOException("Неизвестная команда");
   }
  }
 }
 async Task<JsonNode> Player(string name)
 {
  if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"^[A-Za-z0-9_]{3,16}$"))throw new IOException("Некорректный ник");
  var basic=await Net.Get("https://api.mojang.com/users/profiles/minecraft/"+Uri.EscapeDataString(name));
  var full=await Net.Get("https://sessionserver.mojang.com/session/minecraft/profile/"+basic.Str("id"));
  var value=full["properties"]?.AsArray().FirstOrDefault(x=>x.Str("name")=="textures").Str("value");
  var textures=string.IsNullOrEmpty(value)?null:JsonNode.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value)));
  var url=textures?["textures"]?["SKIN"].Str("url").Replace("http://textures.minecraft.net/","https://textures.minecraft.net/");
  return new JsonObject{["id"]=basic.Str("id"),["name"]=basic.Str("name"),["skins"]=new JsonArray(new JsonObject{["url"]=url,["variant"]=textures?["textures"]?["SKIN"]?["metadata"].Str("model")=="slim"?"SLIM":"CLASSIC"}),["capes"]=textures?["textures"]?["CAPE"] is JsonNode cape?new JsonArray(new JsonObject{["url"]=cape.Str("url").Replace("http://textures.minecraft.net/","https://textures.minecraft.net/"),["state"]="ACTIVE"}):new JsonArray()};
 }
 static string[] GetGpus()
 {
  using var p=Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -Command \"Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true});if(p==null)return [];var output=p.StandardOutput.ReadToEnd();p.WaitForExit();return output.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
 }
}
