using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Diagnostics;
namespace Spectra;
/// <summary>Spectra Google session, independent of Minecraft access tokens.</summary>
public sealed class FriendsService(Store store,Authentication auth,GameService game)
{
 readonly HttpClient http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(5)};
 readonly SemaphoreSlim gate=new(1,1);
 static readonly JsonSerializerOptions WireJson=new(Store.Json){WriteIndented=false};
 string token="",scope="",sessionFile="";
 JsonNode? networkAccount;
 string flowId="",flowVerifier="",flowEndpoint="",flowOwner="";
 DateTime flowExpires;
 public Func<Task<string>>? PackPublisher {get;set;}
 public string LastSharingError {get;private set;}="";
 public string LanAddress {get;private set;}="";
 public JsonNode? Account {get{LoadSession();return networkAccount?.DeepClone();}}
 void LoadSession()
 {
  var owner=auth.Profile.Str("id");var endpoint=store.Config.FriendsEndpoint;
  var key=owner==""||endpoint==""?"":Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint+"\n"+owner))).ToLowerInvariant();if(scope==key)return;
  scope=key;token="";networkAccount=null;sessionFile=key==""?"":Path.Combine(store.Root,"network",key+".dat");
  if(sessionFile==""||!File.Exists(sessionFile))return;
  try{if(new FileInfo(sessionFile).Length>65536)return;var data=JsonNode.Parse(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(sessionFile),null,DataProtectionScope.CurrentUser)));var value=data.Str("token");if(value.Length==64&&value.All(Uri.IsHexDigit)&&data?["account"]!=null){token=value;networkAccount=data["account"]!.DeepClone();}}
  catch(Exception e) when(e is IOException or CryptographicException or JsonException){ }
 }
 void SaveSession(){Directory.CreateDirectory(Path.GetDirectoryName(sessionFile)!);var data=new JsonObject{["token"]=token,["account"]=networkAccount?.DeepClone()};File.WriteAllBytes(sessionFile+".tmp",ProtectedData.Protect(Encoding.UTF8.GetBytes(data.ToJsonString()),null,DataProtectionScope.CurrentUser));File.Move(sessionFile+".tmp",sessionFile,true);}
 void ClearSession(){if(sessionFile!=""&&File.Exists(sessionFile))File.Delete(sessionFile);token="";networkAccount=null;LanAddress="";}
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
 public async Task<JsonNode> BeginGoogle()
 {
  await gate.WaitAsync();try{
   if(auth.Profile==null)throw new IOException("Сначала войдите в свой Minecraft-профиль");LoadSession();
   var endpoint=ValidateEndpoint(store.Config.FriendsEndpoint);if(endpoint=="")throw new IOException("Укажите сервер сети Spectra в настройках");
   var verifier=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
   var challenge=Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+','-').Replace('/','_');
   var result=await Send(endpoint,"/oauth/start","",new{challenge});var id=result.Str("flowId");var url=result.Str("url");
   if(id.Length!=64||!id.All(Uri.IsHexDigit)||!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.GetLeftPart(UriPartial.Authority)!=endpoint||uri.AbsolutePath!="/oauth/google/start"||uri.Query!="?flow="+id)throw new IOException("Сервер вернул неверную ссылку входа");
   flowId=id;flowVerifier=verifier;flowEndpoint=endpoint;flowOwner=auth.Profile.Str("id");flowExpires=DateTime.UtcNow.AddMinutes(10);
   Process.Start(new ProcessStartInfo(url){UseShellExecute=true});return new JsonObject{["flowId"]=id,["status"]="pending"};
  }finally{gate.Release();}
 }
 public async Task<JsonNode> PollGoogle(string id)
 {
  await gate.WaitAsync();try{
   if(id==""||id!=flowId||DateTime.UtcNow>flowExpires||flowEndpoint!=store.Config.FriendsEndpoint||flowOwner!=auth.Profile.Str("id"))throw new IOException("Вход Spectra истёк или аккаунт изменился. Начните заново.");
   var result=await Send(flowEndpoint,"/oauth/poll","",new{flowId=id,verifier=flowVerifier});if(flowOwner!=auth.Profile.Str("id")||flowEndpoint!=store.Config.FriendsEndpoint)throw new IOException("Аккаунт изменился во время входа");if(result.Str("status")!="connected")return new JsonObject{["status"]="pending"};
   var value=result.Str("token");var account=result["account"];if(value.Length!=64||!value.All(Uri.IsHexDigit)||account.Str("id").Length!=32)throw new IOException("Сервер вернул неверную сессию Spectra");
   LoadSession();token=value;networkAccount=account?.DeepClone();SaveSession();flowId="";flowVerifier="";
   try{networkAccount=await Send(flowEndpoint,"/account",token,new{minecraftId=auth.Profile.Str("id"),minecraftName=auth.Profile.Str("name")});SaveSession();}catch(IOException){ }
   return new JsonObject{["status"]="connected",["account"]=networkAccount?.DeepClone()};
  }finally{gate.Release();}
 }
 public async Task<JsonNode> UpdateAccount(string name)
 {
  var account=await Call("/account",new{name,minecraftId=auth.Profile.Str("id"),minecraftName=auth.Profile.Str("name")});networkAccount=account;SaveSession();return account;
 }
 public async Task<JsonNode> Call(string path,object? data=null)
 {
  await gate.WaitAsync();try{
   LoadSession();var endpoint=ValidateEndpoint(store.Config.FriendsEndpoint);if(endpoint=="")throw new IOException("Укажите сервер сети Spectra в настройках");
   if(token=="")throw new IOException("Подключитесь к сети Spectra в собственном профиле");
   try{return await Send(endpoint,path,token,data);}catch(FriendsSessionExpired){ClearSession();throw new IOException("Сессия Spectra истекла. Подключитесь через Google в собственном профиле.");}
  }finally{gate.Release();}
 }
 async Task<JsonNode> Send(string endpoint,string path,string bearer,object? data)
 {
  using var request=new HttpRequestMessage(data==null?HttpMethod.Get:HttpMethod.Post,endpoint+path);if(bearer!="")request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
  if(data!=null)request.Content=new StringContent(JsonSerializer.Serialize(data,WireJson),Encoding.UTF8,"application/json");
  using var response=await http.SendAsync(request);var raw=await response.Content.ReadAsStringAsync();if(raw.Length>524288)throw new IOException("Слишком большой ответ сети Spectra");
  JsonNode? result;try{result=JsonNode.Parse(raw);}catch{throw new IOException("Сервер сети Spectra вернул неверный ответ");}
  if(response.StatusCode==HttpStatusCode.Unauthorized&&bearer!="")throw new FriendsSessionExpired();
  if(!response.IsSuccessStatusCode)throw new IOException(result.Str("error") is {Length:>0} error?error:"Сеть Spectra недоступна");return result??new JsonObject();
 }
 public async Task Heartbeat(){LoadSession();if(token==""||store.Config.FriendsEndpoint=="")return;if(game.Running.IsEmpty||!game.Activities.Values.Any(a=>a.GameState is "world" or "lan"))LanAddress="";var shared="";LastSharingError="";
  if(!store.Config.HideOnlineStatus&&store.Config.ShareGameActivity&&!game.Running.IsEmpty&&PackPublisher!=null){try{shared=await PackPublisher();}catch(Exception e) when(e is IOException or HttpRequestException or TaskCanceledException){LastSharingError=e.Message;}}
  // Re-read privacy after asynchronous uploads so a late heartbeat cannot undo a privacy change.
  var visible=store.Config.ShareGameActivity&&!store.Config.HideOnlineStatus;var data=JsonSerializer.SerializeToNode(game.FriendPresence(visible,LanAddress),Store.Json)!.AsObject();data["online"]=!store.Config.HideOnlineStatus;data["sharedPack"]=visible&&!game.Running.IsEmpty?shared:"";await Call("/presence",data);
 }
 public async Task Upload(string path,string file,string expected,long size)
 {
  await gate.WaitAsync();try{LoadSession();if(token=="")throw new IOException("Подключитесь к сети Spectra");if(await Net.Hash(file,"SHA256")!=expected)throw new IOException("Файл изменился во время передачи");using var request=new HttpRequestMessage(HttpMethod.Put,ValidateEndpoint(store.Config.FriendsEndpoint)+path);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);await using var stream=File.OpenRead(file);request.Content=new StreamContent(stream);request.Content.Headers.ContentLength=size;using var response=await http.SendAsync(request);if(!response.IsSuccessStatusCode){var error=JsonNode.Parse(await response.Content.ReadAsStringAsync());throw new IOException(error.Str("error"));}}finally{gate.Release();}
 }
 public async Task Download(string path,string dest,string expected,long size)
 {
  await gate.WaitAsync();var tmp=dest+".part";try{LoadSession();if(token=="")throw new IOException("Подключитесь к сети Spectra");Directory.CreateDirectory(Path.GetDirectoryName(dest)!);using var request=new HttpRequestMessage(HttpMethod.Get,ValidateEndpoint(store.Config.FriendsEndpoint)+path);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead);if(!response.IsSuccessStatusCode){var error=JsonNode.Parse(await response.Content.ReadAsStringAsync());throw new IOException(error.Str("error"));}
   if(response.Content.Headers.ContentLength!=size)throw new IOException("Неверный размер файла сборки");await using(var input=await response.Content.ReadAsStreamAsync()){await using var output=File.Create(tmp);var buffer=new byte[81920];long done=0,last=0;int count;while((count=await input.ReadAsync(buffer))>0){done+=count;if(done>size)throw new IOException("Слишком большой файл");await output.WriteAsync(buffer.AsMemory(0,count));if(Environment.TickCount64-last>150){last=Environment.TickCount64;Net.ProgressSink.Value?.Invoke(new{type="progress",message=Path.GetFileName(dest),downloadedBytes=done,totalBytes=size,percent=done*100d/size});}}if(done!=size)throw new IOException("Файл загружен не полностью");}
   if(await Net.Hash(tmp,"SHA256")!=expected)throw new IOException("Контрольная сумма сборки не совпадает");File.Move(tmp,dest,true);
  }finally{if(File.Exists(tmp))File.Delete(tmp);gate.Release();}
 }
 public async Task Disconnect()
 {
  await gate.WaitAsync();try{LoadSession();try{if(token!="")await Send(store.Config.FriendsEndpoint,"/logout",token,new{});}catch{}finally{ClearSession();flowId="";flowVerifier="";}}finally{gate.Release();}
 }
 sealed class FriendsSessionExpired:IOException {}
 public async Task<JsonNode> JoinInfo(string id,bool requireJoinTarget=true)
 {
  var friends=await Call("/friends");var friend=friends["items"]?.AsArray().FirstOrDefault(x=>x.Str("id")==id&&x.Str("relation")=="friend"&&x?["online"]?.GetValue<bool>()==true)??throw new IOException("Друг не в сети");
  var presence=friend["presence"]??throw new IOException("Нет данных об игре");if(presence["playing"]?.GetValue<bool>()!=true)throw new IOException("Друг не запустил Minecraft");if(requireJoinTarget&&(presence.Str("targetKind") is not ("servers" or "lan")||presence.Str("target")==""))throw new IOException("Друг не поделился адресом подключения");return presence;
 }
}
