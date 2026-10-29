using System.IO;
using System.Text.Json.Nodes;
namespace Spectra;
public partial class MainWindow
{
 readonly SemaphoreSlim mediaGate=new(1,1);readonly LauncherMedia mediaLibrary;readonly LauncherAudio launcherAudio;
 async Task<object?> MediaCommand(string action,JsonNode data)
 {
  await mediaGate.WaitAsync();try{switch(action){
   case "mediaImport":{
    var audio=data.Str("kind")=="audio";var picker=new Microsoft.Win32.OpenFileDialog{Title=localization.Text(audio?"Добавить звук":"Добавить фон"),Filter=localization.Filter(audio?"Аудио|*.mp3;*.wav;*.m4a;*.ogg;*.flac;*.wma":"Фоны|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.mp4;*.webm")};
    if(picker.ShowDialog(this)!=true)return State();await mediaLibrary.Import(picker.FileName,audio);break;
   }
   case "mediaRemove":launcherAudio.Preview("");mediaLibrary.Remove(data.Str("id"),data.Str("kind")=="audio");break;
   case "mediaConfigure":mediaLibrary.Configure(data);break;
   case "mediaBlur":return await Task.Run(()=>mediaLibrary.Blur(data.Str("id"),data["amount"]!.GetValue<int>()));
   case "mediaPoster":mediaLibrary.Poster(data.Str("id"),data.Str("image"));return State();
   case "mediaPreview":launcherAudio.Preview(data.Str("id"));return null;
  }
  launcherAudio.Sync();return State();}finally{mediaGate.Release();}
 }
}
