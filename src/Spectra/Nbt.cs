using System.IO;
using System.IO.Compression;
using System.Text;
namespace Spectra;
// Minimal bounded Java NBT reader, for servers.dat and world metadata.
public static class Nbt
{
 public static Dictionary<string,object>? Load(string path)
 {
  if(!File.Exists(path))return null;using var f=File.OpenRead(path);Stream stream=f;var a=f.ReadByte();var b=f.ReadByte();f.Position=0;if(a==31&&b==139)stream=new GZipStream(f,CompressionMode.Decompress);
  using(stream)using(var r=new BinaryReader(stream,Encoding.UTF8)){var type=r.ReadByte();ReadString(r);return Read(r,type,0) as Dictionary<string,object>;}
 }
 public static object Servers(string path)
 {
  var root=Load(path);if(root==null||!root.TryGetValue("servers",out var list))return Array.Empty<object>();
  return ((List<object>)list).OfType<Dictionary<string,object>>().Select(d=>new{name=d.GetValueOrDefault("name")?.ToString()??"Сервер",address=d.GetValueOrDefault("ip")?.ToString()??"",icon=d.GetValueOrDefault("icon")?.ToString()??""}).ToArray();
 }
 public static (string Name,DateTime? LastPlayed) World(string path)
 {
  try{var data=Load(path)?.GetValueOrDefault("Data") as Dictionary<string,object>;var name=data?.GetValueOrDefault("LevelName")?.ToString()??Path.GetFileName(Path.GetDirectoryName(path));var time=data?.GetValueOrDefault("LastPlayed") is long milliseconds?DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime:(DateTime?)null;return(name!,time);}catch(Exception){return(Path.GetFileName(Path.GetDirectoryName(path))!,null);}
 }
 static short I16(BinaryReader r)=>System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(r.ReadBytes(2));
 static int I32(BinaryReader r)=>System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(r.ReadBytes(4));
 static long I64(BinaryReader r)=>System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(r.ReadBytes(8));
 static int Count(BinaryReader r){var n=I32(r);if(n<0||n>1_000_000)throw new InvalidDataException("NBT слишком большой");return n;}
 static string ReadString(BinaryReader r){var n=(ushort)I16(r);return Encoding.UTF8.GetString(r.ReadBytes(n));}
 static object Read(BinaryReader r,byte type,int depth)
 {
  if(depth>64)throw new InvalidDataException("NBT слишком вложенный");
  switch(type)
  {
   case 1:return r.ReadSByte();case 2:return I16(r);case 3:return I32(r);case 4:return I64(r);case 5:return BitConverter.Int32BitsToSingle(I32(r));case 6:return BitConverter.Int64BitsToDouble(I64(r));case 7:return r.ReadBytes(Count(r));case 8:return ReadString(r);
   case 9:var t=r.ReadByte();var count=Count(r);var list=new List<object>();for(var i=0;i<count;i++)list.Add(Read(r,t,depth+1));return list;
   case 10:var dict=new Dictionary<string,object>();while(true){var next=r.ReadByte();if(next==0)break;var name=ReadString(r);dict[name]=Read(r,next,depth+1);if(dict.Count>100000)throw new InvalidDataException("NBT слишком большой");}return dict;
   case 11:var ints=new int[Count(r)];for(var i=0;i<ints.Length;i++)ints[i]=I32(r);return ints;
   case 12:var longs=new long[Count(r)];for(var i=0;i<longs.Length;i++)longs[i]=I64(r);return longs;
   default:throw new InvalidDataException("Неизвестный NBT тег");
  }
 }
}
