using System.IO;
using System.Net;
using System.Net.Http;
using Spectra;
static class UpdateTests
{
 sealed class Handler:HttpMessageHandler
 {
  public HttpStatusCode Status=HttpStatusCode.OK;public string Body="[]";public bool Fail,NoCache;public int Calls;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation){Calls++;NoCache=request.Headers.CacheControl?.NoCache==true;if(Fail)throw new HttpRequestException("offline");return Task.FromResult(new HttpResponseMessage(Status){Content=new StringContent(Body)});}
 }
 public static async Task Run()
 {
  var root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"Spectra-update-tests-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
  try{
   var a=new string('a',40);var b=new string('b',40);File.WriteAllText(Path.Combine(root,"build-commit.txt"),a);
   using var handler=new Handler{Body="[{\"sha\":\""+b+"\",\"commit\":{\"message\":\"Update B\\nDetails\"}}]"};using var client=new HttpClient(handler);
   var fresh=await SourceUpdater.Check(client,root);if(!fresh.Checked||!fresh.Available||!fresh.Automatic||fresh.Commit!=b||fresh.Version!="Update B"||!handler.NoCache)throw new Exception("Fresh update check failed");
   handler.Body=handler.Body.Replace(b,a);var current=await SourceUpdater.Check(client,root);if(!current.Checked||current.Available)throw new Exception("Installed commit comparison failed");
   handler.Status=HttpStatusCode.TooManyRequests;var limited=await SourceUpdater.Check(client,root);if(limited.Checked||limited.Available||!limited.Update.Contains("429"))throw new Exception("Rate limit was reported as up-to-date");
   handler.Status=HttpStatusCode.OK;handler.Body="{}";if((await SourceUpdater.Check(client,root)).Checked)throw new Exception("Invalid response was accepted");handler.Fail=true;if((await SourceUpdater.Check(client,root)).Checked)throw new Exception("Offline was reported as up-to-date");handler.Fail=false;
   using var canceled=new CancellationTokenSource();canceled.Cancel();try{await SourceUpdater.Check(client,root,canceled.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}
   var tracker=new UpdateNoticeTracker();if(tracker.Observe(a,false)||!tracker.Observe(b,true)||tracker.Observe(b,true)||!tracker.Observe(a,true))throw new Exception("First available commit after an error must notify exactly once");
   var count=0;var active=0;var maximum=0;var recovered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
   using var loop=new UpdateCheckLoop(async ct=>{var running=Interlocked.Increment(ref active);maximum=Math.Max(maximum,running);try{var n=Interlocked.Increment(ref count);if(n==1)throw new HttpRequestException("transient");await Task.Delay(5,ct);if(n>=3)recovered.TrySetResult();}finally{Interlocked.Decrement(ref active);}},TimeSpan.FromMilliseconds(12));
   loop.Start();loop.Start();await recovered.Task.WaitAsync(TimeSpan.FromSeconds(3));loop.Dispose();await Task.Delay(40);var stopped=count;await Task.Delay(40);if(count!=stopped||maximum!=1)throw new Exception("Background checks overlap or continue after shutdown");
  }finally{if(root.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);}
  Console.WriteLine("PASS updates: installed SHA, fresh updates, offline/rate-limit errors, malformed replies, cancellation, first-success notice, dedup and independent retrying polling loop");
 }
}
