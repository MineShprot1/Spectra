using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Spectra;

static class TrayTests
{
 [STAThread]
 static void Main()
 {
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
  var window=new Window();
  var snapshot=new DesktopIdentity("Player","","playing","Minecraft",true,false,"Test pack");
  string appearance="{}",command="";
  using(var desktop=new SocialDesktop(window,_=>{},onCommand:value=>command=value,appearance:()=>appearance,identity:()=>snapshot))
  {
   var populate=typeof(SocialDesktop).GetMethod("PopulateTray",BindingFlags.Instance|BindingFlags.NonPublic)!;
   foreach(var light in new[]{false,true})
   {
    appearance=light?"{\"theme\":\"dark\",\"themeModes\":{\"dark\":\"light\"}}":"{}";
    var menu=(ContextMenu)typeof(SocialDesktop).GetMethod("CreateTrayMenu",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(desktop,null)!;
    menu.ApplyTemplate();menu.Measure(new Size(300,1000));menu.Arrange(new Rect(new Point(),menu.DesiredSize));menu.UpdateLayout();
    if(menu.Items.Count<12||menu.DesiredSize.Width>301||!double.IsFinite(menu.DesiredSize.Height))throw new Exception("Invalid tray layout");
    if(((MenuItem)menu.Items[0]).Header is not Grid identity||identity.Children.Count!=2)throw new Exception("Identity missing");
    var item=(MenuItem)menu.Items[2];item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    if(command!="invisible")throw new Exception("Visibility command missing");
    var foreground=(SolidColorBrush)menu.Foreground;
    if(light!=(foreground.Color.R<100))throw new Exception("Theme palette not applied");
    snapshot=snapshot with{Connected=false,Hidden=true,State="invisible"};populate.Invoke(desktop,[menu]);
    if(((MenuItem)menu.Items[1]).IsEnabled||((MenuItem)menu.Items[2]).IsEnabled)throw new Exception("Disconnected presence controls must be disabled");
    snapshot=snapshot with{Connected=true,Hidden=false,State="playing"};
   }
  }
  window.Close();app.Shutdown();
  Console.WriteLine("PASS native tray: WPF templates, bounded layout, identity/activity header, theme palettes and visibility commands");
 }
}
