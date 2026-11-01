using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Spectra;

public static class BuildIdentity
{
 public static string Channel=>typeof(BuildIdentity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a=>a.Key=="SpectraChannel")?.Value??"stable";
 public static string Version=>typeof(BuildIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]??"0.28.0";
}

public sealed record TestingGrant(int V,string Purpose,string Id,string Label,long Issued,long Expires,string GithubToken);
public sealed class TestingAccess
{
 readonly string path,publicKey;TestingGrant? grant;
 public TestingAccess(string root,string? verificationKey=null)
 {
  path=Path.Combine(root,"testing-access.dat");
  if(verificationKey!=null)publicKey=verificationKey;
  else {using var resource=typeof(TestingAccess).Assembly.GetManifestResourceStream("Spectra.TestingPublicKey.pem")??throw new InvalidOperationException("Testing verification key missing");using var reader=new StreamReader(resource);publicKey=reader.ReadToEnd();}
  try{if(File.Exists(path)&&new FileInfo(path).Length<=16384)grant=Verify(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser)),publicKey);}
  catch(Exception e) when(e is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException){}
 }
 public TestingGrant? Grant=>grant is { } value&&value.Expires>DateTimeOffset.UtcNow.ToUnixTimeSeconds()?value:null;
 public bool Enabled=>Grant!=null;
 public TestingGrant Validate(string token)=>Verify(token,publicKey);
 public SourceUpdateChannel UpdateChannel=>Grant is {} value?SourceUpdateChannel.Testing(value.GithubToken):SourceUpdateChannel.Stable;
 public object Snapshot()=>new{enabled=Enabled,label=Grant?.Label??"",expires=Grant?.Expires,buildChannel=BuildIdentity.Channel,connected=Enabled,repository="MineShprot1/Spectra-Testing"};
 public static TestingGrant Verify(string token,string publicKey,DateTimeOffset? now=null)
 {
  try{
   token=token.Trim();if(token.Length>8192)throw new FormatException();var parts=token.Split('.');if(parts.Length!=3||parts[0]!="SPTEST1")throw new FormatException();
   static byte[] Decode(string raw){if(raw.Length==0||raw.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_'))throw new FormatException();return Convert.FromBase64String(raw.Replace('-','+').Replace('_','/')+new string('=',(4-raw.Length%4)%4));}
   using var verifier=ECDsa.Create();verifier.ImportFromPem(publicKey);
   if(!verifier.VerifyData(Encoding.ASCII.GetBytes(parts[0]+"."+parts[1]),Decode(parts[2]),HashAlgorithmName.SHA256,DSASignatureFormat.Rfc3279DerSequence))throw new FormatException();
   var value=JsonSerializer.Deserialize<TestingGrant>(Decode(parts[1]),Store.Json)??throw new FormatException();var unix=(now??DateTimeOffset.UtcNow).ToUnixTimeSeconds();
   if(value.V!=1||value.Purpose!="spectra-testing"||value.Id==null||value.Id.Length!=32||!value.Id.All(Uri.IsHexDigit)||string.IsNullOrWhiteSpace(value.Label)||value.Label.Length>80||value.Issued<0||value.Issued>unix+300||value.Expires<=unix||value.Expires<=value.Issued||value.Expires-value.Issued>3660L*86400||value.GithubToken==null||!value.GithubToken.StartsWith("github_pat_",StringComparison.Ordinal)||value.GithubToken.Length>512||value.GithubToken.Any(char.IsWhiteSpace))throw new FormatException();
   return value;
  }catch(Exception error) when(error is FormatException or ArgumentException or CryptographicException or JsonException){throw new ArgumentException("Ключ недействителен или срок его действия истёк",error);}
 }
 public void Activate(string token)
 {
  var verified=Verify(token,publicKey);var protectedToken=ProtectedData.Protect(Encoding.UTF8.GetBytes(token.Trim()),null,DataProtectionScope.CurrentUser);
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path+".tmp",protectedToken);File.Move(path+".tmp",path,true);grant=verified;
 }
 public void Disable(){File.Delete(path);grant=null;}
}
