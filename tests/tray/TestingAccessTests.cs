using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Spectra;
static class TestingAccessTests
{
 sealed class Handler:HttpMessageHandler
 {
  public string Secret="",Installation="";public int Redemptions;public bool Revoked;
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
   if(request.Method==HttpMethod.Post){using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));var v=json.RootElement;var secret=v.GetProperty("secret").GetString()!;var id=v.GetProperty("installationId").GetString()!;if(Secret!=""&&Secret!=secret)return new(HttpStatusCode.Conflict);if(Secret=="")Redemptions++;Secret=secret;Installation=id;return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{active=true,installationId=id}))};}
   if(Revoked||request.Headers.Authorization?.Parameter!=Secret)return new(HttpStatusCode.Unauthorized);return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{active=true,installationId=Installation}))};
  }
 }
 static string Encode(byte[] value)=>Convert.ToBase64String(value).TrimEnd('=').Replace('+','-').Replace('/','_');
 public static async Task Run()
 {
  using var signer=ECDsa.Create(ECCurve.NamedCurves.nistP256);var publicKey=signer.ExportSubjectPublicKeyInfoPem();var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
  string Key(long expires,string purpose="spectra-testing-enroll"){var payload=new TestingGrant(2,purpose,new string('a',32),"Developer",now,expires,"https://testing.example");var signed="SPTEST2."+Encode(JsonSerializer.SerializeToUtf8Bytes(payload,Store.Json));return signed+"."+Encode(signer.SignData(Encoding.ASCII.GetBytes(signed),HashAlgorithmName.SHA256,DSASignatureFormat.Rfc3279DerSequence));}
  void Rejected(string token){try{TestingAccess.Verify(token,publicKey);throw new Exception("Invalid access key accepted");}catch(ArgumentException){}}
  var valid=Key(now+600);if(TestingAccess.Verify(valid,publicKey).Label!="Developer")throw new Exception("Valid signature rejected");
  Rejected(valid.Replace("SPTEST2.","SPTEST1."));Rejected(valid[..^5]+"AAAAA");Rejected(Key(now-1));Rejected(Key(now+3600,"other-purpose"));Rejected(new string('x',9000));
  using var stranger=ECDsa.Create(ECCurve.NamedCurves.nistP256);try{TestingAccess.Verify(valid,stranger.ExportSubjectPublicKeyInfoPem());throw new Exception("Wrong signer accepted");}catch(ArgumentException){}
  var root=Path.Combine(Path.GetTempPath(),"Spectra-testing-access-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);using var handler=new Handler();using var client=new HttpClient(handler);
  try{
   var access=new TestingAccess(root,publicKey,client);await access.ActivateAsync(valid);if(!access.Enabled||!await access.ValidateSessionAsync())throw new Exception("Enrollment failed");
   var restored=new TestingAccess(root,publicKey,client);if(!restored.Enabled||!await restored.ValidateSessionAsync()||handler.Redemptions!=1)throw new Exception("Restart required another key");
   if(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,"testing-session.dat"))).Contains(handler.Secret))throw new Exception("Plaintext session saved");if(JsonSerializer.Serialize(access.Snapshot()).Contains(handler.Secret))throw new Exception("Session leaked to UI");
   var second=new TestingAccess(Path.Combine(root,"second-pc"),publicKey,client);try{await second.ActivateAsync(valid);throw new Exception("Used key accepted on a second PC");}catch(IOException){}if(second.Enabled)throw new Exception("Failed enrollment changed the channel");
   access.Disable();if(access.Enabled||access.UpdateChannel.Name!="stable")throw new Exception("Return to stable failed");await access.ActivateAsync("");if(!access.Enabled||handler.Redemptions!=1)throw new Exception("Saved enrollment cannot re-enter testing without another key");
   handler.Revoked=true;if(await access.ValidateSessionAsync())throw new Exception("Revoked access accepted");
  }finally{Directory.Delete(root,true);}
  var fixture=Path.Combine(Environment.CurrentDirectory,"test-results","testing-key-fixture.json");if(File.Exists(fixture)){using var json=JsonDocument.Parse(File.ReadAllText(fixture));var v=json.RootElement;var token=v.GetProperty("key").GetString()!;var encoded=token.Split('.')[1].Replace('-','+').Replace('_','/');using var payload=JsonDocument.Parse(Convert.FromBase64String(encoded+new string('=',(4-encoded.Length%4)%4)));var clock=DateTimeOffset.FromUnixTimeSeconds(payload.RootElement.GetProperty("issued").GetInt64()+1);if(TestingAccess.Verify(token,v.GetProperty("public").GetString()!,clock).Label!="Python integration")throw new Exception("Python/.NET mismatch");}
  Console.WriteLine("PASS testing enrollment: signature/expiry, one redemption, second-PC refusal, DPAPI session, restart/re-entry without keys, revocation and Python compatibility");
 }
}
