using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Spectra;
public sealed partial class CatalogService
{
 public sealed record PackEntry(string Kind,string Name,string File,string Version,string Icon,string Source,string ProjectId,string Hash="",string FileId="",bool Optional=false,string Origin="manifest");
 readonly SemaphoreSlim contentsGate=new(1,1);
 readonly Dictionary<string,object> contentsCache=new();
 static string PackKind(string path)=>path.Split('/')[0].ToLowerInvariant() switch{"mods"=>"mods","shaderpacks"=>"shaders","resourcepacks"=>"resources",_=>""};
 static string ContentPath(string path){var parts=path.Split('/');var kind=PackKind(path);if(kind==""||parts.Length<2)return "";if(kind=="mods")return path.EndsWith(".jar",StringComparison.OrdinalIgnoreCase)||path.EndsWith(".jar.disabled",StringComparison.OrdinalIgnoreCase)?path:"";if(parts.Length>2)return parts[0]+"/"+parts[1];return path.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)||path.EndsWith(".zip.disabled",StringComparison.OrdinalIgnoreCase)?path:"";}
 static bool PackPath(string path)=>path.Length is >0 and <1024&&!path.Contains('\\')&&!path.Contains(':')&&!path.StartsWith('/')&&!path.Split('/').Any(p=>p is "" or "." or "..");
 public static List<PackEntry> ReadPackContents(ZipArchive zip,string source)
 {
  if(zip.Entries.Count>50000)throw new IOException("Архив содержит слишком много файлов");
  var entry=zip.GetEntry(source=="modrinth"?"modrinth.index.json":"manifest.json")??throw new IOException("В модпаке нет манифеста");if(entry.Length>8*1024*1024)throw new IOException("Манифест модпака слишком большой");using var reader=new StreamReader(entry.Open());var manifest=JsonNode.Parse(reader.ReadToEnd())??throw new IOException("Пустой манифест");var items=new List<PackEntry>();
  var files=manifest["files"] as JsonArray??throw new IOException("Манифест не содержит список файлов");if(files.Count>10000)throw new IOException("Слишком много дополнений");
  foreach(var file in files){if(source=="modrinth"){
    var original=file.Str("path");if(!PackPath(original)||file?["env"].Str("client")=="unsupported")continue;var path=ContentPath(original);var kind=PackKind(path);if(path=="")continue;items.Add(new(kind,Path.GetFileName(path),path,"","","modrinth","",path==original?file?["hashes"].Str("sha512")??"":"",Optional:file?["env"].Str("client")=="optional"));
   }else{var mod=file.Str("projectID");var id=file.Str("fileID");if(!long.TryParse(mod,out _)||!long.TryParse(id,out _))throw new IOException("Неверный ID файла CurseForge");items.Add(new("unknown","CurseForge #"+mod,"","","","curseforge",mod,FileId:id,Optional:file?["required"]?.GetValue<bool>()==false));}}
  var overrides=source=="modrinth"?new[]{"overrides/","client-overrides/"}:new[]{manifest.Str("overrides").TrimEnd('/')+"/"};
  foreach(var prefix in overrides){if(prefix=="/"||!PackPath(prefix.TrimEnd('/')))continue;foreach(var file in zip.Entries.Where(e=>e.FullName.StartsWith(prefix,StringComparison.Ordinal)&&!e.FullName.EndsWith('/'))){var original=file.FullName[prefix.Length..];if(!PackPath(original))continue;var path=ContentPath(original);var kind=PackKind(path);if(path==""||kind=="")continue;items.RemoveAll(i=>i.File.Equals(path,StringComparison.OrdinalIgnoreCase));items.Add(new(kind,Path.GetFileName(path),path,"","","","",Origin:"override"));}}
  return items.DistinctBy(i=>i.File.Length>0?i.File:i.ProjectId+":"+i.FileId,StringComparer.OrdinalIgnoreCase).ToList();
 }
 async Task<JsonNode> PostMetadata(string url,object data,bool curse=false){using var request=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(data)};if(curse)request.Headers.Add("x-api-key",store.Config.CurseForgeKey);return await Net.Send(request);}
 async Task DescribeModrinth(List<PackEntry> items,List<string> warnings)
 {
  var byHash=new Dictionary<string,JsonNode>();foreach(var hashes in items.Where(i=>Regex.IsMatch(i.Hash,@"\A[a-fA-F0-9]{128}\z")).Select(i=>i.Hash).Distinct().Chunk(100)){
   try{var results=(await PostMetadata("https://api.modrinth.com/v2/version_files",new{hashes,algorithm="sha512"})).AsObject();foreach(var pair in results)if(pair.Value!=null)byHash[pair.Key]=pair.Value;}catch{warnings.Add("Часть названий Modrinth недоступна; вместо них показаны имена файлов.");break;}}
  var projects=new Dictionary<string,JsonNode>();foreach(var ids in byHash.Values.Select(v=>v.Str("project_id")).Distinct().Chunk(100)){
   try{var result=await Net.Get("https://api.modrinth.com/v2/projects?ids="+Q(JsonSerializer.Serialize(ids)));foreach(var project in result.AsArray())if(project!=null)projects[project.Str("id")]=project;}catch{warnings.Add("Не удалось получить часть иконок и названий проектов.");break;}}
  for(var index=0;index<items.Count;index++){var item=items[index];if(!byHash.TryGetValue(item.Hash,out var version))continue;var id=version.Str("project_id");projects.TryGetValue(id,out var project);items[index]=item with{Name=project?.Str("title") is {Length:>0} title?title:item.Name,Version=version.Str("version_number"),Icon=project?.Str("icon_url")??"",ProjectId=id};}
 }
 async Task DescribeCurse(List<PackEntry> items,List<string> warnings)
 {
  var projects=new Dictionary<string,JsonNode>();var files=new Dictionary<string,JsonNode>();var projectIds=items.Where(i=>i.Source=="curseforge").Select(i=>long.Parse(i.ProjectId)).Distinct().ToArray();var fileIds=items.Where(i=>i.Source=="curseforge").Select(i=>long.Parse(i.FileId)).Distinct().ToArray();
  foreach(var ids in projectIds.Chunk(100)){try{var result=await PostMetadata("https://api.curseforge.com/v1/mods",new{modIds=ids},true);foreach(var project in result["data"]?.AsArray()??[])if(project!=null)projects[project.Str("id")]=project;}catch{warnings.Add("Часть проектов CurseForge недоступна; неизвестные дополнения показаны отдельно.");break;}}
  foreach(var ids in fileIds.Chunk(100)){try{var result=await PostMetadata("https://api.curseforge.com/v1/mods/files",new{fileIds=ids},true);foreach(var file in result["data"]?.AsArray()??[])if(file!=null)files[file.Str("id")]=file;}catch{warnings.Add("Версии части файлов CurseForge недоступны.");break;}}
  for(var index=0;index<items.Count;index++){var item=items[index];if(item.Source!="curseforge")continue;projects.TryGetValue(item.ProjectId,out var project);files.TryGetValue(item.FileId,out var file);var filename=file?.Str("fileName")??"";var kind=project?["classId"]?.GetValue<int>() switch{6=>"mods",6552=>"shaders",12=>"resources",_=>filename.EndsWith(".jar",StringComparison.OrdinalIgnoreCase)?"mods":"unknown"};items[index]=item with{Kind=kind,Name=project?.Str("name") is {Length:>0} name?name:item.Name,File=filename.Length==0?"":(kind=="mods"?"mods/":kind=="shaders"?"shaderpacks/":kind=="resources"?"resourcepacks/":"")+filename,Version=file?.Str("displayName")??"",Icon=project?["logo"].Str("thumbnailUrl")??""};}
 }
 public async Task<object> PackContents(string source,string projectId,string versionId)
 {
  if(source is not ("modrinth" or "curseforge")||!Regex.IsMatch(projectId,@"\A[A-Za-z0-9_-]{1,100}\z")||!Regex.IsMatch(versionId,@"\A[A-Za-z0-9_-]{1,100}\z"))throw new IOException("Неверный выпуск модпака");
  var key=source+":"+projectId+":"+versionId;await contentsGate.WaitAsync();try{
   if(contentsCache.TryGetValue(key,out var cached))return cached;
   string url,hash,algorithm,name;
   if(source=="modrinth"){
    var version=await Net.Get("https://api.modrinth.com/v2/version/"+Q(versionId));if(version.Str("project_id")!=projectId)throw new IOException("Выпуск принадлежит другому модпаку");var file=version["files"]?.AsArray().FirstOrDefault(f=>f.Str("filename").EndsWith(".mrpack",StringComparison.OrdinalIgnoreCase))??throw new IOException("У выпуска нет MRPACK");url=file.Str("url");hash=file["hashes"].Str("sha512");algorithm="SHA512";name=version.Str("name");
   }else{
    if(!long.TryParse(projectId,out _)||!long.TryParse(versionId,out _))throw new IOException("Неверный ID CurseForge");var file=(await Curse("https://api.curseforge.com/v1/mods/"+Q(projectId)+"/files/"+Q(versionId)))["data"]??throw new IOException("Файл не найден");if(file.Str("modId")!=projectId)throw new IOException("Файл принадлежит другому модпаку");url=file.Str("downloadUrl");hash=file["hashes"]?.AsArray().FirstOrDefault(h=>h?["algo"]?.GetValue<int>()==1).Str("value")??"";algorithm="SHA1";name=file.Str("displayName");
   }
   if(!Uri.TryCreate(url,UriKind.Absolute,out var address)||address.Scheme!="https"||address.UserInfo!=""||!(address.Host=="cdn.modrinth.com"||address.Host=="mediafilez.forgecdn.net"||address.Host=="edge.forgecdn.net"||address.Host.EndsWith(".forgecdn.net",StringComparison.Ordinal)))throw new IOException("Автор не разрешил загрузку архива через API; состав недоступен.");
   var folder=Path.Combine(store.Root,"pack-previews");Directory.CreateDirectory(folder);var path=Path.Combine(folder,source+"-"+projectId+"-"+versionId+".zip");
   if(!File.Exists(path)||hash!=""&&await Net.Hash(path,algorithm)!=hash.ToLowerInvariant())await Net.Download(url,path,hash==""?null:hash,algorithm,256L*1024*1024);
   var warnings=new List<string>();List<PackEntry> items;using(var zip=ZipFile.OpenRead(path))items=ReadPackContents(zip,source);
   if(source=="modrinth")await DescribeModrinth(items,warnings);else await DescribeCurse(items,warnings);items=items.DistinctBy(i=>i.File.Length>0?i.File:i.ProjectId+":"+i.FileId,StringComparer.OrdinalIgnoreCase).ToList();
   var result=new{versionId,name,counts=new{mods=items.Count(i=>i.Kind=="mods"),shaders=items.Count(i=>i.Kind=="shaders"),resources=items.Count(i=>i.Kind=="resources"),unknown=items.Count(i=>i.Kind=="unknown")},items=items.OrderBy(i=>i.Name,StringComparer.OrdinalIgnoreCase),warnings=warnings.Distinct().ToArray()};
   if(contentsCache.Count>=12)contentsCache.Remove(contentsCache.Keys.First());contentsCache[key]=result;
   long bytes=0;foreach(var file in new DirectoryInfo(folder).EnumerateFiles("*.zip").OrderByDescending(f=>f.LastWriteTimeUtc)){bytes+=file.Length;if(bytes>512L*1024*1024&&file.FullName!=path)try{file.Delete();}catch{}}
   return result;
  }finally{contentsGate.Release();}
 }
}
