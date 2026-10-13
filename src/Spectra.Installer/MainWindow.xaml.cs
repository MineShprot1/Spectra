using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
namespace Spectra.Installer;
public partial class MainWindow:Window
{
 readonly Dictionary<string,string> languages=new(){["ru"]="Русский",["en"]="English",["de"]="Deutsch",["fr"]="Français",["es"]="Español",["pt"]="Português",["it"]="Italiano",["pl"]="Polski",["uk"]="Українська",["tr"]="Türkçe",["zh"]="简体中文",["ja"]="日本語",["ko"]="한국어"};
 string selectedLanguage="ru";
 string T(string ru,string en)=>selectedLanguage=="ru"?ru:en;
 void LanguageChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e){if(Language.SelectedValue is string language)selectedLanguage=language;ShowStep();}
 int step;bool installing,installed;string target="";
 public MainWindow(){InitializeComponent();Language.ItemsSource=languages;Language.DisplayMemberPath="Value";Language.SelectedValuePath="Key";Language.SelectedValue="ru";Folder.Text=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);Folder.TextChanged+=(_,_)=>TargetHint.Text=T("Будет установлено в: ","Install location: ")+Path.Combine(Folder.Text,"Spectra");TargetHint.Text=T("Будет установлено в: ","Install location: ")+Path.Combine(Folder.Text,"Spectra");Closing+=(_,e)=>{if(installing)e.Cancel=true;};}
 void DragHeader(object sender,MouseButtonEventArgs e){if(e.ChangedButton==MouseButton.Left)DragMove();}
 void CloseClick(object sender,RoutedEventArgs e){if(!installing)Close();}
 void BrowseClick(object sender,RoutedEventArgs e){using var picker=new System.Windows.Forms.FolderBrowserDialog{SelectedPath=Folder.Text,Description="Выберите папку, в которой будет создана Spectra"};if(picker.ShowDialog()==System.Windows.Forms.DialogResult.OK)Folder.Text=picker.SelectedPath;}
 void BackClick(object sender,RoutedEventArgs e){if(!installing&&step>0){step--;ShowStep();}}
 void ShowStep(){Next.Visibility=step==3?Visibility.Collapsed:Visibility.Visible;LanguagePanel.Visibility=step==0?Visibility.Visible:Visibility.Collapsed;FolderPanel.Visibility=step==1?Visibility.Visible:Visibility.Collapsed;ShortcutPanel.Visibility=step==2?Visibility.Visible:Visibility.Collapsed;ProgressPanel.Visibility=step==3?Visibility.Visible:Visibility.Collapsed;Back.Visibility=step is 1 or 2?Visibility.Visible:Visibility.Collapsed;Heading.Text=step switch{0=>"Добро пожаловать в Spectra",1=>"Где установить Spectra?",2=>"Быстрый доступ",_=>"Установка Spectra"};Description.Text=step switch{0=>"Ваш Minecraft. Ваши сборки. Ваш стиль.",1=>"В выбранной папке будет создана папка Spectra.",2=>"Выберите, где разместить ярлыки лаунчера.",_=>"Копируем файлы лаунчера…"};Next.Content=step==2?T("Установить","Install"):T("Далее →","Next →");
  if(selectedLanguage!="ru"){Heading.Text=step switch{0=>"WELCOME TO SPECTRA",1=>"INSTALL LOCATION",2=>"QUICK ACCESS",_=>"INSTALLING SPECTRA"};Description.Text=step switch{0=>"Your Minecraft. Your instances. Your style.",1=>"A Spectra folder will be created in the selected location.",2=>"Choose where to place launcher shortcuts.",_=>"Copying launcher files…"};}
  Back.Content=T("Назад","Back");LanguageLabel.Text=T("Язык лаунчера / Launcher language","Launcher language");FolderLabel.Text=T("Родительская папка установки","Parent installation folder");Browse.Content=T("Обзор…","Browse…");DesktopShortcut.Content=T("Создать ярлык на рабочем столе","Create desktop shortcut");StartShortcut.Content=T("Добавить Spectra в меню «Пуск»","Add Spectra to the Start menu");TargetHint.Text=T("Будет установлено в: ","Install location: ")+Path.Combine(Folder.Text,"Spectra");}
 async void NextClick(object sender,RoutedEventArgs e)
 {
  if(installed){Launch();return;}if(installing)return;
  try{
   if(step==1){var parent=Path.GetFullPath(Folder.Text.Trim());if(!Path.IsPathFullyQualified(Folder.Text.Trim()))throw new IOException("Укажите полный путь папки");target=Path.Combine(parent,"Spectra");}
   if(step<2){step++;ShowStep();return;}
   if(Process.GetProcessesByName("Spectra").Length>0)throw new IOException("Закройте Spectra перед установкой");
   var desktop=DesktopShortcut.IsChecked==true;var start=StartShortcut.IsChecked==true;step=3;ShowStep();installing=true;Next.IsEnabled=false;
   var progress=new Progress<(string File,long Done,long Total)>(p=>{Detail.Text=p.File+"\n"+Size(p.Done)+" / "+Size(p.Total);Progress.Value=p.Total>0?p.Done*100d/p.Total:100;});
   await Task.Run(()=>Install(target,progress));
   File.WriteAllText(Path.Combine(target,"install-preferences.json"),JsonSerializer.Serialize(new{language=selectedLanguage,id=Guid.NewGuid().ToString("N")}));
   var exe=Path.Combine(target,"Spectra.exe");
   if(desktop)Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Spectra.lnk"),exe);
   if(start){var menu=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Spectra");Directory.CreateDirectory(menu);Shortcut(Path.Combine(menu,"Spectra.lnk"),exe);}
   installing=false;installed=true;Badge.Text="✓";Heading.Text=T("Spectra установлена","SPECTRA INSTALLED");Description.Text=T("Лаунчер готов к запуску.","The launcher is ready to start.");Progress.Value=100;Detail.Text=target;Next.IsEnabled=true;Next.Visibility=Visibility.Visible;Next.Content=T("Запустить Spectra","Launch Spectra");Launch();
  }catch(Exception ex){installing=false;Next.IsEnabled=true;step=2;ShowStep();MessageBox.Show(this,ex.Message,"Установка Spectra",MessageBoxButton.OK,MessageBoxImage.Error);}
 }
 static string Size(long size)=>size>=1048576?(size/1048576d).ToString("0.0")+" МБ":(size/1024d).ToString("0.0")+" КБ";
 static void Install(string destination,IProgress<(string File,long Done,long Total)> progress)
 {
  using var payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("Spectra.Payload")??throw new IOException("В установщик не включены файлы Spectra. Запустите BuildInstaller.ps1.");
  using var zip=new ZipArchive(payload,ZipArchiveMode.Read);var staging=Path.Combine(Path.GetTempPath(),"SpectraInstall",Guid.NewGuid().ToString("N"));var fresh=Path.Combine(staging,"new");var backup=Path.Combine(staging,"backup");Directory.CreateDirectory(fresh);Directory.CreateDirectory(backup);
  var changed=new List<string>();var created=new List<string>();long done=0,total=zip.Entries.Sum(x=>x.Length);
  string Under(string root,string relative){var path=Path.GetFullPath(Path.Combine(root,relative));if(!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Некорректный путь архива");return path;}
  try{
   foreach(var entry in zip.Entries){if(entry.FullName.EndsWith('/'))continue;var file=Under(fresh,entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(file)!);using var input=entry.Open();using var output=File.Create(file);var buffer=new byte[65536];int count;long last=0;while((count=input.Read(buffer))>0){output.Write(buffer,0,count);done+=count;if(Environment.TickCount64-last>100){last=Environment.TickCount64;progress.Report((entry.FullName,done,total));}}}
   if(!File.Exists(Path.Combine(fresh,"Spectra.exe")))throw new IOException("В пакете нет Spectra.exe");
   Directory.CreateDirectory(destination);
   foreach(var file in Directory.EnumerateFiles(fresh,"*",SearchOption.AllDirectories)){
    var relative=Path.GetRelativePath(fresh,file);var installedFile=Under(destination,relative);Directory.CreateDirectory(Path.GetDirectoryName(installedFile)!);
    if(File.Exists(installedFile)){var saved=Under(backup,relative);Directory.CreateDirectory(Path.GetDirectoryName(saved)!);File.Copy(installedFile,saved);changed.Add(relative);}else created.Add(relative);
    File.Copy(file,installedFile,true);progress.Report(("Установка · "+relative,done,total));
   }
  }catch{
   foreach(var relative in changed){try{File.Copy(Under(backup,relative),Under(destination,relative),true);}catch{}}
   foreach(var relative in created){try{File.Delete(Under(destination,relative));}catch{}}
   throw;
  }finally{try{Directory.Delete(staging,true);}catch{}}
 }
 static void Shortcut(string path,string exe){object? shell=null,link=null;try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);dynamic script=shell!;link=script.CreateShortcut(path);dynamic shortcut=link;shortcut.TargetPath=exe;shortcut.WorkingDirectory=Path.GetDirectoryName(exe);shortcut.IconLocation=exe+",0";shortcut.Description="Spectra Minecraft Launcher";shortcut.Save();}finally{if(link!=null)Marshal.FinalReleaseComObject(link);if(shell!=null)Marshal.FinalReleaseComObject(shell);}}
 void Launch(){try{Process.Start(new ProcessStartInfo("explorer.exe",'"'+Path.Combine(target,"Spectra.exe")+'"'){UseShellExecute=true});Close();}catch(Exception ex){MessageBox.Show(this,"Spectra установлена, но не удалось запустить её автоматически: "+ex.Message);}}
}
