using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Spectra;
public sealed class ContentService(Store store,GameService game,Authentication auth)
{
 public record ContentItem(string Id,string Name,string Kind,string File,bool Installed=false);
 public record ContentSource(string Id,string Name,string Url);
 public static ContentSource[] Sources(string edition,string kind,string query)
 {
  if(edition is not ("java" or "bedrock")||kind is not ("skins" or "worlds" or "addons" or "resources" or "modpacks"))throw new IOException("Неизвестная категория");
  var q=Uri.EscapeDataString(query);var sources=new List<ContentSource>();
  if(edition=="java"&&kind=="worlds")sources.Add(new("curseforge","CurseForge","https://www.curseforge.com/minecraft/search?class=worlds&search="+q));
  if(edition=="bedrock"&&(kind is "skins" or "worlds" or "addons" or "resources"))sources.Add(new("curseforge","CurseForge","https://www.curseforge.com/minecraft-bedrock/search?search="+q));
  return sources.ToArray();
 }
 string ContentRoot=>Path.Combine(store.Root,"bedrock","content");
 public ContentItem[] QueuedBedrockItems()
 {
  if(!Directory.Exists(ContentRoot))return [];
  return Directory.EnumerateFiles(ContentRoot,"*.json").Select(file=>JsonSerializer.Deserialize<ContentItem>(File.ReadAllText(file),Store.Json)).Where(x=>x!=null&&File.Exists(Store.SafePath(ContentRoot,x.File))).Select(x=>x!).ToArray();
 }
 public ContentItem[] BedrockItems()=>QueuedBedrockItems().Concat(InstalledBedrockItems()).ToArray();
 static bool Linked(string path){for(var current=new DirectoryInfo(path);current!=null;current=current.Parent)if(current.Exists&&(current.Attributes&FileAttributes.ReparsePoint)!=0)return true;return false;}
 static IEnumerable<string> BedrockRoots(bool? preview=null)
 {
  foreach(var family in new[]{"Microsoft.MinecraftUWP_8wekyb3d8bbwe","Microsoft.MinecraftWindows_8wekyb3d8bbwe","Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe","Microsoft.MinecraftWindowsPreview_8wekyb3d8bbwe"}){
   if(preview.HasValue&&(family.Contains("Beta")||family.Contains("Preview"))!=preview.Value)continue;
   var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Packages",family,"LocalState","games","com.mojang");if(Directory.Exists(root)&&!Linked(root))yield return root;
  }
  foreach(var gameName in new[]{"Minecraft Bedrock","Minecraft Bedrock Preview"}){
   if(preview.HasValue&&gameName.EndsWith("Preview",StringComparison.Ordinal)!=preview.Value)continue;
   var users=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),gameName,"Users");if(!Directory.Exists(users)||Linked(users))continue;
   foreach(var user in Directory.EnumerateDirectories(users).Take(100)){var root=Path.Combine(user,"games","com.mojang");if(Directory.Exists(root)&&!Linked(root))yield return root;}
  }
 }
 static ContentItem[] InstalledBedrockItems()
 {
  var result=new List<ContentItem>();
  foreach(var root in BedrockRoots())foreach(var (kind,folder) in new[]{("worlds","minecraftWorlds"),("addons","behavior_packs"),("resources","resource_packs")}){
   var category=Path.Combine(root,folder);if(!Directory.Exists(category)||Linked(category))continue;
   foreach(var path in Directory.EnumerateDirectories(category).Take(2000)){
    if(Linked(path))continue;var name=Path.GetFileName(path);
    try{if(kind=="worlds"){var label=Path.Combine(path,"levelname.txt");if(File.Exists(label)&&new FileInfo(label).Length<4096)name=File.ReadAllText(label).Trim();}
     else{var manifest=Path.Combine(path,"manifest.json");if(File.Exists(manifest)&&new FileInfo(manifest).Length<1024*1024)name=JsonNode.Parse(File.ReadAllText(manifest))?["header"].Str("name")??name;}
    }catch(Exception e) when(e is IOException or JsonException){}
    var id="game:"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant()))).ToLowerInvariant();
    result.Add(new ContentItem(id,name,kind,path,true));
   }
  }
  return result.ToArray();
 }
 static void BackupFolder(string source,string destination)
 {
  if(Linked(source))throw new IOException("Папки-ссылки не поддерживаются");Directory.CreateDirectory(destination);
  foreach(var entry in Directory.EnumerateFileSystemEntries(source)){
   var attributes=File.GetAttributes(entry);if((attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Ссылки внутри дополнения не поддерживаются");
   var target=Path.Combine(destination,Path.GetFileName(entry));if((attributes&FileAttributes.Directory)!=0)BackupFolder(entry,target);else File.Copy(entry,target);
  }
 }
 public static void BackupInstalledData(Store store)
 {
  var roots=BedrockRoots().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
  if(roots.Length==0)return;var backup=Path.Combine(store.Root,"bedrock","backups","before-remove-"+Guid.NewGuid().ToString("N"));
  for(int index=0;index<roots.Length;index++)BackupFolder(roots[index],Path.Combine(backup,"storage-"+index,"com.mojang"));
 }
 public void DeleteBedrock(string id)
 {
  if(id.StartsWith("game:",StringComparison.Ordinal)){
   if(Process.GetProcessesByName("Minecraft.Windows").Length>0)throw new IOException("Закройте Bedrock перед удалением");
   var installed=InstalledBedrockItems().SingleOrDefault(x=>x.Id==id)??throw new IOException("Дополнение не найдено");
   var backup=Path.Combine(store.Root,"bedrock","backups","content-"+Guid.NewGuid().ToString("N"),Path.GetFileName(installed.File));
   BackupFolder(installed.File,backup);Directory.Delete(installed.File,true);return;
  }
  if(!Regex.IsMatch(id,@"\A[a-f0-9]{32}\z"))throw new IOException("Неверный ID");
  var item=QueuedBedrockItems().Single(x=>x.Id==id);File.Delete(Store.SafePath(ContentRoot,item.File));File.Delete(Path.Combine(ContentRoot,id+".json"));
 }
 public static async Task<int> OpenPendingBedrockPacks(Store store,bool preview,CancellationToken cancellationToken=default)
 {
  var root=Path.Combine(store.Root,"bedrock","content");if(!Directory.Exists(root))return 0;
  var files=Directory.EnumerateFiles(root).Where(p=>Path.GetExtension(p).Equals(".mcpack",StringComparison.OrdinalIgnoreCase)||Path.GetExtension(p).Equals(".mcaddon",StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToArray();
  var identities=await Task.Run(()=>files.ToDictionary(file=>file,file=>BedrockPackIndex.Required(file)));
  // Re-scan the game storage, but parse each queued archive only once per attempt.
  Task<bool> Installed(string file)=>Task.Run(()=>BedrockPackIndex.Satisfied(identities[file],BedrockPackIndex.Installed(BedrockRoots(preview))));
  return await BedrockImportQueue.Run(files,Installed,file=>Process.Start(new ProcessStartInfo(file){UseShellExecute=true}),()=>Task.Delay(1000),
   (file,index,total)=>Net.ProgressSink.Value?.Invoke(new{type="progress",message="Импорт дополнений Bedrock · "+(index+1)+" / "+total+" · "+Path.GetFileName(file),percent=index*100d/total,indeterminate=true}),running:BedrockService.IsRunning,restart:BedrockService.StopProcesses,cancellationToken:cancellationToken);
 }
 public static async Task OpenBedrockWorlds(Store store)
 {
  var root=Path.Combine(store.Root,"bedrock","content");if(!Directory.Exists(root))return;
  foreach(var file in Directory.EnumerateFiles(root).Where(p=>Path.GetExtension(p).Equals(".mcworld",StringComparison.OrdinalIgnoreCase))){Process.Start(new ProcessStartInfo(file){UseShellExecute=true});await Task.Delay(1500);}
 }
 public async Task<object> Import(JsonNode data,System.Windows.Window owner)
 {
  string edition=data.Str("edition"),kind=data.Str("kind");
  if(edition is not ("java" or "bedrock")||kind is not ("skins" or "worlds" or "addons" or "resources"))throw new IOException("Неизвестная категория");
  var filter=kind=="skins"?"PNG|*.png":edition=="java"?"Карта ZIP|*.zip":kind=="worlds"?"Bedrock World|*.mcworld":kind=="addons"?"Bedrock Addon|*.mcaddon;*.mcpack":"Bedrock Resource Pack|*.mcpack";
  var picker=new Microsoft.Win32.OpenFileDialog{Filter=filter,Title="Выберите скачанный файл"};
  if(picker.ShowDialog(owner)!=true)return new{cancelled=true};
  return await InstallFile(picker.FileName,edition,kind,data.Str("instanceId"),data.Str("version"),data.Str("variant"));
 }
 public async Task<object> InstallFile(string file,string edition,string kind,string instanceId,string version,string variant,string displayName="")
 {
  var name=string.IsNullOrWhiteSpace(displayName)?Path.GetFileNameWithoutExtension(file):displayName[..Math.Min(displayName.Length,160)];
  if(new FileInfo(file).Length>1024L*1024*1024)throw new IOException("Файл больше 1 ГиБ");
  if(kind=="skins"){
   if(new FileInfo(file).Length>1024*1024)throw new IOException("Скин больше 1 МиБ");
   var signature=new byte[8];await using(var png=File.OpenRead(file)){if(await png.ReadAsync(signature)!=8||!signature.SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw new IOException("Нужен PNG-файл");}
   var bitmap=new System.Windows.Media.Imaging.BitmapImage();bitmap.BeginInit();bitmap.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(Path.GetFullPath(file));bitmap.EndInit();
   if(bitmap.PixelWidth!=64||bitmap.PixelHeight is not (32 or 64))throw new IOException("Нужен скин PNG 64×64 или 64×32");
   if(edition=="java"){
    if(auth.Profile==null)throw new IOException("Войдите в Minecraft");
    var skin=new SavedSkin{Name=name,Owner=auth.Profile.Str("id"),Variant=variant=="slim"?"slim":"classic"};
    var root=Path.Combine(store.Root,"skins");Directory.CreateDirectory(root);File.Copy(file,Path.Combine(root,skin.Id+".png"));store.Config.Skins.Add(skin);store.Save();return new{cancelled=false,message="Скин добавлен в профиль"};
   }
   Directory.CreateDirectory(store.Config.BedrockSkinFolder);var dest=Path.Combine(store.Config.BedrockSkinFolder,string.Concat(name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c))+"-"+Guid.NewGuid().ToString("N")[..6]+".png");File.Copy(file,dest);return new{cancelled=false,message="Скин сохранён в "+store.Config.BedrockSkinFolder};
  }
  if(edition=="bedrock"){
   var ext=Path.GetExtension(file).ToLowerInvariant();if(kind=="worlds"&&ext!=".mcworld"||kind=="resources"&&ext!=".mcpack"||kind=="addons"&&ext is not (".mcaddon" or ".mcpack"))throw new IOException("Неверный формат дополнения");
   using(var zip=ZipFile.OpenRead(file)){if(!zip.Entries.Any(e=>kind=="worlds"?e.FullName=="level.dat":e.FullName.EndsWith("manifest.json")||e.FullName.EndsWith(".mcpack")))throw new IOException("Не найден манифест дополнения / level.dat");}
   Directory.CreateDirectory(ContentRoot);var id=Guid.NewGuid().ToString("N");var item=new ContentItem(id,name,kind,id+ext);File.Copy(file,Path.Combine(ContentRoot,item.File));File.WriteAllText(Path.Combine(ContentRoot,id+".json"),JsonSerializer.Serialize(item,Store.Json));return new{cancelled=false,message="Добавлено в дополнения Bedrock"};
  }
  if(kind!="worlds")throw new IOException("Категория недоступна");
  Instance instance;
  if(instanceId!="")instance=store.Get(instanceId);else{await game.Metadata(version);await game.InstallVersion(version);instance=new Instance{Id="vanilla",Version=version,Settings=store.Config.Defaults with {}};}
  if(game.Running.ContainsKey(instance.Id))throw new IOException("Закройте игру перед установкой карты");
  if(instanceId!=""&&!game.InstalledJava().Contains(instance.Version))await game.InstallVersion(instance.Version);
  var saves=Path.Combine(store.Folder(instance),"saves");Directory.CreateDirectory(saves);var staging=Path.Combine(saves,".import-"+Guid.NewGuid().ToString("N"));var destination=Path.Combine(saves,"Map-"+Guid.NewGuid().ToString("N")[..8]);
  try{
   using var zip=ZipFile.OpenRead(file);if(zip.Entries.Count>100000)throw new IOException("Слишком много файлов");
   var levels=zip.Entries.Where(e=>e.FullName=="level.dat"||e.FullName.EndsWith("/level.dat",StringComparison.Ordinal)).ToArray();if(levels.Length!=1)throw new IOException("Архив должен содержать один мир с level.dat");
   var prefix=levels[0].FullName[..^9];long total=0;
   foreach(var entry in zip.Entries){if(!entry.FullName.StartsWith(prefix,StringComparison.Ordinal))continue;var relative=entry.FullName[prefix.Length..];if(relative==""||entry.FullName.EndsWith('/'))continue;
    if(relative.Contains('\\')||relative.Contains(':')||((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new IOException("Недопустимый файл в карте");
    total=checked(total+entry.Length);if(total>4L*1024*1024*1024)throw new IOException("Карта больше 4 ГиБ");
    var path=Store.SafePath(staging,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);await using var input=entry.Open();await using var output=File.Create(path);await input.CopyToAsync(output);
   }
   Directory.Move(staging,destination);return new{cancelled=false,message="Карта установлена: "+instance.Name};
  }finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
 }
}
