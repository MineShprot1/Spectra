using System.Windows;using System.Threading;using System.Security.Cryptography;using System.Text;
namespace Spectra;
public partial class App : Application
{
 Mutex? single;EventWaitHandle? showEvent;RegisteredWaitHandle? showWait;bool owns;
 protected override void OnStartup(StartupEventArgs e){var user=System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value??Environment.UserName;var name="Local\\Spectra-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user)))[..24];single=new Mutex(false,name);try{owns=single.WaitOne(0);}catch(AbandonedMutexException){owns=true;}if(!owns){try{using var existing=EventWaitHandle.OpenExisting(name+"-show");existing.Set();}catch(WaitHandleCannotBeOpenedException){}Shutdown();return;}showEvent=new(false,EventResetMode.AutoReset,name+"-show");showWait=ThreadPool.RegisterWaitForSingleObject(showEvent,(_,_)=>{if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(()=>{var window=MainWindow;if(window==null)return;window.Show();if(window.WindowState==WindowState.Minimized)window.WindowState=WindowState.Normal;window.Activate();}));},null,Timeout.Infinite,false);base.OnStartup(e);}
 protected override void OnExit(ExitEventArgs e){showWait?.Unregister(null);showEvent?.Dispose();if(owns)single?.ReleaseMutex();single?.Dispose();base.OnExit(e);}
}
