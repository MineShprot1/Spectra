using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Spectra;
public static class CommunityServers
{
 public static async Task<object> Search(string query,int offset){if(offset<0||offset>2400)throw new IOException("Неверная страница");var facets=Uri.EscapeDataString(JsonSerializer.Serialize(new[]{new[]{"project_type:server"}}));var response=await Net.Get("https://api.modrinth.com/v3/search?query="+Uri.EscapeDataString(query)+"&limit=24&offset="+offset+"&facets="+facets);var items=response["hits"]?.AsArray()??[];return new{items=items.Select(p=>new{id=p.Str("project_id"),title=p.Str("title"),description=p.Str("description"),icon=p.Str("icon_url"),url="https://modrinth.com/server/"+Uri.EscapeDataString(p.Str("slug"))}),hasMore=offset+items.Count<(response["total_hits"]?.GetValue<int>()??0)};}
 public static async Task<object> Details(string id){if(!System.Text.RegularExpressions.Regex.IsMatch(id,@"\A[A-Za-z0-9_-]{1,100}\z"))throw new IOException("Неверный проект");var project=await Net.Get("https://api.modrinth.com/v3/project/"+Uri.EscapeDataString(id));var types=project["project_types"]?.AsArray().Select(t=>t?.ToString()).ToArray()??[project.Str("project_type")];if(!types.Contains("server"))throw new IOException("Это не серверный проект");var server=project["minecraft_server"]??project["server"];
  return new{title=project.Str("title"),summary=project.Str("description"),description=project.Str("body"),format="markdown",icon=project.Str("icon_url"),url="https://modrinth.com/server/"+Uri.EscapeDataString(project.Str("slug")),address=server.Str("address"),online=server?["online"],players=server?["players"],screenshots=project["gallery"]?.AsArray().Select(s=>new{url=s.Str("url"),caption=s.Str("title")})??[],authors=Array.Empty<object>(),versions=Array.Empty<object>()};}
}
