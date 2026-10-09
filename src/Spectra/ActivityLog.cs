using System.Net;
using System.Text.RegularExpressions;
namespace Spectra;
public record LogPlayState(string Mode="menu",string Target="",string World="",int LanPort=0);
/// <summary>Conservative log observations. Chat and unrelated mod loggers cannot announce a server.</summary>
public sealed class ActivityLog(string firstWorldHint="")
{
 readonly object sync=new();LogPlayState state=new();string hint=firstWorldHint;
 static readonly Regex Envelope=new(@"^(?:\[[^\]\r\n]{1,80}\]\s*)?\[(?<thread>[^\]/\r\n]{1,80})/(?:INFO|WARN|ERROR)\]\s*(?:\[(?<logger>[^\]\r\n]{1,200})\])?:?\s*(?<message>.*)$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50));
 static readonly Regex Connect=new(@"^Connecting to (?<host>[^,\s]{1,253}), (?<port>\d{1,5})$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50));
 static readonly Regex Lan=new(@"^Started serving on (?<port>\d{1,5})$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50));
 static readonly Regex Prepared=new("^Preparing level \"(?<name>[^\"\\r\\n]{1,100})\"$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50));
 static readonly Regex Saved=new(@"^Saving chunks for level '(?:ServerLevel\[)?(?<name>[^'\r\n]{1,100}?)(?:\])?'/(?:minecraft:|Overworld|Nether|The End)",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50));
 public LogPlayState State {get{lock(sync)return state;}}
 public LogPlayState? Observe(string line){try{return ObserveLine(line);}catch(RegexMatchTimeoutException){return null;}}
 LogPlayState? ObserveLine(string line)
 {
  if(line.Length>16384)return null;var envelope=Envelope.Match(line);if(!envelope.Success)return null;
  var thread=envelope.Groups["thread"].Value;var logger=envelope.Groups["logger"].Value;
  if(logger!=""&&!logger.StartsWith("minecraft/",StringComparison.Ordinal)&&!logger.StartsWith("net.minecraft.",StringComparison.Ordinal))return null;
  var client=thread is "Render thread" or "Client thread" or "main";var server=thread.Equals("Server thread",StringComparison.OrdinalIgnoreCase);var connector=thread.StartsWith("Server Connector",StringComparison.Ordinal);
  var message=envelope.Groups["message"].Value.Trim();if(message.StartsWith("[",StringComparison.Ordinal))return null;
  lock(sync){var next=state;
   var connecting=client?Connect.Match(message):Match.Empty;
   if(connecting.Success){var address=Address(connecting.Groups["host"].Value,connecting.Groups["port"].Value);if(address!="")next=new("connecting",address);}
   else if((server||client)&&message.StartsWith("Starting integrated minecraft server version ",StringComparison.Ordinal)){next=new("world","",hint);hint="";}
   else if((server||client)&&(state.Mode is "world" or "lan")){
    var opened=client?Lan.Match(message):Match.Empty;var prepared=Prepared.Match(message);var saved=Saved.Match(message);
    if(opened.Success&&int.TryParse(opened.Groups["port"].Value,out var port)&&port is >0 and <=65535)next=state with{Mode="lan",LanPort=port};
    else if(prepared.Success)next=state with{World=prepared.Groups["name"].Value};
    else if(saved.Success)next=state with{World=saved.Groups["name"].Value};
   }
   if(client&&state.Mode=="connecting"&&Regex.IsMatch(message,@"^Loaded \d+ advancements$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50)))next=state with{Mode="server"};
   if(server&&(message=="Stopping server"||message=="Stopping singleplayer server as player logged out")&&(state.Mode is "world" or "lan"))next=new();
   if((client||connector)&&(state.Mode is "connecting" or "server")&&(message.StartsWith("Failed to connect to the server",StringComparison.Ordinal)||message.StartsWith("Couldn't connect to server",StringComparison.Ordinal)||message.StartsWith("Can't connect to server",StringComparison.Ordinal)||message.StartsWith("Disconnected from server",StringComparison.Ordinal)||message.StartsWith("Lost connection to server",StringComparison.Ordinal)))next=new();
   if(client&&(message=="Stopping!"||message=="Returning to main menu"||message=="Disconnected"))next=new();
   if(next==state)return null;state=next;return next;
  }
 }
 public static string Address(string host,string rawPort)
 {
  if(!int.TryParse(rawPort,out var port)||port is <1 or >65535)return "";
  var unwrapped=host.Trim('[',']');if(IPAddress.TryParse(unwrapped,out var ip)){if(ip.Equals(IPAddress.Any)||ip.Equals(IPAddress.IPv6Any))return "";return ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetworkV6?"["+ip+"]:"+port:ip+":"+port;}
  if(Uri.CheckHostName(host)!=UriHostNameType.Dns||host.Contains(':')||host.Contains('/')||host.Contains('\\')||host.Contains('@'))return "";return host.ToLowerInvariant()+":"+port;
 }
}
