using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Spectra;

// Resolve actual profile textures rather than a third-party head service's default skin.
public sealed class PlayerAvatars(Func<string,Task<byte[]>>? fetch=null)
{
 readonly Func<string,Task<byte[]>> fetch=fetch??Download;
 readonly Dictionary<string,(DateTime Expires,Task<string> Image)> cache=new();
 public static string NormalizeSkin(string value)=>value.Replace("http://textures.minecraft.net/","https://textures.minecraft.net/");
 public static bool AllowedSkin(string value)=>Uri.TryCreate(NormalizeSkin(value),UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.UserInfo==""&&uri.IsDefaultPort&&(uri.Host=="textures.minecraft.net"||uri.Host=="skinsystem.ely.by");
 public Task<string> Get(string id,string name,string skin="")
 {
  id=id.Replace("-","");skin=NormalizeSkin(skin);
  if(skin!=""&&!AllowedSkin(skin))return Task.FromResult("");
  if(skin==""&&!Regex.IsMatch(id,@"\A[a-fA-F0-9]{32}\z")&&!Regex.IsMatch(name,@"\A[A-Za-z0-9_]{3,16}\z"))return Task.FromResult("");
  var key=skin!=""?skin:id!=""?id:name;
  lock(cache){if(cache.TryGetValue(key,out var item)&&item.Expires>DateTime.UtcNow)return item.Image;if(cache.Count>=128)cache.Remove(cache.Keys.First());var task=Load(id,name,skin);cache[key]=(DateTime.UtcNow.AddMinutes(5),task);_=task.ContinueWith(done=>{if(done.Status==TaskStatus.RanToCompletion&&done.Result=="")lock(cache){if(cache.TryGetValue(key,out var saved)&&ReferenceEquals(saved.Image,done))cache[key]=(DateTime.UtcNow.AddSeconds(30),done);}},TaskScheduler.Default);return task;}
 }
 async Task<string> Load(string id,string name,string skin)
 {
  try{
   if(skin==""){
    if(!Regex.IsMatch(id,@"\A[a-fA-F0-9]{32}\z")){var basic=JsonNode.Parse(await fetch("https://api.mojang.com/users/profiles/minecraft/"+Uri.EscapeDataString(name)));id=basic.Str("id");}
    if(!Regex.IsMatch(id,@"\A[a-fA-F0-9]{32}\z"))return "";
    var profile=JsonNode.Parse(await fetch("https://sessionserver.mojang.com/session/minecraft/profile/"+id));
    var value=profile?["properties"]?.AsArray().FirstOrDefault(x=>x.Str("name")=="textures").Str("value");
    if(string.IsNullOrEmpty(value)||value.Length>65536)return "";
    var textures=JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(value)));skin=NormalizeSkin(textures?["textures"]?["SKIN"].Str("url")??"");
   }
   if(!AllowedSkin(skin))return "";
   return Crop(await fetch(skin));
  }catch(Exception e) when(e is IOException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or FormatException or ArgumentException or InvalidOperationException or NotSupportedException){return "";}
 }
 static async Task<byte[]> Download(string url)
 {
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
  using var response=await Net.Http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,timeout.Token);response.EnsureSuccessStatusCode();
  await using var input=await response.Content.ReadAsStreamAsync(timeout.Token);using var output=new MemoryStream();var buffer=new byte[8192];int count;
  while((count=await input.ReadAsync(buffer,timeout.Token))>0){if(output.Length+count>1024*1024)throw new IOException("Avatar texture too large");output.Write(buffer,0,count);}return output.ToArray();
 }
 public static string Crop(byte[] bytes)
 {
  if(bytes.Length>1024*1024)throw new IOException("Avatar texture too large");
  using var source=new MemoryStream(bytes);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=source;bitmap.EndInit();bitmap.Freeze();
  if(bitmap.PixelWidth!=64||bitmap.PixelHeight is not (32 or 64))throw new IOException("Invalid skin dimensions");
  var pixels=new byte[8*8*4];new CroppedBitmap(new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0),new Int32Rect(8,8,8,8)).CopyPixels(pixels,32,0);
  // Old 64x32 skins often use an opaque background for the unused hat area.
  var hat=new byte[8*8*4];new CroppedBitmap(new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0),new Int32Rect(40,8,8,8)).CopyPixels(hat,32,0);
  var overlay=bitmap.PixelHeight==64||Enumerable.Range(0,64).Any(i=>hat[i*4+3]<255);
  if(overlay)for(var i=0;i<pixels.Length;i+=4){var alpha=hat[i+3];for(var c=0;c<3;c++)pixels[i+c]=(byte)((hat[i+c]*alpha+pixels[i+c]*(255-alpha)+127)/255);pixels[i+3]=255;}
  var face=BitmapSource.Create(8,8,96,96,PixelFormats.Bgra32,null,pixels,32);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(face));using var output=new MemoryStream();encoder.Save(output);return "data:image/png;base64,"+Convert.ToBase64String(output.ToArray());
 }
}
