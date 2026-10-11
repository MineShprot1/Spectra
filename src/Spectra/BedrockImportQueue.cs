namespace Spectra;

public static class BedrockImportQueue
{
 // A pair of positive checks avoids immediately advancing when a manifest first appears.
 public static async Task<int> Run(IEnumerable<string> files,Func<string,Task<bool>> installed,Action<string> open,Func<Task> delay,Action<string,int,int>? progress=null,int maxPolls=90)
 {
  if(maxPolls<2)throw new ArgumentOutOfRangeException(nameof(maxPolls));
  var pending=files.ToArray();int opened=0;
  for(int index=0;index<pending.Length;index++){
   var file=pending[index];if(await installed(file))continue;
   progress?.Invoke(file,index,pending.Length);open(file);opened++;int confirmations=0;
   for(int attempt=0;attempt<maxPolls;attempt++){
    await delay();confirmations=await installed(file)?confirmations+1:0;
    if(confirmations>=2)break;
   }
   if(confirmations<2)throw new TimeoutException("Не удалось подтвердить импорт дополнения: "+System.IO.Path.GetFileName(file)+". Очередь остановлена. Проверьте сообщение об импорте в Minecraft и нажмите ИГРАТЬ ещё раз.");
  }
  return opened;
 }
}
