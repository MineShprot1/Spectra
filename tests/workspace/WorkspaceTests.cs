using System.IO.Compression;
using System.Text.Json;
using Spectra;
void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS: "+name);}
Check(Authentication.OfflineUuid("Notch")=="b50ad385829d3141a2167e7d7539ba7f","Java-compatible offline UUID");
foreach(var name in new[]{"../outside","ab","a b","name;exec","abcdefghijklmnopq"}){try{Authentication.ValidateName(name);throw new Exception("Accepted invalid name");}catch(IOException){}}
Check(true,"account name validation");
var secret="eyJ"+new string('A',90);var log="Normal line\nERROR Caused by Missing dependency\npassword=abc access_token="+secret+"\nC:\\Users\\Alice\\logs 192.168.1.10:25565";
var compact=CrashAssistant.Compact(log);
Check(!compact.Contains(secret)&&!compact.Contains("password=abc")&&!compact.Contains("Alice")&&!compact.Contains("192.168.1.10"),"diagnostic redaction");
Check(compact.Contains("Missing dependency")&&CrashAssistant.Compact(new string('x',20000)).Length<=6500,"bounded diagnostic context");
var folder=Path.Combine(Path.GetTempPath(),"spectra-workspace-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
try{foreach(var env in new[]{"client","server","*"}){var path=Path.Combine(folder,env.Replace("*","both")+".jar");using(var zip=ZipFile.Open(path,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("fabric.mod.json").Open());writer.Write(JsonSerializer.Serialize(new{environment=env}));}Check(InstanceServer.ModSide(path)==(env=="*"?"both":env),"Fabric side "+env);}
 var unknown=Path.Combine(folder,"unknown.jar");using(var zip=ZipFile.Open(unknown,ZipArchiveMode.Create)){zip.CreateEntry("README");}Check(InstanceServer.ModSide(unknown)=="unknown","unknown mods are not assumed server compatible");
 using(var clock=new PlaytimeService(folder)){
  clock.Import("pack","Imported pack","1.21",3600,"prism-1");clock.Import("pack","Imported pack","1.21",3600,"prism-1");clock.Import("pack","Imported pack","1.21",double.NaN,"bad");
  Check(clock.Snapshot().TotalSeconds==3600,"Prism playtime import is finite and deduplicated");
  clock.Start("multi-pack-extra","pack","Pack","1.21");Check(clock.Snapshot().Items.Length==1,"offline extra clients do not multiply playtime");
  clock.Start("vanilla","vanilla","Minecraft","1.20");Thread.Sleep(30);clock.Stop("vanilla");clock.Checkpoint();
  Check(clock.Snapshot().Items.Any(e=>e.Key=="version:1.20"&&e.Seconds>0),"vanilla versions keep separate elapsed time");
 }
 using(var restoredTime=new PlaytimeService(folder)){Check(restoredTime.Snapshot().TotalSeconds>3600,"playtime survives launcher restart");}
 var config=new Configuration{Accounts=[new LauncherAccount{Id="licensed",Name="Player"},new LauncherAccount{Id="offline",Name="Offline",Offline=true,OwnerId="licensed",ElyName="Notch"}],ActiveAccountId="offline",InterfaceScale=1.25,MultiClients=[new MultiClientProfile{Name="SpectraAccount1",ElyName="Notch"}]};
 var restored=JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(config))!;Check(restored.Accounts.Count==2&&restored.MultiClients[0].ElyName=="Notch"&&restored.InterfaceScale==1.25,"account, skin, multi-client and scale persistence");
 var broken=Path.Combine(folder,"broken.jar");File.WriteAllText(broken,"incomplete ZIP");Check(InstanceServer.ModSide(broken)=="unknown","damaged ZIP does not abort mod-side discovery");
 var store=new Store(Path.Combine(folder,"isolated-store"));var instance=new Instance{Id="test-pack",Name="Test",Version="1.21.1"};store.Config.Instances.Add(instance);var gameFolder=store.Folder(instance);var mods=Path.Combine(gameFolder,"mods");Directory.CreateDirectory(mods);File.Copy(broken,Path.Combine(mods,"broken.jar"));
 var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jV9kAAAAASUVORK5CYII=");
 void Archive(string filename,string metadata,string text,string iconName="icon.png") {using var zip=ZipFile.Open(Path.Combine(mods,filename),ZipArchiveMode.Create);using(var writer=new StreamWriter(zip.CreateEntry(metadata).Open()))writer.Write(text);using var icon=zip.CreateEntry(iconName).Open();icon.Write(png);}
 Archive("fabric.jar","fabric.mod.json","{\"name\":\"Fabric sample\",\"version\":\"1\",\"icon\":{\"32\":\"icon.png\"}}");
 Archive("quilt.jar","quilt.mod.json","{\"quilt_loader\":{\"version\":\"2\",\"metadata\":{\"name\":\"Quilt sample\",\"icon\":\"icon.png\"},\"depends\":[]}}");
 Archive("forge.jar","META-INF/mods.toml","[[mods]]\nmodId=\"sample\"\ndisplayName = \"Forge sample\"\nversion = \"3\"\nlogoFile = \"icon.png\"\n");
 var legacyLibrary=Path.Combine(gameFolder,"libraries","example","library.jar");Directory.CreateDirectory(Path.GetDirectoryName(legacyLibrary)!);File.WriteAllText(legacyLibrary,"legacy bytes");await new SharedLibraries(store).Prepare();Check(File.ReadAllText(Path.Combine(store.Root,"cache","libraries","example","library.jar"))=="legacy bytes"&&File.Exists(legacyLibrary),"existing libraries seed the shared cache without removing instance files");
 var game=new GameService(store,new Authentication(store,_=>{}),_=>{});try{
  var entries=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(game.Files(instance.Id,"mods"),Store.Json))!.AsArray();Check(entries.Count==4,"corrupted mod is retained in the list alongside healthy mods");
  foreach(var title in new[]{"Fabric sample","Quilt sample","Forge sample"}){var entry=entries.Single(e=>e?["label"]?.ToString()==title);Check(entry?["icon"]?.ToString().StartsWith("https://data.spectra.local/cache/content-icons/")==true,title+" metadata and cached icon");}
  Check(Directory.GetFiles(Path.Combine(store.Root,"cache","content-icons")).Length==1,"identical mod icons deduplicate on disk instead of duplicating base64 in messages");
  var method=typeof(GameService).GetMethod("CreateLauncher",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;var launcher=method.Invoke(game,[gameFolder]);Check(launcher!=null,"launcher accepts shared library path");
  var actions=new LibraryActions(store,game);try{actions.Delete(instance.Id,"mods",["../outside"],[]);throw new Exception("Accepted path escape");}catch(InvalidOperationException){}Check(File.Exists(Path.Combine(mods,"fabric.jar")),"bulk deletion rejects paths outside the category");
  store.Config.Preferences.HideQuickLaunch=true;store.Config.Preferences.ChatNotifications=false;store.Save();var restoredStore=new Store(store.Root);Check(restoredStore.Config.Preferences.HideQuickLaunch&&!restoredStore.Config.Preferences.ChatNotifications,"launcher preferences survive native persistence");
 }finally{game.Playtime.Dispose();}
 var bedrock=new BedrockService(store);var extract=typeof(BedrockService).GetMethod("ExtractPackage",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
 async Task<string> Extract(string path)=>await (Task<string>)extract.Invoke(bedrock,[path,"Microsoft.MinecraftUWP","1.21.100.0"])!;
 try{await Extract(broken);throw new Exception("Accepted broken package");}catch(InvalidDataException){}
 var versionRoot=Path.Combine(store.Root,"bedrock","versions");Check(!Directory.Exists(Path.Combine(versionRoot,"Microsoft.MinecraftUWP_1.21.100.0"))&&(!Directory.Exists(versionRoot)||!Directory.EnumerateDirectories(versionRoot,".extract-*").Any()),"failed Bedrock extraction leaves no installed or temporary files");
 var malicious=Path.Combine(folder,"traversal.appx");using(var zip=ZipFile.Open(malicious,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("../escaped.txt").Open());writer.Write("invalid");}
 try{await Extract(malicious);throw new Exception("Accepted traversal package");}catch(InvalidOperationException){}Check(!File.Exists(Path.Combine(versionRoot,"escaped.txt"))&&!Directory.EnumerateDirectories(versionRoot,".extract-*").Any(),"Bedrock extraction rejects traversal and cleans staging");
 var valid=Path.Combine(folder,"valid.appx");using(var zip=ZipFile.Open(valid,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("AppxManifest.xml").Open());writer.Write("<Package/>");}
 var manifestPath=await Extract(valid);Check(File.Exists(manifestPath)&&File.Exists(Path.Combine(Path.GetDirectoryName(manifestPath)!,".spectra-ready")),"complete Bedrock extraction commits manifest and ready marker together");
}finally{Directory.Delete(folder,true);}
