using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
namespace Spectra;
public static class Net
{
 public static readonly HttpClient Http = new() { Timeout=TimeSpan.FromMinutes(10) };
 static Net() { Http.DefaultRequestHeaders.UserAgent.ParseAdd("Spectra/0.2.1 (+https://github.com/MineShprot1/Spectra)"); }
 public static async Task<JsonNode> Get(string url) => JsonNode.Parse(await Http.GetStringAsync(url)) ?? throw new IOException("Пустой ответ сервера");
 public static async Task<JsonNode> Send(HttpRequestMessage req) { using var r=await Http.SendAsync(req); var body=await r.Content.ReadAsStringAsync(); if(!r.IsSuccessStatusCode) throw new IOException($"Сервис вернул {(int)r.StatusCode}. Проверьте права аккаунта и настройки API."); return JsonNode.Parse(string.IsNullOrEmpty(body)?"{}":body)!; }
 public static async Task Download(string url,string dest,string? hash=null,string algorithm="SHA1")
 {
  if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https") throw new IOException("Разрешены только HTTPS загрузки");
  Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
  if(File.Exists(dest)&&hash!=null&&await Hash(dest,algorithm)==hash.ToLowerInvariant()) return;
  var tmp=dest+"."+Guid.NewGuid().ToString("N")+".part";
  try { using var response=await Http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead); response.EnsureSuccessStatusCode(); await using(var f=File.Create(tmp)) await response.Content.CopyToAsync(f); if(hash!=null&&await Hash(tmp,algorithm)!=hash.ToLowerInvariant()) throw new IOException("Контрольная сумма загрузки не совпадает"); File.Move(tmp,dest,true); }
  finally { if(File.Exists(tmp)) File.Delete(tmp); }
 }
 public static async Task<string> Hash(string path,string algorithm) { await using var f=File.OpenRead(path); using var h=algorithm=="SHA512"? (HashAlgorithm)SHA512.Create():algorithm=="SHA256"?SHA256.Create():SHA1.Create(); return Convert.ToHexString(await h.ComputeHashAsync(f)).ToLowerInvariant(); }
 public static string Str(this JsonNode? n,string key)=>n?[key]?.ToString()??"";
}
