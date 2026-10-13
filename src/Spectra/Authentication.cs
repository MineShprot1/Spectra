using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using XboxAuthNet.Game.Msal;
using Microsoft.Identity.Client;
namespace Spectra;
public sealed class Authentication
{
 readonly Store store;
 readonly Action<object> emit;
 readonly ProtectedAccountManager accounts;
 readonly CmlLib.Core.Auth.Microsoft.JELoginHandler handler;
 IPublicClientApplication? browserApp;
 readonly SemaphoreSlim loginGate=new(1);
 // Application (client) ID приложения Spectra в Microsoft Entra; microsoft-oauth.json может его переопределить.
 const string DefaultClientId="ace5fdf7-f1a5-47a1-9379-a6a4486f0164";
 public MSession? Session { get; private set; }
 public JsonNode? Profile { get; private set; }
 public LauncherAccount? ActiveAccount=>store.Config.Accounts.FirstOrDefault(a=>a.Id==store.Config.ActiveAccountId);
 public bool IsOffline=>ActiveAccount?.Offline==true;
 public JsonNode? FriendsProfile=>store.Config.Accounts.FirstOrDefault(a=>!a.Offline&&a.Id==(IsOffline?store.Config.FriendsOwnerId:ActiveAccount?.Id))?.Profile??(!IsOffline?Profile:null);
 public bool HasSavedAccount=>ActiveAccount!=null||(store.Config.Accounts.Count==0&&accounts.HasAccounts);
 DateTime refreshedAt;
 public static string OfflineUuid(string name)=>AccountPolicy.OfflineUuid(name);
 public static MSession OfflineSession(string name){ValidateName(name);return new MSession(name,"0",OfflineUuid(name));}
 public static void ValidateName(string name)=>AccountPolicy.ValidateName(name);
 void UseOffline(LauncherAccount account){Session=OfflineSession(account.Name);Profile=new JsonObject{["id"]=OfflineUuid(account.Name),["name"]=account.Name,["offline"]=true,["elyName"]=account.ElyName,["skins"]=account.ElyName==""?new JsonArray():new JsonArray(new JsonObject{["url"]="https://skinsystem.ely.by/skins/"+account.ElyName+".png",["variant"]="CLASSIC"}),["capes"]=new JsonArray()};}
 public LauncherAccount CreateOffline(string name,string elyName,string ownerId){ValidateName(name);if(elyName!="")ValidateName(elyName);var owner=store.Config.Accounts.FirstOrDefault(a=>a.Id==ownerId&&!a.Offline)??throw new IOException("Сначала подключите лицензионный Minecraft-аккаунт");var account=new LauncherAccount{Name=name,Offline=true,OwnerId=owner.Id,ElyName=elyName};store.Config.Accounts.Add(account);store.Save();return account;}
 public async Task SelectAccount(string id){var account=store.Config.Accounts.Single(a=>a.Id==id);var previous=store.Config.ActiveAccountId;var session=Session;var profile=Profile;store.Config.ActiveAccountId=id;Session=null;Profile=account.Profile?.DeepClone();try{if(account.Offline){store.Config.FriendsOwnerId=account.OwnerId;UseOffline(account);}else await Login(false);store.Save();}catch{store.Config.ActiveAccountId=previous;Session=session;Profile=profile;throw;}}

