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
  return ((List<object>)list).OfType<Dictionary<string,object>>().Select((d,index)=>new{index,name=d.GetValueOrDefault("name")?.ToString()??"Сервер",address=d.GetValueOrDefault("ip")?.ToString()??"",icon=d.GetValueOrDefault("icon")?.ToString()??""}).ToArray();
 }
 public static (string Name,DateTime? LastPlayed) World(string path)
 {
  try{var data=Load(path)?.GetValueOrDefault("Data") as Dictionary<string,object>;var name=data?.GetValueOrDefault("LevelName")?.ToString()??Path.GetFileName(Path.GetDirectoryName(path));var time=data?.GetValueOrDefault("LastPlayed") is long milliseconds?DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime:(DateTime?)null;return(name!,time);}catch(Exception){return(Path.GetFileName(Path.GetDirectoryName(path))!,null);}
 }
 public sealed class NbtList(byte elementType):List<object>{public byte ElementType {get;}=elementType;}
 public static void Save(string path,Dictionary<string,object> root,bool compressed=false){
  var temp=path+".tmp";try{using(var file=File.Create(temp)){using Stream stream=compressed?new GZipStream(file,CompressionMode.Compress):file;using var writer=new BinaryWriter(stream,Encoding.UTF8);writer.Write((byte)10);String(writer,"");Write(writer,root);}
   if(File.Exists(path))File.Copy(path,path+".spectra-backup",true);File.Move(temp,path,true);
  }finally{if(File.Exists(temp))File.Delete(temp);}
 }
 static byte Type(object value) => value switch
 {
  sbyte _ => 1,
  short _ => 2,
  int _ => 3,
  long _ => 4,
  float _ => 5,
  double _ => 6,
  byte[] _ => 7,
  string _ => 8,
  List<object> _ => 9,
  Dictionary<string, object> _ => 10,
  int[] _ => 11,
  long[] _ => 12,
  _ => throw new InvalidDataException("Unsupported NBT type")
 };

 static void Number(BinaryWriter w,long number,int count){for(int n=count-1;n>=0;n--)w.Write((byte)(number>>(n*8)));}
 static void String(BinaryWriter w,string text){var bytes=Encoding.UTF8.GetBytes(text);if(bytes.Length>65535)throw new InvalidDataException("NBT string too long");Number(w,bytes.Length,2);w.Write(bytes);}
 static void Write(BinaryWriter w,object value){switch(value){
  case sbyte byteValue:w.Write(byteValue);break;case short shortValue:Number(w,shortValue,2);break;case int intValue:Number(w,intValue,4);break;case long longValue:Number(w,longValue,8);break;case float floatValue:Number(w,BitConverter.SingleToInt32Bits(floatValue),4);break;case double doubleValue:Number(w,BitConverter.DoubleToInt64Bits(doubleValue),8);break;
  case string text:String(w,text);break;case byte[] data:Number(w,data.Length,4);w.Write(data);break;
  case List<object> list:var type=list is NbtList typed?typed.ElementType:list.Count>0?Type(list[0]):(byte)10;w.Write(type);Number(w,list.Count,4);foreach(var item in list){if(Type(item)!=type)throw new InvalidDataException("Mixed NBT list");Write(w,item);}break;
  case Dictionary<string,object> compound:foreach(var entry in compound){w.Write(Type(entry.Value));String(w,entry.Key);Write(w,entry.Value);}w.Write((byte)0);break;
  case int[] intArray:Number(w,intArray.Length,4);foreach(var integer in intArray)Number(w,integer,4);break;
  case long[] longArray:Number(w,longArray.Length,4);foreach(var integer in longArray)Number(w,integer,8);break;
  default:throw new InvalidDataException("Unsupported NBT value");
 }}
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
   case 9:var t=r.ReadByte();var count=Count(r);var list=new NbtList(t);for(var i=0;i<count;i++)list.Add(Read(r,t,depth+1));return list;
   case 10:var dict=new Dictionary<string,object>();while(true){var next=r.ReadByte();if(next==0)break;var name=ReadString(r);dict[name]=Read(r,next,depth+1);if(dict.Count>100000)throw new InvalidDataException("NBT слишком большой");}return dict;
   case 11:var ints=new int[Count(r)];for(var i=0;i<ints.Length;i++)ints[i]=I32(r);return ints;
   case 12:var longs=new long[Count(r)];for(var i=0;i<longs.Length;i++)longs[i]=I64(r);return longs;
   default:throw new InvalidDataException("Неизвестный NBT тег");
  }
 }
}
