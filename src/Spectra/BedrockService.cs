using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
namespace Spectra;
public sealed partial class BedrockService(Store store)
{
 readonly SemaphoreSlim gate=new(1,1);
 JsonNode? updates;
 public sealed record Package(string Id,string Name,string Version,string Family,string AppId,bool Preview,bool Legacy,bool Installed);
 internal static string? CanonicalIdentity(string name)=>new[]{"Microsoft.MinecraftUWP","Microsoft.MinecraftWindowsBeta","Microsoft.MinecraftWindows","Microsoft.MinecraftWindowsPreview"}.FirstOrDefault(n=>n.Equals(name,StringComparison.OrdinalIgnoreCase));
 static bool MinecraftIdentity(string name)=>CanonicalIdentity(name)!=null;
 async Task<List<Package>> Bridge(string action,string? package=null,string? expectedName=null,string? expectedVersion=null)
 {
  var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
  foreach(var arg in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",Path.Combine(AppContext.BaseDirectory,"BedrockBridge.ps1"),"-Action",action})info.ArgumentList.Add(arg);
  if(package!=null)info.Environment["SPECTRA_BEDROCK_PACKAGE"]=package;
  if(expectedName!=null)info.Environment["SPECTRA_BEDROCK_NAME"]=expectedName;
  if(expectedVersion!=null)info.Environment["SPECTRA_BEDROCK_VERSION"]=expectedVersion;
  using var process=Process.Start(info)??throw new IOException("Не удалось проверить Bedrock в Windows");var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(action=="storeUpdate"?30:action is "install" or "replace" or "register"?10:1));
  try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){try{process.Kill(true);}catch{}throw new IOException("Проверка или установка Bedrock превысила время ожидания");}
  var raw=await output;var message=await errors;if(process.ExitCode!=0)throw new IOException("Windows: "+message.Trim()+(message.Contains("Developer Mode")?" — Включите режим разработчика: Параметры → Конфиденциальность и защита → Для разработчиков.":""));
  return (JsonSerializer.Deserialize<List<Package>>(raw,Store.Json)??[]).Where(p=>MinecraftIdentity(p.Name)).Select(p=>p with{Name=CanonicalIdentity(p.Name)!}).ToList();
 }
 public async Task<object> Versions(bool refresh=false)
 {
  var installed=await Bridge("list");var available=await Catalogue(refresh);var imported=new List<Package>();var folder=Path.Combine(store.Root,"bedrock","packages");
  if(Directory.Exists(folder))foreach(var path in Directory.EnumerateFiles(folder,"*.json")){var p=JsonSerializer.Deserialize<Package>(await File.ReadAllTextAsync(path),Store.Json);if(p!=null&&MinecraftIdentity(p.Name))imported.Add(p);}
  string warning="";try{updates??=await Net.Get("https://launchercontent.mojang.com/v2/bedrockPatchNotes.json");}catch{warning="Каталог Mojang недоступен; установленные и импортированные версии доступны.";}
  warning=string.Join(" ",new[]{warning,catalogueWarning}.Where(x=>x!=""));return new{installed,imported,available,latestStatus=LatestStatus(installed,available),updates=updates?["entries"]?.DeepClone()??new JsonArray(),warning};
 }
 public async Task<Package> Import(string source,bool cache=true)
 {
  var ext=Path.GetExtension(source).ToLowerInvariant();if(ext is not (".appx" or ".msix"))throw new IOException("Выберите отдельный APPX/MSIX-пакет Minecraft, не bundle");if(new FileInfo(source).Length>4L*1024*1024*1024)throw new IOException("Пакет превышает 4 ГиБ");
  using var zip=ZipFile.OpenRead(source);var entry=zip.GetEntry("AppxManifest.xml")??throw new IOException("В пакете нет AppxManifest.xml");if(entry.Length>1024*1024)throw new IOException("Манифест слишком большой");
  using var stream=entry.Open();using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024});var manifest=XDocument.Load(reader);var identity=manifest.Root?.Elements().FirstOrDefault(e=>e.Name.LocalName=="Identity")??throw new IOException("Нет идентификатора Minecraft");
  var name=identity.Attribute("Name")?.Value??"";var version=identity.Attribute("Version")?.Value??"";var publisher=identity.Attribute("Publisher")?.Value??"";
  if(!MinecraftIdentity(name)||!publisher.Contains("CN=Microsoft Corporation",StringComparison.Ordinal)||!Version.TryParse(version,out var parsed))throw new IOException("Это не пакет Minecraft Microsoft");
  var app=manifest.Descendants().FirstOrDefault(e=>e.Name.LocalName=="Application")?.Attribute("Id")?.Value??"";if(app.Length==0)throw new IOException("Пакет не содержит приложение");
  if(!cache)return new Package("",name,version,"",app,name.Contains("Beta")||name.Contains("Preview"),parsed.Major==0||parsed.Major==1&&parsed.Minor<2,false);
  var hash=await Net.Hash(source,"SHA256");var folder=Path.Combine(store.Root,"bedrock","packages");Directory.CreateDirectory(folder);var destination=Path.Combine(folder,hash+ext);
  if(!File.Exists(destination)){await using var input=File.OpenRead(source);await using var output=File.Create(destination+".part");await input.CopyToAsync(output);}
  if(File.Exists(destination+".part"))File.Move(destination+".part",destination,true);
  var item=new Package("import:"+hash+ext,name,version,"",app,name.Contains("Beta")||name.Contains("Preview"),parsed.Major==0||parsed.Major==1&&parsed.Minor<2,false);await File.WriteAllTextAsync(Path.Combine(folder,hash+ext+".json"),JsonSerializer.Serialize(item,Store.Json));return item;
 }
 public async Task<object> Launch(string id,bool install=false,bool skipUpdate=false,bool replace=false,bool installOnly=false)
 {
  await gate.WaitAsync();try{
   Package? selected;
   if(id=="latest"){
    var current=await Bridge("list");selected=LatestAnyInstalled(current);var status=LatestStatus(current,await Catalogue());
    if(status.UpdateAvailable&&!skipUpdate){await OpenOfficialLauncher();return new{status="officialLauncher",message="Скачайте обновление через Minecraft Launcher. Выберите Minecraft for Windows."};}
    if(selected==null){
     if(!install)return new{status="needsInstall",message="Minecraft не найден. Установить через Microsoft Store в фоне? Нужны WinGet и аккаунт Store с лицензией игры."};
     var installResult=await InstallLatest();if(!installResult.Success)return new{status="installerUnavailable",message=installResult.Message,code=installResult.Code};selected=LatestInstalled(await Bridge("list"));
     if(selected==null)throw new IOException("Store сообщил об установке, но Minecraft не найден. Подробности: bedrock/last-store-install.log");
    }
   }else if(id.StartsWith("online:",StringComparison.Ordinal)){
    if(!install)throw new IOException("Подтвердите установку версии Bedrock");selected=await InstallRelease(id,await Bridge("list"),replace);
   }else if(id.StartsWith("import:",StringComparison.Ordinal)){
    var file=id[7..];if(!System.Text.RegularExpressions.Regex.IsMatch(file,@"\A[a-f0-9]{64}\.(appx|msix)\z"))throw new IOException("Неверный пакет Bedrock");var folder=Path.Combine(store.Root,"bedrock","packages");var item=JsonSerializer.Deserialize<Package>(await File.ReadAllTextAsync(Path.Combine(folder,file+".json")),Store.Json)??throw new IOException("Пакет не найден");var current=await Bridge("list");selected=current.FirstOrDefault(p=>p.Name==item.Name&&p.Version==item.Version);
    if(selected==null){if(WouldReplace(item.Name,item.Version,current)&&!replace)throw new IOException("Этот пакет заменит установленную игру. Установка отменена; пакет сохранён отдельно.");if(!install)throw new IOException("Сначала установите выбранный пакет Bedrock");var path=Path.Combine(folder,file);if(await Net.Hash(path,"SHA256")!=file[..64])throw new IOException("Пакет Bedrock изменился");var manifest=replace?await ExtractPackage(path,item.Name,item.Version):"";selected=(replace?await ReplaceWithBackup(current,item.Name,()=>Bridge("register",manifest,item.Name,item.Version)):await Bridge("install",path,item.Name,item.Version)).FirstOrDefault(p=>p.Name==item.Name&&p.Version==item.Version);}
   }else selected=(await Bridge("list")).FirstOrDefault(p=>p.Id==id);
   if(selected==null)throw new IOException("Выбранная версия не установлена. Обновите список Bedrock.");
   if(installOnly)return new{status="installed",version=selected.Version,message="Версия установлена. Нажмите ИГРАТЬ для запуска."};
   if(!System.Text.RegularExpressions.Regex.IsMatch(selected.Family,@"\A[A-Za-z0-9_.-]+\z")||!System.Text.RegularExpressions.Regex.IsMatch(selected.AppId,@"\A[A-Za-z0-9_.-]+\z"))throw new IOException("Неверный идентификатор приложения Windows");
   var info=new ProcessStartInfo("explorer.exe"){UseShellExecute=true};info.ArgumentList.Add("shell:AppsFolder\\"+selected.Family+"!"+selected.AppId);Process.Start(info);if(id=="latest")await ContentService.OpenBedrockFiles(store);return new{status="launched",version=selected.Version,message="Запуск передан Windows; права на игру проверяются Minecraft / Microsoft Store."};
  }finally{gate.Release();}
 }
 public async Task DeleteVersion(string id)
 {
  await gate.WaitAsync();try{
   var current=await Bridge("list");var installed=current.FirstOrDefault(p=>p.Id==id);
   if(installed==null&&id.StartsWith("online:",StringComparison.Ordinal)){var release=(await Catalogue()).FirstOrDefault(r=>r.Id==id);if(release!=null)installed=current.FirstOrDefault(p=>p.Name==release.Name&&p.Version==release.PackageVersion);}
   if(installed==null&&id.StartsWith("import:",StringComparison.Ordinal)){
    var file=id[7..];if(!System.Text.RegularExpressions.Regex.IsMatch(file,@"\A[a-f0-9]{64}\.(appx|msix)\z"))throw new IOException("Неверный пакет");
    var path=Path.Combine(store.Root,"bedrock","packages",file);var item=JsonSerializer.Deserialize<Package>(await File.ReadAllTextAsync(path+".json"),Store.Json);
    installed=current.FirstOrDefault(p=>p.Name==item?.Name&&p.Version==item.Version);
    if(installed==null){File.Delete(path);File.Delete(path+".json");return;}
   }
   if(installed==null)throw new IOException("Установленная версия не найдена");
   if(Process.GetProcessesByName("Minecraft.Windows").Length>0)throw new IOException("Закройте Bedrock перед удалением");
   ContentService.BackupInstalledData(store);
   await Bridge("removeVersion",installed.Id,installed.Name,installed.Version);
   if(store.Config.SelectedBedrock==id){store.Config.SelectedBedrock="";store.Config.SelectedBedrockLabel="";store.Save();}
  }finally{gate.Release();}
 }
 public async Task<object> Unregister()
 {
  await gate.WaitAsync();try{await Bridge("unregister");return new{status="unregistered",message="Версии, зарегистрированные Spectra, сняты с регистрации. Файлы остались в bedrock/versions. Чтобы вернуть основную копию игры, установите Minecraft из Microsoft Store или официального лаунчера."};}finally{gate.Release();}
 }
 internal static string[] LatestInstallArguments(bool upgrade=false)=>[upgrade?"upgrade":"install","--id","9NBLGGH2JHXJ","--exact","--source","msstore","--silent","--accept-package-agreements","--accept-source-agreements","--disable-interactivity"];
 internal sealed record StoreOperation(bool Success,string Code,string Message);
 internal static StoreOperation StoreResult(int exitCode)
 {
  var code="0x"+exitCode.ToString("X8");return unchecked((uint)exitCode) switch {
   0=>new(true,code,""),
   0x8A150014=>new(false,code,"WinGet не нашёл Minecraft в выбранном источнике или среди сопоставленных установленных приложений ("+code+"). Этот код не означает отсутствие лицензии. Обновите игру через официальный Minecraft Launcher. Подробности: bedrock/last-store-install.log"),
   0x8A15002B=>new(false,code,"WinGet не нашёл применимого обновления ("+code+"). Каталог Spectra не гарантирует доступность обновления для вашего аккаунта. Можно запустить установленную игру или проверить обновление в официальном лаунчере. Подробности: bedrock/last-store-install.log"),
   _=>new(false,code,"Установка через Microsoft Store / WinGet не завершена ("+code+"). Причина записана в bedrock/last-store-install.log; по одному этому коду Spectra не делает вывод об отсутствии лицензии.")};
 }
 public async Task OpenOfficialLauncher(){await Bridge("officialLauncher");}
 async Task<StoreOperation> InstallLatest(bool upgrade=false)
 {
  var result=await InstallLatestOnce(upgrade);
  if(upgrade&&result.Code=="0x8A150014"){
   Net.ProgressSink.Value?.Invoke(new{type="progress",message="WinGet не нашёл Minecraft; обновление через Microsoft Store…",percent=0,indeterminate=true,scope="store"});
   try{await Bridge("storeUpdate");return new(true,"0x00000000","");}
   catch(IOException e){return new(false,result.Code,result.Message+" Запасной способ через Store API тоже не сработал: "+e.Message);}
  }
  return result;
 }
 async Task<StoreOperation> InstallLatestOnce(bool upgrade)
 {
  var folder=Path.Combine(store.Root,"bedrock");Directory.CreateDirectory(folder);var log=Path.Combine(folder,"last-store-install.log");
  var info=new ProcessStartInfo("winget.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};foreach(var arg in LatestInstallArguments(upgrade))info.ArgumentList.Add(arg);
  using var process=new Process{StartInfo=info};try{process.Start();}catch(System.ComponentModel.Win32Exception){return new(false,"unavailable","WinGet недоступен. Можно обновить Minecraft через официальный Minecraft Launcher или установить «Установщик приложений» Microsoft.");}
  Net.ProgressSink.Value?.Invoke(new{type="progress",message="Установка Minecraft через Microsoft Store…",percent=0,indeterminate=true,scope="store"});
  using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(45));
  var output=new StringBuilder();var errorsTask=process.StandardError.ReadToEndAsync();
  async Task ReadProgress(){var buffer=new char[512];var line=new StringBuilder();int read;long last=0;while((read=await process.StandardOutput.ReadAsync(buffer.AsMemory(),timeout.Token))>0){output.Append(buffer,0,read);if(output.Length>256*1024)output.Remove(0,output.Length-256*1024);for(int i=0;i<read;i++){var ch=buffer[i];if(ch is '\r' or '\n'){var message=line.ToString();line.Clear();if(message.Length>0&&Environment.TickCount64-last>300){last=Environment.TickCount64;var progress=StoreProgress(message);Net.ProgressSink.Value?.Invoke(new{type="progress",message=progress==null?"Установка Minecraft через Microsoft Store…":"Скачивание Minecraft через Microsoft Store",downloadedBytes=progress?.Done,totalBytes=progress?.Total,percent=progress==null?0:progress.Value.Done*100d/progress.Value.Total,indeterminate=progress==null,scope="store"});}}else if(line.Length<2048)line.Append(ch);}}}
  var reader=ReadProgress();try{await process.WaitForExitAsync(timeout.Token);await reader;}catch(OperationCanceledException){try{process.Kill(true);}catch{}try{await reader;}catch{}throw new IOException("Установка Store превысила время ожидания. Проверьте очередь загрузок Store.");}
  var errors=await errorsTask;await File.WriteAllTextAsync(log,"Exit code: "+process.ExitCode+Environment.NewLine+output+Environment.NewLine+errors);
  return StoreResult(process.ExitCode);
 }
 internal static (long Done,long Total)? StoreProgress(string text)
 {
  var match=System.Text.RegularExpressions.Regex.Match(text,@"([\d.,]+)\s*(B|KB|MB|GB|KiB|MiB|GiB)\s*/\s*([\d.,]+)\s*(B|KB|MB|GB|KiB|MiB|GiB)",System.Text.RegularExpressions.RegexOptions.IgnoreCase);if(!match.Success)return null;
  static long Bytes(string value,string unit){var n=double.Parse(value.Replace(',','.'),System.Globalization.CultureInfo.InvariantCulture);var power=unit.ToUpperInvariant() switch{"KB" or "KIB"=>1,"MB" or "MIB"=>2,"GB" or "GIB"=>3,_=>0};return checked((long)(n*Math.Pow(unit.Contains("i",StringComparison.OrdinalIgnoreCase)?1024:1000,power)));}
  try{var done=Bytes(match.Groups[1].Value,match.Groups[2].Value);var total=Bytes(match.Groups[3].Value,match.Groups[4].Value);return total>0&&done<=total?(done,total):null;}catch{return null;}
 }
 public static void OpenStore()=>Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?ProductId=9NBLGGH2JHXJ"){UseShellExecute=true});
}
