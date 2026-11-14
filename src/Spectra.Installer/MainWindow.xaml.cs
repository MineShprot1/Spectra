using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
namespace Spectra.Installer;
public partial class MainWindow:Window
{
 string language="ru",target="",found="";int step;bool installing,installed,forceUpdate;CancellationTokenSource? cancellation;
 string T(string key)=>SetupText.Get(language,key);
 public MainWindow(){InitializeComponent();SetupText.Validate();language=SetupText.Languages.ContainsKey(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)?CultureInfo.CurrentUICulture.TwoLetterISOLanguageName:"en";LanguagePicker.ItemsSource=SetupText.Languages;LanguagePicker.DisplayMemberPath="Value";LanguagePicker.SelectedValuePath="Key";LanguagePicker.SelectedValue=language;found=Detect();Folder.Text=found.Length>0?found:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Spectra");Folder.TextChanged+=(_,_)=>RefreshTarget();RefreshTarget();Closing+=(_,e)=>{if(installing){e.Cancel=true;cancellation?.Cancel();}};MaxHeight=SystemParameters.WorkArea.Height;ShowStep();}
 static string Detect(){var candidates=new List<string>{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Spectra"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Spectra")};try{var saved=Registry.CurrentUser.OpenSubKey("Software\\Spectra")?.GetValue("InstallPath") as string;if(saved!=null)candidates.Insert(0,saved);}catch{}try{foreach(var process in Process.GetProcessesByName("Spectra")){using(process){var file=process.MainModule?.FileName;if(file!=null)candidates.Insert(0,Path.GetDirectoryName(file)!);}}}catch{}return candidates.FirstOrDefault(p=>File.Exists(Path.Combine(p,"Spectra.exe")))??"";}
 void LanguageChanged(object sender,SelectionChangedEventArgs e){if(LanguagePicker.SelectedValue is string code)language=code;if(Next!=null)ShowStep();}
 void RefreshTarget(){var path=Folder.Text.Trim();TargetHint.Text=T("space")+": ~500 MB";if(File.Exists(Path.Combine(path,"Spectra.exe")))found=path;ExistingPanel.Visibility=found.Length>0?Visibility.Visible:Visibility.Collapsed;ExistingPath.Text=found;}
 void DragHeader(object sender,MouseButtonEventArgs e){if(e.ChangedButton==MouseButton.Left&&e.OriginalSource is not Button)try{DragMove();}catch(InvalidOperationException){}}
 void MinimizeClick(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
 void CloseClick(object sender,RoutedEventArgs e)=>CancelClick(sender,e);
 void CancelClick(object sender,RoutedEventArgs e){if(installing){cancellation?.Cancel();Cancel.IsEnabled=false;}else Close();}
 void BrowseClick(object sender,RoutedEventArgs e){using var picker=new System.Windows.Forms.FolderBrowserDialog{SelectedPath=Folder.Text,Description=T("folder"),UseDescriptionForTitle=true};if(picker.ShowDialog()==System.Windows.Forms.DialogResult.OK)Folder.Text=picker.SelectedPath;}
 void BackClick(object sender,RoutedEventArgs e){if(!installing&&step>0){step--;forceUpdate=false;ShowStep();}}
 void UpdateClick(object sender,RoutedEventArgs e){if(found.Length==0)return;Folder.Text=found;target=InstallEngine.Target(found);forceUpdate=true;step=2;ShowStep();}
 void ShowStep(){if(Heading==null)return;SideIntro.Text=T("intro");VersionLabel.Text=T("version")+" "+(Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)??"");StepOne.Text="01 / "+T("language");StepTwo.Text="02 / "+T("options");StepThree.Text="03 / "+T("install");var muted=new SolidColorBrush(Color.FromRgb(101,124,152));var accent=new SolidColorBrush(Color.FromRgb(159,200,243));StepOne.Foreground=step==0?accent:muted;StepTwo.Foreground=step==1?accent:muted;StepThree.Foreground=step>=2?accent:muted;
  Heading.Text=T(step switch{0=>"welcome",1=>"options",2=>"review",3=>"progress",_=>"done"});Description.Text=step switch{0=>T("intro"),1=>T("folder"),2=>forceUpdate?"GitHub · MineShprot1/Spectra · main":T("ready"),3=>T("prepare"),_=>T("finish")};
  LanguageLabel.Text=T("language");FolderLabel.Text=T("folder");Browse.Content=T("browse");DesktopShortcut.Content=T("desktop");StartShortcut.Content=T("start");ExistingLabel.Text=T("found");UpdateButton.Content=T("update");Back.Content=T("back");Cancel.Content=T(installed?"close":"cancel");LaunchAfter.Content=T("launchCheck");Next.Content=T(step==4?"finish":step==2?(forceUpdate?"update":"install"):"next");
  LanguagePanel.Visibility=step==0?Visibility.Visible:Visibility.Collapsed;FolderPanel.Visibility=step==1?Visibility.Visible:Visibility.Collapsed;ReviewPanel.Visibility=step==2?Visibility.Visible:Visibility.Collapsed;ProgressPanel.Visibility=step==3?Visibility.Visible:Visibility.Collapsed;FinishPanel.Visibility=step==4?Visibility.Visible:Visibility.Collapsed;Back.Visibility=step is 1 or 2?Visibility.Visible:Visibility.Collapsed;Next.Visibility=step==3?Visibility.Collapsed:Visibility.Visible;CloseButton.IsEnabled=!installing;
  ReviewText.Text=T("folder")+"\n"+Folder.Text+"\n\n"+T("language")+": "+SetupText.Languages[language]+"\n"+(DesktopShortcut.IsChecked==true?"✓ ":"— ")+T("desktop")+"\n"+(StartShortcut.IsChecked==true?"✓ ":"— ")+T("start");RefreshTarget();
 }
 void Error(Exception error){ErrorPanel.Visibility=Visibility.Visible;ErrorText.Text=T("error")+"\n"+error.Message;}
 bool Running(){foreach(var process in Process.GetProcessesByName("Spectra"))using(process){try{if(string.Equals(Path.GetDirectoryName(process.MainModule?.FileName),target,StringComparison.OrdinalIgnoreCase))return true;}catch{return true;}}return false;}
 async void NextClick(object sender,RoutedEventArgs e)
 {
  if(installed){if(LaunchAfter.IsChecked==true)Launch();else Close();return;}if(installing)return;ErrorPanel.Visibility=Visibility.Collapsed;
  try{if(step==1)target=InstallEngine.Target(Folder.Text.Trim());if(step<2){step++;ShowStep();return;}target=InstallEngine.Target(Folder.Text.Trim());if(Running())throw new IOException(T("stop"));
   installing=true;cancellation=new();var ct=cancellation.Token;var chosenLanguage=language;var desktop=DesktopShortcut.IsChecked==true;var start=StartShortcut.IsChecked==true;step=3;Cancel.IsEnabled=true;ShowStep();
   var progress=new Progress<SetupProgress>(p=>{PhaseLabel.Text=T(p.Phase);Progress.IsIndeterminate=p.Total==0;Progress.Value=p.Total>0?p.Done*100d/p.Total:0;ProgressNumber.Text=p.Total>0?Math.Min(100,(int)Progress.Value)+"%":"";Detail.Text=p.File;});
   using var prepared=forceUpdate?await Task.Run(()=>GithubUpdate.Prepare(target,progress,ct),ct):null;
   if(Running())throw new IOException(T("stop"));
   await Task.Run(()=>{using var payload=prepared!=null?File.OpenRead(prepared.File):(Assembly.GetExecutingAssembly().GetManifestResourceStream("Spectra.Payload")??throw new IOException("Missing launcher package. BuildInstaller.ps1"));InstallEngine.Install(payload,target,chosenLanguage,progress,ct);},ct);
   if(desktop)Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Spectra.lnk"),Path.Combine(target,"Spectra.exe"));
   if(start){var menu=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Spectra");Directory.CreateDirectory(menu);Shortcut(Path.Combine(menu,"Spectra.lnk"),Path.Combine(target,"Spectra.exe"));}
   try{using var key=Registry.CurrentUser.CreateSubKey("Software\\Spectra");key.SetValue("InstallPath",target);}catch{}
   installed=true;installing=false;step=4;InstalledPath.Text=target;ShowStep();
  }catch(OperationCanceledException){installing=false;step=2;ShowStep();ErrorPanel.Visibility=Visibility.Visible;ErrorText.Text=T("cancelled");}
  catch(Exception error){installing=false;step=2;ShowStep();Error(error);}finally{Cancel.IsEnabled=true;cancellation?.Dispose();cancellation=null;CloseButton.IsEnabled=true;}
 }
 static void Shortcut(string path,string exe){object? shell=null,link=null;try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);dynamic script=shell!;link=script.CreateShortcut(path);dynamic shortcut=link;shortcut.TargetPath=exe;shortcut.WorkingDirectory=Path.GetDirectoryName(exe);shortcut.IconLocation=exe+",0";shortcut.Description="Spectra Minecraft Launcher";shortcut.Save();}finally{if(link!=null)Marshal.FinalReleaseComObject(link);if(shell!=null)Marshal.FinalReleaseComObject(shell);}}
 void Launch(){try{Process.Start(new ProcessStartInfo("explorer.exe") {Arguments='"'+Path.Combine(target,"Spectra.exe")+'"',UseShellExecute=true});Close();}catch(Exception error){Error(error);}}
}
