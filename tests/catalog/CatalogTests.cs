using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spectra;
static class CatalogTests
{
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static JsonNode Json(string s)=>JsonNode.Parse(s)!;
 static byte[] Archive(params (string Name,byte[] Bytes)[] files){using var stream=new MemoryStream();using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))foreach(var file in files){var e=zip.CreateEntry(file.Name);using var f=e.Open();f.Write(file.Bytes);}return stream.ToArray();}
 static async Task Reject(Func<Task> action,string message){try{await action();}catch(IOException){return;}throw new Exception(message);}
 static async Task Main()
 {
  var store=new Store();var game=new GameService();var catalog=new CatalogService(store,game,_=>{});var target=new Instance{Version="1.21.1",Loader="fabric"};store.Config.Instances.Add(target);
  try{
   Net.Reply=url=>url.Contains("/search?")?Json("{\"hits\":[]}"):new JsonArray();await catalog.Search("test","resources",target.Id,0,["16x","blocks","vanilla-like"]);
   var search=Net.Requests.Last(u=>u.Contains("modrinth"));var facets=Uri.UnescapeDataString(search.Split("facets=")[1]);Check(facets.Contains("categories:16x")&&facets.Contains("categories:blocks")&&facets.Contains("versions:1.21.1"),"Filters or compatibility missing");await Reject(()=>catalog.Search("","mods",target.Id,0,["potato"]),"Cross-kind filter accepted");
   Console.WriteLine("PASS: catalogue filters preserve version/loader and reject tags from another content type");
   store.Config.CurseForgeKey="fixture";Net.Reply=url=>url.Contains("/categories?")?Json("{\"data\":[{\"id\":9,\"slug\":\"16x\"}]}"):url.Contains("curseforge")?Json("{\"data\":[{\"id\":1,\"name\":\"Match\",\"categories\":[{\"id\":9}],\"downloadCount\":1},{\"id\":2,\"name\":\"Wrong\",\"categories\":[],\"downloadCount\":2}]}"):Json("{\"hits\":[]}");
   var result=JsonSerializer.SerializeToNode(await catalog.Search("","resources",target.Id,0,["16x"]))!;Check(result["items"]!.AsArray().Count==1,"CurseForge leaked unrelated categories");
   var payload=Encoding.UTF8.GetBytes("jar fixture");var sha=Convert.ToHexString(SHA1.HashData(payload)).ToLowerInvariant();Net.Files["https://cdn.example.org/mod.jar"]=payload;
   var project=Json("{\"status\":\"success\",\"id\":125,\"name\":\"Fixture FTB\",\"slug\":\"fixture\",\"versions\":[{\"id\":7,\"name\":\"1.0\",\"updated\":1700000000}],\"art\":[]}");
   var manifest=Json("{\"id\":7,\"targets\":[{\"name\":\"minecraft\",\"type\":\"game\",\"version\":\"1.21.1\"},{\"name\":\"neoforge\",\"type\":\"modloader\",\"version\":\"21.1.72\"}],\"files\":[{\"name\":\"mod.jar\",\"path\":\"mods/\",\"url\":\"https://cdn.example.org/mod.jar\",\"size\":11,\"sha1\":\""+sha+"\"},{\"name\":\"server.jar\",\"path\":\"mods/\",\"serveronly\":true}]}");
   Net.Reply=url=>url.EndsWith("/125/7")?manifest.DeepClone():project.DeepClone();var ftb=(Instance)await catalog.InstallPack("ftb","125","7");Check(ftb.Loader=="neoforge"&&File.Exists(Path.Combine(store.Folder(ftb),"mods","mod.jar")),"FTB installation failed");Check(!File.Exists(Path.Combine(store.Folder(ftb),"mods","server.jar")),"Server-only file installed");
   var count=store.Config.Instances.Count;manifest["files"]![0]!["sha1"]=new string('0',40);await Reject(()=>catalog.InstallPack("ftb","125","7"),"Corrupt FTB file accepted");Check(store.Config.Instances.Count==count,"Failed install retained instance");manifest["files"]![0]!["sha1"]=sha;manifest["files"]![0]!["path"]="../../escape/";await Reject(()=>catalog.InstallPack("ftb","125","7"),"FTB path traversal accepted");
   Console.WriteLine("PASS: FTB selected release, loader, server-only exclusion, hash verification, rollback and path traversal rejection");
   var forge=Encoding.UTF8.GetBytes("{\"libraries\":[{\"name\":\"net.minecraftforge:forge:1.12.2-14.23.5.2860\"}]}");var jar=Archive(("version.json",forge));var zipBytes=Archive(("bin/modpack.jar",jar),("mods/test.jar",payload));Net.Files["https://cdn.example.org/technic.zip"]=zipBytes;
   var technic=Json("{\"name\":\"fixture\",\"displayName\":\"Fixture Technic\",\"minecraft\":\"1.12.2\",\"version\":\"1\",\"url\":\"https://cdn.example.org/technic.zip\",\"icon\":{}}");Net.Reply=_=>technic.DeepClone();var pack=(Instance)await catalog.InstallPack("technic","fixture","1");Check(pack.Loader=="forge"&&pack.LoaderVersion=="14.23.5.2860","Technic Forge detection failed");Check(File.Exists(Path.Combine(store.Folder(pack),"mods","test.jar")),"Technic mods absent");
   Net.Files["https://cdn.example.org/technic.zip"]=Archive(("../escape.txt",payload));await Reject(()=>catalog.InstallPack("technic","fixture","1"),"Technic traversal accepted");Net.Files["https://cdn.example.org/technic.zip"]=Archive(("bin/modpack.jar",Archive(("unknown.txt",payload))));await Reject(()=>catalog.InstallPack("technic","fixture","1"),"Unsupported custom game jar accepted");
   // Solder components use MD5 supplied by the publisher; each ZIP is extracted only inside the new instance.
   var solder=Json("{\"recommended\":\"2\",\"builds\":[\"2\"]}");technic["solder"]="https://solder.example.org/api/";var md5=Convert.ToHexString(MD5.HashData(zipBytes)).ToLowerInvariant();var build=Json("{\"minecraft\":\"1.12.2\",\"mods\":[{\"name\":\"fixture\",\"url\":\"https://cdn.example.org/technic.zip\",\"md5\":\""+md5+"\"}]}");Net.Files["https://cdn.example.org/technic.zip"]=zipBytes;Net.Reply=url=>url.EndsWith("/fixture/2")?build.DeepClone():url.Contains("solder.example.org")?solder.DeepClone():technic.DeepClone();var solderPack=(Instance)await catalog.InstallPack("technic","fixture","2");Check(solderPack.PackVersion=="2"&&solderPack.Loader=="forge","Solder selected release failed");
   Console.WriteLine("PASS: Technic archive and Solder components, selected release, Forge metadata and unsafe/unsupported archive rejection");
  }finally{Directory.Delete(store.Root,true);}
 }
}
