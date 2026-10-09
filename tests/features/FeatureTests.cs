using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Reflection;
using Spectra;
static class FeatureTests
{
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static ZipArchive Archive(Dictionary<string,string> files){var stream=new MemoryStream();using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true)){foreach(var (name,body) in files){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(body);}}stream.Position=0;return new ZipArchive(stream,ZipArchiveMode.Read);}
 sealed class PackageHandler(Func<System.Net.Http.HttpRequestMessage,System.Net.Http.HttpResponseMessage> respond):System.Net.Http.HttpMessageHandler
 { protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,CancellationToken token)=>Task.FromResult(respond(request)); }
 static async Task Main(string[] args){
  Check(BedrockService.MicrosoftUrl("http://assets1.xboxlive.com/game.msixvc").StartsWith("http://"),"official HTTP source scheme preserved");
  Check(BedrockService.MicrosoftUrl("https://assets2.xboxlive.com/game.msixvc").StartsWith("https://"),"official HTTPS preserved");
  foreach(var bad in new[]{"http://evil.example/game.msixvc","http://assets1.xboxlive.com.evil.example/game.msixvc","http://user@assets1.xboxlive.com/game.msixvc","http://assets1.xboxlive.com:443/game.msixvc","https://assets1.xboxlive.com/#fragment"}){bool rejected=false;try{BedrockService.MicrosoftUrl(bad);}catch(IOException){rejected=true;}Check(rejected,"untrusted package URL rejected");}
  var downloadFolder=Path.Combine(Path.GetTempPath(),"Spectra-download-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(downloadFolder);
  try{
   var requests=0;
   using var client=new System.Net.Http.HttpClient(new PackageHandler(req=>{requests++;Check(req.Headers.Authorization==null,"no authentication sent to CDN");if(req.RequestUri!.Host=="assets1.xboxlive.com"){var redirect=new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Found);redirect.Headers.Location=new Uri("http://assets2.xboxlive.com/game.msixvc");return redirect;}return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new System.Net.Http.ByteArrayContent(new byte[]{1,2,3})};}));
   var path=Path.Combine(downloadFolder,"game.msixvc");await BedrockService.DownloadMicrosoftPackage("http://assets1.xboxlive.com/game.msixvc",path,client);Check(requests==2&&File.ReadAllBytes(path).SequenceEqual(new byte[]{1,2,3}),"HTTP download and official mirror redirect");
   foreach(var destination in new[]{"https://evil.example/package","http://assets2.xboxlive.com/game.msixvc"}){
    using var unsafeClient=new System.Net.Http.HttpClient(new PackageHandler(req=>{var redirect=new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Found);redirect.Headers.Location=new Uri(destination);return redirect;}));
    bool rejected=false;var failed=Path.Combine(downloadFolder,"failed.msixvc");try{await BedrockService.DownloadMicrosoftPackage("https://assets1.xboxlive.com/game.msixvc",failed,unsafeClient);}catch(IOException){rejected=true;}Check(rejected&&!File.Exists(failed),"untrusted redirect and HTTPS downgrade rejected");
   }
   Check(!Directory.EnumerateFiles(downloadFolder,"*.part").Any(),"temporary downloads cleaned up");
   bool httpRejected=false;try{await Net.Download("http://assets1.xboxlive.com/game.msixvc",path);}catch(IOException){httpRejected=true;}Check(httpRejected,"general downloader remains HTTPS only");
   Console.WriteLine("PASS: Bedrock CDN schemes, redirects, host restrictions, cleanup and HTTPS-only general downloads");
  }finally{Directory.Delete(downloadFolder,true);}

  Check(BedrockService.CanonicalIdentity("MICROSOFT.MINECRAFTUWP")=="Microsoft.MinecraftUWP","Windows package names ignore case");
  Check(BedrockService.CanonicalIdentity("Microsoft.MinecraftUWP.fake")==null,"unknown identities rejected");
  Check(BedrockService.MatchesUwpVersion("0.15.8.0","0.15.8.0"),"old direct package version accepted");
  Check(BedrockService.MatchesUwpVersion("0.15.8.0","0.15.800.0"),"packed package version accepted");
  Check(!BedrockService.MatchesUwpVersion("0.15.8.0","0.15.900.0"),"other game version rejected");
  Check(BedrockService.MatchesUwpVersion("1.21.114.1","1.21.11401.0"),"modern Windows package encoding accepted");
  Check(BedrockService.PackageVersion("0.14.2.1")=="0.142.1.0","reported historical package encoding");
  Check(BedrockService.MatchesUwpVersion("0.14.2.1","0.142.1.0"),"reported old package accepted");
  Check(!BedrockService.MatchesUwpVersion("0.14.2.1","0.142.2.0"),"old package revision mismatch rejected");
  Check(BedrockService.PackageVersion("0.15.10.0")=="0.1510.0.0","old build concatenation handles two-digit builds");
  Check(BedrockService.StoreResult(unchecked((int)0x8A150014)).Message.Contains("не нашёл Minecraft"),"not-found WinGet code correctly identified");
  Check(BedrockService.StoreResult(unchecked((int)0x8A15002B)).Message.Contains("применимого обновления"),"no-update WinGet code correctly identified");
  Check(!BedrockService.StoreResult(unchecked((int)0x8A150014)).Success&&BedrockService.StoreResult(0).Success,"Store status reflects actual exit code");
  var progress=BedrockService.StoreProgress(" 12.5 MB / 100 MB ");Check(progress?.Done==12500000&&progress?.Total==100000000,"WinGet byte progress");
  Check(BedrockService.StoreProgress("Signing in")==null,"unknown Store progress stays indeterminate");
  Check(BedrockService.LatestInstallArguments().Contains("msstore")&&BedrockService.LatestInstallArguments().Contains("--disable-interactivity"),"background Store installation arguments");
  var installed=new[]{new BedrockService.Package("stable","Microsoft.MinecraftUWP","1.21.10000.0","family","app",false,false,true),new BedrockService.Package("preview","Microsoft.MinecraftWindowsBeta","1.99.0.0","family","app",true,false,true),new BedrockService.Package("legacy","Microsoft.MinecraftUWP","1.1.0.0","family","app",false,true,true)};
  var latestRelease=new BedrockService.Release("latest-test","1.26.50","1.26.5000.0","Microsoft.MinecraftUWP",false,false,"UWP","",[]);
  Check(BedrockService.LatestStatus(installed,new[]{latestRelease}).UpdateAvailable,"older installed stable offers update");
  Check(!BedrockService.LatestStatus(installed.Select(p=>p with{Version="1.26.5000.0"}),new[]{latestRelease}).UpdateAvailable,"current stable needs no update");
  Check(!BedrockService.LatestStatus([],new[]{latestRelease}).UpdateAvailable,"missing game offers install rather than update");
  Check(!BedrockService.LatestStatus(installed.Select(p=>p with{Version="0.0.0.0"}),new[]{latestRelease}).UpdateAvailable,"unknown version is not guessed as outdated");
  Check(BedrockService.LatestStatus(installed.Where(p=>p.Legacy),new[]{latestRelease}).UpdateAvailable,"legacy-only install offers current upgrade");
  Check(BedrockService.LatestInstallArguments(true)[0]=="upgrade","latest update uses upgrade command");
  Check(BedrockService.LatestInstalled(installed)?.Id=="stable","latest launch ignores Preview and legacy");
  Check(BedrockService.LatestInstalled(installed.Where(p=>p.Preview||p.Legacy))==null,"missing regular edition invokes Store path");
  Check(BedrockService.WouldReplace("Microsoft.MinecraftUWP","1.20.0.0",installed),"version replacement blocked");
  Check(!BedrockService.WouldReplace("Microsoft.MinecraftWindowsPreview","1.20.0.0",installed),"independent Preview identity allowed");
  Check(!BedrockService.WouldReplace("Microsoft.MinecraftUWP","1.21.10000.0",installed.Take(1)),"existing exact version launches without install");
  Console.WriteLine("PASS: latest stable selection and replacement guard");
  using(var zip=Archive(new(){["modrinth.index.json"]="""{"files":[{"path":"mods/client.jar","hashes":{},"env":{"client":"optional"}},{"path":"mods/server.jar","env":{"client":"unsupported"}},{"path":"shaderpacks/light.zip"},{"path":"resourcepacks/art.zip"},{"path":"mods/../fake.jar"}]}""",["overrides/mods/client.jar"]="override",["client-overrides/mods/local.jar"]="local",["overrides/config/settings.json"]="ignored"})){
   var items=CatalogService.ReadPackContents(zip,"modrinth");Check(items.Count==4,"client-only files, overrides and paths");Check(items.Count(i=>i.Kind=="mods")==2,"mod count");Check(items.Single(i=>i.File=="mods/client.jar").Origin=="override","override supersedes manifest");Check(items.All(i=>!i.File.Contains("..")),"invalid paths excluded");Check(items.Count(i=>i.Kind=="shaders")==1&&items.Count(i=>i.Kind=="resources")==1,"shader/resource classification");
  }
  using(var zip=Archive(new(){["modrinth.index.json"]="""{"files":[]}""",["overrides/resourcepacks/Folder/pack.mcmeta"]="{}",["overrides/resourcepacks/Folder/assets/a.png"]="a",["overrides/shaderpacks/Shader/shaders/a.glsl"]="a",["overrides/mods/readme.txt"]="ignored"})){var items=CatalogService.ReadPackContents(zip,"modrinth");Check(items.Count==2,"unpacked resource/shader packs count once, non-mod files ignored");}
  using(var zip=Archive(new(){["manifest.json"]="""{"overrides":"custom","files":[{"projectID":123,"fileID":456,"required":false}]}""",["custom/shaderpacks/a.zip"]="a",["custom/resourcepacks/b.zip"]="b"})){
   var items=CatalogService.ReadPackContents(zip,"curseforge");Check(items.Count==3,"CF references + custom overrides");Check(items.Single(i=>i.Kind=="unknown").Optional,"optional CF file retained");
  }
  using(var zip=Archive(new(){["manifest.json"]="""{"overrides":"overrides","files":[{"projectID":"bad","fileID":1}]}"""})){
   bool rejected=false;try{CatalogService.ReadPackContents(zip,"curseforge");}catch(IOException){rejected=true;}Check(rejected,"invalid IDs rejected");
  }
  var update=Guid.NewGuid().ToString();var soap=System.Xml.Linq.XDocument.Parse(BedrockService.BuildUwpRequest(update,DateTime.UtcNow));Check(soap.Descendants().Single(e=>e.Name.LocalName=="UpdateID").Value==update,"SOAP update ID");Check(soap.Descendants().Single(e=>e.Name.LocalName=="Action").Value.EndsWith("/GetExtendedUpdateInfo2"),"SOAP action URI");
  Console.WriteLine("PASS: actual C# modpack parser counts categories, ignores server-only files, respects overrides and rejects invalid IDs");
  if(args.Contains("--live-pack")){
   var store=new Store();try{var project=await Net.Get("https://api.modrinth.com/v2/project/fabulously-optimized");var versions=await Net.Get("https://api.modrinth.com/v2/project/"+project.Str("id")+"/version");var result=await new CatalogService(store).PackContents("modrinth",project.Str("id"),versions[0].Str("id"));var data=JsonSerializer.SerializeToNode(result,Store.Json)!;Check(data["counts"]!["mods"]!.GetValue<int>()>5,"live pack mod count");Console.WriteLine("PASS: live Modrinth pack content read: "+data["counts"]!.ToJsonString());}finally{if(Directory.Exists(store.Root))Directory.Delete(store.Root,true);}
  }
  if(args.Contains("--live-bedrock")){
   var store=new Store();try{var service=new BedrockService(store);var rows=await service.Catalogue();Check(rows.Count>100,"live Bedrock catalogue");Check(rows.Any(r=>r.Format=="GDK")&&rows.Any(r=>r.Legacy),"GDK and old Windows 10 versions");Check(rows.SelectMany(r=>r.Urls).All(u=>BedrockService.MicrosoftUrl(u)==u),"official CDN mirrors");Console.WriteLine($"PASS: live Bedrock catalogue ({rows.Count} releases), UWP + GDK + Windows 10");
    var method=typeof(BedrockService).GetMethod("ResolveUwp",BindingFlags.NonPublic|BindingFlags.Instance)!;var latest=rows.First(r=>r.Format=="UWP"&&!r.Preview);var resolved=await (Task<string>)method.Invoke(service,new object[]{latest})!;Check(new Uri(resolved).Host=="tlu.dl.delivery.mp.microsoft.com","Store CDN URL");Console.WriteLine("PASS: official Microsoft download URL resolved for Bedrock "+latest.Version);
   }finally{if(Directory.Exists(store.Root))Directory.Delete(store.Root,true);}
  }
 }
}
