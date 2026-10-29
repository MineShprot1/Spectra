using System.Text.Json.Nodes;
namespace Spectra;
public partial class MainWindow
{
 readonly PlayerAvatars playerAvatars=new();
 DesktopIdentity DesktopSnapshot(){var playing=!game.Running.IsEmpty||BedrockService.IsRunning();var hidden=store.Config.HideOnlineStatus;var status=hidden?"invisible":playing?"playing":friends.Account!=null?"online":"offline";var detail=status switch{"invisible"=>"Активность скрыта от друзей","playing"=>"Minecraft","online"=>"Spectra Network",_=>"Spectra Network не подключён"};var selected=store.Config.SelectedEdition=="bedrock"?store.Config.SelectedBedrockLabel:store.Config.Instances.Find(i=>i.Id==store.Config.SelectedInstance)?.Name??(store.Config.SelectedVersion!=""?"Minecraft "+store.Config.SelectedVersion:"");return new(auth.Profile.Str("name") is {Length:>0} name?name:"Spectra",auth.Profile.Str("id"),status,detail,friends.Account!=null,hidden,selected,auth.Profile?["skins"]?.AsArray().FirstOrDefault(s=>s.Str("state")=="ACTIVE").Str("url") is {Length:>0} activeSkin?activeSkin:auth.Profile?["skins"]?.AsArray().FirstOrDefault().Str("url")??"");}
 void NotifySourceUpdate(JsonNode value){if(store.Config.Preferences.SystemNotifications)socialDesktop.Notify("Доступно обновление Spectra",value.Str("version"),new JsonObject{["type"]="sourceUpdate",["update"]=value.DeepClone()});}
 void TrayCommand(string command)
 {
  try{switch(command){
   case "online":case "invisible":store.Config.HideOnlineStatus=command=="invisible";store.Save();RefreshPresence();Emit(new{type="presenceState",state=State()});break;
   case "friends":socialDesktop.Show();ExpandLibrary();Emit(new{type="openFriends"});break;
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
}
