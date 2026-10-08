param([Parameter(Mandatory=$true)][string]$JobFile)
$ErrorActionPreference='Stop'
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
 static void Fail(string operation,string path){throw new IOException(operation+": "+path,new Win32Exception(Marshal.GetLastWin32Error()));}
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
  if(!CopyFileW(Extended(from),Extended(to),false))Fail("Copy file",to);
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
 $fresh=@([SpectraUpdateFiles]::Read((Under $task.publish 'installed-files.json')) | ConvertFrom-Json)+@('installed-files.json')
 $oldManifest=Under $task.install 'installed-files.json'
 $old=@();if([SpectraUpdateFiles]::Exists($oldManifest)){$old=@([SpectraUpdateFiles]::Read($oldManifest) | ConvertFrom-Json)+@('installed-files.json')}
 foreach($relative in @($fresh+$old | Select-Object -Unique)) {
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
 foreach($relative in $changed){try{[SpectraUpdateFiles]::Copy((Under $backup $relative),(Under $task.install $relative))}catch{$failure+="; Restore failed: $relative"}}
 foreach($relative in $created){try{[SpectraUpdateFiles]::Delete((Under $task.install $relative))}catch{}}
 try{[SpectraUpdateFiles]::Write($task.failureMarker,$task.commit);ReportResult $false $failure;[SpectraUpdateFiles]::Write((Under $task.job 'apply-error.txt'),$failure)}catch{}
 if(-not (Get-Process -Id $task.pid -ErrorAction SilentlyContinue)){Start-Process -FilePath (Under $task.install 'Spectra.exe') -WorkingDirectory $task.install -ErrorAction SilentlyContinue}
 exit 1
}
