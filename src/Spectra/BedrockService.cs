using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
namespace Spectra;
public sealed class BedrockService(Store store)
{
 readonly SemaphoreSlim gate=new(1,1);
 JsonNode? updates;
 public sealed record Package(string Id,string Name,string Version,string Family,string AppId,bool Preview,bool Legacy,bool Installed);
 static bool MinecraftIdentity(string name)=>name is "Microsoft.MinecraftUWP" or "Microsoft.MinecraftWindowsBeta" or "Microsoft.MinecraftWindows" or "Microsoft.MinecraftWindowsPreview";
 async Task<List<Package>> Bridge(string action,string? package=null)
 {
  var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
  foreach(var arg in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",Path.Combine(AppContext.BaseDirectory,"BedrockBridge.ps1"),"-Action",action})info.ArgumentList.Add(arg);
  if(package!=null)info.Environment["SPECTRA_BEDROCK_PACKAGE"]=package;
  using var process=Process.Start(info)??throw new IOException("Не удалось проверить Bedrock в Windows");var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(action=="install"?10:1));
  try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){try{process.Kill(true);}catch{}throw new IOException("Проверка или установка Bedrock превысила время ожидания");}
  var raw=await output;var message=await errors;if(process.ExitCode!=0)throw new IOException("Windows: "+message.Trim());
  return (JsonSerializer.Deserialize<List<Package>>(raw,Store.Json)??[]).Where(p=>MinecraftIdentity(p.Name)).ToList();
 }
 public async Task<object> Versions()
 {
  var installed=await Bridge("list");var imported=new List<Package>();var folder=Path.Combine(store.Root,"bedrock","packages");
  if(Directory.Exists(folder))foreach(var path in Directory.EnumerateFiles(folder,"*.json")){var p=JsonSerializer.Deserialize<Package>(await File.ReadAllTextAsync(path),Store.Json);if(p!=null&&MinecraftIdentity(p.Name))imported.Add(p);}
  string warning="";try{updates??=await Net.Get("https://launchercontent.mojang.com/v2/bedrockPatchNotes.json");}catch{warning="Каталог Mojang недоступен; установленные и импортированные версии доступны.";}
  return new{installed,imported,updates=updates?["entries"]?.DeepClone()??new JsonArray(),warning};
 }
 public async Task<Package> Import(string source)
 {
  var ext=Path.GetExtension(source).ToLowerInvariant();if(ext is not (".appx" or ".msix"))throw new IOException("Выберите отдельный APPX/MSIX-пакет Minecraft, не bundle");if(new FileInfo(source).Length>4L*1024*1024*1024)throw new IOException("Пакет превышает 4 ГиБ");
  using var zip=ZipFile.OpenRead(source);var entry=zip.GetEntry("AppxManifest.xml")??throw new IOException("В пакете нет AppxManifest.xml");if(entry.Length>1024*1024)throw new IOException("Манифест слишком большой");
  using var stream=entry.Open();using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024});var manifest=XDocument.Load(reader);var identity=manifest.Root?.Elements().FirstOrDefault(e=>e.Name.LocalName=="Identity")??throw new IOException("Нет идентификатора Minecraft");
  var name=identity.Attribute("Name")?.Value??"";var version=identity.Attribute("Version")?.Value??"";var publisher=identity.Attribute("Publisher")?.Value??"";
  if(!MinecraftIdentity(name)||!publisher.Contains("CN=Microsoft Corporation",StringComparison.Ordinal)||!Version.TryParse(version,out var parsed))throw new IOException("Это не пакет Minecraft Microsoft");
  var app=manifest.Descendants().FirstOrDefault(e=>e.Name.LocalName=="Application")?.Attribute("Id")?.Value??"";if(app.Length==0)throw new IOException("Пакет не содержит приложение");
  var hash=await Net.Hash(source,"SHA256");var folder=Path.Combine(store.Root,"bedrock","packages");Directory.CreateDirectory(folder);var destination=Path.Combine(folder,hash+ext);
  if(!File.Exists(destination)){await using var input=File.OpenRead(source);await using var output=File.Create(destination+".part");await input.CopyToAsync(output);}
  if(File.Exists(destination+".part"))File.Move(destination+".part",destination,true);
  var item=new Package("import:"+hash+ext,name,version,"",app,name.Contains("Beta")||name.Contains("Preview"),parsed.Major==0||parsed.Major==1&&parsed.Minor<2,false);await File.WriteAllTextAsync(Path.Combine(folder,hash+ext+".json"),JsonSerializer.Serialize(item,Store.Json));return item;
 }
 public async Task Launch(string id,bool install=false)
 {
  await gate.WaitAsync();try{
   Package? selected;
   if(id.StartsWith("import:",StringComparison.Ordinal)){
    var file=id[7..];if(!System.Text.RegularExpressions.Regex.IsMatch(file,@"\A[a-f0-9]{64}\.(appx|msix)\z"))throw new IOException("Неверный пакет Bedrock");var folder=Path.Combine(store.Root,"bedrock","packages");var item=JsonSerializer.Deserialize<Package>(await File.ReadAllTextAsync(Path.Combine(folder,file+".json")),Store.Json)??throw new IOException("Пакет не найден");var current=await Bridge("list");selected=current.FirstOrDefault(p=>p.Name==item.Name&&p.Version==item.Version);
    if(selected==null){if(!install)throw new IOException("Сначала установите выбранный пакет Bedrock");var path=Path.Combine(folder,file);if(await Net.Hash(path,"SHA256")!=file[..64])throw new IOException("Пакет Bedrock изменился");selected=(await Bridge("install",path)).FirstOrDefault(p=>p.Name==item.Name&&p.Version==item.Version);}
   }else selected=(await Bridge("list")).FirstOrDefault(p=>p.Id==id);
   if(selected==null)throw new IOException("Выбранная версия не установлена. Обновите список Bedrock.");
   if(!System.Text.RegularExpressions.Regex.IsMatch(selected.Family,@"\A[A-Za-z0-9_.-]+\z")||!System.Text.RegularExpressions.Regex.IsMatch(selected.AppId,@"\A[A-Za-z0-9_.-]+\z"))throw new IOException("Неверный идентификатор приложения Windows");
   var info=new ProcessStartInfo("explorer.exe"){UseShellExecute=true};info.ArgumentList.Add("shell:AppsFolder\\"+selected.Family+"!"+selected.AppId);Process.Start(info);
  }finally{gate.Release();}
 }
 public static void OpenStore()=>Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?ProductId=9NBLGGH2JHXJ"){UseShellExecute=true});
}