 public Authentication(Store store,Action<object> emit)
 {
  this.store=store;this.emit=emit;
  accounts=new ProtectedAccountManager(Path.Combine(store.Root,"microsoft-accounts.dat"));
  handler=new CmlLib.Core.Auth.Microsoft.JELoginHandlerBuilder()
   .WithHttpClient(Net.Http).WithAccountManager(accounts).Build();
  Profile=ActiveAccount?.Profile?.DeepClone();if(IsOffline)UseOffline(ActiveAccount!);
 }
 public async Task Login(bool interactive)
 {
  await loginGate.WaitAsync();
  try
  {
   if(!interactive&&IsOffline){UseOffline(ActiveAccount!);return;}
   if(!interactive&&Session!=null&&DateTime.UtcNow-refreshedAt<TimeSpan.FromMinutes(25))return;
   if(!interactive&&!HasSavedAccount)throw new InvalidOperationException("Войдите с Microsoft, чтобы продолжить");
   if(interactive)emit(new{type="auth",state="waiting",message="Подтвердите вход на странице Microsoft"});
   // Вход всегда идёт через системный браузер с собственным appID приложения:
   // пользователь видит обычную страницу Microsoft и только подтверждает доступ.
   var configPath=Path.Combine(AppContext.BaseDirectory,"microsoft-oauth.json");
   var clientId=DefaultClientId;
   if(File.Exists(configPath))
   {
    var configured=JsonNode.Parse(await File.ReadAllTextAsync(configPath)).Str("clientId");
    if(!string.IsNullOrWhiteSpace(configured))clientId=configured;
   }
   MSession session;AuthenticationResult result;
   {
   if(!Guid.TryParse(clientId,out var appId)||appId==Guid.Empty)
    throw new InvalidOperationException("Некорректный clientId в microsoft-oauth.json");
   if(browserApp==null)
   {
    browserApp=PublicClientApplicationBuilder.Create(clientId).WithAuthority("https://login.microsoftonline.com/consumers")
     .WithRedirectUri("http://localhost").Build();
    var cacheFile=Path.Combine(store.Root,"msal-browser.dat");
    browserApp.UserTokenCache.SetBeforeAccess(args=>
    {
     if(!File.Exists(cacheFile))return;
     byte[]? bytes=null;
     try{bytes=ProtectedData.Unprotect(File.ReadAllBytes(cacheFile),null,DataProtectionScope.CurrentUser);args.TokenCache.DeserializeMsalV3(bytes);}
     catch(Exception ex) when(ex is CryptographicException or IOException){ }
     finally{if(bytes!=null)CryptographicOperations.ZeroMemory(bytes);}
    });
    browserApp.UserTokenCache.SetAfterAccess(args=>
    {
     if(!args.HasStateChanged)return;
     var bytes=args.TokenCache.SerializeMsalV3();
     try{File.WriteAllBytes(cacheFile+".tmp",ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser));File.Move(cacheFile+".tmp",cacheFile,true);}
     finally{CryptographicOperations.ZeroMemory(bytes);}
    });
   }
   using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(5));
   if(interactive)
    result=await browserApp.AcquireTokenInteractive(MsalClientHelper.XboxScopes).WithPrompt(Prompt.SelectAccount).WithUseEmbeddedWebView(false)
     .WithSystemWebViewOptions(new SystemWebViewOptions
     {
      HtmlMessageSuccess=AuthPages.Success,
      HtmlMessageError=AuthPages.Error
     }).ExecuteAsync(timeout.Token);
   else
   {
    var available=await browserApp.GetAccountsAsync();var cached=ActiveAccount?.MicrosoftId is {Length:>0} msalId?available.FirstOrDefault(a=>a.HomeAccountId.Identifier==msalId):available.LastOrDefault();
    if(cached==null)throw new InvalidOperationException("Войдите с Microsoft в браузере");
    result=await browserApp.AcquireTokenSilent(MsalClientHelper.XboxScopes,cached).ExecuteAsync(timeout.Token);
   }
   var authenticator=handler.CreateAuthenticatorWithNewAccount();
   authenticator.AddMsalOAuth(browserApp,msal=>msal.FromResult(result));
   authenticator.AddXboxAuthForJE(xbox=>xbox.Basic());
   authenticator.AddForceJEAuthenticator();
   session=await authenticator.ExecuteForLauncherAsync();
   }
   using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.minecraftservices.com/minecraft/profile");
   request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",session.AccessToken);
   var profile=await Net.Send(request);
   Session=session;Profile=profile;refreshedAt=DateTime.UtcNow;accounts.SaveAccounts();
   var microsoft=(await browserApp.GetAccountsAsync()).FirstOrDefault(a=>a.HomeAccountId.Identifier==result.Account.HomeAccountId.Identifier);
   var saved=store.Config.Accounts.FirstOrDefault(a=>!a.Offline&&a.Id==profile.Str("id"));if(saved==null){saved=new LauncherAccount{Id=profile.Str("id")};store.Config.Accounts.Add(saved);}
   saved.Name=profile.Str("name");saved.MicrosoftId=microsoft?.HomeAccountId.Identifier??result.Account.HomeAccountId.Identifier;saved.Profile=profile.DeepClone();store.Config.ActiveAccountId=saved.Id;store.Config.FriendsOwnerId=saved.Id;store.Save();
   emit(new{type="auth",state="complete",message="Аккаунт подключён"});
  }
  catch
  {
   emit(new{type="auth",state="failed",message="Вход не завершён. Попробуйте ещё раз"});throw;
  }
  finally{loginGate.Release();}
 }
 public async Task Logout()
 {
  await loginGate.WaitAsync();
  try
  {
   store.Config.ActiveAccountId="";store.Config.FriendsOwnerId="";store.Save();Session=null;Profile=null;refreshedAt=DateTime.MinValue;

  }
  finally{loginGate.Release();}
 }
 void RememberProfile(){if(ActiveAccount!=null&&!IsOffline&&Profile!=null){ActiveAccount.Name=Profile.Str("name");ActiveAccount.Profile=Profile.DeepClone();store.Save();}}
 public async Task EquipSkinUrl(string url,string variant){if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="textures.minecraft.net")throw new IOException("Неизвестная текстура");var path=Path.Combine(store.Root,"downloads","profile-skin-"+Guid.NewGuid().ToString("N")+".png");await Net.Download(url,path,maxBytes:1024*1024);try{await ApplySavedSkin(path,variant.Equals("slim",StringComparison.OrdinalIgnoreCase)?"slim":"classic");}finally{if(File.Exists(path))File.Delete(path);}}
 public async Task ApplySavedSkin(string path,string variant)
 {
  if(IsOffline)throw new IOException("Офлайн-аккаунт не может изменять официальный профиль");await Login(false);if(Session==null)throw new InvalidOperationException("Сначала войдите");
  using var req=new HttpRequestMessage(HttpMethod.Post,"https://api.minecraftservices.com/minecraft/profile/skins");
  var content=new MultipartFormDataContent();content.Add(new StringContent(variant),"variant");
  var image=new ByteArrayContent(await File.ReadAllBytesAsync(path));image.Headers.ContentType=new("image/png");content.Add(image,"file",Path.GetFileName(path));req.Content=content;req.Headers.Authorization=new("Bearer",Session.AccessToken);await Net.Send(req);
  using var updated=new HttpRequestMessage(HttpMethod.Get,"https://api.minecraftservices.com/minecraft/profile");updated.Headers.Authorization=new("Bearer",Session.AccessToken);Profile=await Net.Send(updated);RememberProfile();
 }
 public async Task<JsonNode> ProfileAction(string action,JsonNode data)
 {
  if(IsOffline)throw new IOException("Офлайн-аккаунт не может изменять официальный профиль");await Login(false);if(Session==null) throw new InvalidOperationException("Сначала войдите в Microsoft");
  var baseUrl="https://api.minecraftservices.com/minecraft/profile/";
  HttpRequestMessage req;
  switch(action)
  {
   case "name": req=new(HttpMethod.Put,baseUrl+"name/"+Uri.EscapeDataString(data.Str("name"))); break;
   case "cape": req=new(HttpMethod.Put,baseUrl+"capes/active"){Content=JsonContent.Create(new{capeId=data.Str("capeId")})}; break;
   case "hideCape": req=new(HttpMethod.Delete,baseUrl+"capes/active");break;
   case "skin":
    var dlg=new Microsoft.Win32.OpenFileDialog{Filter="PNG скин|*.png"}; if(dlg.ShowDialog()!=true) return new JsonObject();
    var content=new MultipartFormDataContent(); content.Add(new StringContent(data.Str("variant")=="slim"?"slim":"classic"),"variant"); var image=new ByteArrayContent(await File.ReadAllBytesAsync(dlg.FileName)); image.Headers.ContentType=new("image/png");content.Add(image,"file",Path.GetFileName(dlg.FileName));req=new(HttpMethod.Post,baseUrl+"skins"){Content=content};break;
   default: throw new InvalidOperationException("Неизвестная операция профиля");
  }
  using(req) { req.Headers.Authorization=new("Bearer",Session.AccessToken);await Net.Send(req); }
  using var updated=new HttpRequestMessage(HttpMethod.Get,"https://api.minecraftservices.com/minecraft/profile");
  updated.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Session.AccessToken);Profile=await Net.Send(updated);RememberProfile();
  Session=new MSession(Profile.Str("name"),Session.AccessToken,Profile.Str("id"));return Profile!;
 }
}
