using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Spectra;
// Loopback-only skin bridge. It supplies public textures; it never authenticates a Minecraft account.
public sealed class OfflineSkinBridge
{
 public static OfflineSkinBridge Shared {get;}=new();
 readonly TcpListener listener=new(IPAddress.Loopback,0);
 readonly RSA signing=RSA.Create(2048);
 readonly ConcurrentDictionary<string,JsonNode> profiles=new();
 readonly string secret=Guid.NewGuid().ToString("N");
 public string Url {get;}
 OfflineSkinBridge(){listener.Start();Url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/"+secret+"/";_=Accept();}
 async Task Accept(){while(true){var client=await listener.AcceptTcpClientAsync();_=Serve(client);}}
 async Task Serve(TcpClient client)
 {
  using(client){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));try{
   using var stream=client.GetStream();using var reader=new StreamReader(stream,Encoding.UTF8,false,1024,true);var first=await reader.ReadLineAsync(timeout.Token);if(first==null)return;var words=first.Split(' ');if(words.Length<2)return;var path=words[1].Split('?')[0];int length=0;for(int n=0;n<40;n++){var line=await reader.ReadLineAsync(timeout.Token);if(string.IsNullOrEmpty(line))break;if(line.Length>4096)return;if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))int.TryParse(line.Split(':')[1],out length);}
   if(length<0||length>16384)return;JsonNode? result=null;int status=200;
   if(!path.StartsWith("/"+secret+"/",StringComparison.Ordinal))status=404;
   else{path=path[(secret.Length+2)..];if(path=="")result=JsonSerializer.SerializeToNode(new{meta=new{serverName="Spectra offline textures",implementationName="Spectra"},skinDomains=new[]{".ely.by",".minecraft.net"},signaturePublickey=signing.ExportSubjectPublicKeyInfoPem()});
    else if(path.StartsWith("sessionserver/session/minecraft/profile/"))profiles.TryGetValue(path.Split('/').Last(),out result);
    else if(path=="api/profiles/minecraft"&&words[0]=="POST"){var chars=new char[length];await reader.ReadBlockAsync(chars.AsMemory(),timeout.Token);var names=JsonNode.Parse(new string(chars))?.AsArray()??[];result=new JsonArray(names.Take(20).Select(n=>profiles.Values.FirstOrDefault(p=>p.Str("name").Equals(n?.ToString(),StringComparison.OrdinalIgnoreCase))?.DeepClone()).Where(n=>n!=null).ToArray());}
    else status=404;
   }
   if(result==null&&status==200)status=204;var payload=Encoding.UTF8.GetBytes(result?.ToJsonString()??"");var header=Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status==200?"OK":"No Content")}\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");await stream.WriteAsync(header,timeout.Token);await stream.WriteAsync(payload,timeout.Token);
  }catch(Exception e) when(e is IOException or JsonException or OperationCanceledException or SocketException){}}
 }
 public async Task Register(string name,string elyName)
 {
  Authentication.ValidateName(name);Authentication.ValidateName(elyName);var info=await Net.Get("https://skinsystem.ely.by/textures/"+Uri.EscapeDataString(elyName));var textures=info["textures"]?.DeepClone()??info.DeepClone();
  foreach(var entry in textures.AsObject()){var url=entry.Value.Str("url");if(url=="")continue;if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||!(uri.Host.EndsWith(".ely.by")||uri.Host=="textures.minecraft.net"))throw new IOException("Ely.by вернул неизвестный адрес текстуры");entry.Value!["url"]=new UriBuilder(uri){Scheme="https",Port=-1}.Uri.ToString();}
  var uuid=Authentication.OfflineUuid(name);var data=JsonSerializer.SerializeToUtf8Bytes(new{timestamp=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),profileId=uuid,profileName=name,textures});var value=Convert.ToBase64String(data);var signature=Convert.ToBase64String(signing.SignData(Encoding.ASCII.GetBytes(value),HashAlgorithmName.SHA1,RSASignaturePadding.Pkcs1));profiles[uuid]=new JsonObject{["id"]=uuid,["name"]=name,["properties"]=new JsonArray(new JsonObject{["name"]="textures",["value"]=value,["signature"]=signature})};
 }
 static readonly SemaphoreSlim agentGate=new(1,1);
 public static async Task<string> Agent(Store store){await agentGate.WaitAsync();try{var path=Path.Combine(store.Root,"tools","authlib-injector.jar");if(File.Exists(path))return path;var meta=await Net.Get("https://authlib-injector.yushi.moe/artifact/latest.json");var url=new Uri(meta.Str("download_url"));if(url.Scheme!="https"||url.Host!="authlib-injector.yushi.moe")throw new IOException("Неверный адрес authlib-injector");await Net.Download(url.ToString(),path,meta["checksums"].Str("sha256"),"SHA256",10*1024*1024);return path;}finally{agentGate.Release();}}
}
