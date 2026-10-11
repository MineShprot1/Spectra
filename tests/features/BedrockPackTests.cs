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
 Console.WriteLine($"{checks} Bedrock pack checks passed");
}finally{Directory.Delete(root,true);}
