using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
namespace Spectra;
public sealed class InstanceArchive(Store store,GameService game,Action<object> emit)
{
 readonly SemaphoreSlim gate=new(1);
 static readonly Dictionary<string,string> Loaders=new(){["net.fabricmc.fabric-loader"]="fabric",["org.quiltmc.quilt-loader"]="quilt",["net.minecraftforge"]="forge",["net.neoforged"]="neoforge"};
 static string Normalize(string path)
 {
  var n=path.Replace('\\','/');if(n.StartsWith('/')||n.Split('/').Any(p=>p is ".." or "."||p.Contains(':')))throw new IOException("Недопустимый путь в архиве");return n;
 }
 static Dictionary<string,string> ReadCfg(string text)
 {
  var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(var line in text.Split('\n')){var t=line.Trim();var eq=t.IndexOf('=');if(eq>0&&!t.StartsWith('#'))values[t[..eq]]=t[(eq+1)..].Replace("\\n"," ").Replace("\\\\","\\");}return values;
 }
 public async Task<Instance> Import(string path)
 {
  await gate.WaitAsync();Instance? instance=null;
  try
  {
   using var zip=ZipFile.OpenRead(path);if(zip.Entries.Count>50000)throw new IOException("Слишком много файлов в архиве");
   var entries=zip.Entries.Select(e=>(Entry:e,Name:Normalize(e.FullName))).ToArray();
   var packs=entries.Where(e=>e.Name=="mmc-pack.json"||e.Name.EndsWith("/mmc-pack.json")).ToArray();if(packs.Length!=1)throw new IOException("Нужен архив одной сборки Prism/MultiMC с mmc-pack.json");
   var prefix=packs[0].Name[..^"mmc-pack.json".Length];var cfgEntry=entries.SingleOrDefault(e=>e.Name==prefix+"instance.cfg").Entry??throw new IOException("Нет instance.cfg");
   if(packs[0].Entry.Length>1024*1024||cfgEntry.Length>1024*1024)throw new IOException("Слишком большие метаданные");
   using var reader=new StreamReader(packs[0].Entry.Open());var pack=JsonNode.Parse(await reader.ReadToEndAsync())??throw new IOException("Неверный mmc-pack.json");
   var components=pack["components"]?.AsArray()??throw new IOException("Нет списка компонентов");
   var mc=components.SingleOrDefault(c=>c.Str("uid")=="net.minecraft").Str("version");if(string.IsNullOrEmpty(mc))throw new IOException("Нет версии Minecraft");
   foreach(var c in components){var uid=c.Str("uid");if(!Loaders.ContainsKey(uid)&&uid is not ("net.minecraft" or "org.lwjgl" or "org.lwjgl3" or "net.fabricmc.intermediary" or "org.quiltmc.hashed" or "net.minecraft.java"))throw new IOException("Компонент пока не поддерживается Spectra: "+uid);}
   if(entries.Any(e=>e.Name.StartsWith(prefix+"patches/")&&!e.Name.EndsWith('/')))throw new IOException("Сборки с пользовательскими patches пока не поддерживаются");
   var loaders=components.Where(c=>Loaders.ContainsKey(c.Str("uid"))).ToArray();if(loaders.Length>1)throw new IOException("Несколько загрузчиков в одной сборке");
   await game.Metadata(mc);using var cfgReader=new StreamReader(cfgEntry.Open());var cfg=ReadCfg(await cfgReader.ReadToEndAsync());
   var settings=store.Config.Defaults with {};if(cfg.GetValueOrDefault("OverrideMemory")=="true"){if(int.TryParse(cfg.GetValueOrDefault("MinMemAlloc"),out var min))settings.MinRam=min;if(int.TryParse(cfg.GetValueOrDefault("MaxMemAlloc"),out var max))settings.MaxRam=max;}Store.Validate(settings);
   instance=new Instance{Name=cfg.GetValueOrDefault("name",Path.GetFileNameWithoutExtension(path)),Version=mc,Settings=settings};if(loaders.Length==1){instance.Loader=Loaders[loaders[0].Str("uid")];instance.LoaderVersion=loaders[0].Str("version");if(instance.Loader=="forge"&&instance.LoaderVersion.StartsWith(mc+"-"))instance.LoaderVersion=instance.LoaderVersion[(mc.Length+1)..];}
   var root=store.Folder(instance);long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var e in entries)
   {
    if(e.Name.EndsWith('/'))continue;var relative=e.Name.StartsWith(prefix+".minecraft/")?e.Name[(prefix.Length+11)..]:e.Name.StartsWith(prefix+"minecraft/")?e.Name[(prefix.Length+10)..]:null;
    if(relative==null)continue;if(!names.Add(relative))throw new IOException("Повторяющийся путь в архиве");total+=e.Entry.Length;if(total>8L*1024*1024*1024)throw new IOException("Распакованная сборка больше 8 ГиБ");
    if(((e.Entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new IOException("Ссылки в архиве не поддерживаются");
    var dest=Store.SafePath(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);await using var source=e.Entry.Open();await using var output=File.Create(dest);await source.CopyToAsync(output);
   }
   var iconKey=cfg.GetValueOrDefault("iconKey","");var icon=entries.FirstOrDefault(e=>e.Name==prefix+iconKey+".png");if(icon.Entry!=null&&icon.Entry.Length<=1024*1024&&((icon.Entry.ExternalAttributes>>16)&0xF000)!=0xA000){var dest=Path.Combine(Path.GetDirectoryName(root)!,"icon.png");icon.Entry.ExtractToFile(dest,true);instance.Icon="https://data.spectra.local/instances/"+instance.Id+"/icon.png";}
   store.Config.Instances.Add(instance);try{store.Save();}catch{store.Config.Instances.Remove(instance);throw;}emit(new{type="progress",message="Сборка импортирована",percent=100});return instance;
  }
  catch{if(instance!=null){var folder=Path.Combine(store.Root,"instances",instance.Id);if(Directory.Exists(folder))Directory.Delete(folder,true);}throw;}
  finally{gate.Release();}
 }
 public async Task Export(string id,string destination,bool worlds,string target)
 {
  var instance=store.Get(id);if(target=="multimc"&&(instance.Loader is "quilt" or "neoforge"))throw new IOException("Для Quilt / NeoForge выберите Prism Launcher");if(game.Running.ContainsKey(id))throw new IOException("Закройте игру перед экспортом");var root=store.Folder(instance);string? iconPath=null;if(Uri.TryCreate(instance.Icon,UriKind.Absolute,out var iconUri)&&iconUri.Host=="data.spectra.local")iconPath=Store.SafePath(store.Root,Uri.UnescapeDataString(iconUri.AbsolutePath.TrimStart('/')));var temp=destination+"."+Guid.NewGuid().ToString("N")+".part";
  try
  {
   await Task.Run(()=>
   {
    using var zip=ZipFile.Open(temp,ZipArchiveMode.Create);var components=new JsonArray(new JsonObject{["uid"]="net.minecraft",["version"]=instance.Version,["important"]=true});
    if(instance.Loader!="vanilla"){var uid=Loaders.Single(x=>x.Value==instance.Loader).Key;components.Add(new JsonObject{["uid"]=uid,["version"]=instance.LoaderVersion});if(instance.Loader=="fabric")components.Add(new JsonObject{["uid"]="net.fabricmc.intermediary",["version"]=instance.Version});}
    void Text(string name,string content){using var writer=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(content);}
    Text("mmc-pack.json",new JsonObject{["formatVersion"]=1,["components"]=components}.ToJsonString());
    Text("instance.cfg","[General]\nInstanceType=OneSix\nname="+instance.Name.Replace("\\","\\\\").Replace("\r"," ").Replace("\n"," ")+"\niconKey="+(iconPath!=null&&File.Exists(iconPath)?"spectra":"default")+"\nOverrideMemory=true\nMinMemAlloc="+instance.Settings.MinRam+"\nMaxMemAlloc="+instance.Settings.MaxRam+"\n");
    if(iconPath!=null&&File.Exists(iconPath))zip.CreateEntryFromFile(iconPath,"spectra.png",CompressionLevel.Fastest);
    var opts=new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint};int count=0;
    foreach(var file in Directory.EnumerateFiles(root,"*",opts))
    {
     var relative=Path.GetRelativePath(root,file).Replace('\\','/');var first=relative.Split('/')[0];
     if(first is "versions" or "libraries" or "assets" or "natives" or "logs" or "crash-reports"||(!worlds&&(first is "saves" or "screenshots"))||Path.GetFileName(file) is "launcher_accounts.json" or "launcher_profiles.json" or "usercache.json")continue;
     if(++count>50000)throw new IOException("Слишком много файлов для экспорта");zip.CreateEntryFromFile(file,".minecraft/"+relative,CompressionLevel.Fastest);
    }
   });File.Move(temp,destination,true);
  }
  finally{if(File.Exists(temp))File.Delete(temp);}
 }
}
