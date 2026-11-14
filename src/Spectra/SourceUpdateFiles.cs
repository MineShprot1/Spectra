using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Spectra;

// Git hashes, rather than channel/version labels, decide whether bytes can be reused.
public static class SourceUpdateFiles
{
 public static bool Required(string path)
 {
  if(path.Split('/').Any(p=>p is "bin" or "obj" or ".git" or "node_modules"))return false;
  if(Path.GetFileName(path) is "api-keys.local.json" or "build-commit.txt" or "build-commit-name.txt")return false;
  return path.StartsWith("src/Spectra/",StringComparison.Ordinal)||new[]{"Directory.Build.props","Directory.Build.targets","NuGet.Config","global.json","testing-build.json","launcher-channel.json"}.Contains(path,StringComparer.OrdinalIgnoreCase);
 }
 public static async Task Stage(JsonArray tree,string source,string updates,string install,string sha,SourceUpdateChannel channel,HttpClient client,Action<object> emit,CancellationToken cancellation=default)
 {
  var cache=Path.Combine(updates,channel.Name,"source");
  var roots=new[]{cache,Path.Combine(updates,channel.Name=="testing"?"stable":"testing","source"),Path.Combine(updates,"source")};
  var downloads=new List<(string Path,string Destination,string Hash,long Size)>();
  var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;int reused=0;
  foreach(var file in tree)
  {
   cancellation.ThrowIfCancellationRequested();if(file.Str("type")!="blob")continue;
   var path=file.Str("path");
   if(string.IsNullOrEmpty(path)||path.StartsWith('/')||path.Split('/').Any(p=>p.Length==0||p is "." or ".."||p.Contains(':')||p.Contains('\\')))throw new IOException("Небезопасный путь исходника");
   if(!Required(path))continue;
   if(!paths.Add(path)||paths.Count>10000)throw new IOException("Неверное дерево исходников");
   if(file.Str("mode") is not ("100644" or "100755"))throw new IOException("Ссылки в исходниках не поддерживаются");
   var size=file?["size"]?.GetValue<long>()??-1;var hash=file.Str("sha");total+=size;
   if(!SourceUpdater.ValidCommit(hash)||size<0||size>32*1024*1024||total>256L*1024*1024)throw new IOException("Неверный размер или хеш исходника");
   var dest=Store.SafePath(source,path);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
   var candidates=roots.Select(root=>Store.SafePath(root,path));
   // Published Web assets and helper scripts retain exactly their source bytes.
   const string prefix="src/Spectra/";
   if(path.StartsWith(prefix,StringComparison.Ordinal))candidates=candidates.Append(Store.SafePath(install,path[prefix.Length..]));
   bool found=false;
   foreach(var old in candidates)
   {
    try{if(File.Exists(old)&&new FileInfo(old).Length==size&&await SourceUpdater.GitHash(old)==hash){File.Copy(old,dest);found=true;break;}}
    catch(IOException){}catch(UnauthorizedAccessException){}
   }
   if(found){reused++;Remember(dest,Store.SafePath(cache,path));}else downloads.Add((path,dest,hash,size));
  }
  long received=0,lastReport=0;int completed=0;var downloadTotal=downloads.Sum(f=>f.Size);var progressLock=new object();
  void Report(long delta,bool finished=false)
  {
   lock(progressLock){received+=delta;if(finished)completed++;var now=Environment.TickCount64;if(!finished&&now-lastReport<150)return;lastReport=now;
    emit(new{type="progress",message=$"Исходники · {completed}/{downloads.Count} файлов · {reused} из локальных файлов",downloadedBytes=received,totalBytes=downloadTotal,percent=downloadTotal>0?received*100d/downloadTotal:100,scope="update"});}
  }
  Report(0);
  await Parallel.ForEachAsync(downloads,new ParallelOptions{MaxDegreeOfParallelism=6,CancellationToken=cancellation},async(file,ct)=>
  {
   for(int attempt=0;;attempt++)
   {
    long attemptBytes=0;TimeSpan delay=TimeSpan.FromSeconds(attempt+1);
    try
    {
     using var request=channel.Name=="testing"?channel.Request("/git/blobs/"+file.Hash):new HttpRequestMessage(HttpMethod.Get,"https://raw.githubusercontent.com/"+channel.Repository+"/"+sha+"/"+string.Join('/',file.Path.Split('/').Select(Uri.EscapeDataString)));
     if(channel.Name=="testing")request.Headers.Accept.ParseAdd("application/vnd.github.raw+json");
     using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
     if(response.Headers.RetryAfter?.Delta is TimeSpan wait)delay=TimeSpan.FromSeconds(Math.Clamp(wait.TotalSeconds,1,30));
     if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)response.EnsureSuccessStatusCode();
     response.EnsureSuccessStatusCode();
     await using(var input=await response.Content.ReadAsStreamAsync(ct))
     await using(var output=new FileStream(file.Destination,FileMode.Create,FileAccess.Write,FileShare.None,65536,true))
     {
      var buffer=new byte[65536];int count;
      while((count=await input.ReadAsync(buffer,ct))>0){attemptBytes+=count;if(attemptBytes>file.Size)throw new InvalidDataException("Размер исходника не совпадает: "+file.Path);await output.WriteAsync(buffer.AsMemory(0,count),ct);Report(count);}
      if(attemptBytes!=file.Size)throw new IOException("Исходник загружен не полностью: "+file.Path);
     }
     if(await SourceUpdater.GitHash(file.Destination)!=file.Hash)throw new InvalidDataException("Контрольная сумма Git не совпала: "+file.Path);
     // Persist each verified file before building/applying. Failed attempts resume here.
     Remember(file.Destination,Store.SafePath(cache,file.Path));Report(0,true);break;
    }
    catch(Exception ex) when(attempt<2&&!ct.IsCancellationRequested&&ex is not InvalidDataException&&(ex is IOException or TaskCanceledException||ex is HttpRequestException requestError&&(requestError.StatusCode==null||requestError.StatusCode==(HttpStatusCode)429||(int)requestError.StatusCode>=500)))
    {Report(-attemptBytes);await Task.Delay(delay,ct);}
   }
  });
 }
 static void Remember(string source,string destination)
 {
  // Optional cache failures cannot invalidate a verified staged file.
  string temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(source,temporary);File.Move(temporary,destination,true);}
  catch(IOException){}catch(UnauthorizedAccessException){}
  finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
 }
}
