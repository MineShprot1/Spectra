using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Json;

namespace Spectra;

public static class BuildIdentity
{
 public static string Channel=>typeof(BuildIdentity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a=>a.Key=="SpectraChannel")?.Value??"stable";
 public static string Version=>typeof(BuildIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]??"0.28.1";
}

public sealed record TestingGrant(int V,string Purpose,string Id,string Label,long Issued,long Expires,string Service);
public sealed record TestingSession(string Service,string InstallationId,string Secret,string Label,bool Selected);
public sealed record TestingPending(string Key,string Service,string InstallationId,string Secret);
public sealed class TestingAccess
{
 readonly string path,pendingPath,legacyPath,publicKey;readonly HttpClient http;TestingSession? session;bool verified;string error="";
 public TestingAccess(string root,string? verificationKey=null,HttpClient? client=null)
 {
  path=Path.Combine(root,"testing-session.dat");pendingPath=Path.Combine(root,"testing-pending.dat");legacyPath=Path.Combine(root,"testing-access.dat");http=client??Net.Http;
  if(verificationKey!=null)publicKey=verificationKey;
  else {using var resource=typeof(TestingAccess).Assembly.GetManifestResourceStream("Spectra.TestingPublicKey.pem")??throw new InvalidOperationException("Testing verification key missing");using var reader=new StreamReader(resource);publicKey=reader.ReadToEnd();}
  session=Read<TestingSession>(path);if(session!=null&&(!SafeService(session.Service)||session.InstallationId?.Length!=32||session.Secret?.Length!=64))session=null;
 }
 public bool Enabled=>session?.Selected==true;
 public TestingGrant Validate(string token)=>Verify(token,publicKey);
 public SourceUpdateChannel UpdateChannel=>Enabled?SourceUpdateChannel.Testing(session!.Service,session.Secret):SourceUpdateChannel.Stable;
 public object Snapshot()=>new{enabled=Enabled,registered=session!=null,verified,error,label=session?.Label??"",buildChannel=BuildIdentity.Channel,repository="MineShprot1/Spectra-Testing"};
 static bool SafeService(string? value)=>Uri.TryCreate(value,UriKind.Absolute,out var url)&&url.Scheme=="https"&&url.UserInfo==""&&url.AbsolutePath=="/"&&url.Query==""&&url.Fragment=="";
 static T? Read<T>(string file){try{if(File.Exists(file)&&new FileInfo(file).Length<=32768)return JsonSerializer.Deserialize<T>(ProtectedData.Unprotect(File.ReadAllBytes(file),null,DataProtectionScope.CurrentUser),Store.Json);}catch(Exception e) when(e is IOException or UnauthorizedAccessException or CryptographicException or JsonException or ArgumentException){}return default;}
 static void Write<T>(string file,T value){Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllBytes(file+".tmp",ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(value,Store.Json),null,DataProtectionScope.CurrentUser));File.Move(file+".tmp",file,true);}
 static void DeleteOld(string file){try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}}
 public static TestingGrant Verify(string token,string publicKey,DateTimeOffset? now=null)
 {
  try{
   token=token.Trim();if(token.Length>8192)throw new FormatException();var parts=token.Split('.');if(parts.Length!=3||parts[0]!="SPTEST2")throw new FormatException();
   static byte[] Decode(string raw){if(raw.Length==0||raw.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_'))throw new FormatException();return Convert.FromBase64String(raw.Replace('-','+').Replace('_','/')+new string('=',(4-raw.Length%4)%4));}
   using var verifier=ECDsa.Create();verifier.ImportFromPem(publicKey);
   if(!verifier.VerifyData(Encoding.ASCII.GetBytes(parts[0]+"."+parts[1]),Decode(parts[2]),HashAlgorithmName.SHA256,DSASignatureFormat.Rfc3279DerSequence))throw new FormatException();
   var value=JsonSerializer.Deserialize<TestingGrant>(Decode(parts[1]),Store.Json)??throw new FormatException();var unix=(now??DateTimeOffset.UtcNow).ToUnixTimeSeconds();
   if(value.V!=2||value.Purpose!="spectra-testing-enroll"||value.Id==null||value.Id.Length!=32||!value.Id.All(Uri.IsHexDigit)||string.IsNullOrWhiteSpace(value.Label)||value.Label.Length>80||value.Issued<0||value.Issued>unix+300||value.Expires<=unix||value.Expires<=value.Issued||value.Expires-value.Issued>86400||!SafeService(value.Service))throw new FormatException();
   return value;
  }catch(Exception error) when(error is FormatException or ArgumentException or CryptographicException or JsonException){throw new ArgumentException("Ключ недействителен или срок его действия истёк",error);}
 }
 public async Task<bool> ValidateSessionAsync()
 {
  verified=false;error="Нет сохранённого доступа к тестовой версии";if(session==null)return false;
  try{using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));using var request=new HttpRequestMessage(HttpMethod.Get,session.Service.TrimEnd('/')+"/v1/testing/session");request.Headers.Authorization=new("Bearer",session.Secret);using var response=await http.SendAsync(request,timeout.Token);if(!response.IsSuccessStatusCode){error="Доступ к тестовой версии недоступен или отозван";return false;}var value=await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:timeout.Token);verified=value.TryGetProperty("active",out var active)&&active.ValueKind==JsonValueKind.True&&value.GetProperty("installationId").GetString()==session.InstallationId;error=verified?"":"Доступ к тестовой версии недоступен или отозван";return verified;}
  catch(Exception e) when(e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException){error="Не удалось проверить доступ к тестовой версии";return false;}
 }
 public async Task ActivateAsync(string token)
 {
  if(string.IsNullOrWhiteSpace(token)&&session!=null){if(!await ValidateSessionAsync())throw new IOException(error);session=session with{Selected=true};Write(path,session);return;}
  token=token.Trim();var coupon=Validate(token);var pending=Read<TestingPending>(pendingPath);
  if(pending==null||pending.Key!=token){pending=new(token,coupon.Service,Guid.NewGuid().ToString("N"),Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());Write(pendingPath,pending);}
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));using var request=new HttpRequestMessage(HttpMethod.Post,coupon.Service.TrimEnd('/')+"/v1/testing/redeem"){Content=JsonContent.Create(new{key=token,installationId=pending.InstallationId,secret=pending.Secret})};
  using var response=await http.SendAsync(request,timeout.Token);if(response.StatusCode==System.Net.HttpStatusCode.Conflict)throw new IOException("Ключ уже использован или срок его действия истёк");if(!response.IsSuccessStatusCode)throw new IOException("Не удалось активировать ключ на сервере тестирования");
  var value=await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:timeout.Token);if(value.GetProperty("active").ValueKind!=JsonValueKind.True||value.GetProperty("installationId").GetString()!=pending.InstallationId)throw new IOException("Неверный ответ сервера тестирования");
  var enrolled=new TestingSession(coupon.Service,pending.InstallationId,pending.Secret,coupon.Label,true);Write(path,enrolled);session=enrolled;verified=true;error="";DeleteOld(pendingPath);DeleteOld(legacyPath);
 }
 public void Disable(){if(session!=null){session=session with{Selected=false};Write(path,session);}verified=false;DeleteOld(pendingPath);DeleteOld(legacyPath);}
}
