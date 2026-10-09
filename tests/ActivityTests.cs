using Spectra;
using System.Text;
var count=0;
void Check(bool test,string message){if(!test)throw new Exception(message);count++;Console.WriteLine("PASS "+message);}
string Client(string text)=>"[18:28:00] [Render thread/INFO]: "+text;
string Server(string text)=>"[18:28:00] [Server thread/INFO]: "+text;
var parser=new ActivityLog("First world");
Check(parser.State.Mode=="menu","initial menu does not invent a launched server");
parser.Observe(Client("[CHAT] Connecting to fake.example, 25565"));Check(parser.State.Mode=="menu","chat cannot spoof presence");
parser.Observe("[18:28:00] [Render thread/INFO] [voicechat/]: Connecting to fake.example, 25565");Check(parser.State.Mode=="menu","unrelated mod messages are ignored");
parser.Observe(Client("Connecting to mc.example.org, 25565"));Check(parser.State.Mode=="connecting"&&parser.State.Target=="mc.example.org:25565","server address and connection attempt");
parser.Observe(Client("Loaded 4 advancements"));Check(parser.State.Mode=="server","client world synchronization confirms server");
parser.Observe(Client("Disconnected from server: Timed out"));Check(parser.State==new LogPlayState(),"disconnect clears server and LAN");
parser.Observe(Server("Starting integrated minecraft server version 1.21.1"));Check(parser.State.Mode=="world"&&parser.State.World=="First world","local world with launcher hint");
parser.Observe(Server("Saving chunks for level 'ServerLevel[Мой мир]'/minecraft:overworld"));Check(parser.State.World=="Мой мир","world name from native save log");
parser.Observe(Client("Started serving on 0"));Check(parser.State.Mode=="world","invalid LAN port ignored");
parser.Observe(Client("Started serving on 54321"));Check(parser.State.Mode=="lan"&&parser.State.LanPort==54321,"open LAN world and automatic port");
parser.Observe(Server("Stopping singleplayer server as player logged out"));Check(parser.State==new LogPlayState(),"closing local world returns to menu");
parser.Observe(Server("Saving chunks for level 'ServerLevel[Old world]'/minecraft:overworld"));Check(parser.State.Mode=="menu","late save lines cannot resurrect closed worlds");
parser.Observe(Server("Starting integrated minecraft server version 1.20.1"));Check(parser.State.World=="","launcher world hint is used only once");parser.Observe(Server("Preparing level \"Another world\""));Check(parser.State.World=="Another world","older native world name format");
parser.Observe("[09Oct2026 18:28:01.000] [Render thread/INFO] [minecraft/ConnectScreen]: Connecting to [2001:db8::1], 25566");Check(parser.State.Mode=="connecting"&&parser.State.Target=="[2001:db8::1]:25566","NeoForge envelope and IPv6 server");
parser.Observe("[18:28:02] [Server Connector #1/ERROR]: Couldn't connect to server");Check(parser.State.Mode=="menu","failed connection clears address");
Check(ActivityLog.Address("https://invalid.example","25565")==""&&ActivityLog.Address("example.org","65536")=="","reject malformed endpoints");
var temp=Path.Combine(Path.GetTempPath(),"Spectra-activity-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
try{
 var file=Path.Combine(temp,"latest.log");await File.WriteAllTextAsync(file,Client("Connecting to old.example, 25565")+"\n");var lines=new List<string>();var locker=new object();var watcher=new ActivityLogWatcher(file,line=>{lock(locker)lines.Add(line);});watcher.Baseline();using var cancel=new CancellationTokenSource();var task=watcher.Run(cancel.Token);
 async Task Until(Func<bool> ready){for(var retry=0;retry<50;retry++){lock(locker)if(ready())return;await Task.Delay(100);}throw new Exception("Log watcher did not catch up");}
 await File.WriteAllTextAsync(file,Server("Starting integrated minecraft server version 1.21.1")+"\n");await Until(()=>lines.Count==1);lock(locker)Check(!lines.Any(l=>l.Contains("old.example")),"stale log content skipped after truncation");
 var text=Server("Preparing level \"Мир é\"")+"\n";var bytes=Encoding.UTF8.GetBytes(text);var split=Array.IndexOf(bytes,(byte)0xc3)+1;await using(var append=new FileStream(file,FileMode.Append,FileAccess.Write,FileShare.ReadWrite)){await append.WriteAsync(bytes.AsMemory(0,split));await append.FlushAsync();await Task.Delay(1200);await append.WriteAsync(bytes.AsMemory(split));}
 await Until(()=>lines.Count==2);lock(locker)Check(lines[1].Contains("Мир é")&&!lines[1].Contains('�'),"incremental UTF-8 preserves split characters");
 File.Move(file,file+".old");await File.WriteAllTextAsync(file,Client("Connecting to new.example, 25565")+"\n");await Until(()=>lines.Count==3);lock(locker)Check(lines[2].Contains("new.example"),"new current log read after rotation");cancel.Cancel();try{await task;}catch(OperationCanceledException){}
}finally{Directory.Delete(temp,true);}
Console.WriteLine($"{count} checks passed");
