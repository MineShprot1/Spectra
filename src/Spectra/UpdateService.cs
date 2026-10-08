using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Spectra;
public static class UpdateService
{
 public static Version? ParseVersion(string tag)
 {
  var match=Regex.Match(tag,@"^(?:spectra[-_ ]*)?v?(\d+\.\d+(?:\.\d+){0,2})$",RegexOptions.IgnoreCase);
  if(!match.Success||!Version.TryParse(match.Groups[1].Value,out var v))return null;
  return new Version(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision));
 }
 public static async Task<object> Check()
 {
  var current=Assembly.GetExecutingAssembly().GetName().Version??new Version(0,4,2,0);
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
  try
  {
   using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.github.com/repos/MineShprot1/Spectra/releases/latest");request.Headers.Accept.ParseAdd("application/vnd.github+json");
   using var response=await Net.Http.SendAsync(request,timeout.Token);
   if(response.StatusCode==HttpStatusCode.NotFound)return new{update="Нет доступного публичного релиза Spectra",available=false};
   if(!response.IsSuccessStatusCode)return new{update="Проверка обновлений недоступна · GitHub "+(int)response.StatusCode,available=false};
   var release=JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
   var tag=release.Str("tag_name");var latest=ParseVersion(tag);var url=release.Str("html_url");
   var validUrl=Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host=="github.com"&&uri.AbsolutePath.StartsWith("/MineShprot1/Spectra/releases/",StringComparison.OrdinalIgnoreCase);
   var available=latest!=null&&latest>current&&release?["draft"]?.GetValue<bool>()!=true&&release?["prerelease"]?.GetValue<bool>()!=true&&validUrl;
   return new{update=available?"Доступна новая версия Spectra "+tag:latest==null?"Релиз найден, но номер версии не распознан":"Установлена актуальная версия Spectra",available,version=tag,url=validUrl?url:""};
  }
  catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
  {return new{update="Не удалось проверить обновления · можно продолжить запуск",available=false};}
 }
}
