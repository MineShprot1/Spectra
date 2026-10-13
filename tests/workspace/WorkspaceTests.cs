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
}finally{Directory.Delete(folder,true);}
