using System.IO;
using System.IO.Compression;
using System.Text.Json;
namespace Spectra;

// Match imported packs by manifest identity, never by display name or by having opened a file.
public static class BedrockPackIndex
{
 public sealed record FileSignature(long Length,string Hash);
 public sealed record Identity(Guid Uuid,int[] Version,string Directory="",IReadOnlyDictionary<string,FileSignature>? Files=null);
 const int ManifestLimit=1024*1024;
 static readonly JsonDocumentOptions JsonOptions=new(){AllowTrailingCommas=true,CommentHandling=JsonCommentHandling.Skip};
 static Identity? ReadManifest(Stream stream)
 {
  using var buffer=new MemoryStream();var bytes=new byte[8192];int count;
  while((count=stream.Read(bytes,0,bytes.Length))>0){if(buffer.Length+count>ManifestLimit)throw new InvalidDataException("Pack manifest exceeds limit");buffer.Write(bytes,0,count);}
  buffer.Position=0;using var doc=JsonDocument.Parse(buffer,JsonOptions);
  if(!doc.RootElement.TryGetProperty("header",out var header)||!header.TryGetProperty("uuid",out var uuid)||uuid.ValueKind!=JsonValueKind.String||!Guid.TryParse(uuid.GetString(),out var id)||!header.TryGetProperty("version",out var version)||version.ValueKind!=JsonValueKind.Array)return null;
  var parts=new List<int>();foreach(var part in version.EnumerateArray()){if(!part.TryGetInt32(out var n)||n<0)return null;parts.Add(n);}return parts.Count==3?new(id,parts.ToArray()):null;
 }
 static void ReadArchive(ZipArchive zip,List<Identity> identities,int depth)
 {
  var manifests=zip.Entries.Where(e=>e.FullName.Replace('\\','/').Split('/').Last().Equals("manifest.json",StringComparison.OrdinalIgnoreCase)).ToArray();
  var nested=zip.Entries.Where(e=>e.FullName.EndsWith(".mcpack",StringComparison.OrdinalIgnoreCase)).ToArray();
  if(manifests.Length>256||nested.Length>64||depth>1)throw new InvalidDataException("Too many pack manifests");
  foreach(var entry in manifests){if(entry.Length>ManifestLimit)throw new InvalidDataException("Pack manifest exceeds limit");using var stream=entry.Open();var identity=ReadManifest(stream)??throw new InvalidDataException("Invalid pack manifest");
   var slash=entry.FullName.LastIndexOf('/');var prefix=slash<0?"":entry.FullName[..(slash+1)];var files=new Dictionary<string,FileSignature>(StringComparer.OrdinalIgnoreCase);long size=0;
   foreach(var asset in zip.Entries.Where(e=>e.FullName.StartsWith(prefix,StringComparison.Ordinal)&&!e.FullName.EndsWith('/')&&e.FullName!=entry.FullName)){
    var relative=asset.FullName[prefix.Length..];if(relative.Split('/').Any(part=>part is ".." or ".")||relative.Contains('\\')||relative.Contains(':')||relative.StartsWith('/'))throw new InvalidDataException("Invalid pack path");
    if((size+=asset.Length)>4L*1024*1024*1024||files.Count>=100000)throw new InvalidDataException("Pack exceeds limit");
    using var input=asset.Open();files[relative]=new(asset.Length,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)));
   }
   identities.Add(identity with{Files=files});}
  long total=0;
  foreach(var entry in nested){if(depth!=0||entry.Length>64L*1024*1024||(total+=entry.Length)>256L*1024*1024)throw new InvalidDataException("Nested pack exceeds limit");using var stream=entry.Open();using var memory=new MemoryStream();var bytes=new byte[81920];int count;while((count=stream.Read(bytes,0,bytes.Length))>0){if(memory.Length+count>64L*1024*1024)throw new InvalidDataException("Nested pack exceeds limit");memory.Write(bytes,0,count);}memory.Position=0;using var archive=new ZipArchive(memory,ZipArchiveMode.Read);ReadArchive(archive,identities,depth+1);}
 }
 public static Identity[] Installed(IEnumerable<string> roots)
 {
  var result=new List<Identity>();
  foreach(var root in roots)foreach(var folder in new[]{"behavior_packs","resource_packs"}){
   var category=Path.Combine(root,folder);if(!Directory.Exists(category)||(File.GetAttributes(category)&FileAttributes.ReparsePoint)!=0)continue;
   foreach(var directory in Directory.EnumerateDirectories(category).Take(2000)){
    try{if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)continue;var file=Path.Combine(directory,"manifest.json");if(!File.Exists(file)||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)continue;using var stream=File.OpenRead(file);if(ReadManifest(stream) is {} identity)result.Add(identity with{Directory=directory});}
    catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException){}
   }
  }
  return result.ToArray();
 }
 static int Compare(int[] a,int[] b){for(int i=0;i<3;i++){int cmp=a[i].CompareTo(b[i]);if(cmp!=0)return cmp;}return 0;}
 public static Identity[] Required(string file)
 {
  try{using var zip=ZipFile.OpenRead(file);var required=new List<Identity>();ReadArchive(zip,required,0);return required.ToArray();}
  catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException){return [];}
 }
 public static bool Satisfied(IReadOnlyCollection<Identity> required,IEnumerable<Identity> installed)
 {
  var present=installed.ToArray();return required.Count>0&&required.All(pack=>present.Any(p=>p.Uuid==pack.Uuid&&Compare(p.Version,pack.Version)>=0));
 }
 public static bool FilesComplete(IReadOnlyCollection<Identity> required,IEnumerable<Identity> installed)
 {
  var present=installed.ToArray();return required.Count>0&&required.All(pack=>present.Any(p=>p.Uuid==pack.Uuid&&Compare(p.Version,pack.Version)>=0&&(Compare(p.Version,pack.Version)>0||VerifyFiles(pack,p.Directory))));
 }
 static bool VerifyFiles(Identity pack,string directory)
 {
  if(pack.Files==null||pack.Files.Count==0)return true;if(directory=="")return false;
  try{foreach(var (relative,expected) in pack.Files){var path=Path.Combine(directory,relative.Replace('/',Path.DirectorySeparatorChar));if(!File.Exists(path)||new FileInfo(path).Length!=expected.Length)return false;using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);if(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input))!=expected.Hash)return false;}return true;}
  catch(Exception e) when(e is IOException or UnauthorizedAccessException){return false;}
 }
 public static string? Snapshot(IReadOnlyCollection<Identity> required,IEnumerable<Identity> installed)
 {
  var present=installed.ToArray();var rows=new List<string>();
  try{foreach(var pack in required){var match=present.FirstOrDefault(p=>p.Uuid==pack.Uuid&&Compare(p.Version,pack.Version)>=0);if(match==null||match.Directory=="")return null;
   foreach(var path in System.IO.Directory.EnumerateFiles(match.Directory,"*",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase)){var info=new FileInfo(path);rows.Add(path+"|"+info.Length+"|"+info.LastWriteTimeUtc.Ticks);}}
   return string.Join("\n",rows);
  }catch(Exception e) when(e is IOException or UnauthorizedAccessException){return null;}
 }
 public static bool IsInstalled(string file,IEnumerable<Identity> installed)=>Satisfied(Required(file),installed);
}
