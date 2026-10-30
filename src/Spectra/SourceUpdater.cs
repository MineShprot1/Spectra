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
public sealed record SourceUpdateCheck(bool Checked,bool Available,string Update,string Version="",string CommitName="",string Commit="") { public bool Automatic=>true; }
public static class SourceUpdater
{
 const string Repo="https://api.github.com/repos/MineShprot1/Spectra";
 public static bool ValidCommit(string sha)=>Regex.IsMatch(sha,@"^[0-9a-f]{40}$");
 public static string InstalledCommit(string? directory=null)
 {
  var marker=Path.Combine(directory??AppContext.BaseDirectory,"build-commit.txt");try{if(File.Exists(marker)){var value=File.ReadAllText(marker).Trim().ToLowerInvariant();if(ValidCommit(value))return value;}}catch(IOException){}catch(UnauthorizedAccessException){}
  var info=System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion??"";
  return Regex.Match(info,@"\+([a-f0-9]{40})(?:\W|$)",RegexOptions.IgnoreCase).Groups[1].Value.ToLowerInvariant();
 }
 public static async Task<SourceUpdateCheck> Check(HttpClient? client=null,string? directory=null,CancellationToken cancellation=default)
 {
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(8));
  try{
   using var request=new HttpRequestMessage(HttpMethod.Get,Repo+"/commits?per_page=1");request.Headers.CacheControl=new System.Net.Http.Headers.CacheControlHeaderValue{NoCache=true};
   using var response=await (client??Net.Http).SendAsync(request,timeout.Token).ConfigureAwait(false);
   if(!response.IsSuccessStatusCode)return new(false,false,"Проверка коммитов недоступна · GitHub "+(int)response.StatusCode);
   var raw=await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);if(raw.Length>1024*1024)return new(false,false,"Сервер обновлений вернул неверный ответ");
   if(JsonNode.Parse(raw) is not JsonArray {Count:>0} commits||commits[0] is not JsonObject item||!ValidCommit(item.Str("sha")))return new(false,false,"Сервер обновлений вернул неверный ответ");
   var sha=item.Str("sha");var installed=InstalledCommit(directory);var name=item["commit"].Str("message").Split('\n')[0].Trim();if(name=="")name=sha[..7];
   return new(true,installed!=sha,installed==sha?"Исходники Spectra актуальны":"Найден новый коммит Spectra "+sha[..7],name,name,sha);
  }catch(OperationCanceledException) when(cancellation.IsCancellationRequested){throw;}
  catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException or IOException){return new(false,false,"Не удалось проверить коммиты · можно продолжить запуск");}
 }
 public static async Task Prepare(string sha,Store store,Action<object> emit,Func<bool>? background=null)
 {
  if(!ValidCommit(sha))throw new IOException("Некорректный SHA коммита");
  if(InstalledCommit()==sha)throw new IOException("Этот коммит уже установлен");
  var sdk=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};sdk.ArgumentList.Add("--list-sdks");
  try{using var p=Process.Start(sdk)??throw new IOException("Не удалось найти .NET SDK");var output=await p.StandardOutput.ReadToEndAsync();await p.WaitForExitAsync();if(!output.Split('\n').Any(x=>x.TrimStart().StartsWith("8.")))throw new IOException("Для обновления из исходников установите .NET SDK 8 x64");}
  catch(System.ComponentModel.Win32Exception){throw new IOException("Для обновления из исходников установите .NET SDK 8 x64");}
  var job=Path.Combine(Path.GetTempPath(),"SpU",Guid.NewGuid().ToString("N")[..12]);var source=Path.Combine(job,"source");var publish=Path.Combine(job,"publish");var cache=Path.Combine(store.Root,"updates","source");Directory.CreateDirectory(source);
  bool handedOff=false;
  try
  {
   var commit=await Net.Get(Repo+"/commits/"+sha);var treeSha=commit?["commit"]?["tree"].Str("sha")??"";if(!ValidCommit(treeSha))throw new IOException("Нет дерева коммита");
   var commitName=commit?["commit"].Str("message").Split('\n')[0].Trim()??sha[..7];
   var tree=await Net.Get(Repo+"/git/trees/"+treeSha+"?recursive=1");if(tree?["truncated"]?.GetValue<bool>()==true)throw new IOException("Дерево GitHub неполное");
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
   await Parallel.ForEachAsync(downloads,new ParallelOptions{MaxDegreeOfParallelism=8},async(file,cancellation)=>
   {
    var url="https://raw.githubusercontent.com/MineShprot1/Spectra/"+sha+"/"+string.Join('/',file.Path.Split('/').Select(Uri.EscapeDataString));
    using var response=await Net.Http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,cancellation);response.EnsureSuccessStatusCode();
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
