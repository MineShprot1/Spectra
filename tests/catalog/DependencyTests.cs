using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Spectra;

static class DependencyTests
{
 public static async Task Run(Store store,GameService game,CatalogService catalog,Instance target)
 {
  static void Check(bool value,string message){if(!value)throw new Exception(message);}
  JsonObject Version(string id,string project,params JsonObject[] deps)
  {
   var bytes=Encoding.UTF8.GetBytes("fixture-"+id);var url="https://cdn.example.org/"+id+".jar";Net.Files[url]=bytes;
   return new(){["id"]=id,["project_id"]=project,["game_versions"]=new JsonArray("1.21.1"),["loaders"]=new JsonArray("fabric"),["dependencies"]=new JsonArray(deps.Select(d=>(JsonNode)d).ToArray()),["files"]=new JsonArray(new JsonObject{["filename"]=id+".jar",["url"]=url,["primary"]=true,["hashes"]=new JsonObject{["sha512"]=Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant()}})};
  }
  var versions=new Dictionary<string,JsonObject>{
   ["root-v"]=Version("root-v","root",new JsonObject{["project_id"]="library",["dependency_type"]="required"},new JsonObject{["version_id"]="pinned-v",["dependency_type"]="required"},new JsonObject{["project_id"]="optional",["dependency_type"]="optional"}),
   ["library-v"]=Version("library-v","library",new JsonObject{["project_id"]="transitive",["dependency_type"]="required"}),
   ["pinned-v"]=Version("pinned-v","pinned"),["transitive-v"]=Version("transitive-v","transitive")};
  Net.Reply=url=>{
   if(url.Contains("/version/"))return versions[url.Split('/').Last()].DeepClone();
   if(url.Contains("/version?")){var project=url.Split("/project/")[1].Split('/')[0];var full=versions.Values.Single(v=>v.Str("project_id")==project).DeepClone();full!["dependencies"]=null;return new JsonArray(full);}
   return new JsonObject{["client_side"]="required",["server_side"]="optional"};
  };
  game.Running[target.Id]=new object();
  var result=System.Text.Json.JsonSerializer.SerializeToNode(await catalog.Install("modrinth","root","mods",target.Id))!;
  Check(result["restartRequired"]!.GetValue<bool>(),"Running instance does not report restart requirement");
  foreach(var name in versions.Keys)Check(File.Exists(Path.Combine(store.Folder(target),"mods",name+".jar")),"Missing Modrinth dependency: "+name);
  Check(!Net.Requests.Any(u=>u.Contains("/project/optional")),"Optional dependency installed automatically");
  foreach(var kind in new[]{"resources","shaders"})await catalog.Install("modrinth","transitive",kind,target.Id);

  JsonObject CurseFixture(string project,string id,bool dependencies=false,bool manual=false)
  {
   var bytes=Encoding.UTF8.GetBytes("curse-"+id);var url="https://cdn.example.org/cf-"+id+".jar";Net.Files[url]=bytes;
   return new(){["id"]=id,["modId"]=project,["fileName"]="cf-"+id+".jar",["downloadUrl"]=manual?"":url,["gameVersions"]=new JsonArray("1.21.1"),["hashes"]=new JsonArray(new JsonObject{["algo"]=1,["value"]=Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant()}),["dependencies"]=dependencies?new JsonArray(new JsonObject{["modId"]=200,["relationType"]=3}):new JsonArray()};
  }
  bool manual=false;
  Net.Reply=url=>{
   var route=new Uri(url).AbsolutePath.Split('/',StringSplitOptions.RemoveEmptyEntries);var id=route[2];var file=CurseFixture(id,id=="100"?"101":"201",id=="100",manual&&id=="200");
   if(route.Length==4){file["dependencies"]=null;return new JsonObject{["data"]=new JsonArray(file)};}
   if(route.Length==5)return new JsonObject{["data"]=file};
   return new JsonObject{["data"]=new JsonObject{["id"]=id,["name"]="Fixture",["links"]=new JsonObject{["websiteUrl"]="https://www.curseforge.com/minecraft/mc-mods/fixture"}}};
  };
  await catalog.Install("curseforge","100","mods",target.Id);
  Check(File.Exists(Path.Combine(store.Folder(target),"mods/cf-201.jar")),"Required dependency missing from the file-list response was not installed");
  File.Delete(Path.Combine(store.Folder(target),"mods/cf-201.jar"));manual=true;
  var pending=System.Text.Json.JsonSerializer.SerializeToNode(await catalog.Install("curseforge","100","mods",target.Id))!;
  Check(pending.Str("status")=="manualDownload","Restricted dependency was silently omitted");
  try{await catalog.Install("curseforge","100","mods",target.Id,pending.Str("planId"),[]);throw new Exception("Mandatory dependency could be skipped");}catch(IOException){}
  game.Running.TryRemove(target.Id,out _);
  Console.WriteLine("PASS dependencies: authoritative release metadata, transitive/pinned/required dependencies, optional exclusion, live mod/resource/shader install and no skipping required manual files");
 }
}
