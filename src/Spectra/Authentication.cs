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
 public bool HasSavedAccount=>accounts.HasAccounts;
 public Authentication(Store store,Action<object> emit)
 {
  this.store=store;this.emit=emit;
  accounts=new ProtectedAccountManager(Path.Combine(store.Root,"microsoft-accounts.dat"));
  handler=new CmlLib.Core.Auth.Microsoft.JELoginHandlerBuilder()
   .WithHttpClient(Net.Http).WithAccountManager(accounts).Build();
 }
 public async Task Login(bool interactive)
 {
  await loginGate.WaitAsync();
  try
  {
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
   MSession session;
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
   AuthenticationResult result;
   if(interactive)
    result=await browserApp.AcquireTokenInteractive(MsalClientHelper.XboxScopes).WithUseEmbeddedWebView(false)
     .WithSystemWebViewOptions(new SystemWebViewOptions
     {
      HtmlMessageSuccess="<html><head><meta charset=\"utf-8\"><title>Spectra</title></head><body style=\"font-family:Segoe UI,sans-serif;text-align:center;padding-top:15vh\"><h2>Готово!</h2><p>Доступ подтверждён. Можно закрыть эту вкладку и вернуться в Spectra.</p></body></html>",
      HtmlMessageError="<html><head><meta charset=\"utf-8\"><title>Spectra</title></head><body style=\"font-family:Segoe UI,sans-serif;text-align:center;padding-top:15vh\"><h2>Не удалось войти</h2><p>Вернитесь в Spectra и попробуйте ещё раз.</p></body></html>"
     }).ExecuteAsync(timeout.Token);
   else
   {
    var cached=(await browserApp.GetAccountsAsync()).LastOrDefault();
    if(cached==null)throw new InvalidOperationException("Войдите с Microsoft в браузере");
    result=await browserApp.AcquireTokenSilent(MsalClientHelper.XboxScopes,cached).ExecuteAsync(timeout.Token);
   }
   var authenticator=interactive?handler.CreateAuthenticatorWithNewAccount():handler.CreateAuthenticatorWithDefaultAccount();
   authenticator.AddMsalOAuth(browserApp,msal=>msal.FromResult(result));
   authenticator.AddXboxAuthForJE(xbox=>xbox.Basic());
   authenticator.AddForceJEAuthenticator();
   session=await authenticator.ExecuteForLauncherAsync();
   }
   using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.minecraftservices.com/minecraft/profile");
   request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",session.AccessToken);
   var profile=await Net.Send(request);
   Session=session;Profile=profile;accounts.SaveAccounts();
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
   if(browserApp!=null)foreach(var account in await browserApp.GetAccountsAsync())await browserApp.RemoveAsync(account);
   var cache=Path.Combine(store.Root,"msal-browser.dat");if(File.Exists(cache))File.Delete(cache);
   accounts.ClearAccounts();accounts.SaveAccounts();Session=null;Profile=null;
   var legacy=Path.Combine(store.Root,"account.dat");if(File.Exists(legacy))File.Delete(legacy);
  }
  finally{loginGate.Release();}
 }
 public async Task ApplySavedSkin(string path,string variant)
 {
  if(Session==null)throw new InvalidOperationException("Сначала войдите");
  using var req=new HttpRequestMessage(HttpMethod.Post,"https://api.minecraftservices.com/minecraft/profile/skins");
  var content=new MultipartFormDataContent();content.Add(new StringContent(variant),"variant");
  var image=new ByteArrayContent(await File.ReadAllBytesAsync(path));image.Headers.ContentType=new("image/png");content.Add(image,"file",Path.GetFileName(path));req.Content=content;req.Headers.Authorization=new("Bearer",Session.AccessToken);await Net.Send(req);
  using var updated=new HttpRequestMessage(HttpMethod.Get,"https://api.minecraftservices.com/minecraft/profile");updated.Headers.Authorization=new("Bearer",Session.AccessToken);Profile=await Net.Send(updated);
 }
 public async Task<JsonNode> ProfileAction(string action,JsonNode data)
 {
  if(Session==null) throw new InvalidOperationException("Сначала войдите в Microsoft");
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
  updated.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Session.AccessToken);Profile=await Net.Send(updated);
  Session=new MSession(Profile.Str("name"),Session.AccessToken,Profile.Str("id"));return Profile!;
 }
}
