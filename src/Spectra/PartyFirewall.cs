using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
namespace Spectra;
public static class PartyFirewall
{
 static readonly SemaphoreSlim Gate=new(1,1);
 public sealed record Result(bool Success,bool Cancelled,string Message);
 // Only native code resolves this path; the web interface cannot supply programs or ports.
 public static string BuildScript(string executable,bool remove=false)
 {
  if(string.IsNullOrWhiteSpace(executable)||!executable.EndsWith(@"\msedgewebview2.exe",StringComparison.OrdinalIgnoreCase)||executable.IndexOfAny(['\0','\r','\n'])>=0||!(executable.StartsWith(@"\\")||executable.Length>3&&char.IsLetter(executable[0])&&executable[1]==':'&&executable[2]=='\\'))throw new IOException("Не удалось определить путь WebView2");
  var program="'"+executable.Replace("'","''")+"'";
  var script="$ErrorActionPreference='Stop'\nImport-Module NetSecurity -ErrorAction Stop\ntry {\n";
  script+="foreach($name in @('Spectra-Party-WebRTC-UDP-In','Spectra-Party-WebRTC-UDP-Out')) { Get-NetFirewallRule -PolicyStore PersistentStore -Name $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction Stop }\n";
  if(!remove){
   script+="$program="+program+"\nif(-not (Test-Path -LiteralPath $program -PathType Leaf)){ throw 'WebView2 not found' }\n";
   foreach(var direction in new[]{"Inbound","Outbound"}){var name=direction=="Inbound"?"In":"Out";script+=$"New-NetFirewallRule -PolicyStore PersistentStore -Name 'Spectra-Party-WebRTC-UDP-{name}' -DisplayName 'Spectra Party - WebRTC UDP {name}' -Group 'Spectra Party' -Description 'WebRTC UDP for Spectra Party. WebView2 runtime executable; Private and Public networks.' -Direction {direction} -Action Allow -Enabled True -Profile Private,Public -Protocol UDP -Program $program | Out-Null\n";}
  }
  return script+="exit 0\n} catch { exit 1 }\n";
 }
 public static async Task<Result> ConfigureAsync(int browserProcessId,bool remove=false)
 {
  if(!OperatingSystem.IsWindows())throw new IOException("Настройка брандмауэра доступна только в Windows");
  if(!await Gate.WaitAsync(0))throw new IOException("Настройка брандмауэра уже выполняется");
  try{
   using var browser=Process.GetProcessById(browserProcessId);var executable=browser.MainModule?.FileName??throw new IOException("Не удалось определить процесс WebView2");
   if(!Path.IsPathFullyQualified(executable)||!File.Exists(executable))throw new IOException("Файл WebView2 не найден");
   var script=BuildScript(executable,remove);var system=Environment.GetFolderPath(Environment.SpecialFolder.System);var shell=Path.Combine(system,"WindowsPowerShell","v1.0","powershell.exe");
   var info=new ProcessStartInfo(shell){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=system};
   foreach(var arg in new[]{"-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(script))})info.ArgumentList.Add(arg);
   try{using var process=Process.Start(info)??throw new IOException("Не удалось открыть настройку брандмауэра");await process.WaitForExitAsync();if(process.ExitCode!=0)throw new IOException("Windows не удалось изменить правила. Проверьте службу брандмауэра и ограничения администратора.");}
   catch(Win32Exception e) when(e.NativeErrorCode==1223){return new(false,true,"Запрос прав администратора отменён. Правила не изменены.");}
   return new(true,false,remove?"Правила Spectra Party удалены.":"UDP-правила для WebView2 созданы. Повторите подключение к группе. После обновления WebView2 может потребоваться нажать эту кнопку снова.");
  }finally{Gate.Release();}
 }
}
