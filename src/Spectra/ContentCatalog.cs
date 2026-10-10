using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Spectra;
/// <summary>API adapter for CurseForge and bounded public-page adapters for community sources.</summary>
public sealed class ContentCatalog(Store store,Func<Uri,Task<ContentCatalog.CatalogPage?>>? readBrowser=null)
{
 static readonly System.Collections.Concurrent.ConcurrentDictionary<string,(int Game,int Class)> CategoryCache=new();
 public sealed record CatalogPage(Uri Url,string Html);
 public record Item(string Id,string Title,string Description,string Icon,string Url);
 static readonly Regex Links=new("<a\\b[^>]*\\bhref\\s*=\\s*[\"'](?<url>[^\"']+)[\"'][^>]*>(?<body>.*?)</a>",RegexOptions.IgnoreCase|RegexOptions.Singleline,TimeSpan.FromSeconds(2));
 static readonly Regex Attributes=new("(?:href|src|data-src)\\s*=\\s*[\"'](?<url>[^\"']+)[\"']",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(2));
 static readonly Regex Tags=new("<[^>]+>",RegexOptions.Singleline,TimeSpan.FromSeconds(2));
 static string Text(string value)=>WebUtility.HtmlDecode(Tags.Replace(value," ")).Trim();
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
 static bool ProjectPath(string source,string kind,string path)=>source switch{
  "planetminecraft"=>Regex.IsMatch(path,kind=="skins"?@"^/skin/[^/]+/":kind=="worlds"?@"^/project/[^/]+/":kind=="addons"?@"^/mod/[^/]+/":@"^/texture-pack/[^/]+/"),
  "namemc"=>Regex.IsMatch(path,@"^/skin/[a-fA-F0-9]+$"),
  "skindex"=>Regex.IsMatch(path,@"^/skin/\d+/"),
  "novaskin"=>Regex.IsMatch(path,@"^/skin/\d+"),
  "mcpedl"=>path.Count(c=>c=='/')==2&&!new[]{"/category/","/tag/","/page/"}.Any(path.StartsWith)&&path!="/",
  "minecraftmaps"=>path.StartsWith("/maps/")&&path.Count(c=>c=='/')>=3,
  "minecraftinside"=>Regex.IsMatch(path,@"^/maps/\d+[^/]*\.html$"),_=>false};
 async Task<string> Page(Uri uri)
 {
  using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.UserAgent.ParseAdd("Spectra/0.16.0");
  using var response=await Net.Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead);response.EnsureSuccessStatusCode();
  await using var stream=await response.Content.ReadAsStreamAsync();using var output=new MemoryStream();var buffer=new byte[8192];int count;
  while((count=await stream.ReadAsync(buffer))>0){if(output.Length+count>4*1024*1024)throw new IOException("Страница каталога слишком большая");output.Write(buffer,0,count);}
  return System.Text.Encoding.UTF8.GetString(output.ToArray());
 }
 public async Task<object> Search(JsonNode data,bool browser=false)
 {
  var source=Source(data);var offset=Math.Max(0,data["offset"]?.GetValue<int>()??0);if(offset>2400)throw new IOException("Слишком большая страница");
  try{
   if(source.Id=="curseforge"){
    var category=await CurseCategory(data.Str("edition"),data.Str("kind"));
    var result=await Curse($"mods/search?gameId={category.Game}&classId={category.Class}&pageSize=24&index={offset}&sortField=2&sortOrder=desc&searchFilter="+Uri.EscapeDataString(data.Str("query")));
    var items=(result["data"]?.AsArray()??[]).Select(x=>new Item(x.Str("id"),x.Str("name"),x.Str("summary"),x?["logo"].Str("thumbnailUrl")??"",x?["links"].Str("websiteUrl")??source.Url)).ToArray();
    return new{items,hasMore=items.Length==24,url=source.Url,warning=""};
   }
   var uri=new Uri(source.Url);var page=offset/24+1;
   if(page>1){var builder=new UriBuilder(uri);builder.Query=builder.Query.TrimStart('?')+"&page="+page;uri=builder.Uri;}
   string html;
   if(browser&&readBrowser!=null){var captured=await readBrowser(uri);if(captured==null)return new{items=Array.Empty<Item>(),hasMore=false,url=source.Url,warning="Каталог закрыт. Повторите открытие при необходимости.",browserRequired=true};uri=captured.Url;html=captured.Html;}
   else html=await Page(uri);
   var seen=new HashSet<string>();var entries=new List<Item>();
   foreach(Match match in Links.Matches(html)){
    if(!Uri.TryCreate(uri,WebUtility.HtmlDecode(match.Groups["url"].Value),out var link)||link.Scheme!="https"||link.Host!=uri.Host||!ProjectPath(source.Id,data.Str("kind"),link.AbsolutePath))continue;
    if(!seen.Add(link.GetLeftPart(UriPartial.Path)))continue;var title=Text(match.Groups["body"].Value);if(title.Length<2)title=Uri.UnescapeDataString(link.Segments.LastOrDefault(x=>x!="/")??"Minecraft").Trim('/').Replace('-',' ');
    if(title.Length>180)title=title[..180];var image=Attributes.Matches(match.Groups["body"].Value).Select(m=>WebUtility.HtmlDecode(m.Groups["url"].Value)).FirstOrDefault(v=>v.Contains(".png")||v.Contains(".jpg")||v.Contains(".webp"));
    var icon=image!=null&&Uri.TryCreate(uri,image,out var imageUri)&&imageUri.Scheme=="https"?imageUri.AbsoluteUri:"";
    entries.Add(new Item(link.AbsoluteUri,title,"",icon,link.AbsoluteUri));if(entries.Count>=24)break;
   }
   return new{items=entries,hasMore=entries.Count==24,url=source.Url,warning=entries.Count==0?"На этой странице не найден список проектов. Откройте список в браузере Spectra и нажмите «Показать список».":"",browserRequired=entries.Count==0};
  }catch(HttpRequestException e) when(e.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests){return new{items=Array.Empty<Item>(),hasMore=false,url=source.Url,warning="Сайт требует открытие в браузере. Нажмите «Открыть в Spectra», завершите проверку сайта и нажмите «Показать список».",browserRequired=true};}
  catch(Exception e) when(e is IOException or HttpRequestException or TaskCanceledException){return new{items=Array.Empty<Item>(),hasMore=false,url=source.Url,warning="Каталог не ответил. Можно открыть его в Spectra или в обычном браузере.",browserRequired=true};}
 }
 static bool AllowedDownload(string source,Uri uri)
 {
  if(uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||!uri.IsDefaultPort)return false;
  string[] hosts=source switch{"curseforge"=>["forgecdn.net"],"planetminecraft"=>["planetminecraft.com"],"namemc"=>["namemc.com"],"skindex"=>["minecraftskins.com"],"novaskin"=>["novaskin.me"],"mcpedl"=>["mcpedl.com"],"minecraftmaps"=>["minecraftmaps.com"],"minecraftinside"=>["minecraft-inside.ru"],_=>[]};
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
  }else{
   if(!Uri.TryCreate(data.Str("url"),UriKind.Absolute,out var project)||project.Scheme!="https"||project.Host!=new Uri(source.Url).Host||!ProjectPath(source.Id,kind,project.AbsolutePath))throw new IOException("Неверная страница проекта");
   displayName=Uri.UnescapeDataString(project.Segments.LastOrDefault(x=>x!="/")??"Minecraft").Trim('/').Replace('-',' ');var html=await Page(project);var candidates=Attributes.Matches(html).Select(m=>WebUtility.HtmlDecode(m.Groups["url"].Value)).Select(value=>Uri.TryCreate(project,value,out var link)?link:null).Where(link=>link!=null&&AllowedDownload(source.Id,link)&&ValidExtension(Path.GetExtension(link.AbsolutePath).ToLowerInvariant(),data.Str("edition"),kind));
   if(kind=="skins")candidates=candidates.Where(link=>!link!.Host.StartsWith("render.")&&(link.AbsolutePath.Contains("/skins/")||link.AbsolutePath.Contains("/skin/")||link.AbsolutePath.Contains("/texture/")||link.AbsolutePath.Contains("resource_media/")));
   url=candidates.FirstOrDefault()?.AbsoluteUri??"";
   if(url=="")return new{status="manualDownload",url=project.AbsoluteUri,message="Для загрузки на этом сайте требуется действие в браузере. Скачайте файл и нажмите «Импорт скачанного файла»."};
  }
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
