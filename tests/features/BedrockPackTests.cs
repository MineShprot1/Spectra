using Spectra;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
var root=Path.Combine(Path.GetTempPath(),"spectra-packs-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var a=Guid.NewGuid();var b=Guid.NewGuid();var checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);checks++;}
string Manifest(Guid uuid,int patch)=>JsonSerializer.Serialize(new{format_version=2,header=new{name="Same display name",uuid=uuid.ToString(),version=new[]{1,0,patch}}});
byte[] Pack(Guid uuid,int patch){using var output=new MemoryStream();using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)){using var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open(),new UTF8Encoding(false));writer.Write(Manifest(uuid,patch));}return output.ToArray();}
try{
 var pack=Path.Combine(root,"test.mcpack");File.WriteAllBytes(pack,Pack(a,1));
 Check(!BedrockPackIndex.IsInstalled(pack,[]),"Uninstalled pack stays pending");
 var folder=Path.Combine(root,"game","resource_packs","different-folder-name");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"manifest.json"),Manifest(a,1));
 var installed=BedrockPackIndex.Installed([Path.Combine(root,"game")]);Check(BedrockPackIndex.IsInstalled(pack,installed),"Installed UUID and version match independently of names");
 Check(!BedrockPackIndex.IsInstalled(pack,[new(b,[1,0,1])]),"Same display name with a different UUID stays pending");
 Check(!BedrockPackIndex.IsInstalled(pack,[new(a,[1,0,0])]),"Older installed version requires import");
 Check(BedrockPackIndex.IsInstalled(pack,[new(a,[1,0,2])]),"Newer installed version does not trigger downgrade");
 var addon=Path.Combine(root,"bundle.mcaddon");using(var zip=ZipFile.Open(addon,ZipArchiveMode.Create)){foreach(var (name,id) in new[]{("behavior.mcpack",a),("resource.mcpack",b)}){using var stream=zip.CreateEntry(name).Open();stream.Write(Pack(id,1));}}
 Check(!BedrockPackIndex.IsInstalled(addon,installed),"Partly imported mcaddon stays pending");
 Check(BedrockPackIndex.IsInstalled(addon,[new(a,[1,0,1]),new(b,[1,0,1])]),"All nested mcaddon packs must be installed");
 var broken=Path.Combine(root,"broken.mcpack");File.WriteAllText(broken,"not a ZIP");Check(!BedrockPackIndex.IsInstalled(broken,installed),"Opening or malformed input does not count as installation");
 Check(!BedrockPackIndex.IsInstalled(pack,BedrockPackIndex.Installed([Path.Combine(root,"other-edition")])),"A different edition storage does not inherit installation state");
 var opened=new List<string>();var polls=new Dictionary<string,int>();
 Task<bool> Imported(string file){if(!opened.Contains(file))return Task.FromResult(false);polls[file]=polls.GetValueOrDefault(file)+1;return Task.FromResult(polls[file]>=2);}
 await BedrockImportQueue.Run(["first","second"],Imported,file=>{if(file=="second")Check(polls["first"]>=3,"Second file waits for two import confirmations");opened.Add(file);},()=>Task.CompletedTask,maxPolls:5);
 Check(opened.SequenceEqual(new[]{"first","second"}),"Packs open sequentially");
 opened.Clear();await BedrockImportQueue.Run(["already"],_=>Task.FromResult(true),file=>opened.Add(file),()=>Task.CompletedTask);Check(opened.Count==0,"Installed files are skipped");
 opened.Clear();try{await BedrockImportQueue.Run(["failed","never-open"],_=>Task.FromResult(false),file=>opened.Add(file),()=>Task.CompletedTask,maxPolls:3);throw new Exception("Expected timeout");}catch(TimeoutException){Check(opened.SequenceEqual(new[]{"failed"}),"Failed import stops the queue");}
 opened.Clear();var elapsed=TimeSpan.Zero;int retryPoll=0;
 await BedrockImportQueue.Run(["retry"],_=>Task.FromResult(++retryPoll>=18),file=>opened.Add(file),()=>{elapsed+=TimeSpan.FromSeconds(1);return Task.CompletedTask;},maxPolls:25,elapsed:()=>elapsed);
 Check(opened.SequenceEqual(new[]{"retry","retry"}),"Unconfirmed pack reopens after 15 seconds");
 opened.Clear();int ticks=0;
 try{await BedrockImportQueue.Run(["closing","must-not-open"],_=>Task.FromResult(false),file=>opened.Add(file),()=>{ticks++;return Task.CompletedTask;},maxPolls:20,running:()=>ticks<2);throw new Exception("Expected cancellation");}catch(OperationCanceledException){Check(opened.SequenceEqual(new[]{"closing"}),"Closing Bedrock stops imports and prevents reopening");}
 opened.Clear();elapsed=TimeSpan.Zero;
 try{await BedrockImportQueue.Run(["starting"],_=>Task.FromResult(false),file=>opened.Add(file),()=>{elapsed+=TimeSpan.FromSeconds(1);return Task.CompletedTask;},maxPolls:16,running:()=>false,elapsed:()=>elapsed);}catch(TimeoutException){}
 Check(opened.Count==2,"Startup delay allows retry before a game process has been observed");
 opened.Clear();polls.Clear();int restarts=0;
 await BedrockImportQueue.Run(["first","second"],Imported,file=>opened.Add(file),()=>Task.CompletedTask,maxPolls:5,restart:()=>{Check(opened.SequenceEqual(new[]{"first"}),"Restart happens after first import, before second opening");restarts++;return Task.CompletedTask;});
 Check(restarts==1,"Two imported files require one restart; the final game stays open");
 opened.Clear();polls.Clear();restarts=0;
 await BedrockImportQueue.Run(["only"],Imported,file=>opened.Add(file),()=>Task.CompletedTask,maxPolls:5,restart:()=>{restarts++;return Task.CompletedTask;});Check(restarts==0,"Single/final pack never restarts Minecraft");
 var assets=Path.Combine(root,"assets.mcpack");using(var zip=ZipFile.Open(assets,ZipArchiveMode.Create)){using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open()))writer.Write(Manifest(a,1));using(var writer=new StreamWriter(zip.CreateEntry("textures/test.txt").Open(),new UTF8Encoding(false)))writer.Write("abc");}
 var required=BedrockPackIndex.Required(assets);installed=BedrockPackIndex.Installed([Path.Combine(root,"game")]);
 Check(!BedrockPackIndex.FilesComplete(required,installed),"Manifest alone cannot confirm resource copying");
 Directory.CreateDirectory(Path.Combine(folder,"textures"));var texture=Path.Combine(folder,"textures","test.txt");File.WriteAllText(texture,"abx",new UTF8Encoding(false));
 Check(!BedrockPackIndex.FilesComplete(required,installed),"Same-size unfinished resource fails checksum");File.WriteAllText(texture,"abc",new UTF8Encoding(false));Check(BedrockPackIndex.FilesComplete(required,installed),"Complete resource set matches archive");
 opened.Clear();int completionTicks=0;elapsed=TimeSpan.Zero;restarts=0;
 await BedrockImportQueue.Run(["copying","next"],file=>Task.FromResult(opened.Contains(file)),file=>opened.Add(file),()=>{completionTicks++;elapsed+=TimeSpan.FromSeconds(3);return Task.CompletedTask;},maxPolls:20,elapsed:()=>elapsed,registered:file=>Task.FromResult(opened.Contains(file)),complete:_=>Task.FromResult(completionTicks>=6),restart:()=>{Check(completionTicks>=7,"Restart waits for completed resources and consecutive confirmations");restarts++;return Task.CompletedTask;});
 Check(opened.SequenceEqual(new[]{"copying","next"})&&restarts==1,"Registered but still copying pack is not reopened at 15 seconds");
 Console.WriteLine($"{checks} Bedrock pack checks passed");
}finally{Directory.Delete(root,true);}
