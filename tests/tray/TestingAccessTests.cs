using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Spectra;

static class TestingAccessTests
{
 static string Encode(byte[] value)=>Convert.ToBase64String(value).TrimEnd('=').Replace('+','-').Replace('/','_');
 public static void Run()
 {
  using var signer=ECDsa.Create(ECCurve.NamedCurves.nistP256);var publicKey=signer.ExportSubjectPublicKeyInfoPem();var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
  string Key(long expires,string purpose="spectra-testing"){
   var payload=new TestingGrant(1,purpose,new string('a',32),"Developer",now,expires,"github_pat_fixture_only");var signed="SPTEST1."+Encode(JsonSerializer.SerializeToUtf8Bytes(payload,Store.Json));return signed+"."+Encode(signer.SignData(Encoding.ASCII.GetBytes(signed),HashAlgorithmName.SHA256,DSASignatureFormat.Rfc3279DerSequence));
  }
  void Rejected(string token){try{TestingAccess.Verify(token,publicKey);throw new Exception("Invalid access key accepted");}catch(ArgumentException){}}
  var valid=Key(now+3600);if(TestingAccess.Verify(valid,publicKey).Label!="Developer")throw new Exception("Valid signature rejected");
  Rejected(valid.Replace("SPTEST1.","SPTEST2."));Rejected(valid[..^5]+"AAAAA");Rejected(Key(now-1));Rejected(Key(now+3600,"other-purpose"));Rejected(new string('x',9000));
  using var stranger=ECDsa.Create(ECCurve.NamedCurves.nistP256);try{TestingAccess.Verify(valid,stranger.ExportSubjectPublicKeyInfoPem());throw new Exception("Wrong signer accepted");}catch(ArgumentException){}
  var root=Path.Combine(Path.GetTempPath(),"Spectra-testing-access-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   var access=new TestingAccess(root,publicKey);if(access.Enabled)throw new Exception("Access enabled by default");access.Activate(valid);
   if(!access.Enabled||!new TestingAccess(root,publicKey).Enabled)throw new Exception("Access did not persist");
   if(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,"testing-access.dat"))).Contains("github_pat_"))throw new Exception("Credential saved in plaintext");
   if(JsonSerializer.Serialize(access.Snapshot()).Contains("github_pat_")||JsonSerializer.Serialize(access.Snapshot()).Contains("SPTEST1"))throw new Exception("Credential leaked to UI state");
   try{access.Activate("bad");throw new Exception("Bad replacement accepted");}catch(ArgumentException){}if(!access.Enabled)throw new Exception("Bad key destroyed existing access");
   if(access.UpdateChannel.Repository!="MineShprot1/Spectra-Testing")throw new Exception("Wrong testing repository");access.Disable();if(new TestingAccess(root,publicKey).Enabled||access.UpdateChannel.Repository!="MineShprot1/Spectra")throw new Exception("Disable did not restore stable channel");
   File.WriteAllText(Path.Combine(root,"testing-access.dat"),"corrupt");if(new TestingAccess(root,publicKey).Enabled)throw new Exception("Corrupt store unlocked access");
  }finally{Directory.Delete(root,true);}
  // Optional Python-produced fixture, created by tests/testing-keys.py. This is
  // an ephemeral test signer, unrelated to the actual developer identity.
  var fixture=Path.Combine(Environment.CurrentDirectory,"test-results","testing-key-fixture.json");
  if(File.Exists(fixture)){using var json=JsonDocument.Parse(File.ReadAllText(fixture));var value=json.RootElement;if(TestingAccess.Verify(value.GetProperty("key").GetString()!,value.GetProperty("public").GetString()!).Label!="Python integration")throw new Exception("Python/.NET key format mismatch");}
  Console.WriteLine("PASS testing access: signature, wrong signer, expiry, purpose, tamper, protected persistence, safe state, disable and Python interoperability");
 }
}
