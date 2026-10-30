namespace Spectra;
// Runs independently of WebView timers and window visibility. A failed request
// does not end the loop or overlap the next request.
public sealed class UpdateCheckLoop(Func<CancellationToken,Task> check,TimeSpan interval):IDisposable
{
 readonly CancellationTokenSource stop=new();Task? worker;int disposed;
 public void Start(){if(Volatile.Read(ref disposed)!=0)return;worker??=Task.Run(async()=>{using var timer=new PeriodicTimer(interval);try{while(await timer.WaitForNextTickAsync(stop.Token)){try{await check(stop.Token);}catch(OperationCanceledException) when(stop.IsCancellationRequested){break;}catch{/* Retry at the next interval. */}}}catch(OperationCanceledException) when(stop.IsCancellationRequested){}});}
 public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;stop.Cancel();if(worker==null)stop.Dispose();else _=worker.ContinueWith(_=>stop.Dispose(),TaskScheduler.Default);}
}
