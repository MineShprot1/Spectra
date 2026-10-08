using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Spectra;
/// <summary>Only the trusted configured Worker receives the Minecraft token; never exposed to WebView.</summary>
public sealed class FriendsService(Store store,Authentication auth,GameService game)
{
 readonly HttpClient http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(15)};
 readonly SemaphoreSlim gate=new(1,1);
 string token="",tokenOwner="",tokenEndpoint="";
 public string LanAddress {get;private set;}="";
 public static string ValidateEndpoint(string value)
 {
  if(string.IsNullOrWhiteSpace(value))return "";
  if(!Uri.TryCreate(value.Trim(),UriKind.Absolute,out var uri)||uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment)||uri.AbsolutePath!="/"||uri.IsLoopback)throw new IOException("Укажите HTTPS-адрес своего сервера друзей без пути");
  return uri.GetLeftPart(UriPartial.Authority);
 }
 public void ShareLan(string address)
 {
  if(address==""){LanAddress="";return;}
  var parts=address.Split(':');if(parts.Length!=2||!IPAddress.TryParse(parts[0],out var ip)||ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork||!int.TryParse(parts[1],out var port)||port<1||port>65535)throw new IOException("Укажите LAN-адрес в формате 192.168.1.5:12345");
  var b=ip.GetAddressBytes();if(!(b[0]==10||b[0]==192&&b[1]==168||b[0]==172&&b[1]>=16&&b[1]<=31))throw new IOException("Разрешён только адрес локальной сети");
  if(game.Running.IsEmpty)throw new IOException("Сначала запустите Minecraft и откройте мир для сети");LanAddress=address;
 }
 public async Task<JsonNode> Call(string path,object? data=null)
 {
  await gate.WaitAsync();try{
   var endpoint=ValidateEndpoint(store.Config.FriendsEndpoint);if(endpoint=="")throw new IOException("Укажите сервер друзей в настройках Spectra");
   if(auth.Session==null||auth.Profile==null)throw new IOException("Войдите в Minecraft-аккаунт");
   var owner=auth.Profile.Str("id");if(token==""||tokenOwner!=owner||tokenEndpoint!=endpoint){token="";await auth.Login(false);owner=auth.Profile.Str("id");var session=await Send(endpoint,"/auth",auth.Session!.AccessToken,new{});token=session.Str("token");if(token.Length!=64||session.Str("id")!=owner.Replace("-","").ToLowerInvariant())throw new IOException("Сервер друзей вернул неверную сессию аккаунта");tokenOwner=owner;tokenEndpoint=endpoint;}
   try{return await Send(endpoint,path,token,data);}catch(FriendsSessionExpired){token="";throw new IOException("Сессия друзей истекла. Повторите действие.");}
  }finally{gate.Release();}
 }
 async Task<JsonNode> Send(string endpoint,string path,string bearer,object? data)
 {
  using var request=new HttpRequestMessage(data==null?HttpMethod.Get:HttpMethod.Post,endpoint+path);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
  if(data!=null)request.Content=new StringContent(JsonSerializer.Serialize(data,Store.Json),Encoding.UTF8,"application/json");
  using var response=await http.SendAsync(request);var raw=await response.Content.ReadAsStringAsync();if(raw.Length>524288)throw new IOException("Слишком большой ответ сервера друзей");
  JsonNode? result;try{result=JsonNode.Parse(raw);}catch{throw new IOException("Сервер друзей вернул неверный ответ");}
  if(response.StatusCode==HttpStatusCode.Unauthorized&&path!="/auth")throw new FriendsSessionExpired();
  if(!response.IsSuccessStatusCode&&path=="/auth"){
   var code=result.Str("code");var detail=result.Str("error");
   throw new IOException(code switch{
    "minecraft_unauthorized"=>"Minecraft Services отклонил обновлённый токен (HTTP 401). Выйдите из аккаунта и войдите снова.",
    "minecraft_forbidden"=>"Minecraft Services запретил проверку с сервера друзей (HTTP 403). Локальный вход обновлён; проблема на стороне доступа Worker к Minecraft API. Сообщите владельцу сервера.",
    "minecraft_rate_limited"=>"Minecraft Services ограничил запросы сервера друзей (HTTP 429). Попробуйте позже.",
    "minecraft_unavailable"=>"Minecraft Services временно недоступен для сервера друзей. Попробуйте позже.",
    _=>"Не удалось проверить Minecraft-аккаунт на сервере друзей: "+(detail==""?"HTTP "+(int)response.StatusCode:detail)+". Если Worker старый, обновите его из архива сервера."
   });
  }
  if(!response.IsSuccessStatusCode)throw new IOException(result.Str("error") is {Length:>0} error?error:"Сервер друзей недоступен");return result??new JsonObject();
 }
 public async Task Heartbeat(){if(store.Config.FriendsEndpoint==""||auth.Session==null)return;if(game.Running.IsEmpty)LanAddress="";await Call("/presence",game.FriendPresence(store.Config.ShareGameActivity,LanAddress));}
 public async Task Disconnect(){try{if(token!="")await Call("/logout",new{});}catch{}finally{token="";tokenOwner="";LanAddress="";}}
 sealed class FriendsSessionExpired:Exception {}
 public async Task<JsonNode> JoinInfo(string id)
 {
  var friends=await Call("/friends");var friend=friends["items"]?.AsArray().FirstOrDefault(x=>x.Str("id")==id&&x.Str("relation")=="friend"&&x?["online"]?.GetValue<bool>()==true)??throw new IOException("Друг не в сети");
  var presence=friend["presence"]??throw new IOException("Нет данных об игре");if(presence.Str("targetKind") is not ("servers" or "lan")||presence.Str("target")=="")throw new IOException("Друг не поделился адресом подключения");return presence;
 }
}
