using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Spectra;
public record CatalogItem(string Source,string Id,string Title,string Description,string Icon,long Downloads);
public sealed class CatalogService(Store store,GameService game,Action<object> emit)
{
 readonly SemaphoreSlim installation=new(1);
 readonly Dictionary<string,(long Size,long Ticks,string Hash)> installedHashes=new();
 readonly Dictionary<string,List<InstallFile>> knownVersions=new();
 static string Q(string x)=>Uri.EscapeDataString(x);
 public async Task<object> Search(string query,string kind,string? instanceId,int offset)
 {
  var i=string.IsNullOrEmpty(instanceId)?null:store.Get(instanceId);
  var facets=new List<string[]>{new[]{"project_type:"+(kind=="mods"?"mod":kind=="shaders"?"shader":kind=="resources"?"resourcepack":"modpack")}};
  if(i!=null){facets.Add(new[]{"versions:"+i.Version});if(kind=="mods"&&i.Loader!="vanilla")facets.Add(new[]{"categories:"+i.Loader});}
  var warnings=new List<string>();var results=new List<CatalogItem>();
  try{var n=await Net.Get("https://api.modrinth.com/v2/search?query="+Q(query)+"&limit=24&offset="+offset+"&index=downloads&facets="+Q(JsonSerializer.Serialize(facets)));results.AddRange(n["hits"]!.AsArray().Select(x=>new CatalogItem("modrinth",x.Str("project_id"),x.Str("title"),x.Str("description"),x.Str("icon_url"),x?["downloads"]?.GetValue<long>()??0)));}catch(Exception){warnings.Add("Modrinth сейчас недоступен");}
  if(string.IsNullOrWhiteSpace(store.Config.CurseForgeKey))warnings.Add("CurseForge: добавьте API-ключ в настройках");
  else try
  {
   var classId=kind switch{"mods"=>6,"resources"=>12,"shaders"=>6552,_=>4471};
   var url=$"https://api.curseforge.com/v1/mods/search?gameId=432&classId={classId}&pageSize=24&index={offset}&sortField=2&sortOrder=desc&searchFilter="+Q(query);
   if(i!=null){url+="&gameVersion="+Q(i.Version);if(kind=="mods")url+="&modLoaderType="+LoaderNumber(i.Loader);}
   var n=await Curse(url);results.AddRange(n["data"]!.AsArray().Select(x=>new CatalogItem("curseforge",x.Str("id"),x.Str("name"),x.Str("summary"),x?["logo"].Str("thumbnailUrl")??"",(long)(x?["downloadCount"]?.GetValue<double>()??0))));
  }catch(Exception){warnings.Add("CurseForge недоступен или ключ не принят");}
  // Counts belong to different services, preserve source attribution.
  return new{items=results.OrderByDescending(x=>x.Downloads),warnings,nextOffset=offset+24};
 }
 static int LoaderNumber(string loader)=>loader switch{"forge"=>1,"fabric"=>4,"quilt"=>5,"neoforge"=>6,_=>0};
 async Task<JsonNode> Curse(string url){using var req=new HttpRequestMessage(HttpMethod.Get,url);req.Headers.Add("x-api-key",store.Config.CurseForgeKey);return await Net.Send(req);}
 sealed record InstallFile(string Name,string Url,string Hash,string Algorithm);
 public async Task Install(string source,string projectId,string kind,string instanceId)
 {
  await installation.WaitAsync();string stage="";var committed=new List<string>();var restored=new List<(string Original,string Backup)>();
  try{
   if(kind is not ("mods" or "shaders" or "resources"))throw new IOException("Неизвестный тип дополнения");var i=store.Get(instanceId);if(game.Running.ContainsKey(i.Id))throw new IOException("Закройте игру перед установкой дополнений");
   knownVersions.Clear();var root=store.Folder(i);var folder=Path.Combine(root,GameService.KindFolder(kind));var plan=new List<InstallFile>();var visited=new HashSet<string>();var projects=new Dictionary<string,string>();await ResolveProject(source,projectId,kind,i,folder,visited,projects,plan);
   stage=Path.Combine(store.Root,"downloads","dependencies-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);var ready=new List<(InstallFile File,string? Existing)>();
   foreach(var file in plan.DistinctBy(f=>f.Name,StringComparer.OrdinalIgnoreCase)){var existing=await Existing(folder,file);ready.Add((file,existing));if(existing==null){if(!Uri.TryCreate(file.Url,UriKind.Absolute,out var download)||download.Scheme!="https")throw new IOException("Автор файла запретил загрузку через сторонние лаунчеры: "+file.Name);emit(new{type="progress",instanceId=i.Id,message="Загрузка мода или библиотеки: "+file.Name,percent=0});await Net.Download(file.Url,Store.SafePath(stage,file.Name),file.Hash,file.Algorithm);}}
   var outdated=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var old in knownVersions.Values.SelectMany(x=>x).Where(f=>f.Hash!=""&&!plan.Any(p=>p.Algorithm==f.Algorithm&&p.Hash==f.Hash))){var path=await Existing(folder,old);if(path!=null&&!ready.Any(x=>x.Existing==path))outdated.Add(path);}
   if(game.Running.ContainsKey(i.Id))throw new IOException("Закройте игру перед установкой дополнений");Directory.CreateDirectory(folder);
   foreach(var old in outdated){var backup=Path.Combine(stage,"old-"+Guid.NewGuid().ToString("N"));File.Move(old,backup);restored.Add((old,backup));}
   foreach(var item in ready){var dest=Store.SafePath(folder,item.File.Name);if(item.Existing==dest)continue;if(File.Exists(dest)){var backup=Store.SafePath(stage,"backup/"+item.File.Name);Directory.CreateDirectory(Path.GetDirectoryName(backup)!);File.Move(dest,backup);restored.Add((dest,backup));}
    if(item.Existing!=null){File.Move(item.Existing,dest);restored.Add((item.Existing,dest));}else{File.Move(Store.SafePath(stage,item.File.Name),dest);committed.Add(dest);}}
   emit(new{type="progress",instanceId=i.Id,message="Мод и обязательные зависимости установлены",percent=100});
  }catch{foreach(var path in committed)if(File.Exists(path))File.Delete(path);foreach(var item in restored.AsEnumerable().Reverse())if(File.Exists(item.Backup))File.Move(item.Backup,item.Original,true);throw;}
  finally{if(stage!=""&&Directory.Exists(stage))Directory.Delete(stage,true);installation.Release();}
 }
 async Task<string?> Existing(string folder,InstallFile file)
 {
  if(!Directory.Exists(folder))return null;var candidates=Directory.EnumerateFiles(folder).Where(p=>p.EndsWith(".jar",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".disabled",StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p.EndsWith(".disabled",StringComparison.OrdinalIgnoreCase));
  foreach(var p in candidates){var info=new FileInfo(p);var key=file.Algorithm+":"+p;string value;if(installedHashes.TryGetValue(key,out var cached)&&cached.Size==info.Length&&cached.Ticks==info.LastWriteTimeUtc.Ticks)value=cached.Hash;else{value=await Net.Hash(p,file.Algorithm);installedHashes[key]=(info.Length,info.LastWriteTimeUtc.Ticks,value);}if(value==file.Hash.ToLowerInvariant())return p;}return null;
 }
 static InstallFile ModrinthFile(JsonNode v){var f=v["files"]?.AsArray().FirstOrDefault(x=>x?["primary"]?.GetValue<bool>()==true)??v["files"]?[0]??throw new IOException("У версии нет файла");return new(f.Str("filename"),f.Str("url"),f["hashes"].Str("sha512"),"SHA512");}
 static InstallFile CurseFile(JsonNode f)=>new(f.Str("fileName"),f.Str("downloadUrl"),f["hashes"]?.AsArray().FirstOrDefault(x=>x?["algo"]?.GetValue<int>()==1).Str("value")??"","SHA1");
 static void AddFile(List<InstallFile> plan,InstallFile file){if(file.Name!=Path.GetFileName(file.Name)||file.Name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||file.Hash=="")throw new IOException("Неверный файл или автор запретил загрузку через сторонний лаунчер");if(plan.Any(f=>f.Name.Equals(file.Name,StringComparison.OrdinalIgnoreCase)&&f.Hash!=file.Hash))throw new IOException("Конфликт файлов зависимостей: "+file.Name);plan.Add(file);}
 async Task ResolveProject(string source,string id,string kind,Instance i,string folder,HashSet<string> visited,Dictionary<string,string> projects,List<InstallFile> plan)
 {
  if(!visited.Add(source+":"+id))return;if(visited.Count>100)throw new IOException("Слишком много зависимостей");
  if(source=="modrinth"){
   var url="https://api.modrinth.com/v2/project/"+Q(id)+"/version?game_versions="+Q(JsonSerializer.Serialize(new[]{i.Version}));if(kind=="mods")url+="&loaders="+Q(JsonSerializer.Serialize(new[]{i.Loader}));
   var versions=(await Net.Get(url)).AsArray();var v=versions.FirstOrDefault()??throw new IOException("Нет совместимого файла: "+id);
   foreach(var candidate in versions){if(candidate!=null&&await Existing(folder,ModrinthFile(candidate))!=null){v=candidate;break;}}
   knownVersions[v.Str("project_id")]=versions.Where(x=>x!=null).Select(x=>ModrinthFile(x!)).ToList();await ResolveModrinth(v,kind,i,folder,visited,projects,plan);
  }else if(source=="curseforge"){
   var url=$"https://api.curseforge.com/v1/mods/{Q(id)}/files?gameVersion={Q(i.Version)}";if(kind=="mods")url+="&modLoaderType="+LoaderNumber(i.Loader);
   var files=(await Curse(url))["data"]!.AsArray().OrderByDescending(x=>x.Str("fileDate")).ToList();var f=files.FirstOrDefault()??throw new IOException("Нет совместимого файла: "+id);
   foreach(var candidate in files){if(candidate!=null&&await Existing(folder,CurseFile(candidate))!=null){f=candidate;break;}}
   knownVersions["curseforge:"+id]=files.Where(x=>x!=null).Select(x=>CurseFile(x!)).ToList();
   foreach(var dep in f["dependencies"]?.AsArray()??[])if(dep?["relationType"]?.GetValue<int>()==3)await ResolveProject(source,dep.Str("modId"),kind,i,folder,visited,projects,plan);AddFile(plan,CurseFile(f));
  }else throw new IOException("Неизвестный источник");
 }
 async Task ResolveModrinth(JsonNode v,string kind,Instance i,string folder,HashSet<string> visited,Dictionary<string,string> projects,List<InstallFile> plan)
 {
  var id=v.Str("id");var project=v.Str("project_id");
  if(!knownVersions.ContainsKey(project)){var url="https://api.modrinth.com/v2/project/"+Q(project)+"/version?game_versions="+Q(JsonSerializer.Serialize(new[]{i.Version}));if(kind=="mods")url+="&loaders="+Q(JsonSerializer.Serialize(new[]{i.Loader}));knownVersions[project]=(await Net.Get(url)).AsArray().Where(x=>x!=null).Select(x=>ModrinthFile(x!)).ToList();}if(projects.TryGetValue(project,out var version)&&version!=id)throw new IOException("Зависимости требуют разные версии одной библиотеки: "+project);projects[project]=id;
  if(!visited.Add("version:"+id))return;if(visited.Count>100)throw new IOException("Слишком много зависимостей");
  if(v["game_versions"]?.AsArray().Any(x=>x?.ToString()==i.Version)!=true||kind=="mods"&&v["loaders"]?.AsArray().Any(x=>x?.ToString()==i.Loader)!=true)throw new IOException("Библиотека несовместима с версией игры или загрузчиком: "+project);
  foreach(var d in v["dependencies"]?.AsArray()??[]){if(d.Str("dependency_type")!="required")continue;if(d.Str("version_id") is {Length:>0} pinned)await ResolveModrinth(await Net.Get("https://api.modrinth.com/v2/version/"+Q(pinned)),kind,i,folder,visited,projects,plan);else if(d.Str("project_id") is {Length:>0} dependency)await ResolveProject("modrinth",dependency,kind,i,folder,visited,projects,plan);else throw new IOException("Обязательная библиотека доступна только для ручной установки: "+d.Str("file_name"));}AddFile(plan,ModrinthFile(v));
 }
 public async Task<Instance> ImportMrpack(string path)
 {
  await installation.WaitAsync(); Instance? instance=null;
  try
  {
   using var zip=ZipFile.OpenRead(path);var index=zip.GetEntry("modrinth.index.json")??throw new IOException("Это не .mrpack");using var reader=new StreamReader(index.Open());var n=JsonNode.Parse(await reader.ReadToEndAsync())!;
   if(n.Str("game")!="minecraft")throw new IOException("Модпак не для Minecraft");
   var dependencies=n["dependencies"]!;var mc=dependencies.Str("minecraft");await game.Metadata(mc);
   instance=new Instance{Name=n.Str("name"),Version=mc,Settings=store.Config.Defaults with{}};
   foreach(var loader in new[]{("fabric-loader","fabric"),("quilt-loader","quilt"),("forge","forge"),("neoforge","neoforge")})if(dependencies.Str(loader.Item1) is {Length:>0} v){instance.Loader=loader.Item2;instance.LoaderVersion=v;break;}
   var root=store.Folder(instance);var files=n["files"]!.AsArray();if(files.Count>10000)throw new IOException("Слишком много файлов");
   foreach(var f in files)
   {
    if(f?["env"].Str("client")=="unsupported")continue;
    var downloads=f?["downloads"]?.AsArray()??throw new IOException("Нет адреса загрузки");var url=downloads.FirstOrDefault()?.ToString()??"";
    var host=new Uri(url).Host;
    if(!new[]{"cdn.modrinth.com","github.com","raw.githubusercontent.com"}.Contains(host)&&!host.EndsWith(".githubusercontent.com"))throw new IOException("Неизвестный источник модпака: "+host);
    emit(new{type="progress",instanceId=instance.Id,message=f.Str("path"),percent=0});await Net.Download(url,Store.SafePath(root,f.Str("path")),f?["hashes"].Str("sha512"),"SHA512");
   }
   long expanded=0;
   foreach(var e in zip.Entries.Where(e=>e.FullName.StartsWith("overrides/")||e.FullName.StartsWith("client-overrides/")))
   {
    if(e.FullName.EndsWith('/'))continue;expanded+=e.Length;if(expanded>2L*1024*1024*1024)throw new IOException("Распакованный модпак слишком большой");var relative=e.FullName[(e.FullName.IndexOf('/')+1)..];var dest=Store.SafePath(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);e.ExtractToFile(dest,true);
   }
   store.Config.Instances.Add(instance);store.Save();return instance;
  }
  catch {if(instance!=null){var root=Path.GetDirectoryName(store.Folder(instance))!;Directory.Delete(root,true);}throw;}
  finally {installation.Release();}
 }
 public async Task<Instance> ImportCursePack(string path)
 {
  if(string.IsNullOrWhiteSpace(store.Config.CurseForgeKey))throw new IOException("Для импорта CurseForge нужен API-ключ");
  await installation.WaitAsync();Instance? instance=null;
  try
  {
   using var zip=ZipFile.OpenRead(path);var entry=zip.GetEntry("manifest.json")??throw new IOException("Нет manifest.json CurseForge");using var reader=new StreamReader(entry.Open());var n=JsonNode.Parse(await reader.ReadToEndAsync())!;
   var minecraft=n["minecraft"]!;var mc=minecraft.Str("version");await game.Metadata(mc);instance=new Instance{Name=n.Str("name"),Version=mc,Settings=store.Config.Defaults with{}};
   var loader=minecraft["modLoaders"]?.AsArray().FirstOrDefault(x=>x?["primary"]?.GetValue<bool>()==true)??minecraft["modLoaders"]?.AsArray().FirstOrDefault();
   if(loader!=null){var id=loader.Str("id");var separator=id.IndexOf('-');if(separator<0)throw new IOException("Неизвестный загрузчик модпака");instance.Loader=id[..separator];instance.LoaderVersion=id[(separator+1)..];if(instance.Loader is not ("forge" or "neoforge" or "fabric" or "quilt"))throw new IOException("Загрузчик не поддерживается");}
   var root=store.Folder(instance);var files=n["files"]!.AsArray();if(files.Count>10000)throw new IOException("Слишком много файлов");
   foreach(var item in files)
   {
    var file=(await Curse("https://api.curseforge.com/v1/mods/"+Q(item.Str("projectID"))+"/files/"+Q(item.Str("fileID"))))["data"]!;
    var download=file.Str("downloadUrl");if(string.IsNullOrWhiteSpace(download))throw new IOException("Автор файла запретил загрузку через сторонние лаунчеры: "+file.Str("fileName"));
    var project=(await Curse("https://api.curseforge.com/v1/mods/"+Q(item.Str("projectID"))))["data"]!;var classId=project["classId"]?.GetValue<int>()??6;var folder=classId==12?"resourcepacks":classId==6552?"shaderpacks":"mods";
    emit(new{type="progress",instanceId=instance.Id,message=file.Str("fileName"),percent=0});var sha=file["hashes"]?.AsArray().FirstOrDefault(x=>x?["algo"]?.GetValue<int>()==1)?.Str("value");
    await Net.Download(download,Store.SafePath(root,folder+"/"+file.Str("fileName")),sha);
   }
   var overrides=n.Str("overrides");if(string.IsNullOrWhiteSpace(overrides))overrides="overrides";
   long expanded=0;foreach(var file in zip.Entries.Where(e=>e.FullName.StartsWith(overrides+"/")))
   {
    if(file.FullName.EndsWith('/'))continue;expanded+=file.Length;if(expanded>2L*1024*1024*1024)throw new IOException("Модпак слишком большой");var dest=Store.SafePath(root,file.FullName[(overrides.Length+1)..]);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);file.ExtractToFile(dest,true);
   }
   store.Config.Instances.Add(instance);store.Save();return instance;
  }
  catch{if(instance!=null)Directory.Delete(Path.GetDirectoryName(store.Folder(instance))!,true);throw;}
  finally{installation.Release();}
 }
 public async Task<Instance> InstallPack(string source,string id,string pinnedVersion="")
 {
  string url,filename;string? hash;string algorithm;JsonNode? project=null;string packVersion="";
  if(source=="modrinth")
  {
   project=await Net.Get("https://api.modrinth.com/v2/project/"+Q(id));
   var versions=await Net.Get("https://api.modrinth.com/v2/project/"+Q(id)+"/version");var v=(string.IsNullOrEmpty(pinnedVersion)?versions[0]:versions.AsArray().FirstOrDefault(x=>x.Str("id")==pinnedVersion))??throw new IOException("Нет версии модпака");packVersion=v.Str("id");var file=v["files"]!.AsArray().FirstOrDefault(x=>x.Str("filename").EndsWith(".mrpack"))??throw new IOException("Нет .mrpack");
   url=file.Str("url");filename=Guid.NewGuid()+".mrpack";hash=file["hashes"].Str("sha512");algorithm="SHA512";
  }
  else if(source=="curseforge")
  {
   project=(await Curse("https://api.curseforge.com/v1/mods/"+Q(id)))["data"]!;
   var versions=(await Curse("https://api.curseforge.com/v1/mods/"+Q(id)+"/files"))["data"]!.AsArray();var file=(string.IsNullOrEmpty(pinnedVersion)?versions.OrderByDescending(x=>x.Str("fileDate")).FirstOrDefault():(await Curse("https://api.curseforge.com/v1/mods/"+Q(id)+"/files/"+Q(pinnedVersion)))["data"])??throw new IOException("Нет версии модпака");packVersion=file.Str("id");
   url=file.Str("downloadUrl");if(string.IsNullOrWhiteSpace(url))throw new IOException("Автор запретил загрузку модпака через сторонние лаунчеры");filename=Guid.NewGuid()+".zip";hash=file["hashes"]?.AsArray().FirstOrDefault(x=>x?["algo"]?.GetValue<int>()==1)?.Str("value");algorithm="SHA1";
  }
  else throw new IOException("Неизвестный источник");
  var p=Path.Combine(store.Root,"downloads",filename);await Net.Download(url,p,hash,algorithm);
  try
  {
   var i=source=="modrinth"?await ImportMrpack(p):await ImportCursePack(p);
   i.PackSource=source;i.PackId=id;i.PackVersion=packVersion;
   i.Icon=source=="modrinth"?project.Str("icon_url"):project?["logo"].Str("thumbnailUrl")??"";
   i.Banner=source=="modrinth"?project?["gallery"]?.AsArray().FirstOrDefault()?.Str("url")??"":project?["screenshots"]?.AsArray().FirstOrDefault()?.Str("url")??"";
   store.Save();return i;
  }
  finally{File.Delete(p);}
 }
}
