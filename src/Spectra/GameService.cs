using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Xml.Linq;
using CmlLib.Core;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.Installer.NeoForge.Installers;
using Microsoft.Win32;
namespace Spectra;
public sealed class GameService(Store store, Authentication auth, Action<object> emit)
{
 public ConcurrentDictionary<string,Process> Running { get; } = new();
 readonly ConcurrentDictionary<string,byte> busy=new();
 JsonNode? manifest;
 public async Task<JsonNode> Versions() => (await Manifest())["versions"]!.DeepClone();
 async Task<JsonNode> Manifest() => manifest??=await Net.Get("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
 public async Task<JsonNode> Metadata(string version)
 {
  var item=(await Manifest())["versions"]!.AsArray().FirstOrDefault(v=>v.Str("id")==version)??throw new IOException("Версия отсутствует в официальном манифесте");
  return await Net.Get(item.Str("url"));
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
  var assets=await Net.Get($"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture=x64&image_type=jdk&os=windows&vendor=eclipse");
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
 public async Task Launch(string id)
 {
  var i=store.Get(id);Store.Validate(i.Settings);
  if(Running.ContainsKey(id)||!busy.TryAdd(id,0))throw new IOException("Сборка уже запускается или запущена");
  try
  {
   await auth.Login(false);
   var java=await Java(i);Gpu(java,i.Settings.Gpu);
   var root=store.Folder(i);var launcher=new MinecraftLauncher(new MinecraftPath(root));
   long lastProgress=0;
   launcher.FileProgressChanged+=(s,e)=>{var now=Environment.TickCount64;if(now-lastProgress<150)return;lastProgress=now;emit(new{type="progress",instanceId=id,message=e.Name,percent=e.TotalTasks>0?(double)e.ProgressedTasks/e.TotalTasks*100:0});};
   var version=i.Version;
   await launcher.InstallAsync(version);
   if(i.Loader is "fabric" or "quilt")
   {
    if(string.IsNullOrEmpty(i.LoaderVersion))throw new IOException("Выберите версию загрузчика");
    var host=i.Loader=="fabric"?"https://meta.fabricmc.net/v2":"https://meta.quiltmc.org/v3";
    var profile=await Net.Get(host+"/versions/loader/"+Uri.EscapeDataString(i.Version)+"/"+Uri.EscapeDataString(i.LoaderVersion)+"/profile/json");version=profile.Str("id");
    var file=Store.SafePath(root,"versions/"+version+"/"+version+".json");Directory.CreateDirectory(Path.GetDirectoryName(file)!);await File.WriteAllTextAsync(file,profile.ToJsonString());
    launcher=new MinecraftLauncher(new MinecraftPath(root));
   }
   if(i.Loader=="forge") version=await new ForgeInstaller(launcher).Install(i.Version,i.LoaderVersion,new ForgeInstallOptions{JavaPath=java,InstallerOutput=new Progress<string>(line=>emit(new{type="log",instanceId=id,line=Redact(line)}))});
   if(i.Loader=="neoforge") version=await new NeoForgeInstaller(launcher).Install(i.Version,i.LoaderVersion,new NeoForgeInstallOptions{JavaPath=java,InstallerOutput=new Progress<string>(line=>emit(new{type="log",instanceId=id,line=Redact(line)}))});
   var process=await launcher.InstallAndBuildProcessAsync(version,new MLaunchOption{Session=auth.Session!,JavaPath=java,MinimumRamMb=i.Settings.MinRam,MaximumRamMb=i.Settings.MaxRam,ScreenWidth=i.Settings.Width,ScreenHeight=i.Settings.Height,GameLauncherName="Spectra",GameLauncherVersion="0.2.1"});
   process.StartInfo.UseShellExecute=false;process.StartInfo.RedirectStandardOutput=true;process.StartInfo.RedirectStandardError=true;process.StartInfo.CreateNoWindow=true;process.EnableRaisingEvents=true;
   void Log(string? line) { if(line!=null)emit(new{type="log",instanceId=id,line=Redact(line)}); }
   process.OutputDataReceived+=(s,e)=>Log(e.Data);process.ErrorDataReceived+=(s,e)=>Log(e.Data);
   process.Exited+=(s,e)=>{Running.TryRemove(id,out _);emit(new{type="exited",instanceId=id,code=process.ExitCode});process.Dispose();};
   Running[id]=process;try{if(!process.Start())throw new IOException("Java не запустилась");}catch{Running.TryRemove(id,out _);process.Dispose();throw;}process.BeginOutputReadLine();process.BeginErrorReadLine();i.LastPlayed=DateTime.UtcNow;store.Save();
   emit(new{type="started",instanceId=id,hide=i.Settings.HideOnLaunch});
  }
  finally{busy.TryRemove(id,out _);}
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
  return new{name,label=string.IsNullOrEmpty(label)?name:label,version,game,icon,path=Path.GetRelativePath(root,p),enabled=!p.EndsWith(".disabled"),size=new System.IO.FileInfo(p).Length,modified=File.GetLastWriteTimeUtc(p),image=kind=="screenshots"?Asset(p):""};
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
