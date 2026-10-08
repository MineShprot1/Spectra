using Spectra;
using System.Text;
using System.Text.Json;
var checks=0;
void Assert(bool x,string name){if(!x)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
void Throws(Action fn,string name){try{fn();}catch(InvalidOperationException){checks++;Console.WriteLine("PASS "+name);return;}throw new Exception("Expected failure: "+name);}
var root=Path.Combine(Path.GetTempPath(),"spectra-tests");
Throws(()=>Store.SafePath(root,"../escape"),"Reject parent traversal");
Throws(()=>Store.SafePath(root,Path.GetFullPath(Path.Combine(root,"..","outside"))),"Reject absolute paths outside instance");
Assert(Store.SafePath(root,"mods/a.jar").StartsWith(root),"Allow child paths");
Throws(()=>Store.Validate(new(){MinRam=4096,MaxRam=2048}),"Reject inverted RAM");
Throws(()=>Store.Validate(new(){MinRam=128}),"Reject tiny RAM");
Throws(()=>Store.Validate(new(){Width=100}),"Reject invalid dimensions");
var original=new Instance{Name="Тест",Version="1.20.1",Settings=new(){JavaPath=@"C:\Java\bin\javaw.exe",MaxRam=6144}};
var copy=JsonSerializer.Deserialize<Instance>(JsonSerializer.Serialize(original,Store.Json),Store.Json)!;
Assert(copy.Name==original.Name&&copy.Settings.MaxRam==6144&&copy.Id==original.Id,"Roundtrip persisted instance");
var defaults=new GameSettings{MaxRam=4096};var clone=defaults with{};clone.MaxRam=8192;Assert(defaults.MaxRam==4096,"New instance settings are isolated");
var nbtFile=Path.GetTempFileName();
try {
 using(var file=File.Create(nbtFile))using(var w=new BinaryWriter(file,Encoding.UTF8)) {
  void Str(string s){var b=Encoding.UTF8.GetBytes(s);w.Write((byte)(b.Length>>8));w.Write((byte)b.Length);w.Write(b);}
  w.Write((byte)10);Str("");w.Write((byte)9);Str("servers");w.Write((byte)10);w.Write(new byte[]{0,0,0,1});w.Write((byte)8);Str("name");Str("Тест сервер");w.Write((byte)8);Str("ip");Str("localhost:25565");w.Write((byte)0);w.Write((byte)0);
 }
 var json=JsonSerializer.Serialize(Nbt.Servers(nbtFile));Assert(json.Contains("localhost:25565"),"Read Java NBT server list");
}finally{File.Delete(nbtFile);}
Console.WriteLine($"{checks} core checks passed");
