using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
namespace Spectra;
public class GameService {public ConcurrentDictionary<string,object> Running=new();public Task Metadata(string version)=>Task.CompletedTask;public static string KindFolder(string kind)=>kind=="resources"?"resourcepacks":kind=="shaders"?"shaderpacks":"mods";}
public record GameSettings {public int MaxRam{get;set;}=4096;}
public record Instance {public string Id{get;set;}=Guid.NewGuid().ToString("N");public string Name{get;set;}="";public string Version{get;set;}="";public string Loader{get;set;}="vanilla";public string LoaderVersion{get;set;}="";public DateTime? CreatedAt{get;set;}public GameSettings Settings{get;set;}=new();public string PackSource{get;set;}="";public string PackId{get;set;}="";public string PackVersion{get;set;}="";public string Icon{get;set;}="";public string Banner{get;set;}="";public Dictionary<string,string> ModSides=new();}
public class Configuration {public string CurseForgeKey="";public GameSettings Defaults=new();public List<Instance> Instances=new();}
public class Store {public string Root=Path.Combine(Path.GetTempPath(),"spectra-catalog-"+Guid.NewGuid().ToString("N"));public Configuration Config=new();public Instance Get(string id)=>Config.Instances.Single(i=>i.Id==id);public string Folder(Instance instance){var path=Path.Combine(Root,"instances",instance.Id,".minecraft");Directory.CreateDirectory(path);return path;}public static string SafePath(string root,string name){var path=Path.GetFullPath(Path.Combine(root,name));if(!path.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.Ordinal))throw new IOException("Unsafe path");return path;}public void Save(){}}
public static class Net {
 public static Func<string,JsonNode> Reply=_=>new JsonObject();public static Dictionary<string,byte[]> Files=new();public static List<string> Requests=new();public static readonly HttpClient Http=new(new Handler());
 class Handler:HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel){Requests.Add(request.RequestUri!.AbsoluteUri);return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StringContent(Reply(request.RequestUri.AbsoluteUri).ToJsonString())});}}
 public static Task<JsonNode> Get(string url){Requests.Add(url);return Task.FromResult(Reply(url));}
 public static async Task<JsonNode> Send(HttpRequestMessage req)=>JsonNode.Parse(await (await Http.SendAsync(req)).Content.ReadAsStringAsync())!;
 public static async Task Download(string url,string dest,string? hash=null,string algorithm="SHA1",long? maxBytes=null){var bytes=Files[url];if(bytes.Length>maxBytes)throw new IOException("Too large");Directory.CreateDirectory(Path.GetDirectoryName(dest)!);await File.WriteAllBytesAsync(dest,bytes);if(hash!=null&&await Hash(dest,algorithm)!=hash.ToLowerInvariant())throw new IOException("Hash mismatch");}
 public static async Task<string> Hash(string path,string algorithm){var bytes=await File.ReadAllBytesAsync(path);var hash=algorithm=="MD5"?MD5.HashData(bytes):algorithm=="SHA512"?SHA512.HashData(bytes):SHA1.HashData(bytes);return Convert.ToHexString(hash).ToLowerInvariant();}
 public static string Str(this JsonNode? n,string key)=>n?[key]?.ToString()??"";
}
