using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
namespace Spectra;
/// <summary>CurseForge content catalogue and verified downloads.</summary>
public sealed class ContentCatalog(Store store)
{
 static readonly System.Collections.Concurrent.ConcurrentDictionary<string,(int Game,int Class)> CategoryCache=new();
 public record Item(string Id,string Title,string Description,string Icon,string Url);
 async Task<JsonNode> Curse(string path)
 {
  if(string.IsNullOrWhiteSpace(store.Config.CurseForgeKey))throw new IOException("Для CurseForge требуется действующий API-ключ");
  using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.curseforge.com/v1/"+path);request.Headers.Add("x-api-key",store.Config.CurseForgeKey);return await Net.Send(request);
 }
 async Task<(int Game,int Class)> CurseCategory(string edition,string kind)
 {
  if(CategoryCache.TryGetValue(edition+":"+kind,out var cached))return cached;
  int game=432;
  if(edition=="bedrock"){
   game=0;for(int offset=0;offset<1000&&game==0;offset+=50){var list=(await Curse("games?pageSize=50&index="+offset))["data"]?.AsArray()??[];foreach(var row in list){if(row.Str("slug")=="minecraft-bedrock"){game=row!["id"]!.GetValue<int>();break;}}if(list.Count<50)break;}
   if(game==0)throw new IOException("CurseForge API не вернул каталог Minecraft Bedrock");
  }
  var categories=(await Curse("categories?gameId="+game))["data"]?.AsArray()??[];
  string[] slugs=kind switch{"worlds"=>["worlds","maps"],"addons"=>["addons","add-ons"],"resources"=>["texture-packs","resource-packs"],"skins"=>["skins","skin-packs"],_=>throw new IOException("Неизвестная категория")};
  var category=categories.FirstOrDefault(x=>slugs.Contains(x.Str("slug"))&&x?["isClass"]?.GetValue<bool>()==true)??categories.FirstOrDefault(x=>slugs.Contains(x.Str("slug")))??throw new IOException("Категория не опубликована в CurseForge API");
  var result=(game,category!["id"]!.GetValue<int>());CategoryCache[edition+":"+kind]=result;return result;
 }
 static ContentService.ContentSource Source(JsonNode data)=>ContentService.Sources(data.Str("edition"),data.Str("kind"),data.Str("query")).FirstOrDefault(x=>x.Id==data.Str("source"))??throw new IOException("Источник недоступен в этой категории");
 public async Task<object> Search(JsonNode data)
 {
  var source=Source(data);var offset=Math.Max(0,data["offset"]?.GetValue<int>()??0);if(offset>2400)throw new IOException("Слишком большая страница");
  try{
   if(source.Id=="curseforge"){
    var category=await CurseCategory(data.Str("edition"),data.Str("kind"));
    var result=await Curse($"mods/search?gameId={category.Game}&classId={category.Class}&pageSize=24&index={offset}&sortField=2&sortOrder=desc&searchFilter="+Uri.EscapeDataString(data.Str("query")));
    var items=(result["data"]?.AsArray()??[]).Select(x=>new Item(x.Str("id"),x.Str("name"),x.Str("summary"),x?["logo"].Str("thumbnailUrl")??"",x?["links"].Str("websiteUrl")??source.Url)).ToArray();
    return new{items,hasMore=items.Length==24,url=source.Url,warning=""};
   }
   throw new IOException("Источник каталога недоступен");
  }catch(Exception e) when(e is IOException or HttpRequestException or TaskCanceledException){return new{items=Array.Empty<Item>(),hasMore=false,url=source.Url,warning=e is IOException?e.Message:"Каталог недоступен. Повторите запрос позже."};}

 }
 static bool AllowedDownload(string source,Uri uri)
 {
  if(uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||!uri.IsDefaultPort)return false;
  string[] hosts=source=="curseforge"?["forgecdn.net"]:[];
  return hosts.Any(h=>uri.Host==h||uri.Host.EndsWith("."+h,StringComparison.OrdinalIgnoreCase));
 }
 public async Task<object> Download(JsonNode data,ContentService content)
 {
  var source=Source(data);var kind=data.Str("kind");string url="",hash="",algorithm="SHA1",displayName="";
  if(source.Id=="curseforge"){
   var category=await CurseCategory(data.Str("edition"),kind);if(!long.TryParse(data.Str("projectId"),out var projectId)||projectId<=0)throw new IOException("Неверный проект");
   var project=(await Curse("mods/"+projectId))["data"]??throw new IOException("Проект не найден");if(project["gameId"]!.GetValue<int>()!=category.Game||project["classId"]!.GetValue<int>()!=category.Class)throw new IOException("Издание или категория проекта не совпадает");
   var files=(await Curse("mods/"+projectId+"/files?pageSize=50"))["data"]?.AsArray()??[];
   var file=files.Where(f=>f?["isAvailable"]?.GetValue<bool>()!=false).OrderByDescending(f=>f.Str("fileDate")).FirstOrDefault(f=>ValidExtension(Path.GetExtension(f.Str("fileName")).ToLowerInvariant(),data.Str("edition"),kind));
   if(file!=null){displayName=project.Str("name");url=file.Str("downloadUrl");hash=(file["hashes"]?.AsArray()??[]).FirstOrDefault(x=>x?["algo"]?.GetValue<int>()==1).Str("value");}
   if(url=="")return new{status="manualDownload",url=project["links"].Str("websiteUrl"),message="Автор или каталог не предоставляет прямую загрузку подходящего файла. Скачайте его на CurseForge и импортируйте в Spectra."};
  }else throw new IOException("Источник каталога недоступен");

  if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||!AllowedDownload(source.Id,uri))throw new IOException("Неизвестный сервер загрузки");
  var ext=Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();if(!ValidExtension(ext,data.Str("edition"),kind))throw new IOException("Неподдерживаемый файл");
  var temp=Path.Combine(store.Root,"content-downloads",Guid.NewGuid().ToString("N")+ext);Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
  try{
   using var response=await Net.Http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead);response.EnsureSuccessStatusCode();
   if(response.RequestMessage?.RequestUri is not Uri final||!AllowedDownload(source.Id,final))throw new IOException("Сайт перенаправил загрузку на неизвестный сервер");
   long limit=kind=="skins"?1024*1024:1024L*1024*1024;
   await using(var input=await response.Content.ReadAsStreamAsync()){await using var output=File.Create(temp);var buffer=new byte[81920];int count;long done=0,last=0;while((count=await input.ReadAsync(buffer))>0){done+=count;if(done>limit)throw new IOException("Файл слишком большой");await output.WriteAsync(buffer.AsMemory(0,count));if(Environment.TickCount64-last>150){last=Environment.TickCount64;Net.ProgressSink.Value?.Invoke(new{type="progress",message="Скачивание контента",downloadedBytes=done,totalBytes=response.Content.Headers.ContentLength,percent=response.Content.Headers.ContentLength is long size&&size>0?done*100d/size:0});}}}
   if(hash!=""&&!string.Equals(await Net.Hash(temp,algorithm),hash,StringComparison.OrdinalIgnoreCase))throw new IOException("Контрольная сумма не совпадает");
   return await content.InstallFile(temp,data.Str("edition"),kind,data.Str("instanceId"),data.Str("version"),data.Str("variant"),displayName);
  }finally{if(File.Exists(temp))File.Delete(temp);}
 }
 static bool ValidExtension(string extension,string edition,string kind)=>kind=="skins"?extension==".png":edition=="java"?kind=="worlds"&&extension==".zip":kind=="worlds"?extension==".mcworld":kind=="addons"?extension is ".mcaddon" or ".mcpack":extension==".mcpack";
}
