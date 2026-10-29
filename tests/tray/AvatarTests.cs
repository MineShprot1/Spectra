using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spectra;
static class AvatarTests
{
 public static void Run()
 {
  var pixels=new byte[64*64*4];
  void Fill(int x,int y,byte red,byte green,byte blue,byte alpha){var i=(y*64+x)*4;pixels[i]=blue;pixels[i+1]=green;pixels[i+2]=red;pixels[i+3]=alpha;}
  for(int y=8;y<16;y++)for(int x=8;x<16;x++)Fill(x,y,20,80,160,255);
  Fill(40,8,220,100,40,255);
  var source=BitmapSource.Create(64,64,96,96,PixelFormats.Bgra32,null,pixels,64*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));using var png=new MemoryStream();encoder.Save(png);var texture=png.ToArray();
  var calls=new List<string>();var id=new string('a',32);var skin="https://textures.minecraft.net/texture/test";
  var textures=Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new{textures=new{SKIN=new{url=skin}}}));
  var avatars=new PlayerAvatars(url=>{calls.Add(url);return Task.FromResult(url.Contains("sessionserver")?JsonSerializer.SerializeToUtf8Bytes(new{properties=new[]{new{name="textures",value=textures}}}):texture);});
  var data=avatars.Get(id,"Friend").GetAwaiter().GetResult();
  if(!data.StartsWith("data:image/png;base64,"))throw new Exception("No resolved avatar");
  if(calls.Count!=2||!calls[0].EndsWith(id)||calls[1]!=skin)throw new Exception("Wrong profile texture source");
  using var image=new MemoryStream(Convert.FromBase64String(data[22..]));var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=image;bitmap.EndInit();var result=new byte[8*8*4];new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0).CopyPixels(result,32,0);
  if(bitmap.PixelWidth!=8||bitmap.PixelHeight!=8||result[2]!=220||result[6]!=20)throw new Exception("Head/hat crop mismatch");
  avatars.Get(id,"Friend").GetAwaiter().GetResult();if(calls.Count!=2)throw new Exception("Avatar request not cached");
  avatars.Get(id,"Player","https://skinsystem.ely.by/skins/Player.png").GetAwaiter().GetResult();if(calls.Count!=3||!calls[2].Contains("ely.by"))throw new Exception("Own Ely texture ignored");
  if(avatars.Get(id,"Player","https://invalid.example/skin").GetAwaiter().GetResult()!=""||calls.Count!=3)throw new Exception("Untrusted skin source accepted");
  var decision=typeof(SocialDesktop).GetMethod("TrayShouldClose",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
  bool Close(bool visible,bool escape,nint foreground)=>(bool)decision.Invoke(null,[true,visible,escape,foreground,(nint)1,(nint)2,(nint)3])!;
  if(Close(true,false,2)||Close(true,false,3)||!Close(false,false,2)||!Close(true,true,2)||!Close(true,false,4))throw new Exception("Tray anchor lifetime mismatch");
  Console.WriteLine("PASS avatars/tray: actual textures, face and hat pixels, Ely source, deduplicated requests, source validation and overflow/focus/Escape dismissal");
 }
}
