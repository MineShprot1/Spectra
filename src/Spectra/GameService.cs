using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Xml.Linq;
using CmlLib.Core;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Installers;
using CmlLib.Core.FileExtractors;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.Installer.NeoForge.Installers;
using Microsoft.Win32;
namespace Spectra;
public sealed class GameService(Store store, Authentication auth, Action<object> emit)
{
 public ConcurrentDictionary<string,Process> Running { get; } = new();
 readonly ConcurrentDictionary<string,byte> busy=new();
 readonly SemaphoreSlim installationGate=new(1,1);
 MinecraftLauncher CreateLauncher(string root)
 {
  var path=new MinecraftPath(root){Assets=Path.Combine(store.Root,"cache","assets")};
  var parameters=MinecraftLauncherParameters.CreateDefault(path,Net.Http);
  var extractors=DefaultFileExtractors.CreateDefault(Net.Http,parameters.RulesEvaluator!,parameters.JavaPathResolver!);extractors.Java=null;parameters.FileExtractors=extractors.ToExtractorCollection();
  parameters.GameInstaller=new ParallelGameInstaller(Math.Clamp(Environment.ProcessorCount,2,4),16,256,Net.Http);
  return new MinecraftLauncher(parameters);
 }
 readonly ConcurrentDictionary<string,JsonNode> metadata=new();
 JsonNode? manifest;
 public string[] InstalledJava()
 {
  var root=Path.Combine(store.Root,"instances");var ids=new HashSet<string>(StringComparer.Ordinal);if(!Directory.Exists(root))return [];
  foreach(var instance in Directory.EnumerateDirectories(root).Take(1000)){var versionsFolder=Path.Combine(instance,".minecraft","versions");if(!Directory.Exists(versionsFolder))continue;foreach(var folder in Directory.EnumerateDirectories(versionsFolder).Take(2000)){var id=Path.GetFileName(folder);var json=Path.Combine(folder,id+".json");var jar=Path.Combine(folder,id+".jar");try{if(!File.Exists(jar)||new FileInfo(jar).Length==0||!File.Exists(json)||new FileInfo(json).Length>8*1024*1024)continue;var node=JsonNode.Parse(File.ReadAllText(json));if(node.Str("id")==id&&node?["inheritsFrom"]==null)ids.Add(id);}catch{}}}
  return ids.OrderByDescending(id=>id,StringComparer.Ordinal).ToArray();
 }
 public async Task InstallVersion(string version)
 {
  await Metadata(version);await installationGate.WaitAsync();try{
   var instance=new Instance{Id="vanilla",Version=version,Settings=store.Config.Defaults with {}};
   await Java(instance);var launcher=CreateLauncher(store.Folder(instance));
   launcher.FileProgressChanged+=(_,e)=>emit(new{type="progress",message=e.Name,percent=e.TotalTasks>0?e.ProgressedTasks*100d/e.TotalTasks:0});
   await launcher.InstallAsync(version);
  }finally{installationGate.Release();}
 }
 public void DeleteVersion(string version)
 {
  if(!System.Text.RegularExpressions.Regex.IsMatch(version,@"\A[A-Za-z0-9_.-]+\z")||version is "." or "..")throw new IOException("Неверная версия");
  if(Running.ContainsKey("vanilla"))throw new IOException("Закройте Minecraft");
  var root=Path.Combine(store.Root,"instances","vanilla",".minecraft");
  var directory=Store.SafePath(root,"versions/"+version);if(Directory.Exists(directory))Directory.Delete(directory,true);
 }
 public string[] InstalledVanilla()
 {
  var root=Path.Combine(store.Root,"instances","vanilla",".minecraft","versions");
  return !Directory.Exists(root)?[]:Directory.EnumerateDirectories(root).Select(Path.GetFileName).Where(id=>id!=null&&File.Exists(Path.Combine(root,id,id+".jar"))&&File.Exists(Path.Combine(root,id,id+".json"))).Select(id=>id!).ToArray();
 }
 public async Task<JsonNode> Versions() => (await Manifest())["versions"]!.DeepClone();
 async Task<JsonNode> Manifest() => manifest??=await Net.Get("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
 public async Task<JsonNode> Metadata(string version)
 {
  if(metadata.TryGetValue(version,out var cached))return cached.DeepClone();
  var item=(await Manifest())["versions"]!.AsArray().FirstOrDefault(v=>v.Str("id")==version)??throw new IOException("Версия отсутствует в официальном манифесте");
  var result=await Net.Get(item.Str("url"));metadata[version]=result;return result.DeepClone();
 }
 Dictionary<string,string>? artwork;
 public async Task<object> Artwork()
 {
  if(artwork!=null)return artwork;
  var feed=await Net.Get("https://launchercontent.mojang.com/v2/javaPatchNotes.json");
  artwork=new Dictionary<string,string>();
  foreach(var entry in feed["entries"]?.AsArray()??[]){var image=entry?["image"].Str("url");if(!string.IsNullOrEmpty(image))artwork.TryAdd(entry.Str("version"),new Uri(new Uri("https://launchercontent.mojang.com/"),image).ToString());}
  return artwork;
 }
 public async Task<object[]> Loaders(string version,string loader)
 {
  if(loader=="vanilla")return [];
  if(loader is "fabric" or "quilt")
  {
   var host=loader=="fabric"?"https://meta.fabricmc.net/v2":"https://meta.quiltmc.org/v3";
   var list=await Net.Get(host+"/versions/loader/"+Uri.EscapeDataString(version));
   return list.AsArray().Select(x=>(object)new{version=x?["loader"].Str("version"),recommended=x?["loader"]?["stable"]?.GetValue<bool>()??false}).ToArray();
  }
  if(loader=="forge")
  {
   var versions=await new ForgeInstaller(new MinecraftLauncher()).GetForgeVersions(version);
   return versions.Select(x=>(object)new{version=x.ForgeVersionName,recommended=x.IsRecommendedVersion}).ToArray();
  }
  if(loader=="neoforge")
  {
   var versions=await new NeoForgeInstaller(new MinecraftLauncher()).GetForgeVersions(version);
   return versions.Select((x,index)=>(object)new{version=x.VersionName,recommended=index==0&&!x.VersionName.Contains("beta")}).ToArray();
  }
  throw new IOException("Неизвестный загрузчик");
 }
 async Task<string> Java(Instance i)
 {
  if(!string.IsNullOrWhiteSpace(i.Settings.JavaPath)) { if(!File.Exists(i.Settings.JavaPath))throw new IOException("javaw.exe не найден");return i.Settings.JavaPath; }
  var metadata=await Metadata(i.Version);var major=metadata["javaVersion"]?["majorVersion"]?.GetValue<int>()??8;
  var dir=Path.Combine(store.Root,"runtimes",major.ToString());
  var existing=Directory.Exists(dir)?Directory.EnumerateFiles(dir,"javaw.exe",SearchOption.AllDirectories).FirstOrDefault():null;
  if(existing!=null)return existing;
  emit(new{type="progress",instanceId=i.Id,message=$"Установка Eclipse Temurin Java {major}",percent=0});
  var assets=await Net.Get($"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse");
  if(assets.AsArray().Count==0)assets=await Net.Get($"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture=x64&image_type=jdk&os=windows&vendor=eclipse");
  var package=assets[0]?["binary"]?["package"]??throw new IOException("Adoptium не вернул Java");
  var zip=Path.Combine(store.Root,"runtimes",major+".zip"); await Net.Download(package.Str("link"),zip,package.Str("checksum"),"SHA256");
  Directory.CreateDirectory(dir);await Task.Run(()=>ZipFile.ExtractToDirectory(zip,dir,true));File.Delete(zip);
  return Directory.EnumerateFiles(dir,"javaw.exe",SearchOption.AllDirectories).First();
 }
 public static void Gpu(string java,string preference)
 {
  using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
  var path=Path.GetFullPath(java);
  if(preference=="system")key.DeleteValue(path,false);
  else key.SetValue(path,"GpuPreference="+(preference=="high"?"2":"1")+";",RegistryValueKind.String);
 }
 public readonly ConcurrentDictionary<string,GameActivity> Activities=new();
 public record GameActivity(string Version,string Loader,string LoaderVersion,string Pack,string TargetKind,string Target,string PackSource,string PackId,string PackVersion,string GameState="menu",string WorldName="",int LanPort=0,DateTime StartedAt=default);
 readonly ConcurrentDictionary<string,CancellationTokenSource> activityWatchers=new();
 public string? PresenceInstanceId=>Activities.Where(x=>Running.ContainsKey(x.Key)).OrderByDescending(x=>x.Value.StartedAt).ThenBy(x=>x.Key,StringComparer.Ordinal).Select(x=>x.Key).FirstOrDefault();
 static string LocalLanIp(){try{return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==System.Net.NetworkInformation.OperationalStatus.Up&&n.NetworkInterfaceType!=System.Net.NetworkInformation.NetworkInterfaceType.Loopback).OrderByDescending(n=>n.GetIPProperties().GatewayAddresses.Any(g=>g.Address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork)).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(a=>a.Address).Where(ip=>ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork).FirstOrDefault(ip=>{var b=ip.GetAddressBytes();return b[0]==10||b[0]==192&&b[1]==168||b[0]==172&&b[1]>=16&&b[1]<=31;})?.ToString()??"";}catch{return "";}}
 void ObserveActivity(string id,ActivityLog parser,string line,Process process){lock(parser){if(!Running.TryGetValue(id,out var live)||!ReferenceEquals(live,process))return;var observed=parser.Observe(line);if(observed==null)return;if(Activities.TryGetValue(id,out var current)){var kind=observed.Mode is "connecting" or "server"?"servers":observed.Mode=="world"?"worlds":observed.Mode=="lan"?"lan":"";Activities[id]=current with{GameState=observed.Mode,TargetKind=kind,Target=observed.Mode is "connecting" or "server"?observed.Target:observed.World,WorldName=observed.World,LanPort=observed.LanPort};emit(new{type="activity",instanceId=id,clearLan=(observed.Mode is "menu" or "connecting" or "server")||observed.Mode=="world"&&current.LanPort>0});}}}
 void WatchActivity(string id,ActivityLogWatcher watcher,CancellationTokenSource cancellation){_=Task.Run(async()=>{try{await watcher.Run(cancellation.Token);}catch(OperationCanceledException){}catch(Exception e) when(e is IOException or UnauthorizedAccessException){}finally{((ICollection<KeyValuePair<string,CancellationTokenSource>>)activityWatchers).Remove(new(id,cancellation));cancellation.Dispose();}});}
 void StopActivity(string id){if(activityWatchers.TryRemove(id,out var cancellation)){try{cancellation.Cancel();}catch(ObjectDisposedException){}}}

