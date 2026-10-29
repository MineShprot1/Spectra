using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
namespace Spectra;
public sealed partial class SocialDesktop
{
 [StructLayout(LayoutKind.Sequential)] struct TrayPoint{public int X,Y;}
 [DllImport("user32.dll")] static extern bool GetCursorPos(out TrayPoint point);
 [DllImport("user32.dll")] static extern nint WindowFromPoint(TrayPoint point);
 [DllImport("user32.dll")] static extern nint GetAncestor(nint window,uint flags);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(nint window);
 [DllImport("user32.dll")] static extern nint GetForegroundWindow();
 [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
 DispatcherTimer? trayLifetime;
 // The shell overflow panel lives in Explorer, outside WPF's popup capture tree.
 // Watch the window containing the clicked icon for the lifetime of this menu only.
 void FollowTrayAnchor(ContextMenu menu,nint anchor)
 {
  trayLifetime?.Stop();var initial=GetForegroundWindow();var seenVisible=anchor!=0&&IsWindowVisible(anchor);
  var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};trayLifetime=timer;
  timer.Tick+=(_,_)=>{
   if(!menu.IsOpen||disposed){timer.Stop();return;}
   var foreground=GetForegroundWindow();var popup=(PresentationSource.FromVisual(menu) as HwndSource)?.Handle??0;
   if(TrayShouldClose(seenVisible,IsWindowVisible(anchor),(GetAsyncKeyState(0x1B)&0x8000)!=0,foreground,initial,anchor,popup))menu.IsOpen=false;
  };
  menu.Closed+=(_,_)=>{timer.Stop();if(ReferenceEquals(trayLifetime,timer))trayLifetime=null;};timer.Start();
 }
 static nint TrayAnchor(){return GetCursorPos(out var point)?GetAncestor(WindowFromPoint(point),2):0;}
 internal static bool TrayShouldClose(bool wasVisible,bool isVisible,bool escape,nint foreground,nint initial,nint anchor,nint popup)=>wasVisible&&!isVisible||escape||foreground!=0&&foreground!=initial&&foreground!=anchor&&foreground!=popup;
}
