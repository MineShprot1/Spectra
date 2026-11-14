using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Spectra;

static class SourceFilesTests
{
 sealed class Handler:HttpMessageHandler
 {
  public Dictionary<string,string> Data=new();public List<string> Requests=new();public string? Denied,Corrupt;public int Active,Maximum;
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
  {
   if(request.RequestUri!.Host!="testing.example"||request.Headers.Authorization?.Parameter!="fixture_secret")throw new Exception("Private file request escaped the gateway");
   var hash=request.RequestUri.Segments.Last();lock(Requests)Requests.Add(hash);
   var count=Interlocked.Increment(ref Active);lock(Requests)Maximum=Math.Max(Maximum,count);
   try{await Task.Delay(hash==Denied?100:15,ct);return new(hash==Denied?HttpStatusCode.Unauthorized:HttpStatusCode.OK){Content=new StringContent(hash==Corrupt?"damaged":Data[hash],Encoding.UTF8)};}
   finally{Interlocked.Decrement(ref Active);}
  }
 }
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 public static async Task Run()
 {
  var root=Path.Combine(Path.GetTempPath(),"Spectra-source-files-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try
  {
   var updates=Path.Combine(root,"updates");var install=Path.Combine(root,"install");var channel=SourceUpdateChannel.Testing("https://testing.example","fixture_secret");
   using var handler=new Handler();using var client=new HttpClient(handler);var tree=new JsonArray();int sequence=0;
   async Task<string> Add(string path,string value,string? local=null)
   {
    var temp=Path.Combine(root,"hash-"+sequence++);await File.WriteAllTextAsync(temp,value,new UTF8Encoding(false));var hash=await SourceUpdater.GitHash(temp);handler.Data[hash]=value;
    tree.Add(new JsonObject{{"path",path},{"type","blob"},{"mode","100644"},{"sha",hash},{"size",(long)Encoding.UTF8.GetByteCount(value)}});
    if(local!=null){Directory.CreateDirectory(Path.GetDirectoryName(local)!);File.Copy(temp,local);}return hash;
   }
   await Add("src/Spectra/Cached.cs","cached",Path.Combine(updates,"testing/source/src/Spectra/Cached.cs"));
   await Add("src/Spectra/Common.cs","common",Path.Combine(updates,"stable/source/src/Spectra/Common.cs"));
   await Add("src/Spectra/Legacy.cs","legacy",Path.Combine(updates,"source/src/Spectra/Legacy.cs"));
   await Add("src/Spectra/Web/app.js","installed web",Path.Combine(install,"Web/app.js"));
   var changed=await Add("src/Spectra/Changed.cs","changed");
   var stale=Path.Combine(updates,"testing/source/src/Spectra/Changed.cs");await File.WriteAllTextAsync(stale,"stale!!");
   var fresh=await Add("src/Spectra/New.cs","fresh");await Add("tests/big-fixture.txt","not a build input");await Add("dev-server/worker.mjs","not installed");await Add("src/Spectra/obj/temporary.cs","not source");
   await SourceUpdateFiles.Stage(tree,Path.Combine(root,"first"),updates,install,new string('a',40),channel,client,_=>{});
   Check(handler.Requests.Count==2&&handler.Requests.Contains(changed)&&handler.Requests.Contains(fresh),"Unchanged, installed or legacy source was downloaded again");
   Check(!File.Exists(Path.Combine(root,"first/tests/big-fixture.txt")),"Updater downloaded development-only files");
   handler.Requests.Clear();await SourceUpdateFiles.Stage(tree,Path.Combine(root,"second"),updates,install,new string('a',40),channel,client,_=>{});
   Check(handler.Requests.Count==0,"Verified cache was not reusable before an apply/build");

   tree.Clear();var saved=await Add("src/Spectra/Saved.cs","persist before failed update");var denied=await Add("src/Spectra/Denied.cs","retry me");handler.Denied=denied;handler.Requests.Clear();
   try{await SourceUpdateFiles.Stage(tree,Path.Combine(root,"failed"),updates,install,new string('a',40),channel,client,_=>{});throw new Exception("401 accepted");}catch(HttpRequestException ex) when(ex.StatusCode==HttpStatusCode.Unauthorized){}
   Check(File.Exists(Path.Combine(updates,"testing/source/src/Spectra/Saved.cs")),"Successful download lost when another file failed");
   handler.Denied=null;handler.Requests.Clear();await SourceUpdateFiles.Stage(tree,Path.Combine(root,"resumed"),updates,install,new string('a',40),channel,client,_=>{});
   Check(handler.Requests.SequenceEqual(new[]{denied}),"Retry downloaded already verified files");

   tree.Clear();handler.Corrupt=await Add("src/Spectra/Corrupt.cs","correct");
   try{await SourceUpdateFiles.Stage(tree,Path.Combine(root,"corrupt"),updates,install,new string('a',40),channel,client,_=>{});throw new Exception("Corrupt download accepted");}catch(InvalidDataException){}
   Check(!File.Exists(Path.Combine(updates,"testing/source/src/Spectra/Corrupt.cs")),"Corrupt response entered the cache");handler.Corrupt=null;
   tree.Clear();for(int i=0;i<12;i++)await Add("src/Spectra/File"+i+".cs","parallel "+i);
   handler.Maximum=0;await SourceUpdateFiles.Stage(tree,Path.Combine(root,"parallel"),updates,install,new string('a',40),channel,client,_=>{});
   Check(handler.Maximum>1&&handler.Maximum<=6,"Download concurrency is unbounded or serial");
   tree.Clear();await Add("../escape.cs","unsafe");try{await SourceUpdateFiles.Stage(tree,Path.Combine(root,"unsafe"),updates,install,new string('a',40),channel,client,_=>{});throw new Exception("Unsafe path accepted");}catch(IOException){}
  }
  finally{if(root.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);}
  Console.WriteLine("PASS incremental sources: installed/cross-channel/legacy reuse, changed-only requests, resume after failure, corruption rejection, build-input filtering and bounded parallel downloads");
 }
}
