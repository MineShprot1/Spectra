using System.IO;
using System.Text.Json.Nodes;
namespace Spectra;
public sealed partial class CatalogService
{
 static readonly Dictionary<string,HashSet<string>> FilterTags=new()
 {
 ["mods"]=["adventure","cursed","decoration","economy","equipment","food","game-mechanics","library","magic","management","minigame","mobs","optimization","social","storage","technology","transportation","utility","worldgen"],
 ["resources"]=["128x","16x","256x","32x","48x","512x+","64x","8x-","audio","blocks","core-shaders","entities","environment","equipment","fonts","gui","items","locale","models","combat","cursed","decoration","modded","realistic","simplistic","themed","tweaks","utility","vanilla-like"],
 ["shaders"]=["atmosphere","bloom","colored-lighting","foliage","path-tracing","pbr","reflections","shadows","cartoon","cursed","fantasy","realistic","semi-realistic","vanilla-like","high","low","medium","potato","screenshot"],
 };
 static string[] ValidateFilters(string kind,string[]? values){var tags=(values??[]).Distinct().ToArray();if(tags.Length>4||tags.Any(v=>!FilterTags.TryGetValue(kind,out var allowed)||!allowed.Contains(v)))throw new IOException("Неизвестный фильтр каталога");return tags;}
}
