using System.IO;
using System.Security.Cryptography;
using System.Text;
using CmlLib.Core.Auth.Microsoft.Sessions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XboxAuthNet.Game.Accounts;
using XboxAuthNet.Game.SessionStorages;
namespace Spectra;

// CmlLib's account/session interfaces, with DPAPI persistence and no plain-text token file.
public sealed class ProtectedAccountManager : IXboxGameAccountManager
{
 readonly string file;
 readonly List<IXboxGameAccount> accounts = [];
 public ProtectedAccountManager(string path)
 {
  file=path;
  if(!File.Exists(file))return;
  try
  {
   var bytes=ProtectedData.Unprotect(File.ReadAllBytes(file),null,DataProtectionScope.CurrentUser);
   try
   {
    foreach(var item in JArray.Parse(Encoding.UTF8.GetString(bytes)).OfType<JObject>())
     accounts.Add(JEGameAccount.FromSessionStorage(new ProtectedSessionStorage(item)));
   }
   finally{CryptographicOperations.ZeroMemory(bytes);}
  }
  catch(Exception ex) when(ex is CryptographicException or JsonException or IOException)
  {
   // Preserve the old encrypted file. The user can sign in again to replace it.
   accounts.Clear();
  }
 }
 public bool HasAccounts=>accounts.OfType<JEGameAccount>().Any(a=>a.Profile!=null);
 public XboxGameAccountCollection GetAccounts()=>XboxGameAccountCollection.FromAccounts(accounts);
 public IXboxGameAccount GetDefaultAccount()=>accounts.OfType<JEGameAccount>().LastOrDefault(a=>a.Profile!=null)??NewAccount();
 public IXboxGameAccount NewAccount()
 {
  var account=JEGameAccount.FromSessionStorage(new ProtectedSessionStorage());accounts.Add(account);return account;
 }
 public void ClearAccounts()=>accounts.Clear();
 public void SaveAccounts()
 {
  // Failed/cancelled interactive attempts must not become the default saved account.
  var successful=accounts.Where(a=>a is JEGameAccount je&&je.Profile!=null&&!string.IsNullOrEmpty(a.Identifier));
  var array=new JArray(successful.Select(a=>((ProtectedSessionStorage)a.SessionStorage).Export()));
  var bytes=Encoding.UTF8.GetBytes(array.ToString(Formatting.None));
  try
  {
   var encrypted=ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser);
   Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllBytes(file+".tmp",encrypted);File.Move(file+".tmp",file,true);
  }
  finally{CryptographicOperations.ZeroMemory(bytes);}
 }
}
public sealed class ProtectedSessionStorage : ISessionStorage
{
 readonly Dictionary<string,object?> memory=[];
 readonly Dictionary<string,SessionStorageKeyMode> modes=[];
 readonly JObject loaded;
 public ProtectedSessionStorage(JObject? state=null){loaded=state??new JObject();}
 public IEnumerable<string> Keys=>loaded.Properties().Select(p=>p.Name).Concat(memory.Keys).Distinct().ToArray();
 public bool ContainsKey(string key)=>memory.ContainsKey(key)||loaded.ContainsKey(key);
 public bool ContainsKey<T>(string key)=>TryGetValue<T>(key,out _);
 public T Get<T>(string key)=>TryGetValue<T>(key,out var value)?value:throw new KeyNotFoundException(key);
 public T GetOrDefault<T>(string key,T defaultValue)=>TryGetValue<T>(key,out var value)?value:defaultValue;
 public bool TryGetValue<T>(string key,out T value)
 {
  if(memory.TryGetValue(key,out var obj))
  {
   if(obj is T cast){value=cast;return true;}
   if(obj==null){value=default!;return true;}
   value=default!;return false;
  }
  if(loaded.TryGetValue(key,out var token))
  {
   try{value=token.ToObject<T>()!;memory[key]=value;return true;}
   catch(JsonException){value=default!;return false;}
  }
  value=default!;return false;
 }
 public void Set<T>(string key,T obj){memory[key]=obj;loaded.Remove(key);}
 public bool Remove(string key){var a=memory.Remove(key);var b=loaded.Remove(key);modes.Remove(key);return a||b;}
 public SessionStorageKeyMode GetKeyMode(string key)=>modes.GetValueOrDefault(key,SessionStorageKeyMode.Default);
 public void SetKeyMode(string key,SessionStorageKeyMode mode)=>modes[key]=mode;
 public JObject Export()
 {
  var result=new JObject();foreach(var key in Keys)
  {
   if(GetKeyMode(key)==SessionStorageKeyMode.NoStore)continue;
   if(memory.TryGetValue(key,out var value))result[key]=value==null?JValue.CreateNull():JToken.FromObject(value);
   else result[key]=loaded[key]?.DeepClone();
  }
  return result;
 }
}
