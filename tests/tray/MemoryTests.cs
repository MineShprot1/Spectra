using Spectra;
static class MemoryTests
{
 public static void Run()
 {
  var log=new RecentLogBuffer();
  Parallel.For(0,1000,n=>log.Add(n+":"+new string('x',100000)));
  log.Add("latest crash details");var tail=log.ToString();
  if(tail.Length>262144||!tail.EndsWith("latest crash details"))throw new Exception("Native log tail is unbounded or lost latest output");
  Console.WriteLine("PASS memory: concurrent oversized native logs retain a bounded recent diagnostic tail");
 }
}
