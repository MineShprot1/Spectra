global using System;
global using System.Linq;
global using System.Collections.Generic;
global using System.Threading;
global using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Spectra;
// Headless tests use isolated storage and no Windows installation or real game launch.
public sealed class Store
{
 public static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true};
 public string Root{get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"spectra-features-"+Guid.NewGuid().ToString("N"));
 public void Save(){}
 public TestConfig Config{get;}=new();
}
public sealed class TestConfig{public string CurseForgeKey{get;set;}="";public string SelectedBedrock{get;set;}="";public string SelectedBedrockLabel{get;set;}="";}
public static class ContentService{public static void BackupInstalledData(Store store){}public static Task OpenBedrockFiles(Store store)=>Task.CompletedTask;}
public sealed partial class CatalogService(Store store)
{
 static string Q(string value)=>Uri.EscapeDataString(value);
 static Task<JsonNode> Curse(string url)=>throw new NotSupportedException("Network metadata is not used by parser tests");
}
