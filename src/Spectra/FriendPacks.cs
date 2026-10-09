using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Spectra;
public record FriendPackFile(string Kind,string Name,string Hash,long Size);
public record FriendPackManifest(string Name,string Version,string Loader,string LoaderVersion,List<FriendPackFile> Files);
/// <summary>Only enabled jars and zip packs are shared. Game worlds and account files never leave the computer.</summary>
public sealed class FriendPacks(Store store,GameService game,FriendsService friends,Action<object> emit)
{
 readonly SemaphoreSlim gate=new(1,1);
 readonly Dictionary<string,(long Size,long Ticks,string Hash)> hashes=new(StringComparer.OrdinalIgnoreCase);
 string published="",publishedScope="";DateTime publishedAt;
 readonly Dictionary<string,(string Owner,string Pack,string Mods,DateTime Expires)> customized=new();
 static readonly string[] Kinds=["mods","shaders","resources"];
 async Task<string> Hash(string path){var info=new FileInfo(path);if(hashes.TryGetValue(path,out var cached)&&cached.Size==info.Length&&cached.Ticks==info.LastWriteTimeUtc.Ticks)return cached.Hash;var value=await Net.Hash(path,"SHA256");var after=new FileInfo(path);if(info.Length!=after.Length||info.LastWriteTimeUtc!=after.LastWriteTimeUtc)throw new IOException("Файл сборки изменился во время проверки");hashes[path]=(info.Length,info.LastWriteTimeUtc.Ticks,value);return value;}
 public async Task<FriendPackManifest> Snapshot(Instance instance)
 {
  var root=store.Folder(instance);var files=new List<FriendPackFile>();long total=0;
  foreach(var kind in Kinds){var folder=Path.Combine(root,GameService.KindFolder(kind));if(!Directory.Exists(folder))continue;if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new IOException("Папки-ссылки нельзя передавать друзьям");
   foreach(var path in Directory.EnumerateFiles(folder).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase)){
    if(!Path.GetExtension(path).Equals(kind=="mods"?".jar":".zip",StringComparison.OrdinalIgnoreCase))continue;
    var info=new FileInfo(path);if((info.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Ссылки на файлы нельзя передавать друзьям");if(info.Length<1||info.Length>64L*1024*1024)throw new IOException("Файл для обмена должен быть не больше 64 МиБ: "+info.Name);
    total+=info.Length;if(total>512L*1024*1024||files.Count>=2000)throw new IOException("Обмен сборками: максимум 512 МиБ и 2000 файлов");files.Add(new(kind,info.Name,await Hash(path),info.Length));
   }
  }
  return new(instance.Name,instance.Version,instance.Loader,instance.LoaderVersion,files);
 }
 async Task<string> ModHashes(Instance i){var dir=Path.Combine(store.Folder(i),"mods");if(!Directory.Exists(dir))return "";if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)throw new IOException("Папки-ссылки не поддерживаются для обмена сборками");var values=new List<string>();foreach(var p in Directory.EnumerateFiles(dir).Where(p=>Path.GetExtension(p).Equals(".jar",StringComparison.OrdinalIgnoreCase)))values.Add(await Hash(p));return string.Join("\n",values.OrderBy(x=>x,StringComparer.Ordinal));}
 static string Mods(FriendPackManifest manifest)=>string.Join("\n",manifest.Files.Where(f=>f.Kind=="mods").Select(f=>f.Hash).OrderBy(x=>x,StringComparer.Ordinal));
 public async Task<string> Publish()
 {
  await gate.WaitAsync();try{
   var id=game.Running.Keys.FirstOrDefault();if(id==null)return "";
   var instance=store.Get(id);if(game.Activities.TryGetValue(id,out var activity))instance=instance with{Version=activity.Version,Loader=activity.Loader,LoaderVersion=activity.LoaderVersion};var manifest=await Snapshot(instance);var digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest,Store.Json))));var scope=store.Config.FriendsEndpoint+":"+friends.Account.Str("id");
   if(scope+digest==publishedScope&&DateTime.UtcNow-publishedAt<TimeSpan.FromMinutes(10))return published;
   var response=await friends.Call("/packs/publish",manifest);var packId=response.Str("id");if(!ValidHash(packId))throw new IOException("Неверный идентификатор сборки");var root=store.Folder(instance);
   var missing=response["missing"]?.AsArray().Select(x=>x?.ToString()??"").ToHashSet()??[];
   foreach(var file in manifest.Files.DistinctBy(f=>f.Hash).Where(f=>missing.Contains(f.Hash))){if(store.Config.HideOnlineStatus||!store.Config.ShareGameActivity||!game.Running.ContainsKey(id))return "";var path=Store.SafePath(root,GameService.KindFolder(file.Kind)+"/"+file.Name);await friends.Upload("/packs/file/"+packId+"/"+file.Hash,path,file.Hash,file.Size);}
   published=packId;publishedScope=scope+digest;publishedAt=DateTime.UtcNow;return published;
  }finally{gate.Release();}
 }
 static bool ValidHash(string value)=>value.Length==64&&value.All(c=>c is >= 'a' and <= 'f' or >= '0' and <= '9');
 public async Task<(JsonNode Presence,FriendPackManifest Manifest,string PackId)> Remote(string owner)
 {
  var presence=await friends.JoinInfo(owner);var node=await friends.Call("/packs/"+owner);var manifest=node.Deserialize<FriendPackManifest>(Store.Json)??throw new IOException("Неверная сборка");var packId=node.Str("id");
  if(manifest.Files==null||!ValidHash(packId)||packId!=presence.Str("sharedPack")||manifest.Version!=presence.Str("version")||manifest.Loader!=presence.Str("loader")||manifest.LoaderVersion!=presence.Str("loaderVersion")||manifest.Files.Count>2000)throw new IOException("Сборка друга изменилась. Откройте подключение заново.");
  long total=0;var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var f in manifest.Files){if(!Kinds.Contains(f.Kind)||!ValidHash(f.Hash)||f.Size<1||f.Size>64L*1024*1024||f.Name.Length>180||f.Name!=Path.GetFileName(f.Name)||f.Name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||!Path.GetExtension(f.Name).Equals(f.Kind=="mods"?".jar":".zip",StringComparison.OrdinalIgnoreCase)||!paths.Add(f.Kind+"/"+f.Name))throw new IOException("Неверный файл сборки");total+=f.Size;}if(total>512L*1024*1024)throw new IOException("Слишком большая сборка");return(presence,manifest,packId);
 }
 public async Task<object> Plan(string owner)
 {
  await gate.WaitAsync();try{var remote=await Remote(owner);var matching=new List<string>();foreach(var i in store.Config.Instances.Where(i=>!game.Running.ContainsKey(i.Id)&&i.Version==remote.Manifest.Version&&i.Loader==remote.Manifest.Loader&&i.LoaderVersion==remote.Manifest.LoaderVersion)){if(await ModHashes(i)==Mods(remote.Manifest))matching.Add(i.Id);}
   return new{manifest=remote.Manifest,packId=remote.PackId,matching,presence=remote.Presence};
  }finally{gate.Release();}
 }
 public async Task<(Instance Instance,JsonNode Presence)> Install(string owner,string packId,string overwrite,JsonArray selected,JsonArray categories)
 {
  await gate.WaitAsync();Instance? instance=null;var added=false;var moved=new List<(string Destination,string Backup)>();var installed=new List<string>();string stage="";Instance? previous=null;
  try{
   var remote=await Remote(owner);if(remote.PackId!=packId)throw new IOException("Сборка друга изменилась. Откройте список заново.");await game.Metadata(remote.Manifest.Version);
   var kinds=categories.Select(x=>x?.ToString()??"").ToHashSet();if(kinds.Count==0||kinds.Any(k=>!Kinds.Contains(k)))throw new IOException("Неверная категория");
   var requested=selected.Select(x=>x?.ToString()??"").ToHashSet(StringComparer.Ordinal);var files=remote.Manifest.Files.Where(f=>kinds.Contains(f.Kind)&&requested.Contains(f.Kind+"/"+f.Name)).ToList();if(files.Count!=requested.Count)throw new IOException("Выбраны неизвестные файлы");
   instance=overwrite==""?new Instance{Name=remote.Manifest.Name,Settings=store.Config.Defaults with{}}:store.Get(overwrite);if(instance.Id=="vanilla"||game.Running.ContainsKey(instance.Id))throw new IOException("Закройте игру перед заменой сборки");
   previous=instance with{Settings=instance.Settings with{}};
   var root=store.Folder(instance);stage=Path.Combine(store.Root,"downloads","friend-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
   foreach(var f in files){emit(new{type="progress",instanceId=instance.Id,message="Сборка друга: "+f.Name,percent=0});await friends.Download("/packs/"+owner+"/"+packId+"/"+f.Hash,Store.SafePath(stage,GameService.KindFolder(f.Kind)+"/"+f.Name),f.Hash,f.Size);}
   // Check the friend has not switched packs before making changes locally.
   var latest=await Remote(owner);if(latest.PackId!=packId)throw new IOException("Друг сменил сборку во время загрузки");
   if(game.Running.ContainsKey(instance.Id))throw new IOException("Закройте игру перед заменой сборки");
   var backup=Path.Combine(Path.GetDirectoryName(root)!,"backups","friend-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);Directory.CreateDirectory(backup);File.WriteAllText(Path.Combine(backup,"instance.json"),JsonSerializer.Serialize(instance,Store.Json));
   // Only the selected categories are replaced, with a recoverable backup.
   foreach(var kind in Kinds.Where(k=>kinds.Contains(k))){var dest=Path.Combine(root,GameService.KindFolder(kind));var old=Path.Combine(backup,GameService.KindFolder(kind));if(Directory.Exists(dest)){Directory.Move(dest,old);moved.Add((dest,old));}var source=Path.Combine(stage,GameService.KindFolder(kind));if(Directory.Exists(source)){Directory.Move(source,dest);installed.Add(dest);}}
   instance.Version=remote.Manifest.Version;instance.Loader=remote.Manifest.Loader;instance.LoaderVersion=remote.Manifest.LoaderVersion;instance.PackSource="";instance.PackId="";instance.PackVersion="";
   if(overwrite==""){store.Config.Instances.Add(instance);added=true;}var receipt=(owner,packId,await ModHashes(instance),DateTime.UtcNow.AddHours(1));store.Save();customized[instance.Id]=receipt;return(instance,latest.Presence);
  }catch{foreach(var path in installed)if(Directory.Exists(path))Directory.Delete(path,true);foreach(var pair in moved.AsEnumerable().Reverse())if(Directory.Exists(pair.Backup))Directory.Move(pair.Backup,pair.Destination);if(instance!=null&&previous!=null){instance.Version=previous.Version;instance.Loader=previous.Loader;instance.LoaderVersion=previous.LoaderVersion;instance.PackSource=previous.PackSource;instance.PackId=previous.PackId;instance.PackVersion=previous.PackVersion;}if(overwrite==""&&instance!=null){var directory=Path.GetDirectoryName(store.Folder(instance))!;if(Directory.Exists(directory))Directory.Delete(directory,true);}if(added&&instance!=null)store.Config.Instances.Remove(instance);throw;}
  finally{if(stage!=""&&Directory.Exists(stage))Directory.Delete(stage,true);gate.Release();}
 }
 public async Task<(Instance Instance,JsonNode Presence)> Match(string owner,string id,bool allowCustomized=false)
 {
  await gate.WaitAsync();try{var remote=await Remote(owner);var instance=store.Get(id);var mods=await ModHashes(instance);var selected=allowCustomized&&customized.TryGetValue(instance.Id,out var receipt)&&receipt.Owner==owner&&receipt.Pack==remote.PackId&&receipt.Mods==mods&&receipt.Expires>DateTime.UtcNow;if(instance.Version!=remote.Manifest.Version||instance.Loader!=remote.Manifest.Loader||instance.LoaderVersion!=remote.Manifest.LoaderVersion||!selected&&mods!=Mods(remote.Manifest))throw new IOException("Состав включённых модов не совпадает со сборкой друга");return(instance,remote.Presence);}finally{gate.Release();}
 }
}
