using System.IO;
using System.IO.Compression;
using System.Text.Json;
namespace Spectra.Installer;
public sealed record SetupProgress(string Phase,string File,long Done,long Total);
public static class InstallEngine
{
 public static string Target(string value){if(string.IsNullOrWhiteSpace(value)||!Path.IsPathFullyQualified(value))throw new IOException("Choose an absolute installation folder.");var full=Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar);if(full==Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar)||File.Exists(full))throw new IOException("Choose a dedicated folder for Spectra.");return full;}
 public static string Under(string root,string relative){if(Path.IsPathRooted(relative)||relative.Contains(':'))throw new IOException("Invalid package path.");var full=Path.GetFullPath(Path.Combine(root,relative));if(!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid package path.");return full;}
 static void NoLinks(string path){for(var node=new DirectoryInfo(path);node!=null;node=node.Parent)if(node.Exists&&(node.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Installation through a directory link is not supported.");}
 public static void Install(Stream payload,string destination,string language,IProgress<SetupProgress> progress,CancellationToken cancellation)
 {
  destination=Target(destination);NoLinks(destination);if(!SetupText.Languages.ContainsKey(language))throw new IOException("Unsupported language.");
  using var zip=new ZipArchive(payload,ZipArchiveMode.Read,true);if(zip.Entries.Count>20000||zip.Entries.Sum(e=>e.Length)>2L*1024*1024*1024)throw new IOException("Package is too large.");
  var staging=Path.Combine(Path.GetTempPath(),"SpectraInstall",Guid.NewGuid().ToString("N"));var fresh=Path.Combine(staging,"new");var backup=Path.Combine(staging,"backup");Directory.CreateDirectory(fresh);Directory.CreateDirectory(backup);
  bool keepBackup=false;var changed=new List<string>();var created=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long done=0,total=zip.Entries.Sum(e=>e.Length),last=0;var buffer=new byte[131072];
  void Report(string phase,string file,bool force=false){var now=Environment.TickCount64;if(force||now-last>=120){last=now;progress.Report(new(phase,file,done,total));}}
  try{
   foreach(var entry in zip.Entries){cancellation.ThrowIfCancellationRequested();if(entry.FullName.EndsWith('/'))continue;if(!seen.Add(entry.FullName.Replace('/','\\')))throw new IOException("Duplicate package entry.");var file=Under(fresh,entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(file)!);using var input=entry.Open();using var output=File.Create(file);int count;while((count=input.Read(buffer))>0){cancellation.ThrowIfCancellationRequested();output.Write(buffer,0,count);done+=count;Report("prepare",entry.FullName);}}
   if(!File.Exists(Path.Combine(fresh,"Spectra.exe")))throw new IOException("Package has no Spectra.exe.");
   File.WriteAllText(Path.Combine(fresh,"install-preferences.json"),JsonSerializer.Serialize(new{language,id=Guid.NewGuid().ToString("N")}));
   var files=Directory.GetFiles(fresh,"*",SearchOption.AllDirectories);done=0;total=files.LongLength;Directory.CreateDirectory(destination);
   foreach(var file in files){cancellation.ThrowIfCancellationRequested();var relative=Path.GetRelativePath(fresh,file);var installed=Under(destination,relative);NoLinks(Path.GetDirectoryName(installed)!);Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
    if(File.Exists(installed)){if((File.GetAttributes(installed)&FileAttributes.ReparsePoint)!=0)throw new IOException("Cannot replace a linked file.");var saved=Under(backup,relative);Directory.CreateDirectory(Path.GetDirectoryName(saved)!);File.Copy(installed,saved);changed.Add(relative);}else created.Add(relative);
    File.Copy(file,installed,true);done++;Report("copy",relative);
   }Report("finish",destination,true);
  }catch{var failed=new List<string>();foreach(var relative in changed)try{File.Copy(Under(backup,relative),Under(destination,relative),true);}catch{failed.Add(relative);}foreach(var relative in created)try{File.Delete(Under(destination,relative));}catch{failed.Add(relative);}if(failed.Count>0){keepBackup=true;throw new IOException("Restore incomplete. Backup retained: "+backup);}throw;}
  finally{if(!keepBackup)try{Directory.Delete(staging,true);}catch{}}
 }
}
