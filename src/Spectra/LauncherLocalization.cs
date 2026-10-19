using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Spectra;
public sealed class LauncherLocalization(Func<string> language,string? directory=null)
{
 public static readonly string[] Languages=["ru","en","de","fr","es","pt","it","pl","uk","tr","zh","ja","ko","ro","nl","cs","sv","hu","id"];
 readonly string directory=directory??Path.Combine(AppContext.BaseDirectory,"Web","i18n");
 readonly ConcurrentDictionary<string,Dictionary<string,string>> catalogs=new();
 readonly ConcurrentDictionary<string,List<(Regex Pattern,string Value,string[] Ids,string Anchor)>> templates=new();
 Dictionary<string,string> Catalog(string code)=>catalogs.GetOrAdd(code,key=>{try{return JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(this.directory,key+".json")))??[];}catch(Exception ex) when(ex is IOException or JsonException or UnauthorizedAccessException){return [];}});
 public string Filter(string value){var parts=value.Split('|');for(var i=0;i<parts.Length;i+=2)parts[i]=Text(parts[i]);return string.Join('|',parts);}
 public string Text(string value)
 {
  var code=language();if(code=="ru"||string.IsNullOrEmpty(value))return value;if(!Languages.Contains(code))code="en";
  var normalized=Regex.Replace(value.Trim(),@"\s+"," ");var catalog=Catalog(code);if(catalog.TryGetValue(normalized,out var exact)||Catalog("en").TryGetValue(normalized,out exact))return value.Replace(value.Trim(),exact,StringComparison.Ordinal);
  var entries=templates.GetOrAdd(code,key=>Catalog("en").Concat(Catalog(key)).GroupBy(e=>e.Key).Select(g=>g.Last()).Where(e=>Regex.IsMatch(e.Key,@"\{\d+\}")).Select(e=>{var parts=Regex.Split(e.Key,@"\{\d+\}");var ids=Regex.Matches(e.Key,@"\{(\d+)\}").Select(m=>m.Groups[1].Value).ToArray();return(new Regex("^"+string.Join("(.*?)",parts.Select(Regex.Escape))+"$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(30)),e.Value,ids,parts.MaxBy(p=>p.Length)??"");}).OrderByDescending(e=>e.Item4.Length).ToList());
  foreach(var (pattern,target,ids,anchor) in entries){if(anchor.Length>0&&!normalized.Contains(anchor,StringComparison.Ordinal))continue;Match match;try{match=pattern.Match(normalized);}catch(RegexMatchTimeoutException){continue;}if(!match.Success)continue;var args=ids.Select((id,index)=>(id,value:match.Groups[index+1].Value)).GroupBy(p=>p.id).ToDictionary(g=>g.Key,g=>g.Last().value);var result=Regex.Replace(target,@"\{(\d+)\}",m=>args.GetValueOrDefault(m.Groups[1].Value,m.Value));return value.Replace(value.Trim(),result,StringComparison.Ordinal);}
  foreach(var entry in Catalog("en").Concat(catalog).GroupBy(e=>e.Key).Select(g=>g.Last()).Where(e=>e.Key.Length>2&&!Regex.IsMatch(e.Key,@"\{\d+\}")).OrderByDescending(e=>e.Key.Length)){
   var key=entry.Key;if(normalized.Length<=key.Length)continue;
   if(normalized.StartsWith(key,StringComparison.Ordinal)&&(char.IsWhiteSpace(normalized[key.Length])||char.IsDigit(normalized[key.Length])||":([·".Contains(normalized[key.Length])||key.EndsWith(':')||key.EndsWith('('))){var tail=normalized[key.Length..];return value.Replace(value.Trim(),entry.Value+Text(tail),StringComparison.Ordinal);}
   if(normalized.EndsWith(key,StringComparison.Ordinal)&&char.IsWhiteSpace(normalized[normalized.Length-key.Length-1])){var head=normalized[..^key.Length];return value.Replace(value.Trim(),Text(head)+entry.Value,StringComparison.Ordinal);}
  }
  return value;
 }
}
