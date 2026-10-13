using System.Diagnostics;
using System.IO;
using System.Text.Json;
namespace Spectra;
public sealed class PlaytimeService : IDisposable
{
 public record Entry
 {
  public string Key {get;set;}="";
  public string Name {get;set;}="";
  public string Version {get;set;}="";
  public string Kind {get;set;}="instance";
  public double Seconds {get;set;}
 }
 public record Report(double TotalSeconds,Entry[] Items);
 public record Ledger
 {
  public List<Entry> Entries {get;set;}=[];
  public List<string> Imported {get;set;}=[];
 }
 readonly string path;readonly object gate=new();readonly Dictionary<string,(string Key,long Stamp)> active=new();Ledger ledger;
 readonly Timer timer;
 public PlaytimeService(string root){path=Path.Combine(root,"playtime.json");ledger=new();if(File.Exists(path))try{if(new FileInfo(path).Length<=2*1024*1024)ledger=JsonSerializer.Deserialize<Ledger>(File.ReadAllText(path),Store.Json)??new();}catch(Exception e) when(e is IOException or JsonException){ }ledger.Imported??=[];ledger.Entries=(ledger.Entries??[]).Where(e=>e!=null&&e.Key!=null&&e.Key.Length<=200&&double.IsFinite(e.Seconds)&&e.Seconds>=0).DistinctBy(e=>e.Key).ToList();timer=new Timer(_=>{try{Checkpoint();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){}},null,TimeSpan.FromSeconds(20),TimeSpan.FromSeconds(20));}
 public static string Key(string id,string version)=>id=="vanilla"?"version:"+version:"instance:"+id;
 Entry Get(string key,string name,string version,string kind){var entry=ledger.Entries.FirstOrDefault(e=>e.Key==key);if(entry==null){entry=new(){Key=key};ledger.Entries.Add(entry);}entry.Name=name;entry.Version=version;entry.Kind=kind;return entry;}
 public void Start(string processId,string id,string name,string version){if(processId.StartsWith("multi-",StringComparison.Ordinal))return;lock(gate){if(active.ContainsKey(processId))return;var key=Key(id,version);Get(key,name,version,id=="vanilla"?"version":id=="bedrock"?"bedrock":"instance");active[processId]=(key,Stopwatch.GetTimestamp());}}
 void Accumulate(){var now=Stopwatch.GetTimestamp();foreach(var item in active.ToArray()){var elapsed=Stopwatch.GetElapsedTime(item.Value.Stamp,now).TotalSeconds;ledger.Entries.Single(e=>e.Key==item.Value.Key).Seconds+=Math.Max(0,elapsed);active[item.Key]=(item.Value.Key,now);}}
 public void Stop(string processId){lock(gate){Accumulate();if(active.Remove(processId))Save();}}
 public void Import(string id,string name,string version,double seconds,string fingerprint){if(!double.IsFinite(seconds)||seconds<=0||seconds>315576000)return;lock(gate){if(ledger.Imported.Contains(fingerprint,StringComparer.Ordinal))return;Get(Key(id,version),name,version,"instance").Seconds+=seconds;ledger.Imported.Add(fingerprint);Save();}}
 public Report Snapshot(){lock(gate){var entries=ledger.Entries.Select(e=>e with{}).ToArray();var now=Stopwatch.GetTimestamp();foreach(var value in active.Values)entries.Single(e=>e.Key==value.Key).Seconds+=Math.Max(0,Stopwatch.GetElapsedTime(value.Stamp,now).TotalSeconds);return new(entries.Sum(e=>e.Seconds),entries.OrderByDescending(e=>e.Seconds).ToArray());}}
 void Save(){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path+".tmp",JsonSerializer.Serialize(ledger,Store.Json));File.Move(path+".tmp",path,true);}
 public void Checkpoint(){lock(gate){Accumulate();Save();}}
 public void Dispose(){timer.Dispose();lock(gate){Accumulate();Save();active.Clear();}}
}
