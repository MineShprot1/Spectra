using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Spectra;
public record CatalogItem(string Source,string Id,string Title,string Description,string Icon,long Downloads,string Side="unknown");
public sealed partial class CatalogService(Store store,GameService game,Action<object> emit)
{
 readonly SemaphoreSlim installation=new(1);
 readonly System.Collections.Concurrent.ConcurrentDictionary<string,string> sides=new();
 async Task<string> Side(string source,string id){if(source!="modrinth")return "unknown";if(sides.TryGetValue(id,out var cached))return cached;var project=await Net.Get("https://api.modrinth.com/v2/project/"+Q(id));var clientSide=project.Str("client_side");var serverSide=project.Str("server_side");if(!new[]{"required","optional","unsupported"}.Contains(clientSide)||!new[]{"required","optional","unsupported"}.Contains(serverSide))return sides[id]="unknown";var client=clientSide!="unsupported";var server=serverSide!="unsupported";return sides[id]=client&&server?"both":server?"server":"client";}
 readonly Dictionary<string,(long Size,long Ticks,string Hash)> installedHashes=new();
 readonly Dictionary<string,List<InstallFile>> knownVersions=new();
 static string Q(string x)=>Uri.EscapeDataString(x);
 public async Task<object> Search(string query,string kind,string? instanceId,int offset,string[]? filters=null)
 {
  var tags=ValidateFilters(kind,filters);offset=Math.Clamp(offset,0,10000);
  var i=string.IsNullOrEmpty(instanceId)?null:store.Get(instanceId);
  var facets=new List<string[]>{new[]{"project_type:"+(kind=="mods"?"mod":kind=="shaders"?"shader":kind=="resources"?"resourcepack":"modpack")}};
  if(i!=null){facets.Add(new[]{"versions:"+i.Version});if(kind=="mods"&&i.Loader!="vanilla")facets.Add(new[]{"categories:"+i.Loader});}
  foreach(var tag in tags)facets.Add(new[]{"categories:"+tag});
  var warnings=new List<string>();var results=new List<CatalogItem>();
  try{var n=await Net.Get("https://api.modrinth.com/v2/search?query="+Q(query)+"&limit=24&offset="+offset+"&index=downloads&facets="+Q(JsonSerializer.Serialize(facets)));results.AddRange(n["hits"]!.AsArray().Select(x=>new CatalogItem("modrinth",x.Str("project_id"),x.Str("title"),x.Str("description"),x.Str("icon_url"),x?["downloads"]?.GetValue<long>()??0)));}catch(Exception){warnings.Add("Modrinth сейчас недоступен");}
  if(string.IsNullOrWhiteSpace(store.Config.CurseForgeKey))warnings.Add("CurseForge: добавьте API-ключ в настройках");
  else try
  {
   var classId=kind switch{"mods"=>6,"resources"=>12,"shaders"=>6552,_=>4471};
   var url=$"https://api.curseforge.com/v1/mods/search?gameId=432&classId={classId}&pageSize=24&index={offset}&sortField=2&sortOrder=desc&searchFilter="+Q(query);
   if(i!=null){url+="&gameVersion="+Q(i.Version);if(kind=="mods")url+="&modLoaderType="+LoaderNumber(i.Loader);}
   long[] categoryIds=[];
   if(tags.Length>0){var categories=(await Curse("https://api.curseforge.com/v1/categories?gameId=432&classId="+classId))["data"]!.AsArray();categoryIds=tags.Select(tag=>categories.FirstOrDefault(c=>c.Str("slug")==tag||c.Str("name").Replace(" ","-").Equals(tag,StringComparison.OrdinalIgnoreCase))?["id"]?.GetValue<long>()??0).ToArray();}
   if(categoryIds.Contains(0))warnings.Add("CurseForge: для выбранного фильтра нет точного соответствия, показаны подходящие результаты Modrinth");
   else {if(categoryIds.Length>0)url+="&categoryIds="+Q(JsonSerializer.Serialize(categoryIds));
   var n=await Curse(url);results.AddRange(n["data"]!.AsArray().Where(x=>categoryIds.All(id=>x?["categories"]?.AsArray().Any(c=>c?["id"]?.GetValue<long>()==id)==true)).Select(x=>new CatalogItem("curseforge",x.Str("id"),x.Str("name"),x.Str("summary"),x?["logo"].Str("thumbnailUrl")??"",(long)(x?["downloadCount"]?.GetValue<double>()??0))));}
  }catch(Exception){warnings.Add("CurseForge недоступен или ключ не принят");}
  if(kind=="modpacks")foreach(var source in new[]{"ftb","technic"})try{results.AddRange(await ExternalSearch(source,query,offset));}catch(Exception e){warnings.Add(source.ToUpperInvariant()+": "+e.Message);}
  if(kind=="mods")results=(await Task.WhenAll(results.Select(async item=>{try{return item with {Side=await Side(item.Source,item.Id)};}catch{return item;}}))).ToList();
  // Counts belong to different services, preserve source attribution.
  return new{items=results.OrderByDescending(x=>x.Downloads),warnings,nextOffset=offset+24};
 }
 static int LoaderNumber(string loader)=>loader switch{"forge"=>1,"fabric"=>4,"quilt"=>5,"neoforge"=>6,_=>0};
 async Task<JsonNode> Curse(string url){using var req=new HttpRequestMessage(HttpMethod.Get,url);req.Headers.Add("x-api-key",store.Config.CurseForgeKey);return await Net.Send(req);}
 public async Task<object> Details(string source,string id,string kind,string instanceId)
 {
  if(!System.Text.RegularExpressions.Regex.IsMatch(id,@"\A[A-Za-z0-9_-]{1,100}\z"))throw new IOException("Неверный проект");if(kind is not ("mods" or "shaders" or "resources" or "modpacks"))throw new IOException("Неизвестная категория");
  if(source is "ftb" or "technic"){if(kind!="modpacks")throw new IOException("Источник поддерживает только модпаки");return await ExternalDetails(source,id);}
  var instance=string.IsNullOrEmpty(instanceId)?null:store.Get(instanceId);
  if(source=="modrinth"){
   var projectTask=Net.Get("https://api.modrinth.com/v2/project/"+Q(id));var versionsUrl="https://api.modrinth.com/v2/project/"+Q(id)+"/version";
   if(instance!=null){versionsUrl+="?game_versions="+Q(JsonSerializer.Serialize(new[]{instance.Version}));if(kind=="mods")versionsUrl+="&loaders="+Q(JsonSerializer.Serialize(new[]{instance.Loader}));}
   var versionsTask=Net.Get(versionsUrl);await Task.WhenAll(projectTask,versionsTask);var project=await projectTask;var members=await Net.Get("https://api.modrinth.com/v2/team/"+Q(project.Str("team"))+"/members");
   var expected=kind switch{"mods"=>"mod","shaders"=>"shader","resources"=>"resourcepack",_=>"modpack"};if(project.Str("project_type")!=expected)throw new IOException("Категория проекта не совпадает");
   return new{source,id,title=project.Str("title"),summary=project.Str("description"),description=project.Str("body"),format="markdown",icon=project.Str("icon_url"),downloads=project["downloads"]?.GetValue<long>()??0,followers=project["followers"]?.GetValue<long>()??0,created=project.Str("published"),updated=project.Str("updated"),license=project["license"].Str("name"),url="https://modrinth.com/"+expected+"/"+Q(project.Str("slug")),authors=members.AsArray().Select(m=>new{name=m?["user"].Str("username"),role=m.Str("role"),url="https://modrinth.com/user/"+Q(m?["user"].Str("username")??"")}),screenshots=project["gallery"]?.AsArray().OrderBy(g=>g?["ordering"]?.GetValue<int>()??0).Take(50).Select(g=>new{url=g.Str("url"),caption=g.Str("title")})??[],categories=project["categories"]?.DeepClone()??new JsonArray(),versions=(await versionsTask).AsArray().Take(30).Select(v=>new{id=v.Str("id"),name=v.Str("name"),version=v.Str("version_number"),date=v.Str("date_published"),game=v?["game_versions"]?.DeepClone(),loaders=v?["loaders"]?.DeepClone()})};
  }
  if(source=="curseforge"){
   if(!long.TryParse(id,out _))throw new IOException("Неверный проект CurseForge");var projectTask=Curse("https://api.curseforge.com/v1/mods/"+id);var descriptionTask=Curse("https://api.curseforge.com/v1/mods/"+id+"/description");var filesUrl="https://api.curseforge.com/v1/mods/"+id+"/files?pageSize=30";if(instance!=null){filesUrl+="&gameVersion="+Q(instance.Version);if(kind=="mods")filesUrl+="&modLoaderType="+LoaderNumber(instance.Loader);}var filesTask=Curse(filesUrl);await Task.WhenAll(projectTask,descriptionTask,filesTask);var project=(await projectTask)["data"]??throw new IOException("Проект не найден");
   var expected=kind switch{"mods"=>6,"shaders"=>6552,"resources"=>12,_=>4471};if(project["classId"]?.GetValue<int>()!=expected)throw new IOException("Категория проекта не совпадает");
   return new{source,id,title=project.Str("name"),summary=project.Str("summary"),description=(await descriptionTask).Str("data"),format="html",icon=project["logo"].Str("thumbnailUrl"),downloads=(long)(project["downloadCount"]?.GetValue<double>()??0),followers=0,created=project.Str("dateCreated"),updated=project.Str("dateModified"),license="Указана автором на CurseForge",url=project["links"].Str("websiteUrl"),authors=project["authors"]?.AsArray().Select(a=>new{name=a.Str("name"),role="Автор",url=a.Str("url")})??[],screenshots=project["screenshots"]?.AsArray().Take(50).Select(g=>new{url=g.Str("url"),caption=g.Str("title")})??[],categories=project["categories"]?.AsArray().Select(c=>c.Str("name")).ToArray()??[],versions=(await filesTask)["data"]?.AsArray().Take(30).Select(f=>new{id=f.Str("id"),name=f.Str("displayName"),version=f.Str("fileName"),date=f.Str("fileDate"),game=f?["gameVersions"]?.DeepClone(),loaders=(JsonNode?)null})??[]};
  }
  throw new IOException("Неизвестный источник");
 }
 sealed record InstallFile(string Name,string Url,string Hash,string Algorithm,string Side="unknown",string ProjectId="",string FileId="",string PageUrl="",string DisplayName="",string TargetFolder="");
 sealed record ManualPlan(string Source,string ProjectId,string Kind,string InstanceId,List<InstallFile> Files);
 readonly System.Collections.Concurrent.ConcurrentDictionary<string,ManualPlan> manualPlans=new();
 readonly System.Collections.Concurrent.ConcurrentDictionary<string,(long Size,long Ticks,string Hash)> manualHashes=new();
 static InstallFile CurseFile(JsonNode f,string projectUrl="",string displayName="")=>new(f.Str("fileName"),f.Str("downloadUrl"),f["hashes"]?.AsArray().FirstOrDefault(x=>x?["algo"]?.GetValue<int>()==1).Str("value")??"","SHA1",ProjectId:f.Str("modId"),FileId:f.Str("id"),PageUrl:projectUrl==""?"":projectUrl.TrimEnd('/')+"/files/"+f.Str("id"),DisplayName:displayName);
 public async Task<object> Install(string source,string projectId,string kind,string instanceId,string planId="",string[]? selectedNames=null)
 {
  await installation.WaitAsync();string stage="";var committed=new List<string>();var restored=new List<(string Original,string Backup)>();
  try{
   if(kind is not ("mods" or "shaders" or "resources" or "pack"))throw new IOException("Неизвестный тип дополнения");var i=store.Get(instanceId);if(game.Running.ContainsKey(i.Id))throw new IOException("Закройте игру перед установкой дополнений");
   knownVersions.Clear();var root=store.Folder(i);var folder=kind=="pack"?root:Path.Combine(root,GameService.KindFolder(kind));List<InstallFile> plan;
   if(planId!=""){
    if(!manualPlans.TryGetValue(planId,out var pending)||pending.InstanceId!=instanceId||pending.Kind!=kind)throw new IOException("Срок ожидания ручной загрузки истёк. Повторите установку.");
    var selected=(selectedNames??[]).ToHashSet(StringComparer.OrdinalIgnoreCase);if(selected.Count>pending.Files.Count)throw new IOException("Неверный список файлов");
    var blocked=pending.Files.Where(f=>f.Url=="").ToArray();foreach(var name in selected)if(!blocked.Any(f=>FileKey(f).Equals(name,StringComparison.OrdinalIgnoreCase)))throw new IOException("Файл не относится к этой установке");
    plan=pending.Files.Where(f=>f.Url!=""||selected.Contains(FileKey(f))).ToList();
   }else{
    plan=[];var visited=new HashSet<string>();var projects=new Dictionary<string,string>();await ResolveProject(source,projectId,kind,i,folder,visited,projects,plan);
    var blocked=plan.Where(f=>f.Url=="").ToArray();if(blocked.Length>0){var key=Guid.NewGuid().ToString("N");manualPlans[key]=new ManualPlan(source,projectId,kind,instanceId,plan);if(manualPlans.Count>30)manualPlans.TryRemove(manualPlans.Keys.First(),out _);return new{status="manualDownload",planId=key,items=blocked.Select(f=>new{name=f.DisplayName!=""?f.DisplayName:f.Name,file=f.Name,kind,version=i.Version,source="curseforge",url=f.PageUrl,required=true}).ToArray(),message="Некоторые файлы нельзя скачать автоматически: это ограничение правообладателя. Выберите файлы для ручной загрузки или продолжите без них."};}
   }
   stage=Path.Combine(store.Root,"downloads","dependencies-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);var ready=new List<(InstallFile File,string? Existing,string Folder)>();
   var manual=plan.Where(f=>f.Url=="").ToDictionary(FileKey,StringComparer.OrdinalIgnoreCase);foreach(var file in plan.DistinctBy(FileKey,StringComparer.OrdinalIgnoreCase)){var targetFolder=file.TargetFolder==""?folder:Path.Combine(root,file.TargetFolder);var existing=await Existing(targetFolder,file);ready.Add((file,existing,targetFolder));if(existing==null){var destination=Store.SafePath(stage,FileKey(file));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);if(manual.ContainsKey(FileKey(file))){var sourceFile=DownloadedManualFile(file);File.Copy(sourceFile,destination);if(file.Hash!=""&&!string.Equals(await Net.Hash(destination,file.Algorithm),file.Hash,StringComparison.OrdinalIgnoreCase))throw new IOException("Контрольная сумма скачанного файла не совпадает: "+file.Name);}else{if(!Uri.TryCreate(file.Url,UriKind.Absolute,out var download)||download.Scheme!="https")throw new IOException("Автор файла запретил загрузку через сторонние лаунчеры: "+file.Name);emit(new{type="progress",instanceId=i.Id,message="Загрузка мода или библиотеки: "+file.Name,percent=0});await Net.Download(file.Url,destination,file.Hash==""?null:file.Hash,file.Algorithm);}}}
   var outdated=new HashSet<string>(StringComparer.OrdinalIgnoreCase);if(kind!="pack")foreach(var old in knownVersions.Values.SelectMany(x=>x).Where(f=>f.Hash!=""&&!plan.Any(p=>p.Algorithm==f.Algorithm&&p.Hash==f.Hash))){var path=await Existing(folder,old);if(path!=null&&!ready.Any(x=>x.Existing==path))outdated.Add(path);}
   if(game.Running.ContainsKey(i.Id))throw new IOException("Закройте игру перед установкой дополнений");foreach(var target in ready.Select(x=>x.Folder).Distinct(StringComparer.OrdinalIgnoreCase))Directory.CreateDirectory(target);
   foreach(var old in outdated){var backup=Path.Combine(stage,"old-"+Guid.NewGuid().ToString("N"));File.Move(old,backup);restored.Add((old,backup));}
   foreach(var item in ready){var dest=Store.SafePath(item.Folder,item.File.Name);if(item.Existing==dest)continue;if(File.Exists(dest)){var backup=Store.SafePath(stage,"backup/"+FileKey(item.File));Directory.CreateDirectory(Path.GetDirectoryName(backup)!);File.Move(dest,backup);restored.Add((dest,backup));}
    if(item.Existing!=null){File.Move(item.Existing,dest);restored.Add((item.Existing,dest));}else{File.Move(Store.SafePath(stage,FileKey(item.File)),dest);committed.Add(dest);}}
   if(kind=="mods"){foreach(var file in plan)i.ModSides[file.Name]=file.Side;store.Save();}else if(kind=="pack"){i.PendingManualFiles.Clear();store.Save();}
   emit(new{type="progress",instanceId=i.Id,message="Мод и обязательные зависимости установлены",percent=100});
   if(planId!="")manualPlans.TryRemove(planId,out _);return new{status="installed",message="Дополнения установлены"};
  }catch{foreach(var path in committed)if(File.Exists(path))File.Delete(path);foreach(var item in restored.AsEnumerable().Reverse())if(File.Exists(item.Backup))File.Move(item.Backup,item.Original,true);throw;}
  finally{if(stage!=""&&Directory.Exists(stage))Directory.Delete(stage,true);installation.Release();}
 }
 static string FileKey(InstallFile file)=>file.TargetFolder==""?file.Name:file.TargetFolder.Replace('\\','/')+"/"+file.Name;
 static string DownloadedManualFile(InstallFile file){var downloads=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");if(!Directory.Exists(downloads))throw new IOException("Папка загрузок не найдена");var path=Directory.EnumerateFiles(downloads).FirstOrDefault(p=>Path.GetFileName(p).Equals(file.Name,StringComparison.OrdinalIgnoreCase));if(path==null)throw new IOException("Дождитесь загрузки файла: "+file.Name);if(new FileInfo(path).Length>1024L*1024*1024)throw new IOException("Файл слишком большой: "+file.Name);return path;}
 public async Task<object> ManualStatus(string planId,string[] names){if(!manualPlans.TryGetValue(planId,out var plan))throw new IOException("Срок ожидания загрузки истёк");var selected=(names??[]).ToHashSet(StringComparer.OrdinalIgnoreCase);var blocked=plan.Files.Where(f=>f.Url=="").ToArray();if(selected.Any(name=>!blocked.Any(f=>FileKey(f).Equals(name,StringComparison.OrdinalIgnoreCase))))throw new IOException("Неверный список файлов");var rows=new List<object>();foreach(var file in blocked.Where(f=>selected.Contains(FileKey(f)))){var ready=false;var found=false;try{var path=DownloadedManualFile(file);found=true;var info=new FileInfo(path);if(file.Hash=="")ready=true;else{var key=Path.GetFullPath(path);if(!manualHashes.TryGetValue(key,out var cached)||cached.Size!=info.Length||cached.Ticks!=info.LastWriteTimeUtc.Ticks){var hash=await Net.Hash(path,file.Algorithm);manualHashes[key]=(info.Length,info.LastWriteTimeUtc.Ticks,hash);cached=(info.Length,info.LastWriteTimeUtc.Ticks,hash);}ready=string.Equals(cached.Hash,file.Hash,StringComparison.OrdinalIgnoreCase);}}catch(Exception e) when(e is IOException or UnauthorizedAccessException){}rows.Add(new{name=FileKey(file),ready,found});}var readyAll=rows.Count==selected.Count&&rows.All(row=>(bool)row.GetType().GetProperty("ready")!.GetValue(row)!);return new{items=rows,ready=readyAll};}
 public object? PackManualPlan(string instanceId){var instance=store.Get(instanceId);if(instance.PendingManualFiles.Count==0)return null;var files=instance.PendingManualFiles.Select(f=>new InstallFile(f.Name,"",f.Hash,"SHA1",ProjectId:f.ProjectId,FileId:f.FileId,PageUrl:f.PageUrl,DisplayName:f.Name,TargetFolder:f.Folder)).ToList();var planId=Guid.NewGuid().ToString("N");manualPlans[planId]=new ManualPlan("curseforge",instance.PackId,"pack",instanceId,files);return new{status="manualDownload",planId,items=files.Select(f=>new{name=f.DisplayName,file=FileKey(f),kind=instance.PendingManualFiles.First(x=>x.Name==f.Name&&x.Folder==f.TargetFolder).Kind,version=instance.PendingManualFiles.First(x=>x.Name==f.Name&&x.Folder==f.TargetFolder).Version,source="curseforge",url=f.PageUrl,required=true}).ToArray(),message="Авторы запретили прямую загрузку части файлов этой сборки. Выберите, что скачать вручную; остальные файлы останутся в сборке."};}
 async Task<string?> Existing(string folder,InstallFile file)
 {
  if(!Directory.Exists(folder))return null;var candidates=Directory.EnumerateFiles(folder).Where(p=>p.EndsWith(".jar",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".disabled",StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p.EndsWith(".disabled",StringComparison.OrdinalIgnoreCase));
  foreach(var p in candidates){var info=new FileInfo(p);var key=file.Algorithm+":"+p;string value;if(installedHashes.TryGetValue(key,out var cached)&&cached.Size==info.Length&&cached.Ticks==info.LastWriteTimeUtc.Ticks)value=cached.Hash;else{value=await Net.Hash(p,file.Algorithm);installedHashes[key]=(info.Length,info.LastWriteTimeUtc.Ticks,value);}if(value==file.Hash.ToLowerInvariant())return p;}return null;
 }
 static InstallFile ModrinthFile(JsonNode v){var f=v["files"]?.AsArray().FirstOrDefault(x=>x?["primary"]?.GetValue<bool>()==true)??v["files"]?[0]??throw new IOException("У версии нет файла");return new(f.Str("filename"),f.Str("url"),f["hashes"].Str("sha512"),"SHA512",ProjectId:v.Str("project_id"),FileId:v.Str("id"),DisplayName:f.Str("filename"));}
 static void AddFile(List<InstallFile> plan,InstallFile file){if(file.Name==""||file.Name!=Path.GetFileName(file.Name)||file.Name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||file.Hash=="")throw new IOException("Файл нельзя безопасно проверить по контрольной сумме: "+file.Name);if(plan.Any(f=>f.Name.Equals(file.Name,StringComparison.OrdinalIgnoreCase)&&f.Hash!=file.Hash))throw new IOException("Конфликт файлов зависимостей: "+file.Name);plan.Add(file);}
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
   var files=(await Curse(url))["data"]!.AsArray().OrderByDescending(x=>x.Str("fileDate")).ToList();var f=files.FirstOrDefault()??throw new IOException("Нет совместимого файла: "+id);if(f.Str("modId")!=id)throw new IOException("CurseForge вернул файл другого проекта");var compatible=f["gameVersions"]?.AsArray().Select(x=>x?.ToString()??"").ToArray()??[];if(compatible.Length>0&&!compatible.Contains(i.Version,StringComparer.OrdinalIgnoreCase))throw new IOException("Файл CurseForge несовместим с Minecraft "+i.Version);var project=(await Curse("https://api.curseforge.com/v1/mods/"+Q(id)))["data"]!;var page=project["links"].Str("websiteUrl");
   foreach(var candidate in files){if(candidate!=null&&await Existing(folder,CurseFile(candidate,page,project.Str("name")))!=null){f=candidate;break;}}
   knownVersions["curseforge:"+id]=files.Where(x=>x!=null).Select(x=>CurseFile(x!,page,project.Str("name"))).ToList();
   foreach(var dep in f["dependencies"]?.AsArray()??[])if(dep?["relationType"]?.GetValue<int>()==3)await ResolveProject(source,dep.Str("modId"),kind,i,folder,visited,projects,plan);AddFile(plan,CurseFile(f,page,project.Str("name")));
  }else throw new IOException("Неизвестный источник");
 }
 async Task ResolveModrinth(JsonNode v,string kind,Instance i,string folder,HashSet<string> visited,Dictionary<string,string> projects,List<InstallFile> plan)
 {
  var id=v.Str("id");var project=v.Str("project_id");
  if(!knownVersions.ContainsKey(project)){var url="https://api.modrinth.com/v2/project/"+Q(project)+"/version?game_versions="+Q(JsonSerializer.Serialize(new[]{i.Version}));if(kind=="mods")url+="&loaders="+Q(JsonSerializer.Serialize(new[]{i.Loader}));knownVersions[project]=(await Net.Get(url)).AsArray().Where(x=>x!=null).Select(x=>ModrinthFile(x!)).ToList();}if(projects.TryGetValue(project,out var version)&&version!=id)throw new IOException("Зависимости требуют разные версии одной библиотеки: "+project);projects[project]=id;
  if(!visited.Add("version:"+id))return;if(visited.Count>100)throw new IOException("Слишком много зависимостей");
  if(v["game_versions"]?.AsArray().Any(x=>x?.ToString()==i.Version)!=true||kind=="mods"&&v["loaders"]?.AsArray().Any(x=>x?.ToString()==i.Loader)!=true)throw new IOException("Библиотека несовместима с версией игры или загрузчиком: "+project);
  foreach(var d in v["dependencies"]?.AsArray()??[]){if(d.Str("dependency_type")!="required")continue;if(d.Str("version_id") is {Length:>0} pinned)await ResolveModrinth(await Net.Get("https://api.modrinth.com/v2/version/"+Q(pinned)),kind,i,folder,visited,projects,plan);else if(d.Str("project_id") is {Length:>0} dependency)await ResolveProject("modrinth",dependency,kind,i,folder,visited,projects,plan);else throw new IOException("Обязательная библиотека доступна только для ручной установки: "+d.Str("file_name"));}AddFile(plan,ModrinthFile(v) with {Side=kind=="mods"?await Side("modrinth",project):"unknown"});
 }
 public async Task<Instance> ImportMrpack(string path)
 {
  await installation.WaitAsync(); Instance? instance=null;
  try
  {
   using var zip=ZipFile.OpenRead(path);var index=zip.GetEntry("modrinth.index.json")??throw new IOException("Это не .mrpack");using var reader=new StreamReader(index.Open());var n=JsonNode.Parse(await reader.ReadToEndAsync())!;
   if(n.Str("game")!="minecraft")throw new IOException("Модпак не для Minecraft");
   var dependencies=n["dependencies"]!;var mc=dependencies.Str("minecraft");await game.Metadata(mc);
   instance=new Instance{CreatedAt=DateTime.UtcNow,Name=n.Str("name"),Version=mc,Settings=store.Config.Defaults with{}};
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
   var minecraft=n["minecraft"]!;var mc=minecraft.Str("version");await game.Metadata(mc);instance=new Instance{CreatedAt=DateTime.UtcNow,Name=n.Str("name"),Version=mc,Settings=store.Config.Defaults with{}};
   var loader=minecraft["modLoaders"]?.AsArray().FirstOrDefault(x=>x?["primary"]?.GetValue<bool>()==true)??minecraft["modLoaders"]?.AsArray().FirstOrDefault();
   if(loader!=null){var id=loader.Str("id");var separator=id.IndexOf('-');if(separator<0)throw new IOException("Неизвестный загрузчик модпака");instance.Loader=id[..separator];instance.LoaderVersion=id[(separator+1)..];if(instance.Loader is not ("forge" or "neoforge" or "fabric" or "quilt"))throw new IOException("Загрузчик не поддерживается");}
   var root=store.Folder(instance);var files=n["files"]!.AsArray();if(files.Count>10000)throw new IOException("Слишком много файлов");
   foreach(var item in files)
   {
    var file=(await Curse("https://api.curseforge.com/v1/mods/"+Q(item.Str("projectID"))+"/files/"+Q(item.Str("fileID"))))["data"]!;
    var projectId=item.Str("projectID");var fileId=file.Str("id");if(file.Str("modId")!=projectId||fileId!=item.Str("fileID"))throw new IOException("Файл CurseForge не совпадает с манифестом");var versions=file["gameVersions"]?.AsArray().Select(x=>x?.ToString()??"").ToArray()??[];if(versions.Length>0&&!versions.Contains(mc,StringComparer.OrdinalIgnoreCase))throw new IOException("Файл CurseForge несовместим с Minecraft "+mc);
    var project=(await Curse("https://api.curseforge.com/v1/mods/"+Q(projectId)))["data"]!;var classId=project["classId"]?.GetValue<int>()??6;var folder=classId==12?"resourcepacks":classId==6552?"shaderpacks":"mods";var kind=classId==12?"resources":classId==6552?"shaders":classId==6?"mods":"";var filename=file.Str("fileName");if(kind==""||filename!=Path.GetFileName(filename)||filename.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new IOException("Неподдерживаемый файл модпака CurseForge");
    var download=file.Str("downloadUrl");if(string.IsNullOrWhiteSpace(download)){var page=project["links"].Str("websiteUrl").TrimEnd('/')+"/files/"+fileId;var blockedHash=file["hashes"]?.AsArray().FirstOrDefault(x=>x?["algo"]?.GetValue<int>()==1)?.Str("value")??"";if(blockedHash=="")throw new IOException("CurseForge не предоставил контрольную сумму для "+filename);instance.PendingManualFiles.Add(new PendingPackFile{ProjectId=projectId,FileId=fileId,Name=filename,Folder=folder,Hash=blockedHash,PageUrl=page,Kind=kind,Version=mc});continue;}
    emit(new{type="progress",instanceId=instance.Id,message=file.Str("fileName"),percent=0});var sha=file["hashes"]?.AsArray().FirstOrDefault(x=>x?["algo"]?.GetValue<int>()==1)?.Str("value");
    await Net.Download(download,Store.SafePath(root,folder+"/"+filename),sha);
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
 public async Task<object> InstallPack(string source,string id,string pinnedVersion="")
 {
  if(source is "ftb" or "technic")return await InstallExternalPack(source,id,pinnedVersion);
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
   url=file.Str("downloadUrl");if(string.IsNullOrWhiteSpace(url))return new{status="manualPackArchive",url=project["links"].Str("websiteUrl"),message="Автор ограничил загрузку архива через сторонние лаунчеры. Скачайте модпак на CurseForge и импортируйте ZIP сюда; недоступные компоненты можно будет скачать отдельно."};filename=Guid.NewGuid()+".zip";hash=file["hashes"]?.AsArray().FirstOrDefault(x=>x?["algo"]?.GetValue<int>()==1)?.Str("value");algorithm="SHA1";
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
