using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
namespace Spectra;
public sealed partial class BedrockService
{
 public sealed record Release(string Id,string Version,string PackageVersion,string Name,bool Preview,bool Legacy,string Format,string UpdateId,string[] Urls);
 readonly SemaphoreSlim catalogGate=new(1,1);
 List<Release>? releases;
 DateTime catalogueTime;
 string catalogueWarning="";
 static string PackageVersion(string display){var v=Version.Parse(display);return $"{v.Major}.{v.Minor}.{v.Build*100+Math.Max(0,v.Revision)}.0";}
 static string MicrosoftUrl(string value)
 {
  if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.UserInfo!=""||uri.Scheme is not ("http" or "https")||uri.Port is not (80 or 443))throw new IOException("Неверный адрес пакета Microsoft");
  if(uri.Host is not ("tlu.dl.delivery.mp.microsoft.com" or "assets1.xboxlive.com" or "assets2.xboxlive.com"))throw new IOException("Пакеты Bedrock скачиваются только с серверов Microsoft");
  return new UriBuilder(uri){Scheme="https",Port=-1}.Uri.AbsoluteUri;
 }
 public async Task<List<Release>> Catalogue(bool refresh=false)
 {
  await catalogGate.WaitAsync();try{
   if(!refresh&&releases!=null&&DateTime.UtcNow-catalogueTime<TimeSpan.FromMinutes(30))return releases;
   var warnings=new List<string>();var folder=Path.Combine(store.Root,"bedrock");Directory.CreateDirectory(folder);
   async Task<JsonNode?> Database(string filename,string url){var path=Path.Combine(folder,filename);try{using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));var raw=await Net.Http.GetStringAsync(url,timeout.Token);if(raw.Length>8*1024*1024)throw new IOException("Каталог слишком большой");var node=JsonNode.Parse(raw)??throw new IOException("Пустой каталог");await File.WriteAllTextAsync(path+".part",raw);File.Move(path+".part",path,true);return node;}catch{warnings.Add("Источник "+filename+" недоступен; используется сохранённый список, если он есть.");try{return File.Exists(path)?JsonNode.Parse(await File.ReadAllTextAsync(path)):null;}catch{return null;}}}
   var uwpTask=Database("uwp-catalog.json","https://raw.githubusercontent.com/ddf8196/mc-w10-versiondb-auto-update/master/versions.json.min");var gdkTask=Database("gdk-catalog.json","https://raw.githubusercontent.com/LukasPAH/minecraft-windows-gdk-version-db/main/historical_versions.json");await Task.WhenAll(uwpTask,gdkTask);var rows=new List<Release>();
   if(await uwpTask is JsonArray uwp)foreach(var n in uwp.Take(10000)){
    if(n is not JsonArray a||a.Count<3||!Version.TryParse(a[0]?.ToString(),out var version)||version.Build<0||!Guid.TryParse(a[1]?.ToString(),out var guid)||!int.TryParse(a[2]?.ToString(),out var channel)||channel is <0 or >2)continue;
    var name=channel==2?"Microsoft.MinecraftWindowsBeta":"Microsoft.MinecraftUWP";var id="online:uwp:"+guid.ToString("N");rows.Add(new(id,version.ToString(),PackageVersion(version.ToString()),name,channel!=0,version.Major==0||version.Major==1&&version.Minor<2,"UWP",guid.ToString(),[]));
   }
   var gdk=await gdkTask;foreach(var channel in new[]{"releaseVersions","previewVersions"})foreach(var n in (gdk?[channel] as JsonArray??[]).Take(10000)){
    var label=Regex.Replace(n.Str("version"),@"^(Release|Preview)\s+","");if(!Version.TryParse(label,out _))continue;var urls=new List<string>();string? name=null,pv=null;
    foreach(var url in n?["urls"] as JsonArray??[]){try{var normalized=MicrosoftUrl(url?.ToString()??"");var match=Regex.Match(Uri.UnescapeDataString(new Uri(normalized).AbsolutePath),@"/(Microsoft\.Minecraft(?:UWP|WindowsBeta|Windows|WindowsPreview))_(\d+\.\d+\.\d+\.\d+)_x64__8wekyb3d8bbwe\.msixvc\z");if(!match.Success)continue;if(name!=null&&(name!=match.Groups[1].Value||pv!=match.Groups[2].Value))continue;name=match.Groups[1].Value;pv=match.Groups[2].Value;urls.Add(normalized);}catch{}}
    if(name==null||pv==null||urls.Count==0)continue;var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name+"_"+pv))).ToLowerInvariant();rows.Add(new("online:gdk:"+key,label,pv,name,channel=="previewVersions",false,"GDK","",urls.ToArray()));
   }
   releases=rows.DistinctBy(r=>r.Id).OrderByDescending(r=>Version.Parse(r.PackageVersion)).ToList();catalogueTime=DateTime.UtcNow;catalogueWarning=string.Join(" ",warnings);return releases;
  }finally{catalogGate.Release();}
 }
 public static string BuildUwpRequest(string updateId,DateTime now)
 {
  if(!Guid.TryParse(updateId,out _))throw new IOException("Неверный Microsoft Update ID");
  XNamespace soap="http://www.w3.org/2003/05/soap-envelope",address="http://www.w3.org/2005/08/addressing",service="http://www.microsoft.com/SoftwareDistribution/Server/ClientWebService",security="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd",utility="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd",authorization="http://schemas.microsoft.com/msus/2014/10/WindowsUpdateAuthorization";
  const string endpoint="https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx/secured";
  var xml=new XDocument(new XElement(soap+"Envelope",new XAttribute(XNamespace.Xmlns+"s",soap),new XAttribute(XNamespace.Xmlns+"a",address),new XElement(soap+"Header",new XElement(address+"Action",new XAttribute(soap+"mustUnderstand","1"),service.NamespaceName+"/GetExtendedUpdateInfo2"),new XElement(address+"MessageID","urn:uuid:"+Guid.NewGuid()),new XElement(address+"To",new XAttribute(soap+"mustUnderstand","1"),endpoint),new XElement(security+"Security",new XAttribute(soap+"mustUnderstand","1"),new XElement(utility+"Timestamp",new XElement(utility+"Created",now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")),new XElement(utility+"Expires",now.AddMinutes(5).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))),new XElement(authorization+"WindowsUpdateTicketsToken",new XAttribute(utility+"id","ClientMSA"),new XElement("TicketType",new XAttribute("Name","AAD"),new XAttribute("Version","1.0"),new XAttribute("Policy","MBI_SSL"))))),new XElement(soap+"Body",new XElement(service+"GetExtendedUpdateInfo2",new XElement(service+"updateIDs",new XElement(service+"UpdateIdentity",new XElement(service+"UpdateID",updateId),new XElement(service+"RevisionNumber","1"))),new XElement(service+"infoTypes",new XElement(service+"XmlUpdateFragmentType","FileUrl")),new XElement(service+"deviceAttributes","E:App=WU&OSArchitecture=AMD64&DeviceFamily=Windows.Desktop&OSVersion=10.0.19045.0&FlightRing=Retail")))));
  return xml.ToString();
 }
 async Task<string> ResolveUwp(Release release)
 {
  const string endpoint="https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx/secured";
  var payload=BuildUwpRequest(release.UpdateId,DateTime.UtcNow);
  using var request=new HttpRequestMessage(HttpMethod.Post,endpoint){Content=new StringContent(payload,Encoding.UTF8,"application/soap+xml")};using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));using var response=await Net.Http.SendAsync(request,timeout.Token);if(!response.IsSuccessStatusCode)throw new IOException("Microsoft не выдал ссылку для выбранной версии (HTTP "+(int)response.StatusCode+"). Попробуйте позже или выберите другой выпуск.");var body=await response.Content.ReadAsStringAsync(timeout.Token);
  using var reader=XmlReader.Create(new StringReader(body),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8*1024*1024});var document=XDocument.Load(reader);
  foreach(var item in document.Descendants().Where(e=>e.Name.LocalName=="Url")){try{var url=MicrosoftUrl(item.Value);if(new Uri(url).Host=="tlu.dl.delivery.mp.microsoft.com")return url;}catch{}}
  throw new IOException("Microsoft больше не выдаёт ссылку для этой версии или требует доступ Store. Выберите другую версию; импорт пакета остаётся доступен.");
 }
 async Task<Package?> InstallRelease(string id,List<Package> current)
 {
  var release=(await Catalogue()).FirstOrDefault(r=>r.Id==id)??throw new IOException("Версия отсутствует в каталоге Bedrock");var selected=current.FirstOrDefault(p=>p.Name==release.Name&&p.Version==release.PackageVersion);if(selected!=null)return selected;
  var folder=Path.Combine(store.Root,"bedrock","downloads");Directory.CreateDirectory(folder);var filename=id.Replace(':','-')+(release.Format=="GDK"?".msixvc":".appx");var path=Path.Combine(folder,filename);var receipt=path+".json";
  bool cached=false;try{var saved=JsonNode.Parse(await File.ReadAllTextAsync(receipt));cached=File.Exists(path)&&new FileInfo(path).Length>0&&saved.Str("sha256")==await Net.Hash(path,"SHA256");}catch{}
  if(!cached){var urls=release.Format=="GDK"?release.Urls:new[]{await ResolveUwp(release)};Exception? last=null;bool ready=false;
   foreach(var url in urls){try{await Net.Download(MicrosoftUrl(url),path,maxBytes:8L*1024*1024*1024);ready=true;break;}catch(Exception e){last=e;}}
   if(!ready)throw new IOException("Не удалось скачать выбранный пакет Bedrock с Microsoft. "+last?.Message,last);
   if(release.Format=="UWP"){
    try{var package=await Import(path,false);if(package.Name!=release.Name||package.Version!=release.PackageVersion)throw new IOException("Microsoft вернул другую версию; установка отменена");}catch{File.Delete(path);throw;}
   }
   await File.WriteAllTextAsync(receipt,JsonSerializer.Serialize(new{sha256=await Net.Hash(path,"SHA256"),release.Id,release.Version,release.PackageVersion},Store.Json));
  }
  Net.ProgressSink.Value?.Invoke(new{type="progress",message="Установка Bedrock "+release.Version,percent=100});selected=(await Bridge("install",path)).FirstOrDefault(p=>p.Name==release.Name&&p.Version==release.PackageVersion);
  if(selected==null)throw new IOException("Windows не зарегистрировал выбранную версию Bedrock. Убедитесь, что установлены Gaming Services и лицензия Minecraft доступна в Microsoft Store.");return selected;
 }
}
