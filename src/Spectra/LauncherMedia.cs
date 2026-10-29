using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Spectra;
public sealed class LauncherMedia(Store store)
{
 readonly object filesGate=new();readonly Dictionary<string,long> deletions=new();long deletionSerial;
 public string Root {get;}=Path.Combine(store.Root,"media");
 public LauncherMediaSettings Settings=>store.Config.Media;
 public string FilePath(string id){if(!System.Text.RegularExpressions.Regex.IsMatch(id,@"\A[a-f0-9]{64}\.[a-z0-9]{2,5}\z"))throw new IOException("Неверный медиафайл");return Path.Combine(Root,id);}
 static string Url(string id)=>"https://media.spectra.local/"+id;
 public object Snapshot()=>new{background=Settings.Background,music=Settings.Music,login=Settings.Login,placements=Settings.Placements,backgrounds=Settings.Backgrounds.Select(x=>new{x.Id,x.Name,x.Kind,url=Url(x.Id),thumbnail=File.Exists(Path.Combine(Root,x.Id+".thumb.jpg"))?Url(x.Id+".thumb.jpg"):""}),sounds=Settings.Sounds.Select(x=>new{x.Id,x.Name,x.Volume,url=Url(x.Id)})};
 public async Task Import(string path,bool audio)
 {
  var ext=Path.GetExtension(path).ToLowerInvariant();var allowed=audio?new[]{".mp3",".wav",".m4a",".ogg",".flac",".wma"}:new[]{".png",".jpg",".jpeg",".webp",".bmp",".gif",".mp4",".webm"};
  if(!allowed.Contains(ext))throw new IOException("Этот формат не поддерживается");var size=new FileInfo(path).Length;if(size==0||size>(audio?100L:ext is ".mp4" or ".webm"?512L:40L)*1024*1024)throw new IOException("Медиафайл слишком большой");
  Directory.CreateDirectory(Root);string hash;await using(var stream=File.OpenRead(path))hash=Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();var id=hash+ext;
  var list=audio?Settings.Sounds:Settings.Backgrounds;if(list.Any(x=>x.Id==id))return;if(list.Count>=100)throw new IOException("Можно сохранить до 100 файлов");
  var target=FilePath(id);var kind=audio?"audio":ext is ".mp4" or ".webm"?"video":ext==".gif"?"gif":"image";
  var existed=File.Exists(target);try{await Task.Run(()=>{lock(filesGate){deletions.Remove(id);if(!File.Exists(target))File.Copy(path,target,false);}if(kind is "image" or "gif")MakeThumbnail(target,id);});
   list.Add(new(){Id=id,Name=Path.GetFileNameWithoutExtension(path),Kind=kind});if(!audio)Settings.Background=id;store.Save();
  }catch{if(!existed&&File.Exists(target))File.Delete(target);throw;}
 }
 void MakeThumbnail(string path,string id)
 {
  var image=ReadImage(path,480);var encoder=new JpegBitmapEncoder{QualityLevel=80};encoder.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(Path.Combine(Root,id+".thumb.jpg"));encoder.Save(output);
 }
 static BitmapImage ReadImage(string path,int width)
 {
  var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=width;image.UriSource=new Uri(path);image.EndInit();image.Freeze();return image;
 }
 public void Remove(string id,bool audio)
 {
  FilePath(id);var list=audio?Settings.Sounds:Settings.Backgrounds;var asset=list.SingleOrDefault(x=>x.Id==id)??throw new IOException("Медиафайл не найден");
  list.Remove(asset);if(Settings.Background==id)Settings.Background="default";if(Settings.Music==id)Settings.Music="";if(Settings.Login==id)Settings.Login="";Settings.Placements.Remove(id);store.Save();
  // Originals may be shared between the two libraries; never remove a selected sound's source.
  if(!Settings.Sounds.Concat(Settings.Backgrounds).Any(x=>x.Id==id)){long ticket;lock(filesGate){ticket=++deletionSerial;deletions[id]=ticket;}_=Task.Run(async()=>{for(var attempt=0;attempt<4;attempt++){await Task.Delay(250*(attempt+1));lock(filesGate){if(!deletions.TryGetValue(id,out var current)||current!=ticket||!Directory.Exists(Root))return;foreach(var file in Directory.EnumerateFiles(Root,id+"*"))try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}}}});}

 }
 public void Configure(JsonNode d)
 {
  var settings=System.Text.Json.JsonSerializer.Deserialize<LauncherMediaSettings>(System.Text.Json.JsonSerializer.Serialize(Settings,Store.Json),Store.Json)!;
  foreach(var key in new[]{"background","music","login"})if(d[key]!=null){var id=d.Str(key);var valid=key=="background"?id=="default"||settings.Backgrounds.Any(x=>x.Id==id):id==""||settings.Sounds.Any(x=>x.Id==id);if(!valid)throw new IOException("Медиафайл не найден");if(key=="background")settings.Background=id;else if(key=="music")settings.Music=id;else settings.Login=id;}
  if(d["placement"]!=null){var id=d.Str("id");if(id!="default"&&!settings.Backgrounds.Any(x=>x.Id==id))throw new IOException("Фон не найден");var value=d["placement"]!;var zoom=value["zoom"]?.GetValue<double>()??1.15;var blur=value["blur"]?.GetValue<int>()??0;if(!double.IsFinite(zoom)||zoom<1||zoom>2.5||blur<0||blur>30)throw new IOException("Некорректная настройка фона");var asset=settings.Backgrounds.FirstOrDefault(x=>x.Id==id);settings.Placements[id]=new(){Live=value["live"]?.GetValue<bool>()==true,Zoom=zoom,Blur=asset?.Kind is "gif" or "video"?0:blur};}
  if(d["volume"]!=null){var value=d["volume"]!.GetValue<double>();if(!double.IsFinite(value)||value<0||value>1)throw new IOException("Громкость: от 0 до 100%");var asset=settings.Sounds.SingleOrDefault(x=>x.Id==d.Str("id"))??throw new IOException("Звук не найден");asset.Volume=value;}
  store.Config.Media=settings;store.Save();
 }
 public string Blur(string id,int amount)
 {
  var asset=Settings.Backgrounds.SingleOrDefault(x=>x.Id==id&&x.Kind=="image")??throw new IOException("Размытие доступно только для изображений");if(amount<1||amount>30)throw new IOException("Некорректная настройка фона");
  var name=id+".blur"+amount+".jpg";var target=Path.Combine(Root,name);if(File.Exists(target))return Url(name);
  // Blur a reduced texture once. The compositor subsequently draws a plain image,
  // without a full-window CSS blur pass on every frame or pointer movement.
  var source=new FormatConvertedBitmap(ReadImage(FilePath(id),960),PixelFormats.Bgra32,null,0);var w=source.PixelWidth;var h=source.PixelHeight;if(h>8192)throw new IOException("Изображение слишком высокое");var pixels=new byte[w*h*4];source.CopyPixels(pixels,w*4,0);var scratch=new byte[pixels.Length];var radius=Math.Max(1,amount/2);
  for(var pass=0;pass<3;pass++){Box(pixels,scratch,w,h,radius,true);Box(scratch,pixels,w,h,radius,false);}
  var encoder=new JpegBitmapEncoder{QualityLevel=88};encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,pixels,w*4)));using(var output=File.Create(target))encoder.Save(output);foreach(var old in Directory.EnumerateFiles(Root,id+".blur*.jpg").Where(x=>x!=target).OrderByDescending(File.GetLastWriteTimeUtc).Skip(2))try{File.Delete(old);}catch(IOException){}return Url(name);
 }
 static void Box(byte[] input,byte[] output,int w,int h,int radius,bool horizontal)
 {
  var lines=horizontal?h:w;var length=horizontal?w:h;var step=horizontal?4:w*4;var count=radius*2+1;
  for(int line=0;line<lines;line++){var origin=horizontal?line*w*4:line*4;for(int c=0;c<4;c++){long sum=0;for(var k=-radius;k<=radius;k++)sum+=input[origin+Math.Clamp(k,0,length-1)*step+c];for(var i=0;i<length;i++){output[origin+i*step+c]=(byte)(sum/count);sum+=input[origin+Math.Clamp(i+radius+1,0,length-1)*step+c]-input[origin+Math.Clamp(i-radius,0,length-1)*step+c];}}}
 }
 public void Poster(string id,string data)
 {
  if(!Settings.Backgrounds.Any(x=>x.Id==id&&x.Kind=="video"))throw new IOException("Фон не найден");if(!data.StartsWith("data:image/jpeg;base64,")||data.Length>512000)throw new IOException("Неверное изображение");var bytes=Convert.FromBase64String(data[23..]);using var stream=new MemoryStream(bytes);var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);if(decoder.Frames[0].PixelWidth>640||decoder.Frames[0].PixelHeight>640)throw new IOException("Неверное изображение");File.WriteAllBytes(Path.Combine(Root,id+".thumb.jpg"),bytes);
 }
}
