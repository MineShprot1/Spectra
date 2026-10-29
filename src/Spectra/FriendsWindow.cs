using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
namespace Spectra;

// A separate view over the existing friend service. Chat keys and polling stay in the main view.
public sealed class FriendsWindow : Window
{
 readonly WebView2 view=new();readonly Func<string,JsonNode,Task<object?>> call;readonly Action<JsonNode> navigate;readonly Store store;
 public FriendsWindow(Store store,Func<string,JsonNode,Task<object?>> call,Action<JsonNode> navigate)
 {
  this.store=store;this.call=call;this.navigate=navigate;Title="Spectra · Друзья";Width=390;Height=650;MinWidth=340;MinHeight=400;
  Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(23,29,37));Content=view;
  Loaded+=async(_,_)=>await Initialize();Closed+=(_,_)=>view.Dispose();
 }
 async Task Initialize()
 {
  try{
   var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(store.Root,"webview"));await view.EnsureCoreWebView2Async(environment);var core=view.CoreWebView2;
   core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreDevToolsEnabled=false;core.Settings.IsStatusBarEnabled=false;core.Settings.IsZoomControlEnabled=false;
   core.SetVirtualHostNameToFolderMapping("app.spectra.local",Path.Combine(AppContext.BaseDirectory,"Web"),CoreWebView2HostResourceAccessKind.DenyCors);
   core.NavigationStarting+=(_,e)=>{if(e.Uri!="https://app.spectra.local/friends-window.html")e.Cancel=true;};core.NewWindowRequested+=(_,e)=>e.Handled=true;core.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
   core.WebMessageReceived+=async(_,e)=>{
    if(e.Source!="https://app.spectra.local/friends-window.html")return;string id="";
    try{var m=JsonNode.Parse(e.WebMessageAsJson)!;id=m.Str("id");var action=m.Str("action");var data=m["data"]??new JsonObject();object? result;
     if(action=="navigate"){navigate(data);result=null;}
     else if(action=="state")result=await call("friendsWindowState",data);
     else if(action is "friends" or "friendsSearch" or "friendAction")result=await call(action,data);
     else throw new IOException("Неизвестная команда окна друзей");
     if(view.CoreWebView2!=null)core.PostWebMessageAsJson(JsonSerializer.Serialize(new{id,ok=true,result},Store.Json));
    }catch(Exception error){if(view.CoreWebView2!=null)core.PostWebMessageAsJson(JsonSerializer.Serialize(new{id,ok=false,error=error.Message},Store.Json));}
   };
   view.ZoomFactor=Math.Clamp(store.Config.InterfaceScale,.65,1.5);core.Navigate("https://app.spectra.local/friends-window.html");
  }catch(Exception error){Content=new System.Windows.Controls.TextBlock{Text=error.Message,Foreground=System.Windows.Media.Brushes.White,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(20)};}
 }
}
