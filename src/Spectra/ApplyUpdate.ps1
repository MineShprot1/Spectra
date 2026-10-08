param([Parameter(Mandatory=$true)][string]$JobFile)
$ErrorActionPreference='Stop'
# Program Files requires an elevated installer even if the directory itself
# happens to allow creating a probe file. Existing files can have stricter ACLs.
$initialTask=Get-Content -LiteralPath $JobFile -Raw | ConvertFrom-Json
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$principal=New-Object Security.Principal.WindowsPrincipal($identity)
$protectedInstall=$false
foreach($root in @($env:ProgramFiles,${env:ProgramFiles(x86)})){if($root -and $initialTask.install.StartsWith($root.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){$protectedInstall=$true}}
if($protectedInstall -and -not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
 $arguments='-NoProfile -ExecutionPolicy Bypass -File "'+$PSCommandPath+'" -JobFile "'+$JobFile+'"'
 Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -ErrorAction Stop | Out-Null
 exit
}
# Windows PowerShell 5.1 uses legacy .NET path handling. Use Unicode Win32
# file APIs with extended paths throughout the installation transaction.
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
public static class SpectraUpdateFiles {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CopyFileW(string from,string to,bool failIfExists);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool DeleteFileW(string path);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateDirectoryW(string path,IntPtr security);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetFileAttributesW(string path);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 static string Extended(string path) {
  path=path.Replace('/','\\');
  if(path.StartsWith(@"\\?\"))return path;
  if(path.StartsWith(@"\\"))return @"\\?\UNC\"+path.Substring(2);
  if(path.Length<3||path[1]!=':'||path[2]!='\\')throw new IOException("Absolute path required: "+path);
  return @"\\?\"+path;
 }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetFileAttributesW(string path,uint attributes);
 static void Fail(string operation,string path){int error=Marshal.GetLastWin32Error();throw new IOException(operation+": "+path+"; Win32 "+error+": "+new Win32Exception(error).Message); }
 public static bool Exists(string path){return GetFileAttributesW(Extended(path))!=0xffffffff;}
 public static string Under(string root,string relative){
  if(String.IsNullOrEmpty(relative))throw new IOException("Empty update path");
  relative=relative.Replace('/','\\');
  foreach(string part in relative.Split('\\'))if(part.Length==0||part=="."||part==".."||part.IndexOf(':')>=0)throw new IOException("Invalid update path: "+relative);
  // Root is supplied by the running launcher; no legacy GetFullPath call.
  Extended(root);return root.TrimEnd('\\')+"\\"+relative;
 }
 public static void Directory(string path){
  string full=Extended(path).TrimEnd('\\');if(Exists(full))return;
  int separator=full.LastIndexOf('\\');
  if(separator>6)Directory(full.Substring(0,separator));
  if(!CreateDirectoryW(full,IntPtr.Zero)&&Marshal.GetLastWin32Error()!=183)Fail("Create directory",path);
 }
 public static void Copy(string from,string to){
  Directory(to.Substring(0,to.LastIndexOf('\\')));
  string destination=Extended(to);uint attributes=GetFileAttributesW(destination);
  if(attributes!=0xffffffff&&(attributes&1)!=0&&!SetFileAttributesW(destination,attributes&~1u))Fail("Clear read-only attribute",to);
  for(int attempt=0;attempt<20;attempt++){
   if(CopyFileW(Extended(from),destination,false))return;
   int error=Marshal.GetLastWin32Error();
   if((error!=32&&error!=33&&error!=5)||attempt==19)Fail("Copy file",to);
   System.Threading.Thread.Sleep(250);
  }
 }
 public static void Delete(string path){if(Exists(path)&&!DeleteFileW(Extended(path)))Fail("Delete file",path);}
 static FileStream Open(string path,uint access,uint creation){
  SafeFileHandle handle=CreateFileW(Extended(path),access,7,IntPtr.Zero,creation,128,IntPtr.Zero);
  if(handle.IsInvalid){handle.Dispose();Fail("Open file",path);}
  return new FileStream(handle,access==0x80000000?FileAccess.Read:FileAccess.Write);
 }
 public static string Hash(string path){using(var stream=Open(path,0x80000000,3))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
 public static string Read(string path){using(var stream=Open(path,0x80000000,3))using(var reader=new StreamReader(stream))return reader.ReadToEnd();}
 public static void Write(string path,string text){Directory(path.Substring(0,path.LastIndexOf('\\')));using(var stream=Open(path,0x40000000,2))using(var writer=new StreamWriter(stream))writer.Write(text);}
}
'@
$task=[SpectraUpdateFiles]::Read($JobFile) | ConvertFrom-Json
$backup=[SpectraUpdateFiles]::Under($task.job,'backup')
$changed=New-Object 'System.Collections.Generic.List[string]'
$created=New-Object 'System.Collections.Generic.List[string]'
function Under([string]$Root,[string]$Relative){return [SpectraUpdateFiles]::Under($Root,$Relative)}
function ReportResult([bool]$Success,[string]$Message){
 if($task.report){$report=@{success=$Success;message=$Message;commit=$task.commit;install=$task.install;time=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json;[SpectraUpdateFiles]::Write($task.report,$report)}
}
try {
 $running=Get-Process -Id $task.pid -ErrorAction SilentlyContinue
 if($running){Wait-Process -Id $task.pid -Timeout 45 -ErrorAction Stop}
 [SpectraUpdateFiles]::Directory($backup)
 # ConvertFrom-Json in Windows PowerShell may emit the whole JSON array as
 # one pipeline object. Enumerate explicitly; never cast an array to a path.
 $fresh=New-Object 'System.Collections.Generic.List[string]'
 $manifest=[SpectraUpdateFiles]::Read((Under $task.publish 'installed-files.json')) | ConvertFrom-Json
 foreach($entry in $manifest){if($entry -isnot [string]){throw 'Invalid file manifest entry'};$fresh.Add($entry)}
 $fresh.Add('installed-files.json')
 $oldManifest=Under $task.install 'installed-files.json'
 $old=New-Object 'System.Collections.Generic.List[string]'
 if([SpectraUpdateFiles]::Exists($oldManifest)){
  $manifest=[SpectraUpdateFiles]::Read($oldManifest) | ConvertFrom-Json
  foreach($entry in $manifest){if($entry -isnot [string]){throw 'Invalid old file manifest entry'};$old.Add($entry)}
  $old.Add('installed-files.json')
 }
 foreach($relative in @(@($fresh.ToArray())+@($old.ToArray()) | Select-Object -Unique)) {
  $destination=Under $task.install $relative
  $incoming=Under $task.publish $relative
  $hasFresh=$fresh -contains $relative
  if($hasFresh -and [SpectraUpdateFiles]::Exists($destination) -and [SpectraUpdateFiles]::Hash($destination) -eq [SpectraUpdateFiles]::Hash($incoming)){continue}
  if([SpectraUpdateFiles]::Exists($destination)){
   $saved=Under $backup $relative;[SpectraUpdateFiles]::Copy($destination,$saved);$changed.Add($relative)
  }else{$created.Add($relative)}
  if($hasFresh){[SpectraUpdateFiles]::Copy($incoming,$destination)}else{[SpectraUpdateFiles]::Delete($destination)}
 }
 if($task.hashesFile){
  $hashes=[SpectraUpdateFiles]::Read($task.hashesFile) | ConvertFrom-Json
  foreach($entry in $hashes.PSObject.Properties){
   $installed=Under $task.install $entry.Name
   if(-not [SpectraUpdateFiles]::Exists($installed) -or [SpectraUpdateFiles]::Hash($installed) -ne $entry.Value){throw "Installed file verification failed: $($entry.Name)"}
  }
 }
 if([SpectraUpdateFiles]::Read((Under $task.install 'build-commit.txt')).Trim() -ne $task.commit){throw 'Installed commit verification failed'}
 # Cache is optional and never part of the application transaction.
 try{if(Test-Path -LiteralPath $task.cache){Remove-Item -LiteralPath $task.cache -Recurse -Force};Move-Item -LiteralPath $task.source -Destination $task.cache}catch{}
 [SpectraUpdateFiles]::Delete($task.failureMarker)
 ReportResult $true 'Installed files verified; starting new executable.'
 Start-Process -FilePath (Under $task.install 'Spectra.exe') -WorkingDirectory $task.install -ErrorAction Stop
} catch {
 $failure=$_.Exception.Message
 foreach($relative in $changed){try{[SpectraUpdateFiles]::Copy((Under $backup $relative),(Under $task.install $relative))}catch{$failure+="; Restore failed: $relative; $($_.Exception.Message)"}}
 foreach($relative in $created){try{[SpectraUpdateFiles]::Delete((Under $task.install $relative))}catch{}}
 try{[SpectraUpdateFiles]::Write($task.failureMarker,$task.commit);ReportResult $false $failure;[SpectraUpdateFiles]::Write((Under $task.job 'apply-error.txt'),$failure)}catch{}
 if(-not (Get-Process -Id $task.pid -ErrorAction SilentlyContinue)){Start-Process -FilePath (Under $task.install 'Spectra.exe') -WorkingDirectory $task.install -ErrorAction SilentlyContinue}
 exit 1
}
