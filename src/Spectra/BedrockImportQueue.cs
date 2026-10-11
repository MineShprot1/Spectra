namespace Spectra;

public static class BedrockImportQueue
{
 // A pair of positive checks avoids immediately advancing when a manifest first appears.
 public static async Task<int> Run(IEnumerable<string> files,Func<string,Task<bool>> installed,Action<string> open,Func<Task> delay,Action<string,int,int>? progress=null,int maxPolls=180,Func<bool>? running=null,Func<TimeSpan>? elapsed=null)
 {
  if(maxPolls<2)throw new ArgumentOutOfRangeException(nameof(maxPolls));
  var timer=System.Diagnostics.Stopwatch.StartNew();elapsed??=()=>timer.Elapsed;
  bool seenRunning=false;
  void CheckRunning(){if(running==null)return;bool active=running();if(seenRunning&&!active)throw new OperationCanceledException("Импорт дополнений остановлен: Minecraft Bedrock закрыт.");seenRunning|=active;}
  var pending=files.ToArray();int opened=0;
  for(int index=0;index<pending.Length;index++){
   CheckRunning();var file=pending[index];if(await installed(file))continue;CheckRunning();
   progress?.Invoke(file,index,pending.Length);open(file);opened++;var lastOpened=elapsed();int confirmations=0;
   for(int attempt=0;attempt<maxPolls;attempt++){
    await delay();CheckRunning();confirmations=await installed(file)?confirmations+1:0;CheckRunning();
    if(confirmations>=2)break;
    if(attempt+1<maxPolls&&confirmations==0&&elapsed()-lastOpened>=TimeSpan.FromSeconds(15)){progress?.Invoke(file,index,pending.Length);CheckRunning();open(file);lastOpened=elapsed();}
   }
   if(confirmations<2)throw new TimeoutException("Не удалось подтвердить импорт дополнения: "+System.IO.Path.GetFileName(file)+". Очередь остановлена. Проверьте сообщение об импорте в Minecraft и нажмите ИГРАТЬ ещё раз.");
  }
  return opened;
 }
}
