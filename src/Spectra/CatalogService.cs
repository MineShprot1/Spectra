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
 public async Task Install(string source,string projectId,string kind,string instanceId)
 {
  await installation.WaitAsync();try
  {
   var i=store.Get(instanceId);if(game.Running.ContainsKey(i.Id))throw new IOException("Закройте игру перед установкой дополнений");
   var root=store.Folder(i);if(kind=="modpacks")throw new IOException("Для модпака используйте импорт .mrpack");
   var visited=new HashSet<string>();await InstallProject(source,projectId,kind,i,root,visited);
  }finally{installation.Release();}
 }
 async Task InstallProject(string source,string id,string kind,Instance i,string root,HashSet<string> visited)
 {
  if(!visited.Add(source+id))return;if(visited.Count>100)throw new IOException("Слишком много зависимостей");
  emit(new{type="progress",instanceId=i.Id,message="Загрузка дополнения "+id,percent=0});
  if(source=="modrinth")
  {
   var url="https://api.modrinth.com/v2/project/"+Q(id)+"/version?game_versions="+Q(JsonSerializer.Serialize(new[]{i.Version}));
   if(kind=="mods")url+="&loaders="+Q(JsonSerializer.Serialize(new[]{i.Loader}));
   var versions=await Net.Get(url);var v=versions.AsArray().FirstOrDefault()??throw new IOException("Совместимого файла нет");
   await InstallModrinthVersion(v,kind,i,root,visited);
  }
  else if(source=="curseforge")
  {
   var url=$"https://api.curseforge.com/v1/mods/{Q(id)}/files?gameVersion={Q(i.Version)}";
   if(kind=="mods")url+="&modLoaderType="+LoaderNumber(i.Loader);
   var files=await Curse(url);var f=files["data"]!.AsArray().OrderByDescending(x=>x.Str("fileDate")).FirstOrDefault()??throw new IOException("Совместимых файлов нет");
   foreach(var dep in f["dependencies"]?.AsArray()??[])if(dep?["relationType"]?.GetValue<int>()==3)await InstallProject(source,dep.Str("modId"),kind,i,root,visited);
   var download=f.Str("downloadUrl");if(string.IsNullOrEmpty(download))throw new IOException("Автор запретил загрузку через сторонние лаунчеры. Откройте страницу CurseForge.");
   var sha=f["hashes"]?.AsArray().FirstOrDefault(x=>x?["algo"]?.GetValue<int>()==1)?.Str("value");await Net.Download(download,Store.SafePath(root,GameService.KindFolder(kind)+"/"+f.Str("fileName")),sha);
  }
  else throw new IOException("Неизвестный источник");
 }
 async Task InstallModrinthVersion(JsonNode v,string kind,Instance i,string root,HashSet<string> visited)
 {
  foreach(var d in v["dependencies"]?.AsArray()??[])
  {
   if(d.Str("dependency_type")!="required")continue;
   if(d.Str("version_id") is {Length:>0} versionId){if(!visited.Add("version:"+versionId))continue;await InstallModrinthVersion(await Net.Get("https://api.modrinth.com/v2/version/"+Q(versionId)),kind,i,root,visited);}
   else if(d.Str("project_id") is {Length:>0} projectId)await InstallProject("modrinth",projectId,kind,i,root,visited);
  }
  var file=v["files"]!.AsArray().FirstOrDefault(x=>x?["primary"]?.GetValue<bool>()==true)??v["files"]![0]!;
  await Net.Download(file.Str("url"),Store.SafePath(root,GameService.KindFolder(kind)+"/"+file.Str("filename")),file["hashes"].Str("sha512"),"SHA512");
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
 public async Task<Instance> InstallPack(string source,string id)
 {
  string url,filename;string? hash;string algorithm;JsonNode? project=null;
  if(source=="modrinth")
  {
   project=await Net.Get("https://api.modrinth.com/v2/project/"+Q(id));
   var versions=await Net.Get("https://api.modrinth.com/v2/project/"+Q(id)+"/version");var v=versions[0]??throw new IOException("Нет версии модпака");var file=v["files"]!.AsArray().FirstOrDefault(x=>x.Str("filename").EndsWith(".mrpack"))??throw new IOException("Нет .mrpack");
   url=file.Str("url");filename=Guid.NewGuid()+".mrpack";hash=file["hashes"].Str("sha512");algorithm="SHA512";
  }
  else if(source=="curseforge")
  {
   project=(await Curse("https://api.curseforge.com/v1/mods/"+Q(id)))["data"]!;
   var versions=(await Curse("https://api.curseforge.com/v1/mods/"+Q(id)+"/files"))["data"]!.AsArray();var file=versions.OrderByDescending(x=>x.Str("fileDate")).FirstOrDefault()??throw new IOException("Нет версии модпака");
   url=file.Str("downloadUrl");if(string.IsNullOrWhiteSpace(url))throw new IOException("Автор запретил загрузку модпака через сторонние лаунчеры");filename=Guid.NewGuid()+".zip";hash=file["hashes"]?.AsArray().FirstOrDefault(x=>x?["algo"]?.GetValue<int>()==1)?.Str("value");algorithm="SHA1";
  }
  else throw new IOException("Неизвестный источник");
  var p=Path.Combine(store.Root,"downloads",filename);await Net.Download(url,p,hash,algorithm);
  try
  {
   var i=source=="modrinth"?await ImportMrpack(p):await ImportCursePack(p);
   i.Icon=source=="modrinth"?project.Str("icon_url"):project?["logo"].Str("thumbnailUrl")??"";
   i.Banner=source=="modrinth"?project?["gallery"]?.AsArray().FirstOrDefault()?.Str("url")??"":project?["screenshots"]?.AsArray().FirstOrDefault()?.Str("url")??"";
   store.Save();return i;
  }
  finally{File.Delete(p);}
 }
}
