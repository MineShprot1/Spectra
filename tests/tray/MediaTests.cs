using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spectra;
static class MediaTests
{
 public static void Run()
 {
  var root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"Spectra-media-tests-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
  try{
   var store=new Store(root);var media=new LauncherMedia(store);if(media.Settings.Background!="default"||media.Settings.Music!=""||media.Settings.Login!="")throw new Exception("Media must be silent by default");
   var pixels=new byte[128*64*4];for(var i=0;i<pixels.Length;i+=4){pixels[i]=(byte)(i/4%128<64?0:255);pixels[i+2]=(byte)(255-pixels[i]);pixels[i+3]=255;}var source=BitmapSource.Create(128,64,96,96,PixelFormats.Bgra32,null,pixels,128*4);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(source));var path=Path.Combine(root,"background.png");using(var output=File.Create(path))png.Save(output);
   Task.Run(()=>media.Import(path,false)).GetAwaiter().GetResult();var id=media.Settings.Background;if(!File.Exists(media.FilePath(id))||!File.Exists(media.FilePath(id)+".thumb.jpg"))throw new Exception("Image/thumbnail not saved");
   Task.Run(()=>media.Import(path,false)).GetAwaiter().GetResult();if(media.Settings.Backgrounds.Count!=1)throw new Exception("Duplicate media files");
   media.Configure(JsonNode.Parse("{\"id\":\""+id+"\",\"placement\":{\"live\":true,\"zoom\":1.5,\"blur\":12}}")!);
   var blurred=Task.Run(()=>media.Blur(id,12)).GetAwaiter().GetResult();if(!blurred.EndsWith(".blur12.jpg")||!File.Exists(media.FilePath(id)+".blur12.jpg"))throw new Exception("Cached blur missing");var stamp=File.GetLastWriteTimeUtc(media.FilePath(id)+".blur12.jpg");Task.Run(()=>media.Blur(id,12)).GetAwaiter().GetResult();if(stamp!=File.GetLastWriteTimeUtc(media.FilePath(id)+".blur12.jpg"))throw new Exception("Blur was regenerated");
   var audio=Path.Combine(root,"quiet.wav");using(var file=new BinaryWriter(File.Create(audio))){file.Write("RIFF"u8);file.Write(36+800);file.Write("WAVEfmt "u8);file.Write(16);file.Write((short)1);file.Write((short)1);file.Write(8000);file.Write(8000);file.Write((short)1);file.Write((short)8);file.Write("data"u8);file.Write(800);file.Write(Enumerable.Repeat((byte)128,800).ToArray());}
   Task.Run(()=>media.Import(audio,true)).GetAwaiter().GetResult();var sound=media.Settings.Sounds[0].Id;media.Configure(JsonNode.Parse("{\"music\":\""+sound+"\",\"login\":\""+sound+"\",\"id\":\""+sound+"\",\"volume\":0.2}")!);
   var restored=new Store(root).Config.Media;if(restored.Music!=sound||restored.Login!=sound||restored.Sounds[0].Volume!=.2||!restored.Placements[id].Live)throw new Exception("Media settings lost on restart");
   try{media.Configure(JsonNode.Parse("{\"background\":\"default\",\"music\":\"not-found\"}")!);throw new Exception("Invalid selection accepted");}catch(IOException){}if(media.Settings.Background!=id)throw new Exception("Failed update partially changed selection");
   try{media.Remove("default",false);throw new Exception("Default removed");}catch(IOException){}
   try{media.FilePath("../config.json");throw new Exception("Traversal accepted");}catch(IOException){}
   media.Remove(sound,true);if(media.Settings.Music!=""||media.Settings.Login!="")throw new Exception("Deleted audio still selected");media.Remove(id,false);if(media.Settings.Background!="default")throw new Exception("Default wallpaper not restored");
   Thread.Sleep(350);
  }finally{if(root.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);}
  Console.WriteLine("PASS media library: deduplicated local files, thumbnails, cached blur, default protection, atomic validation, volume/selection persistence and removal");
 }
 public static void Toast(SocialDesktop desktop)
 {
  var method=typeof(SocialDesktop).GetMethod("NotificationContent",BindingFlags.NonPublic|BindingFlags.Instance)!;var result=method.Invoke(desktop,["Player",new string('M',300),new JsonObject{["type"]="friend"}])!;var root=(Border)result.GetType().GetProperty("Root")!.GetValue(result)!;
  root.Measure(new Size(340,1000));root.Arrange(new Rect(new Point(),root.DesiredSize));root.UpdateLayout();if(root.DesiredSize.Height>106||root.DesiredSize.Height<76)throw new Exception("Toast is not compact");
  var bitmap=new RenderTargetBitmap(340,(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));var output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../test-results/toast-026.png"));Directory.CreateDirectory(Path.GetDirectoryName(output)!);using var file=File.Create(output);encoder.Save(file);
 }
}
