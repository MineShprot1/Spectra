using System.IO;
using Microsoft.Win32;
namespace Spectra;
public static class WindowsStartup
{
 const string Run=@"Software\Microsoft\Windows\CurrentVersion\Run";
 public static string Command(string executable){if(executable.Contains('"')||string.IsNullOrWhiteSpace(executable))throw new ArgumentException("Неверный путь программы");return "\""+executable+"\" --background";}
 public static void Set(bool enabled)
 {
  if(!OperatingSystem.IsWindows())return;using var key=Registry.CurrentUser.CreateSubKey(Run,true)??throw new IOException("Не удалось открыть настройки автозапуска Windows");
  if(enabled){var path=Path.Combine(AppContext.BaseDirectory,"Spectra.exe");if(!File.Exists(path))throw new IOException("Не найден Spectra.exe");key.SetValue("Spectra",Command(path),RegistryValueKind.String);}else key.DeleteValue("Spectra",false);
 }
}
