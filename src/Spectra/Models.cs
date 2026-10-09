using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
namespace Spectra;
public record GameSettings
{
 public string JavaPath { get; set; } = "";
 public int MinRam { get; set; } = 512;
 public int MaxRam { get; set; } = 4096;
 public int Width { get; set; } = 1280;
 public int Height { get; set; } = 720;
 public string Gpu { get; set; } = "system";
 public bool HideOnLaunch { get; set; }
}
public record Instance
{
 public string Id { get; set; } = Guid.NewGuid().ToString("N");
 public string Name { get; set; } = "Новая сборка";
 public string Version { get; set; } = "";
 public string Loader { get; set; } = "vanilla";
 public string LoaderVersion { get; set; } = "";
 public string Icon { get; set; } = "";
 public string Banner { get; set; } = "";
 public GameSettings Settings { get; set; } = new();
 public string PackSource {get;set;}="";
 public string PackId {get;set;}="";
 public string PackVersion {get;set;}="";
 public DateTime? LastPlayed { get; set; }
}
public record SavedSkin
{
 public string Id {get;set;}=Guid.NewGuid().ToString("N");
 public string Name {get;set;}="Скин";
 public string Owner {get;set;}="";
 public string Variant {get;set;}="classic";
 public DateTime Added {get;set;}=DateTime.UtcNow;
}
public record Configuration
{
 [JsonIgnore] public string CurseForgeKey { get; set; } = "";
 [JsonIgnore] public string CraftyKey { get; set; } = "";
 public string FriendsEndpoint {get;set;}="https://spectra-friends.spectrafriends.workers.dev";
 public bool FriendsEndpointInitialized {get;set;}
 public bool ShareGameActivity {get;set;}=true;
 public bool HideOnlineStatus {get;set;}
 public string AppliedInstallerLanguage {get;set;}="";
 public string AppearanceJson {get;set;}="{}";
 public GameSettings Defaults { get; set; } = new();
 public string SelectedVersion {get;set;}="";
 public string SelectedInstance {get;set;}="";
 public string VersionsView {get;set;}="cards";
 public string InstancesView {get;set;}="cards";
 public List<SavedSkin> Skins {get;set;}=[];
 public List<Instance> Instances { get; set; } = [];
}
public sealed class Store
{
 public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
 public string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spectra");
 public Configuration Config { get; private set; }
 public Store() { Directory.CreateDirectory(Root); var p=Path.Combine(Root,"config.json"); Config=File.Exists(p)?JsonSerializer.Deserialize<Configuration>(File.ReadAllText(p),Json)??new():new();
 var secretPath=Path.Combine(Root,"connections.dat");if(File.Exists(secretPath)){var decrypted=ProtectedData.Unprotect(File.ReadAllBytes(secretPath),null,DataProtectionScope.CurrentUser);var secret=JsonSerializer.Deserialize<Dictionary<string,string>>(decrypted)!;Config.CurseForgeKey=secret.GetValueOrDefault("curseforge","");Config.CraftyKey=secret.GetValueOrDefault("crafty","");}
 if(!Config.FriendsEndpointInitialized){if(string.IsNullOrWhiteSpace(Config.FriendsEndpoint))Config.FriendsEndpoint="https://spectra-friends.spectrafriends.workers.dev";Config.FriendsEndpointInitialized=true;Save();}
 var localKeys=Path.Combine(AppContext.BaseDirectory,"api-keys.local.json");
 if(File.Exists(localKeys))
 {
  if(new FileInfo(localKeys).Length>16384)throw new IOException("Файл API-ключей слишком большой");
  var imported=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(localKeys))??new();
  if(imported.TryGetValue("curseforge",out var curseforge))Config.CurseForgeKey=curseforge;
  if(imported.TryGetValue("crafty",out var crafty))Config.CraftyKey=crafty;
  Save();File.Delete(localKeys); // Consume the explicitly supplied local setup file after DPAPI persistence.
 }
 Config.CurseForgeKey=BuiltInConnections.CurseForge;
 Config.CraftyKey=BuiltInConnections.Crafty;
 var preferences=Path.Combine(AppContext.BaseDirectory,"install-preferences.json");
 if(File.Exists(preferences)){
  var settings=JsonNode.Parse(File.ReadAllText(preferences));var id=settings?["id"]?.ToString()??"";var language=settings?["language"]?.ToString()??"ru";
  if(!string.IsNullOrEmpty(id)&&Config.AppliedInstallerLanguage!=id&&new[]{"ru","en","de","fr","es","pt","it","pl","uk","tr","zh","ja","ko"}.Contains(language)){
   var appearance=JsonNode.Parse(Config.AppearanceJson)?.AsObject()??new JsonObject();appearance["language"]=language;Config.AppearanceJson=appearance.ToJsonString();Config.AppliedInstallerLanguage=id;Save();
  }
 }
 }
 public void Save() { var p=Path.Combine(Root,"config.json"); File.WriteAllText(p+".tmp",JsonSerializer.Serialize(Config,Json)); File.Move(p+".tmp",p,true);
 var secret=JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string,string>{{"curseforge",Config.CurseForgeKey},{"crafty",Config.CraftyKey}});var secretPath=Path.Combine(Root,"connections.dat");File.WriteAllBytes(secretPath+".tmp",ProtectedData.Protect(secret,null,DataProtectionScope.CurrentUser));File.Move(secretPath+".tmp",secretPath,true);
 }
 public Instance Get(string id)=>id=="vanilla"?new Instance{Id="vanilla",Name="Minecraft "+Config.SelectedVersion,Version=Config.SelectedVersion,Settings=Config.Defaults with {}}:Config.Instances.Single(x=>x.Id==id);
 public string Folder(Instance i) { var p=Path.Combine(Root,"instances",i.Id,".minecraft"); Directory.CreateDirectory(p); return p; }
 public static string SafePath(string root,string relative)
 {
   var full=Path.GetFullPath(Path.Combine(root,relative)); var prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Недопустимый путь");
   return full;
 }
 public static void Validate(GameSettings s) { if(s.MinRam<256||s.MaxRam<s.MinRam||s.MaxRam>131072||s.Width<320||s.Height<240) throw new InvalidOperationException("Проверьте память и разрешение"); }
}
