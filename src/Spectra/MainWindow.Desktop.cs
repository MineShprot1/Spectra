using System.Text.Json.Nodes;
namespace Spectra;
public partial class MainWindow
{
 FriendsWindow? friendsWindow;
 void NotifySourceUpdate(JsonNode value){if(store.Config.Preferences.SystemNotifications)socialDesktop.Notify("Доступно обновление Spectra",value.Str("version"),new JsonObject{["type"]="sourceUpdate",["update"]=value.DeepClone()});}
 async Task TrayCommand(string command)
 {
  try{switch(command){
   case "friends":await OpenFriendsWindow();break;
   case "folder":System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(store.Root){UseShellExecute=true});break;
   case "mute":store.Config.Preferences.Notifications=!store.Config.Preferences.Notifications;store.Save();socialDesktop.Clear();Emit(new{type="preferencesChanged",preferences=store.Config.Preferences});break;
   case "launch":socialDesktop.Show();ExpandLibrary();Emit(new{type="trayLaunch"});break;
   case "instances":case "versions":case "settings":socialDesktop.Show();ExpandLibrary();Emit(new{type="trayNavigate",page=command});break;
  }}catch(Exception error){Emit(new{type="networkError",message=error.Message});}
 }
 bool AllowNotification(JsonNode? action)
 {
  var p=store.Config.Preferences;if(!p.Notifications||!p.SystemNotifications)return false;
  return action.Str("type") switch{"friend"=>p.FriendGameNotifications,"chat"=>action.Str("chat").StartsWith("party:")?p.GroupNotifications:p.ChatNotifications,"party"=>p.GroupNotifications,_=>true};
 }
 Task OpenFriendsWindow()
 {
  if(friendsWindow!=null){friendsWindow.Show();friendsWindow.WindowState=System.Windows.WindowState.Normal;friendsWindow.Activate();return Task.CompletedTask;}
  friendsWindow=new FriendsWindow(store,Handle,action=>{var type=action.Str("type");if(type is not ("friend" or "chat" or "party"))return;socialDesktop.Show();ExpandLibrary();Emit(new{type="socialNavigate",action});});
  friendsWindow.Closed+=(_,_)=>friendsWindow=null;friendsWindow.Show();return Task.CompletedTask;
 }
}
