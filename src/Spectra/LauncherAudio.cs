using System.Windows;
using System.Windows.Media;
namespace Spectra;
public sealed class LauncherAudio:IDisposable
{
 readonly Window window;readonly LauncherMedia media;readonly Action<object> emit;
 readonly MediaPlayer music=new(),login=new(),preview=new();string current="",previewId="";bool disposed;
 public LauncherAudio(Window window,LauncherMedia media,Action<object> emit)
 {
  this.window=window;this.media=media;this.emit=emit;
  window.IsVisibleChanged+=Visibility;window.StateChanged+=Changed;
  music.MediaEnded+=(_,_)=>{music.Position=TimeSpan.Zero;Sync();};preview.MediaEnded+=(_,_)=>{previewId="";preview.Close();Sync();};
  foreach(var player in new[]{music,login,preview})player.MediaFailed+=(_,_)=>{player.Close();if(ReferenceEquals(player,preview)){previewId="";Sync();}emit(new{type="mediaError",message="Не удалось воспроизвести звук. Попробуйте MP3 или WAV."});};
 }
 bool Visible=>window.IsVisible&&window.WindowState!=WindowState.Minimized;
 void Visibility(object sender,DependencyPropertyChangedEventArgs e)=>Sync();
 void Changed(object? sender,EventArgs e)=>Sync();
 public void Sync()
 {
  if(disposed)return;var id=media.Settings.Music;var asset=media.Settings.Sounds.FirstOrDefault(x=>x.Id==id);
  if(current!=id){music.Close();current=id;if(asset!=null)music.Open(new Uri(media.FilePath(id)));}
  if(asset!=null)music.Volume=asset.Volume;if(previewId!="")preview.Volume=media.Settings.Sounds.FirstOrDefault(a=>a.Id==previewId)?.Volume??0;
  if(Visible&&asset!=null&&previewId=="")music.Play();else music.Pause();
  if(!Visible){login.Stop();preview.Close();previewId="";}
  emit(new{type="mediaWindow",visible=Visible});
 }
 public void Login()
 {
  if(disposed||!Visible)return;var asset=media.Settings.Sounds.FirstOrDefault(x=>x.Id==media.Settings.Login);if(asset==null)return;login.Close();login.Open(new Uri(media.FilePath(asset.Id)));login.Volume=asset.Volume;login.Play();
 }
 public void Preview(string id)
 {
  if(disposed)return;if(id==""||id==previewId){preview.Close();previewId="";Sync();return;}
  var asset=media.Settings.Sounds.Single(x=>x.Id==id);preview.Close();previewId=id;preview.Open(new Uri(media.FilePath(id)));preview.Volume=asset.Volume;Sync();preview.Play();
 }
 public void Dispose(){disposed=true;window.IsVisibleChanged-=Visibility;window.StateChanged-=Changed;music.Close();login.Close();preview.Close();}
}
