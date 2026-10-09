using System.IO;
using System.Text;
namespace Spectra;
/// <summary>Tail current launch only, bounded reads, shared handles and incremental UTF-8 decoding.</summary>
public sealed class ActivityLogWatcher(string path,Action<string> consume)
{
 volatile bool hasCurrentLog;
 public bool HasCurrentLog=>hasCurrentLog;
 long offset;DateTime creation;byte[] prefix=[];readonly Decoder decoder=Encoding.UTF8.GetDecoder();readonly StringBuilder pending=new();bool skipping;
 public void Baseline(){if(!File.Exists(path))return;using var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);offset=f.Length;creation=File.GetCreationTimeUtc(path);prefix=new byte[Math.Min(128,(int)Math.Min(offset,128))];f.ReadExactly(prefix);}
 public async Task Run(CancellationToken cancellation)
 {
  var bytes=new byte[32768];var chars=new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
  while(!cancellation.IsCancellationRequested){try{
   if(File.Exists(path)){
    await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,32768,true);var born=File.GetCreationTimeUtc(path);var head=new byte[Math.Min(128,(int)Math.Min(stream.Length,128))];await stream.ReadExactlyAsync(head,cancellation);
    if(born!=creation||stream.Length<offset||prefix.Length>0&&!head.AsSpan(0,Math.Min(prefix.Length,head.Length)).SequenceEqual(prefix.AsSpan(0,Math.Min(prefix.Length,head.Length)))){offset=0;decoder.Reset();pending.Clear();skipping=false;hasCurrentLog=false;}
    creation=born;prefix=head;if(stream.Length>offset)hasCurrentLog=true;stream.Position=offset;var budget=1024*1024;int read;
    while(budget>0&&(read=await stream.ReadAsync(bytes.AsMemory(0,Math.Min(bytes.Length,budget)),cancellation))>0){offset+=read;budget-=read;var count=decoder.GetChars(bytes,0,read,chars,0);for(var j=0;j<count;j++){var ch=chars[j];if(ch=='\n'){if(!skipping)consume(pending.ToString().TrimEnd('\r'));pending.Clear();skipping=false;}else if(!skipping){if(pending.Length>=16384){pending.Clear();skipping=true;}else pending.Append(ch);}}}
   }
  }catch(Exception e) when(e is IOException or UnauthorizedAccessException){/* A rotating/locked log is retried. */}
   try{await Task.Delay(1000,cancellation);}catch(OperationCanceledException){break;}
  }
 }
}
