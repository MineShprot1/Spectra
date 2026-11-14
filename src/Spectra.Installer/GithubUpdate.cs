using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Spectra.Installer;
public sealed class UpdatePackage(string folder,string package,string version):IDisposable
{public string File=>package;public string Version=>version;public void Dispose()=>GithubUpdate.Cleanup(folder);}
public static class GithubUpdate
{
 const string Api="https://api.github.com/repos/MineShprot1/Spectra";
 static readonly HttpClient Http=new(){Timeout=TimeSpan.FromMinutes(30)};
 static GithubUpdate(){Http.DefaultRequestHeaders.UserAgent.ParseAdd("SpectraSetup/0.28.2");}
 public static void Cleanup(string folder){var full=Path.GetFullPath(folder);var allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"SpectraUpdate"))+Path.DirectorySeparatorChar;if(!full.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))return;try{Directory.Delete(full,true);}catch{}}
 static async Task<JsonNode> Json(string url,CancellationToken ct){using var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.CacheControl=new(){NoCache=true};using var response=await Http.SendAsync(request,ct);response.EnsureSuccessStatusCode();var text=await response.Content.ReadAsStringAsync(ct);if(text.Length>16*1024*1024)throw new IOException("Response too large.");return JsonNode.Parse(text)??throw new IOException("Empty response.");}
 static async Task Download(string url,string file,CancellationToken ct,long limit){using var response=await Http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>limit)throw new IOException("Download too large.");await using var input=await response.Content.ReadAsStreamAsync(ct);await using var output=System.IO.File.Create(file);var buffer=new byte[131072];int count;long read=0;while((count=await input.ReadAsync(buffer,ct))>0){read+=count;if(read>limit)throw new IOException("Download too large.");await output.WriteAsync(buffer.AsMemory(0,count),ct);}}
 static string Sha(string value){if(!Regex.IsMatch(value,@"\A[a-f0-9]{40}\z"))throw new IOException("Invalid commit.");return value;}
 static string GitHash(string file){using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA1);using var stream=System.IO.File.OpenRead(file);hash.AppendData(Encoding.UTF8.GetBytes("blob "+stream.Length+"\0"));var b=new byte[131072];int n;while((n=stream.Read(b))>0)hash.AppendData(b.AsSpan(0,n));return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();}
 public static async Task<UpdatePackage> Prepare(string installed,IProgress<SetupProgress> progress,CancellationToken ct)
 {
  var job=Path.Combine(Path.GetTempPath(),"SpectraUpdate",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(job);
  try{
   progress.Report(new("download","GitHub · main",0,0));var head=await Json(Api+"/commits/main",ct);var sha=Sha(head["sha"]!.ToString());var treeSha=Sha(head["commit"]!["tree"]!["sha"]!.ToString());
   var manifest=await Json(Api+"/contents/launcher-channel.json?ref="+sha,ct);var release=JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(manifest["content"]!.ToString().Replace("\n",""))))!;var version=release["version"]?.ToString()??"";
   if(release["channel"]?.ToString()!="stable"||!Regex.IsMatch(version,@"\A\d+\.\d+\.\d+\z"))throw new IOException("GitHub does not point to a stable release.");
   var tree=await Json(Api+"/git/trees/"+treeSha+"?recursive=1",ct);if(tree["truncated"]?.GetValue<bool>()==true)throw new IOException("Incomplete source tree.");
   var source=Path.Combine(job,"source");Directory.CreateDirectory(source);var files=tree["tree"]!.AsArray().Where(f=>f?["type"]?.ToString()=="blob"&&(f["path"]!.ToString().StartsWith("src/Spectra/",StringComparison.Ordinal)||new[]{"Directory.Build.props","Directory.Build.targets","NuGet.Config","global.json","launcher-channel.json"}.Contains(f["path"]!.ToString()))).ToArray();
   if(files.Length>10000||files.Sum(f=>f!["size"]!.GetValue<long>())>256L*1024*1024)throw new IOException("Source too large.");long completed=0;
   await Parallel.ForEachAsync(files,new ParallelOptions{MaxDegreeOfParallelism=6,CancellationToken=ct},async(f,token)=>{var path=f!["path"]!.ToString();if(path.Split('/').Any(p=>p is "bin" or "obj" or ".git" or "node_modules"))return;if(f["mode"]?.ToString() is not ("100644" or "100755"))throw new IOException("Unsupported source entry.");var size=f["size"]!.GetValue<long>();if(size<0||size>32*1024*1024)throw new IOException("Source file too large.");var dest=InstallEngine.Under(source,path);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);var expected=Sha(f["sha"]!.ToString());var reuse=path.StartsWith("src/Spectra/",StringComparison.Ordinal)?InstallEngine.Under(installed,path["src/Spectra/".Length..]):"";
    if(System.IO.File.Exists(reuse)&&new FileInfo(reuse).Length==size&&GitHash(reuse)==expected)System.IO.File.Copy(reuse,dest);else await Download("https://raw.githubusercontent.com/MineShprot1/Spectra/"+sha+"/"+path,dest,token,32*1024*1024);
    if(GitHash(dest)!=expected)throw new IOException("Source checksum mismatch.");var n=Interlocked.Increment(ref completed);if(n%8==0||n==files.Length)progress.Report(new("download","GitHub · "+n+" / "+files.Length,n,files.Length));});
   var dotnet=await Sdk(job,progress,ct);var publish=Path.Combine(job,"publish");progress.Report(new("prepare",".NET · "+version,0,0));
   System.IO.File.WriteAllText(Path.Combine(source,"src/Spectra/build-commit.txt"),sha);System.IO.File.WriteAllText(Path.Combine(source,"src/Spectra/build-commit-name.txt"),head["commit"]!["message"]!.ToString().Split('\n')[0]);
   var info=new ProcessStartInfo(dotnet){WorkingDirectory=source,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{"publish","src/Spectra/Spectra.csproj","-c","Release","-r","win-x64","--self-contained","true","-p:SpectraChannel=stable","-o",publish})info.ArgumentList.Add(arg);info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"]="1";
   using var process=Process.Start(info)??throw new IOException("Cannot start .NET SDK.");var stdout=process.StandardOutput.ReadToEndAsync(ct);var stderr=process.StandardError.ReadToEndAsync(ct);try{await process.WaitForExitAsync(ct);}catch{try{process.Kill(true);}catch{}throw;}var log=await stdout+"\n"+await stderr;System.IO.File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SpectraSetup-build.log"),log);if(process.ExitCode!=0)throw new IOException("Build failed. Details: %LOCALAPPDATA%/SpectraSetup-build.log");
   var package=Path.Combine(job,"latest.zip");ZipFile.CreateFromDirectory(publish,package,CompressionLevel.Fastest,false);return new(job,package,version);
  }catch{Cleanup(job);throw;}
 }
 static async Task<string> Sdk(string job,IProgress<SetupProgress> progress,CancellationToken ct)
 {
  var system=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"dotnet","dotnet.exe");
  if(System.IO.File.Exists(system))try{using var probe=Process.Start(new ProcessStartInfo(system,"--list-sdks"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true});if(probe!=null){var list=await probe.StandardOutput.ReadToEndAsync(ct);await probe.WaitForExitAsync(ct);if(list.Split('\n').Any(line=>line.StartsWith("8.")))return system;}}catch(System.ComponentModel.Win32Exception){}
  var metadata=await Json("https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json",ct);var latest=metadata["latest-sdk"]!.ToString();if(!Regex.IsMatch(latest,@"\A8\.\d+\.\d+\z"))throw new IOException("Invalid SDK version.");var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SpectraSetup","sdk",latest);var exe=Path.Combine(root,"dotnet.exe");var marker=Path.Combine(root,"verified.txt");if(System.IO.File.Exists(exe)&&System.IO.File.Exists(marker))return exe;
  var sdk=metadata["releases"]!.AsArray().SelectMany(r=>r?["sdks"]?.AsArray()??new JsonArray()).First(r=>r?["version"]?.ToString()==latest)!;
  var asset=sdk["files"]!.AsArray().First(f=>f?["rid"]?.ToString()=="win-x64"&&f["name"]!.ToString().EndsWith(".zip"))!;var url=new Uri(asset["url"]!.ToString());if(url.Scheme!="https"||url.Host is not ("builds.dotnet.microsoft.com" or "download.visualstudio.microsoft.com"))throw new IOException("Unexpected SDK host.");
  progress.Report(new("download","Microsoft .NET SDK 8 · "+latest,0,0));var archive=Path.Combine(job,"sdk.zip");await Download(url.AbsoluteUri,archive,ct,512L*1024*1024);await using(var file=System.IO.File.OpenRead(archive)){var hash=Convert.ToHexString(await SHA512.HashDataAsync(file,ct));if(!hash.Equals(asset["hash"]!.ToString(),StringComparison.OrdinalIgnoreCase))throw new IOException("SDK checksum mismatch.");}
  var staging=Path.Combine(job,"sdk");using(var zip=ZipFile.OpenRead(archive))foreach(var entry in zip.Entries){ct.ThrowIfCancellationRequested();var dest=InstallEngine.Under(staging,entry.FullName);if(entry.FullName.EndsWith('/')){Directory.CreateDirectory(dest);continue;}Directory.CreateDirectory(Path.GetDirectoryName(dest)!);entry.ExtractToFile(dest);}
  System.IO.File.WriteAllText(Path.Combine(staging,"verified.txt"),asset["hash"]!.ToString());Directory.CreateDirectory(root);foreach(var file in Directory.EnumerateFiles(staging,"*",SearchOption.AllDirectories)){ct.ThrowIfCancellationRequested();if(Path.GetFileName(file)=="verified.txt")continue;var dest=InstallEngine.Under(root,Path.GetRelativePath(staging,file));Directory.CreateDirectory(Path.GetDirectoryName(dest)!);System.IO.File.Copy(file,dest,true);}System.IO.File.WriteAllText(marker,asset["hash"]!.ToString());return exe;
 }
}
