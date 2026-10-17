using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
namespace Spectra;
public static class LanStatus
{
 static byte[] VarInt(int value){var bytes=new List<byte>();var n=(uint)value;do{var b=(byte)(n&127);n>>=7;bytes.Add((byte)(b|(n>0?128:0)));}while(n>0);return bytes.ToArray();}
 static async Task<int> ReadInt(Stream input,CancellationToken token){int value=0;var buffer=new byte[1];for(int n=0;n<5;n++){await input.ReadExactlyAsync(buffer,token);value|=(buffer[0]&127)<<(7*n);if((buffer[0]&128)==0)return value;}throw new IOException("Неверный VarInt");}
 public static async Task<(int? Players,string Icon)> Query(int port){using var timeout=new CancellationTokenSource(TimeSpan.FromMilliseconds(1200));try{using var client=new TcpClient();await client.ConnectAsync(IPAddress.Loopback,port,timeout.Token);await using var input=client.GetStream();using var packet=new MemoryStream();packet.WriteByte(0);packet.Write(VarInt(-1));var host=Encoding.UTF8.GetBytes("localhost");packet.Write(VarInt(host.Length));packet.Write(host);packet.WriteByte((byte)(port>>8));packet.WriteByte((byte)port);packet.WriteByte(1);var payload=packet.ToArray();await input.WriteAsync(VarInt(payload.Length),timeout.Token);await input.WriteAsync(payload,timeout.Token);await input.WriteAsync(new byte[]{1,0},timeout.Token);var length=await ReadInt(input,timeout.Token);if(length<2||length>1024*1024)return(null,"");var body=new byte[length];await input.ReadExactlyAsync(body,timeout.Token);using var stream=new MemoryStream(body);if(await ReadInt(stream,timeout.Token)!=0)return(null,"");var size=await ReadInt(stream,timeout.Token);if(size<1||size>body.Length)return(null,"");var json=new byte[size];await stream.ReadExactlyAsync(json,timeout.Token);var status=JsonNode.Parse(json);var players=status?["players"]?["online"]?.GetValue<int>();var icon=status?["favicon"]?.GetValue<string>()??"";return(players is >=0 and <=100000?players:null,icon.Length<=4096&&icon.StartsWith("data:image/png;base64,",StringComparison.Ordinal)?icon:"");}catch(Exception e) when(e is IOException or SocketException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException){return(null,"");}}
}
