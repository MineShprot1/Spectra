using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Spectra;
public sealed partial class CatalogService
{
 const string FtbApi="https://api.feed-the-beast.com/v1/modpacks/public/modpack/";
 const string TechnicApi="https://api.technicpack.net/";
 static string ExternalId(string id){if(!Regex.IsMatch(id,@"\A[A-Za-z0-9_-]{1,100}\z"))throw new IOException("Неверный идентификатор модпака");return Uri.EscapeDataString(id);}
 static string SecureUrl(string value){if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo!=""||uri.IsLoopback||uri.HostNameType!=UriHostNameType.Dns||uri.Host.EndsWith(".local",StringComparison.OrdinalIgnoreCase))throw new IOException("Источник модпака должен использовать публичный HTTPS-адрес");return uri.AbsoluteUri;}
 static async Task<JsonNode> PackJson(string url)
 {
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));using var response=await Net.Http.GetAsync(SecureUrl(url),HttpCompletionOption.ResponseHeadersRead,timeout.Token);
  if(!response.IsSuccessStatusCode)throw new IOException("Каталог вернул HTTP "+(int)response.StatusCode);
  await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);using var output=new MemoryStream();var buffer=new byte[32768];int count;
  while((count=await stream.ReadAsync(buffer,timeout.Token))>0){if(output.Length+count>16*1024*1024)throw new IOException("Ответ каталога слишком большой");output.Write(buffer,0,count);}
  var data=JsonNode.Parse(output.ToArray())??throw new IOException("Каталог вернул пустой ответ");if(data.Str("status")=="error"||data["error"]!=null)throw new IOException("Модпак недоступен в каталоге");return data;
 }
 static long Number(JsonNode? n,string key)=>long.TryParse(n.Str(key),out var value)?value:0;
 static string Stamp(JsonNode? n,string key){var value=Number(n,key);return value>0&&value<253402300800?DateTimeOffset.FromUnixTimeSeconds(value).ToString("O"):"";}
 static string FtbArt(JsonNode n,string type="square")=>n["art"]?.AsArray().FirstOrDefault(x=>x.Str("type")==type).Str("url")??"";
 static CatalogItem ExternalItem(string source,JsonNode p)=>source=="ftb"?new(source,p.Str("id"),p.Str("name"),p.Str("synopsis"),FtbArt(p),Number(p,"installs")):new(source,p.Str("name"),p.Str("displayName"),p.Str("description"),p["icon"].Str("url"),Number(p,"installs"));
 async Task<List<CatalogItem>> ExternalSearch(string source,string query,int offset)
 {
  if(source=="technic"){
   if(string.IsNullOrWhiteSpace(query)){
    var slugs=new[]{"tekkit-2","tekkit-legends","hexxit","blightfall"};if(offset>=slugs.Length)return [];
    return (await Task.WhenAll(slugs.Skip(offset).Select(async slug=>{try{return ExternalItem(source,await PackJson(TechnicApi+"modpack/"+slug+"?build=999"));}catch{return null;}}))).OfType<CatalogItem>().ToList();
   }
   var data=await PackJson(TechnicApi+"search?build=999&q="+Q(query));return (data["modpacks"]?.AsArray()??[]).Skip(offset).Take(24).Select(p=>new CatalogItem(source,p.Str("slug"),p.Str("name"),"Модпак Technic Platform",p.Str("iconUrl"),0)).ToList();
  }
  if(offset>=240)return [];
  var url=string.IsNullOrWhiteSpace(query)?FtbApi+"popular/installs/"+(offset+24):FtbApi+"search/"+(offset+24)+"?term="+Q(query);
  var list=await PackJson(url);var ids=(list["packs"]?.AsArray()??[]).Skip(offset).Take(24).Select(p=>ExternalId(p!.ToString())).ToArray();using var gate=new SemaphoreSlim(4);
  return (await Task.WhenAll(ids.Select(async id=>{await gate.WaitAsync();try{return ExternalItem(source,await PackJson(FtbApi+id));}finally{gate.Release();}}))).ToList();
 }
 async Task<(JsonNode Project,JsonNode? Solder)> ExternalProject(string source,string id)
 {
  var project=await PackJson(source=="ftb"?FtbApi+ExternalId(id):TechnicApi+"modpack/"+ExternalId(id)+"?build=999");JsonNode? solder=null;
  if(source=="technic"&&project.Str("solder")!="")solder=await PackJson(SecureUrl(project.Str("solder")).TrimEnd('/')+"/modpack/"+ExternalId(id));
  return(project,solder);
 }
 async Task<JsonNode> ExternalVersion(string source,string id,JsonNode project,JsonNode? solder,string version)
 {
  if(source=="ftb"){
   var versions=project["versions"]?.AsArray()??throw new IOException("Нет выпусков FTB");var selected=(version==""?versions.Where(v=>v?["private"]?.GetValue<bool>()!=true).OrderByDescending(v=>Number(v,"updated")).FirstOrDefault():versions.FirstOrDefault(v=>v.Str("id")==version))??throw new IOException("Выпуск FTB не найден");return await PackJson(FtbApi+ExternalId(id)+"/"+ExternalId(selected.Str("id")));
  }
  if(solder!=null){var selected=version==""?solder.Str("recommended"):version;if(!(solder["builds"]?.AsArray().Any(v=>v?.ToString()==selected)??false))throw new IOException("Выпуск Technic не найден");return await PackJson(SecureUrl(project.Str("solder")).TrimEnd('/')+"/modpack/"+ExternalId(id)+"/"+ExternalId(selected));}
  if(version!=""&&version!=project.Str("version"))throw new IOException("Выпуск Technic изменился");return project;
 }
 async Task<object> ExternalDetails(string source,string id)
 {
  var (p,solder)=await ExternalProject(source,id);var versions=new List<object>();
  if(source=="ftb")foreach(var v in (p["versions"]?.AsArray()??[]).Where(v=>v?["private"]?.GetValue<bool>()!=true).OrderByDescending(v=>Number(v,"updated")).Take(100))versions.Add(new{id=v.Str("id"),name=v.Str("name"),version=v.Str("name"),date=Stamp(v,"updated"),game=v?["targets"]?.AsArray().Where(t=>t.Str("name")=="minecraft").Select(t=>t.Str("version")).ToArray()??[],loaders=v?["targets"]?.AsArray().Where(t=>t.Str("type")=="modloader").Select(t=>t.Str("name")).ToArray()??[]});
  else foreach(var build in solder?["builds"]?.AsArray().Select(b=>b!.ToString()).OrderByDescending(b=>b==solder.Str("recommended")).Take(100)??[p.Str("version")])versions.Add(new{id=build,name=build,version=build,date="",game=new[]{p.Str("minecraft")},loaders=Array.Empty<string>()});
  var ftb=source=="ftb";return new{source,id,title=ftb?p.Str("name"):p.Str("displayName"),summary=ftb?p.Str("synopsis"):p.Str("description"),description=p.Str("description"),format=ftb?"markdown":"html",icon=ftb?FtbArt(p):p["icon"].Str("url"),downloads=Number(p,"installs"),created=ftb?Stamp(p,"released"):"",updated=ftb?Stamp(p,"updated"):"",license="Условия автора модпака",url=ftb?"https://www.feed-the-beast.com/modpacks/"+ExternalId(id)+"-"+Q(p.Str("slug")):p.Str("platformUrl"),authors=ftb?(p["authors"]?.AsArray().Select(a=>new{name=a.Str("name"),role="Автор",url=""}).ToArray()??[]):new[]{new{name=p.Str("user"),role="Автор",url=""}},screenshots=ftb?(p["art"]?.AsArray().Where(a=>a.Str("type") is "screenshot" or "banner").Select(a=>new{url=a.Str("url"),caption=a.Str("title")}).ToArray()??[]):new[]{new{url=p["background"].Str("url"),caption=""}},categories=Array.Empty<string>(),versions};
 }
 async Task<object> ExternalContents(string source,string id,string version)
 {
  var (p,solder)=await ExternalProject(source,id);var v=await ExternalVersion(source,id,p,solder,version);var items=new List<object>();int mods=0,resources=0,shaders=0,unknown=0;
  if(source=="ftb")foreach(var f in v["files"]?.AsArray()??[]){if(f?["serveronly"]?.GetValue<bool>()==true)continue;var path=f.Str("path").Replace("\\","/").TrimStart('/');while(path.StartsWith("./"))path=path[2..];path=path.TrimEnd('/')+"/";var kind=path.StartsWith("mods/")?"mods":path.StartsWith("resourcepacks/")?"resources":path.StartsWith("shaderpacks/")?"shaders":"unknown";if(kind=="mods")mods++;else if(kind=="resources")resources++;else if(kind=="shaders")shaders++;else unknown++;items.Add(new{kind,name=f.Str("name"),file=path+f.Str("name"),version=f.Str("version"),icon="",origin="manifest",source,projectId="",optional=f?["optional"]?.GetValue<bool>()??false});}
  else foreach(var f in v["mods"]?.AsArray()??[]){unknown++;items.Add(new{kind="unknown",name=f.Str("name"),file=f.Str("name"),version=f.Str("version"),icon="",origin="manifest",source,projectId="",optional=false});}
  return new{name=source=="ftb"?p.Str("name"):p.Str("displayName"),items,counts=new{mods,resources,shaders,unknown},warnings=source=="technic"?new[]{"Technic распространяет архивы компонентов. Их внутренний состав проверяется при установке."}:Array.Empty<string>()};
 }
 async Task<Instance> InstallExternalPack(string source,string id,string version)
 {
  await installation.WaitAsync();Instance? instance=null;string temp="";
  try{
   var (project,solder)=await ExternalProject(source,id);var manifest=await ExternalVersion(source,id,project,solder,version);var ftb=source=="ftb";
   var minecraft=ftb?manifest["targets"]?.AsArray().FirstOrDefault(t=>t.Str("name")=="minecraft").Str("version")??"":manifest.Str("minecraft");
   if(minecraft=="")throw new IOException("В манифесте нет версии Minecraft");await game.Metadata(minecraft);
   instance=new Instance{Name=ftb?project.Str("name"):project.Str("displayName"),Version=minecraft,CreatedAt=DateTime.UtcNow,Settings=store.Config.Defaults with{},PackSource=source,PackId=id,PackVersion=ftb?manifest.Str("id"):version!=""?version:solder?.Str("recommended")??project.Str("version"),Icon=ftb?FtbArt(project):project["icon"].Str("url")};
   var minimumMemory=ftb?Number(manifest["specs"],"minimum"):Number(manifest,"memory");if(minimumMemory>131072)throw new IOException("Модпак требует неподдерживаемый объём памяти");if(minimumMemory>=256)instance.Settings.MaxRam=Math.Max(instance.Settings.MaxRam,(int)minimumMemory);
   var root=store.Folder(instance);temp=Path.Combine(store.Root,"downloads","external-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
   if(ftb){
    var loaders=manifest["targets"]?.AsArray().Where(t=>t.Str("type")=="modloader").ToArray()??[];if(loaders.Length>1)throw new IOException("Несколько загрузчиков FTB не поддерживаются");
    if(loaders.Length==1){instance.Loader=loaders[0].Str("name").ToLowerInvariant();instance.LoaderVersion=loaders[0].Str("version");if(instance.Loader is not ("forge" or "neoforge" or "fabric" or "quilt"))throw new IOException("Неизвестный загрузчик FTB");}
    var files=manifest["files"]?.AsArray()??throw new IOException("Нет списка файлов FTB");if(files.Count>10000)throw new IOException("Слишком много файлов FTB");long total=0;var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var downloads=new List<(string Url,string Path,string Hash,long Size,string Name)>();
    foreach(var f in files){if(f?["serveronly"]?.GetValue<bool>()==true)continue;var name=f.Str("name");if(name!=Path.GetFileName(name)||name.Contains('\\'))throw new IOException("Неверное имя файла FTB");var relative=f.Str("path").TrimStart('/').TrimEnd('/')+"/"+name;relative=relative.TrimStart('/');var dest=Store.SafePath(root,relative);if(!paths.Add(dest))throw new IOException("Повторяющийся путь FTB");var size=Number(f,"size");if(size<0||size>512L*1024*1024)throw new IOException("Файл FTB больше 512 МБ");total+=size;if(total>8L*1024*1024*1024)throw new IOException("Сборка больше 8 ГБ");var sha=f.Str("sha1");if(!Regex.IsMatch(sha,@"\A[a-fA-F0-9]{40}\z"))throw new IOException("Нет SHA-1 файла FTB: "+name);downloads.Add((SecureUrl(f.Str("url")),dest,sha,size,name));}
    using var downloadGate=new SemaphoreSlim(6);await Task.WhenAll(downloads.Select(async file=>{await downloadGate.WaitAsync();try{emit(new{type="progress",instanceId=instance.Id,message=file.Name,percent=0});await Net.Download(file.Url,file.Path,file.Hash,"SHA1",file.Size);}finally{downloadGate.Release();}}));

   }else{
    var archives=manifest["mods"]?.AsArray()??new JsonArray(new JsonObject{["name"]=id,["url"]=project.Str("url"),["md5"]=""});if(archives.Count>2000)throw new IOException("Слишком много компонентов Technic");long expanded=0,downloaded=0;int extractedCount=0;
    foreach(var component in archives){var url=SecureUrl(component.Str("url"));var path=Path.Combine(temp,Guid.NewGuid()+".zip");var md5=component.Str("md5");if(md5!=""&&!Regex.IsMatch(md5,@"\A[a-fA-F0-9]{32}\z"))throw new IOException("Неверная контрольная сумма Technic");emit(new{type="progress",instanceId=instance.Id,message=component.Str("name"),percent=0});await Net.Download(url,path,md5==""?null:md5,"MD5",512L*1024*1024);downloaded+=new FileInfo(path).Length;if(downloaded>8L*1024*1024*1024)throw new IOException("Загрузка Technic больше 8 ГБ");using(var zip=ZipFile.OpenRead(path)){foreach(var entry in zip.Entries){if(entry.FullName.EndsWith('/'))continue;expanded+=entry.Length;if(expanded>8L*1024*1024*1024||++extractedCount>20000)throw new IOException("Распакованная сборка Technic слишком большая");var dest=Store.SafePath(root,entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);entry.ExtractToFile(dest,true);}}File.Delete(path);}
    DetectTechnicLoader(instance,root);
   }
   store.Config.Instances.Add(instance);store.Save();return instance;
  }catch{if(instance!=null){store.Config.Instances.Remove(instance);var folder=Path.GetDirectoryName(store.Folder(instance))!;if(Directory.Exists(folder))Directory.Delete(folder,true);}throw;}
  finally{if(temp!=""&&Directory.Exists(temp))Directory.Delete(temp,true);installation.Release();}
 }
 internal static void DetectTechnicLoader(Instance instance,string root)
 {
  var jar=Path.Combine(root,"bin","modpack.jar");if(!File.Exists(jar))return;using var zip=ZipFile.OpenRead(jar);JsonNode? metadata=null;
  foreach(var name in new[]{"version.json","install_profile.json"}){var entry=zip.GetEntry(name);if(entry==null||entry.Length>2*1024*1024)continue;using var reader=new StreamReader(entry.Open());metadata=JsonNode.Parse(reader.ReadToEnd());if(metadata?["versionInfo"]!=null)metadata=metadata["versionInfo"];var libraries=metadata?["libraries"]?.AsArray()??[];var forge=libraries.FirstOrDefault(l=>l.Str("name").StartsWith("net.minecraftforge:forge:"));if(forge!=null){instance.Loader="forge";var version=forge.Str("name").Split(':')[2];instance.LoaderVersion=version.StartsWith(instance.Version+"-")?version[(instance.Version.Length+1)..]:version;return;}}
  throw new IOException("Эта сборка Technic использует старый или нестандартный modpack.jar. Его запуск пока не поддерживается; откройте сборку в Technic Launcher.");
 }
}
