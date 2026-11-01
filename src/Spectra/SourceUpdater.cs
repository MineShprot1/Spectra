using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Spectra;
public sealed record SourceUpdateCheck(bool Checked,bool Available,string Update,string Version="",string CommitName="",string Commit="",string Channel="stable") { public bool Automatic=>true; }
public sealed record SourceUpdateChannel
{
 public string Repository {get;}public string Token {get;}public string Name {get;}
 public string Api {get;}
 SourceUpdateChannel(string repository,string token,string name,string? gateway=null){Repository=repository;Token=token;Name=name;Api=gateway==null?"https://api.github.com/repos/"+Repository:gateway.TrimEnd('/')+"/v1/testing/github";}
 public static SourceUpdateChannel Stable {get;}=new("MineShprot1/Spectra","","stable");
 public static SourceUpdateChannel Testing(string service,string secret)=>new("MineShprot1/Spectra-Testing",secret,"testing",service);
 public HttpRequestMessage Request(string path){var request=new HttpRequestMessage(HttpMethod.Get,Api+path);if(Token!="")request.Headers.Authorization=new("Bearer",Token);return request;}
 public override string ToString()=>Name+":"+Repository;
}
public static class SourceUpdater
{
 public sealed record ReleaseIdentity(string Channel,string Version);
 public static ReleaseIdentity ParseRelease(string json,string expectedChannel)
 {
  var value=JsonSerializer.Deserialize<ReleaseIdentity>(json,Store.Json)??throw new IOException("Неверный канал обновления");
  var pattern=expectedChannel=="stable"?@"\A\d+\.\d+\.\d+\z":@"\A\d+\.\d+\.\d+-test\.\d+\z";
  if(value.Channel!=expectedChannel||value.Version==null||!Regex.IsMatch(value.Version,pattern))throw new IOException("Неверный канал обновления");return value;
 }
 public static bool NewerVersion(string candidate,string current)
 {
  static int[] Parts(string value)=>Regex.Matches(value,@"\d+").Select(m=>int.TryParse(m.Value,out var number)?number:0).ToArray();
  var left=Parts(candidate);var right=Parts(current);for(var i=0;i<Math.Max(left.Length,right.Length);i++){var a=i<left.Length?left[i]:0;var b=i<right.Length?right[i]:0;if(a!=b)return a>b;}return false;
 }
 static async Task<ReleaseIdentity> ReadRelease(SourceUpdateChannel channel,string sha,HttpClient client,CancellationToken cancellation)
 {
  using var request=channel.Request("/contents/launcher-channel.json?ref="+sha);request.Headers.Accept.ParseAdd("application/vnd.github.raw+json");request.Headers.CacheControl=new(){NoCache=true};
  using var response=await client.SendAsync(request,cancellation).ConfigureAwait(false);response.EnsureSuccessStatusCode();var json=await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);if(json.Length>4096)throw new IOException("Неверный канал обновления");return ParseRelease(json,channel.Name);
 }
 public static bool ValidCommit(string sha)=>Regex.IsMatch(sha,@"^[0-9a-f]{40}$");
 public static string InstalledCommit(string? directory=null)
 {
  var marker=Path.Combine(directory??AppContext.BaseDirectory,"build-commit.txt");try{if(File.Exists(marker)){var value=File.ReadAllText(marker).Trim().ToLowerInvariant();if(ValidCommit(value))return value;}}catch(IOException){}catch(UnauthorizedAccessException){}
  var info=System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion??"";
  return Regex.Match(info,@"\+([a-f0-9]{40})(?:\W|$)",RegexOptions.IgnoreCase).Groups[1].Value.ToLowerInvariant();
 }
 public static async Task<SourceUpdateCheck> Check(HttpClient? client=null,string? directory=null,CancellationToken cancellation=default,SourceUpdateChannel? channel=null)
 {
  channel??=SourceUpdateChannel.Stable;
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(8));
  try{
   using var request=channel.Request("/commits?per_page=1&sha=main");request.Headers.CacheControl=new System.Net.Http.Headers.CacheControlHeaderValue{NoCache=true};
   using var response=await (client??Net.Http).SendAsync(request,timeout.Token).ConfigureAwait(false);
   if(!response.IsSuccessStatusCode)return new(false,false,"Проверка коммитов недоступна · GitHub "+(int)response.StatusCode);
   var raw=await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);if(raw.Length>1024*1024)return new(false,false,"Сервер обновлений вернул неверный ответ");
   if(JsonNode.Parse(raw) is not JsonArray {Count:>0} commits||commits[0] is not JsonObject item||!ValidCommit(item.Str("sha")))return new(false,false,"Сервер обновлений вернул неверный ответ");
   var sha=item.Str("sha");var release=await ReadRelease(channel,sha,client??Net.Http,timeout.Token).ConfigureAwait(false);
   var installed=InstalledCommit(directory);var name=item["commit"].Str("message").Split('\n')[0].Trim();if(name=="")name=sha[..7];var available=BuildIdentity.Channel!=channel.Name||installed!=sha&&NewerVersion(release.Version,BuildIdentity.Version);
   return new(true,available,available?"Найдено обновление Spectra "+release.Version:"Исходники Spectra актуальны",release.Version,name,sha,channel.Name);
  }catch(OperationCanceledException) when(cancellation.IsCancellationRequested){throw;}
  catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException or IOException){return new(false,false,"Не удалось проверить коммиты · можно продолжить запуск");}
 }
 static async Task<JsonNode> GithubJson(SourceUpdateChannel channel,string path){using var request=channel.Request(path);using var response=await Net.Http.SendAsync(request);response.EnsureSuccessStatusCode();return JsonNode.Parse(await response.Content.ReadAsStringAsync())??throw new IOException("Пустой ответ сервера");}
 public static async Task Prepare(string sha,Store store,Action<object> emit,Func<bool>? background=null,SourceUpdateChannel? channel=null)
 {
  channel??=SourceUpdateChannel.Stable;
  if(!ValidCommit(sha))throw new IOException("Некорректный SHA коммита");
  var release=await ReadRelease(channel,sha,Net.Http,CancellationToken.None);
  if(BuildIdentity.Channel==channel.Name&&!NewerVersion(release.Version,BuildIdentity.Version))throw new IOException("Эта версия уже установлена или устарела");
  if(InstalledCommit()==sha&&BuildIdentity.Channel==channel.Name)throw new IOException("Этот коммит уже установлен");
  var sdk=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};sdk.ArgumentList.Add("--list-sdks");
  try{using var p=Process.Start(sdk)??throw new IOException("Не удалось найти .NET SDK");var output=await p.StandardOutput.ReadToEndAsync();await p.WaitForExitAsync();if(!output.Split('\n').Any(x=>x.TrimStart().StartsWith("8.")))throw new IOException("Для обновления из исходников установите .NET SDK 8 x64");}
  catch(System.ComponentModel.Win32Exception){throw new IOException("Для обновления из исходников установите .NET SDK 8 x64");}
  var job=Path.Combine(Path.GetTempPath(),"SpU",Guid.NewGuid().ToString("N")[..12]);var source=Path.Combine(job,"source");var publish=Path.Combine(job,"publish");var cache=Path.Combine(store.Root,"updates",channel.Name,"source");Directory.CreateDirectory(source);
  bool handedOff=false;
  try
  {
   var commit=await GithubJson(channel,"/commits/"+sha);var treeSha=commit?["commit"]?["tree"].Str("sha")??"";if(!ValidCommit(treeSha))throw new IOException("Нет дерева коммита");
   var commitName=commit?["commit"].Str("message").Split('\n')[0].Trim()??sha[..7];
   var tree=await GithubJson(channel,"/git/trees/"+treeSha+"?recursive=1");if(tree?["truncated"]?.GetValue<bool>()==true)throw new IOException("Дерево GitHub неполное");
   var files=tree?["tree"]?.AsArray().Where(x=>x.Str("type")=="blob").ToArray()??[];if(files.Length>10000)throw new IOException("Слишком большой репозиторий");
   long total=0;
   var downloads=new List<(string Path,string Destination,string Hash,long Size)>();
   foreach(var file in files)
   {
    var path=file.Str("path");if(file.Str("mode") is not ("100644" or "100755"))throw new IOException("Ссылки в исходниках не поддерживаются");
    if(path.Split('/').Any(x=>x is "." or ".."||x.Contains(':')||x.Contains('\\'))||path.StartsWith('/'))throw new IOException("Небезопасный путь исходника");
    if(Path.GetFileName(path) is "api-keys.local.json" or "build-commit.txt" or "build-commit-name.txt")continue;
    if(path.Split('/').Any(x=>x is "bin" or "obj" or ".git" or "node_modules"))continue;
    var size=file?["size"]?.GetValue<long>()??0;total+=size;if(size<0||size>32*1024*1024||total>256L*1024*1024)throw new IOException("Исходники слишком большие");
    var dest=Store.SafePath(source,path);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);var old=Store.SafePath(cache,path);var hash=file.Str("sha");
    if(File.Exists(old)&&await GitHash(old)==hash)File.Copy(old,dest);
    else downloads.Add((path,dest,hash,size));
   }
   // Reuse one HTTP connection pool and bound concurrency instead of serial requests.
   var downloadTotal=downloads.Sum(x=>x.Size);long received=0,lastReport=0;int completed=0;var progressLock=new object();
   void Report(long delta,bool finished=false)
   {
    lock(progressLock)
    {
     received+=delta;if(finished)completed++;var now=Environment.TickCount64;
     if(!finished&&now-lastReport<150)return;lastReport=now;
     emit(new{type="progress",message=$"Исходники · {completed}/{downloads.Count} файлов",downloadedBytes=received,totalBytes=downloadTotal,percent=downloadTotal>0?received*100d/downloadTotal:100,scope="update"});
    }
   }
   Report(0);
   await Parallel.ForEachAsync(downloads,new ParallelOptions{MaxDegreeOfParallelism=channel.Name=="testing"?2:8},async(file,cancellation)=>
   {
    if(channel.Name=="testing")await Task.Delay(300,cancellation);
    using var request=channel.Name=="testing"?channel.Request("/git/blobs/"+file.Hash):new HttpRequestMessage(HttpMethod.Get,"https://raw.githubusercontent.com/"+channel.Repository+"/"+sha+"/"+string.Join('/',file.Path.Split('/').Select(Uri.EscapeDataString)));
    if(channel.Name=="testing")request.Headers.Accept.ParseAdd("application/vnd.github.raw+json");
    using var response=await Net.Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation);response.EnsureSuccessStatusCode();
    await using(var input=await response.Content.ReadAsStreamAsync(cancellation))
    await using(var output=new FileStream(file.Destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
    {
     var buffer=new byte[65536];long fileReceived=0;int count;
     while((count=await input.ReadAsync(buffer.AsMemory(),cancellation))>0)
     {
      fileReceived+=count;if(fileReceived>file.Size)throw new IOException("Размер исходника не совпадает: "+file.Path);
      await output.WriteAsync(buffer.AsMemory(0,count),cancellation);Report(count);
     }
     if(fileReceived!=file.Size)throw new IOException("Исходник загружен не полностью: "+file.Path);
    }
    if(await GitHash(file.Destination)!=file.Hash)throw new IOException("Контрольная сумма Git не совпала: "+file.Path);
    Report(0,true);
   });
   var project=Path.Combine(source,"src","Spectra","Spectra.csproj");if(!File.Exists(project))throw new IOException("В корне репозитория нет src/Spectra/Spectra.csproj");
   // The marker is part of the rebuilt application, not inferred from its version number.
   await File.WriteAllTextAsync(Path.Combine(source,"src","Spectra","build-commit.txt"),sha);
   emit(new{type="progress",message="Компиляция нового коммита · текущая версия ещё работает",percent=0});
   var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=source};
   foreach(var arg in new[]{"publish",project,"-c","Release","-r","win-x64","--self-contained","true","-o",publish})start.ArgumentList.Add(arg);
   start.ArgumentList.Add("-p:SpectraChannel="+channel.Name);
   if(channel.Name=="testing"){
    var number=JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(source,"testing-build.json")))?["number"]?.GetValue<int>()??0;if(number<1||number>65534)throw new IOException("Invalid testing build number");start.ArgumentList.Add("-p:TestingBuildNumber="+number);
   }
   start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"]="1";
   start.Environment["DOTNET_CLI_UI_LANGUAGE"]="en";
   start.StandardOutputEncoding=Encoding.UTF8;start.StandardErrorEncoding=Encoding.UTF8;
   using(var p=Process.Start(start)??throw new IOException("Не удалось запустить сборку"))
   {
    var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();using var buildTimeout=new CancellationTokenSource(TimeSpan.FromMinutes(15));
    try{await p.WaitForExitAsync(buildTimeout.Token);}catch(OperationCanceledException){p.Kill(true);throw new IOException("Сборка обновления превысила 15 минут");}
    var log=await stdout+"\n"+await stderr;await File.WriteAllTextAsync(Path.Combine(store.Root,"updates","last-build.log"),log);
   if(p.ExitCode!=0||!File.Exists(Path.Combine(publish,"Spectra.exe")))throw new IOException("Новый коммит не собрался. Текущая версия сохранена; подробности: updates/last-build.log");
   }
   var builtVersion=FileVersionInfo.GetVersionInfo(Path.Combine(publish,"Spectra.exe")).ProductVersion?.Split('+')[0];if(builtVersion!=release.Version)throw new IOException("Версия сборки не совпадает с каналом обновления");
   await File.WriteAllTextAsync(Path.Combine(publish,"build-commit.txt"),sha);
   await File.WriteAllTextAsync(Path.Combine(publish,"build-commit-name.txt"),commitName);
   var localOauth=Path.Combine(AppContext.BaseDirectory,"microsoft-oauth.json");if(File.Exists(localOauth))File.Copy(localOauth,Path.Combine(publish,"microsoft-oauth.json"),true);
   var currentFiles=Directory.EnumerateFiles(publish,"*",SearchOption.AllDirectories).Select(p=>Path.GetRelativePath(publish,p)).ToArray();
   await File.WriteAllTextAsync(Path.Combine(publish,"installed-files.json"),JsonSerializer.Serialize(currentFiles));
   var hashes=new Dictionary<string,string>();
   foreach(var path in Directory.EnumerateFiles(publish,"*",SearchOption.AllDirectories))hashes[Path.GetRelativePath(publish,path)]=await Net.Hash(path,"SHA256");
   var hashesFile=Path.Combine(job,"hashes.json");await File.WriteAllTextAsync(hashesFile,JsonSerializer.Serialize(hashes));
   var helper=Path.Combine(job,"ApplyUpdate.ps1");File.Copy(Path.Combine(publish,"ApplyUpdate.ps1"),helper);
   var manifest=Path.Combine(job,"job.json");await File.WriteAllTextAsync(manifest,JsonSerializer.Serialize(new{hashesFile,report=Path.Combine(store.Root,"updates","last-apply.json"),pid=Environment.ProcessId,install=AppContext.BaseDirectory,publish,source,cache,job,commit=sha,background=background?.Invoke()??false,failureMarker=Path.Combine(store.Root,"updates","failed-commit.txt")},Store.Json));
   bool elevate=new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}.Any(root=>!string.IsNullOrEmpty(root)&&AppContext.BaseDirectory.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));var probe=Path.Combine(AppContext.BaseDirectory,".spectra-write-"+Guid.NewGuid().ToString("N"));
   try{File.WriteAllText(probe,"");File.Delete(probe);}catch(UnauthorizedAccessException){elevate=true;}
   var helperStart=new ProcessStartInfo("powershell.exe"){UseShellExecute=elevate,CreateNoWindow=!elevate,WorkingDirectory=job};
   helperStart.WindowStyle=ProcessWindowStyle.Hidden;if(elevate)helperStart.Verb="runas";
   foreach(var arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",helper,"-JobFile",manifest})helperStart.ArgumentList.Add(arg);
   _=Process.Start(helperStart)??throw new IOException("Не удалось запустить замену обновления");handedOff=true;
  }
  finally{if(!handedOff&&Directory.Exists(job))Directory.Delete(job,true);}
 }
 public static async Task<string> GitHash(string path)
 {
  await using var input=File.OpenRead(path);using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
  hash.AppendData(Encoding.ASCII.GetBytes("blob "+input.Length+"\0"));var buffer=new byte[81920];int read;while((read=await input.ReadAsync(buffer))>0)hash.AppendData(buffer.AsSpan(0,read));return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
 }
}