 public object FriendPresence(bool share,string lanAddress="")
 {
  var id=PresenceInstanceId;var item=id!=null&&Activities.TryGetValue(id,out var found)?found:null;
  if(!share||item==null)return new{playing=false};
  var world=item.GameState is "world" or "lan";var address="";if(world){if(lanAddress!="")address=item.LanPort>0?lanAddress.Split(':')[0]+":"+item.LanPort:lanAddress;else if(item.LanPort>0){var local=LocalLanIp();if(local!="")address=local+":"+item.LanPort;}}
  var mode=world&&address!=""?"lan":item.GameState;var kind=mode=="lan"?"lan":item.TargetKind;var target=mode=="lan"?address:item.Target;
  return new{playing=true,gameState=mode,worldName=item.WorldName,version=item.Version,loader=item.Loader,loaderVersion=item.LoaderVersion,pack=item.Pack,targetKind=kind,target,packSource=item.PackSource,packId=item.PackId,packVersion=item.PackVersion};
 }
 public async Task Launch(string id,string targetKind="",string target="")
 {
  var i=store.Get(id);Store.Validate(i.Settings);var activityTarget=target;
  if(targetKind=="worlds")activityTarget=Nbt.World(Path.Combine(new LibraryActions(store,this).PathFor(id,"worlds",target),"level.dat")).Name;
  if(Running.ContainsKey(id)||!busy.TryAdd(id,0))throw new IOException("Сборка уже запускается или запущена");
  await installationGate.WaitAsync();
  try
  {
   await auth.Login(false);
   var java=await Java(i);Gpu(java,i.Settings.Gpu);
   var root=store.Folder(i);var launcher=CreateLauncher(root);
   long lastProgress=0;
   launcher.FileProgressChanged+=(s,e)=>{var now=Environment.TickCount64;if(now-lastProgress<150)return;lastProgress=now;emit(new{type="progress",instanceId=id,message=e.Name,percent=e.TotalTasks>0?(double)e.ProgressedTasks/e.TotalTasks*100:0});};
   long lastBytes=0;
   void AttachBytes(MinecraftLauncher target){target.ByteProgressChanged+=(_,e)=>{var now=Environment.TickCount64;var prior=Interlocked.Read(ref lastBytes);if(now-prior<150&&e.ProgressedBytes<e.TotalBytes)return;if(Interlocked.CompareExchange(ref lastBytes,now,prior)!=prior)return;emit(new{type="progress",instanceId=id,message="Файлы Minecraft",downloadedBytes=e.ProgressedBytes,totalBytes=e.TotalBytes,percent=e.TotalBytes>0?e.ProgressedBytes*100d/e.TotalBytes:0,scope="stage"});};}
   AttachBytes(launcher);
   var version=i.Version;
   await launcher.InstallAsync(version);
   if(i.Loader is "fabric" or "quilt")
   {
    if(string.IsNullOrEmpty(i.LoaderVersion))throw new IOException("Выберите версию загрузчика");
    var host=i.Loader=="fabric"?"https://meta.fabricmc.net/v2":"https://meta.quiltmc.org/v3";
    var profile=await Net.Get(host+"/versions/loader/"+Uri.EscapeDataString(i.Version)+"/"+Uri.EscapeDataString(i.LoaderVersion)+"/profile/json");version=profile.Str("id");
    var file=Store.SafePath(root,"versions/"+version+"/"+version+".json");Directory.CreateDirectory(Path.GetDirectoryName(file)!);await File.WriteAllTextAsync(file,profile.ToJsonString());
    launcher=CreateLauncher(root);AttachBytes(launcher);
   }
   if(i.Loader=="forge") version=await new ForgeInstaller(launcher).Install(i.Version,i.LoaderVersion,new ForgeInstallOptions{JavaPath=java,InstallerOutput=new Progress<string>(line=>emit(new{type="log",instanceId=id,line=Redact(line)}))});
   if(i.Loader=="neoforge") version=await new NeoForgeInstaller(launcher).Install(i.Version,i.LoaderVersion,new NeoForgeInstallOptions{JavaPath=java,InstallerOutput=new Progress<string>(line=>emit(new{type="log",instanceId=id,line=Redact(line)}))});
   if(i.Loader!="vanilla") await launcher.InstallAsync(version);
   var process=await launcher.BuildProcessAsync(version,new MLaunchOption{Session=auth.Session!,JavaPath=java,MinimumRamMb=i.Settings.MinRam,MaximumRamMb=i.Settings.MaxRam,ScreenWidth=i.Settings.Width,ScreenHeight=i.Settings.Height,GameLauncherName="Spectra",GameLauncherVersion="0.17.3"});
   if(targetKind!=""){
    if(target.Contains('"')||target.Contains('\\')&&targetKind=="servers"||target.Any(char.IsControl))throw new IOException("Некорректная цель запуска");
    var modern=Version.TryParse(i.Version,out var mc)&&mc>=new Version(1,20);
    if(targetKind=="worlds"){
     var world=new LibraryActions(store,this).PathFor(id,"worlds",target);if(!Directory.Exists(world))throw new IOException("Мир не найден");
     if(!modern)throw new IOException("Прямой запуск мира поддерживается с Minecraft 1.20. Для старой версии откройте мир в меню игры.");target=Path.GetFileName(world);
    }else if(targetKind!="servers")throw new IOException("Неизвестная цель запуска");
    var args=new List<string>();
    if(modern){args.Add(targetKind=="worlds"?"--quickPlaySingleplayer":"--quickPlayMultiplayer");args.Add(target);}
    else{var address=new Uri("minecraft://"+target);args.AddRange(new[]{"--server",address.Host,"--port",(address.Port>0?address.Port:25565).ToString()});}
    foreach(var arg in args){if(process.StartInfo.ArgumentList.Count>0)process.StartInfo.ArgumentList.Add(arg);else process.StartInfo.Arguments+=" \""+arg+"\"";}
   }
   process.StartInfo.UseShellExecute=false;process.StartInfo.RedirectStandardOutput=true;process.StartInfo.RedirectStandardError=true;process.StartInfo.CreateNoWindow=true;process.EnableRaisingEvents=true;
   var activityParser=new ActivityLog(targetKind=="worlds"?activityTarget:"");var logPath=Path.Combine(root,"logs","latest.log");var watcher=new ActivityLogWatcher(logPath,line=>{if(Running.TryGetValue(id,out var live)&&ReferenceEquals(live,process))ObserveActivity(id,activityParser,line,process);});try{watcher.Baseline();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){}
   void Log(string? line) { if(line!=null){emit(new{type="log",instanceId=id,line=Redact(line)});if(!watcher.HasCurrentLog)ObserveActivity(id,activityParser,line,process);} }
   process.OutputDataReceived+=(s,e)=>Log(e.Data);process.ErrorDataReceived+=(s,e)=>Log(e.Data);
   process.Exited+=(s,e)=>{Running.TryRemove(id,out _);StopActivity(id);Activities.TryRemove(id,out _);emit(new{type="exited",instanceId=id,code=process.ExitCode});process.Dispose();};
   Activities[id]=new(i.Version,i.Loader,i.LoaderVersion,i.Name,"","",i.PackSource,i.PackId,i.PackVersion,StartedAt:DateTime.UtcNow);
   Running[id]=process;try{if(!process.Start())throw new IOException("Java не запустилась");}catch{Running.TryRemove(id,out _);Activities.TryRemove(id,out _);process.Dispose();throw;}var cancellation=new CancellationTokenSource();activityWatchers[id]=cancellation;WatchActivity(id,watcher,cancellation);if(!Running.ContainsKey(id))StopActivity(id);process.BeginOutputReadLine();process.BeginErrorReadLine();i.LastPlayed=DateTime.UtcNow;store.Save();
   if(Running.TryGetValue(id,out var live)&&ReferenceEquals(live,process))emit(new{type="started",instanceId=id,hide=i.Settings.HideOnLaunch});
  }
  finally{installationGate.Release();busy.TryRemove(id,out _);}
 }
 public async Task<object> Components(string id)
 {
  var instance=store.Get(id);var meta=await Metadata(instance.Version);var rows=new List<object>{new{name="Minecraft",version=instance.Version}};
  foreach(var library in meta["libraries"]?.AsArray()??[]){var name=library.Str("name");if(name.StartsWith("org.lwjgl:lwjgl:")){rows.Add(new{name="LWJGL",version=name.Split(':').Last()});break;}}
  if(instance.Loader!="vanilla")rows.Add(new{name=instance.Loader+" Loader",version=instance.LoaderVersion});
  if(instance.Loader is "fabric" or "quilt")
  {
   var host=instance.Loader=="fabric"?"https://meta.fabricmc.net/v2":"https://meta.quiltmc.org/v3";
   var profile=await Net.Get(host+"/versions/loader/"+Uri.EscapeDataString(instance.Version)+"/"+Uri.EscapeDataString(instance.LoaderVersion)+"/profile/json");
   foreach(var lib in profile["libraries"]?.AsArray()??[]){var name=lib.Str("name");if(name.Contains(":intermediary:")||name.Contains(":hashed:"))rows.Add(new{name="Mappings",version=name.Split(':').Last()});}
  }
  return rows;
 }
 public string Redact(string line) { var token=auth.Session?.AccessToken;return string.IsNullOrEmpty(token)?line:line.Replace(token,"[REDACTED]"); }
 public void Stop(string id) { if(Running.TryGetValue(id,out var p))p.Kill(true); }
 public object Files(string id,string kind)
 {
  var i=store.Get(id);var root=store.Folder(i);var folder=KindFolder(kind);var path=Path.Combine(root,folder);Directory.CreateDirectory(path);
  if(kind=="worlds")return Directory.GetDirectories(path).Select(p=>new{name=Nbt.World(Path.Combine(p,"level.dat")).Name,path=Path.GetRelativePath(root,p),lastPlayed=Nbt.World(Path.Combine(p,"level.dat")).LastPlayed,icon=File.Exists(Path.Combine(p,"icon.png"))?Asset(Path.Combine(p,"icon.png")):""}).ToArray();
  if(kind=="servers")return Nbt.Servers(Path.Combine(root,"servers.dat"));
  return Directory.GetFiles(path).Where(p=>kind!="screenshots"||new[]{".png",".jpg",".jpeg"}.Contains(Path.GetExtension(p).ToLowerInvariant())).Select(p=>FileInfo(p,root,kind)).ToArray();
 }
 object FileInfo(string p,string root,string kind)
 {
  var name=Path.GetFileName(p);var label=name;var version="";var game="";var icon="";
  if(kind=="mods")try
  {
   using var zip=ZipFile.OpenRead(p);var entry=zip.GetEntry("fabric.mod.json")??zip.GetEntry("quilt.mod.json");
   if(entry!=null){using var reader=new StreamReader(entry.Open());var n=JsonNode.Parse(reader.ReadToEnd())!;if(n["quilt_loader"]!=null)n=n["quilt_loader"]!;label=n["metadata"].Str("name");if(label=="")label=n.Str("name");version=n.Str("version");game=n["depends"]?.Str("minecraft")??"";var iconName=n.Str("icon");if(iconName!=""&&zip.GetEntry(iconName) is {} image&&image.Length<2*1024*1024){using var stream=image.Open();using var ms=new MemoryStream();stream.CopyTo(ms);icon="data:image/png;base64,"+Convert.ToBase64String(ms.ToArray());}}
  }catch(Exception ex) when(ex is IOException or JsonException or InvalidOperationException){}
  return new{name,label=string.IsNullOrEmpty(label)?name:label,version,game,icon,path=Path.GetRelativePath(root,p),enabled=!p.EndsWith(".disabled",StringComparison.OrdinalIgnoreCase),size=new System.IO.FileInfo(p).Length,modified=File.GetLastWriteTimeUtc(p),image=kind=="screenshots"?Asset(p):""};
 }
 public static string KindFolder(string kind)=>kind switch{"mods"=>"mods","shaders"=>"shaderpacks","resources"=>"resourcepacks","worlds"=>"saves","servers"=>"", "screenshots"=>"screenshots","logs"=>"logs",_=>throw new IOException("Неизвестная папка")};
 public string Asset(string p)=>"https://data.spectra.local/"+Uri.EscapeDataString(Path.GetRelativePath(store.Root,p).Replace('\\','/')).Replace("%2F","/");
 public void Toggle(string id,string relative) { if(Running.ContainsKey(id))throw new IOException("Закройте игру перед изменением файлов");var root=store.Folder(store.Get(id));var file=Store.SafePath(root,relative);if(!Path.GetRelativePath(root,file).StartsWith("mods"+Path.DirectorySeparatorChar))throw new IOException("Это не файл мода");File.Move(file,file.EndsWith(".disabled")?file[..^9]:file+".disabled",false); }
 public async Task<string> ReadLog(string id,string relative)
 {
  var root=store.Folder(store.Get(id));var path=Store.SafePath(Path.Combine(root,"logs"),Path.GetRelativePath(Path.Combine(root,"logs"),Store.SafePath(root,relative)));
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);stream.Seek(Math.Max(0,stream.Length-2*1024*1024),SeekOrigin.Begin);
  if(path.EndsWith(".gz")){stream.Seek(0,SeekOrigin.Begin);using var gzip=new GZipStream(stream,CompressionMode.Decompress);using var reader=new StreamReader(gzip);var buffer=new char[2*1024*1024];var count=await reader.ReadBlockAsync(buffer,0,buffer.Length);return Redact(new string(buffer,0,count));}
  using var text=new StreamReader(stream);return Redact(await text.ReadToEndAsync());
 }
}
