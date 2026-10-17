using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
namespace Spectra;
// TCP is exposed only on loopback. Host destinations must be active Minecraft LAN ports.
public sealed class PartyTunnel(GameService game,Action<object> emit):IDisposable
{
 sealed class StreamState(TcpClient client,string peer,string id){public TcpClient Client=client;public string Peer=peer,Id=id;public TaskCompletionSource<bool>? Ack;public SemaphoreSlim Write=new(1,1);}
 readonly ConcurrentDictionary<string,StreamState> streams=new();readonly ConcurrentDictionary<string,(TcpListener Listener,string Peer,string World)> listeners=new();readonly CancellationTokenSource cancel=new();
 public bool Owns(int port)=>listeners.Values.Any(x=>((IPEndPoint)x.Listener.LocalEndpoint).Port==port);
 public int Listen(string peer,string world){if(listeners.Count>=8)throw new IOException("Слишком много открытых миров");var listener=new TcpListener(IPAddress.Loopback,0);listener.Start(16);var key=Guid.NewGuid().ToString("N");listeners[key]=(listener,peer,world);_=Accept(listener,peer,world);return ((IPEndPoint)listener.LocalEndpoint).Port;}
 async Task Accept(TcpListener listener,string peer,string world){try{while(!cancel.IsCancellationRequested){var client=await listener.AcceptTcpClientAsync(cancel.Token);if(streams.Count>=64){client.Dispose();continue;}var id=Guid.NewGuid().ToString("N");var state=new StreamState(client,peer,id);streams[id]=state;emit(new{type="partyTcp",peer,id,operation="open",world});_=Read(state);}}catch(Exception e) when(e is OperationCanceledException or SocketException or ObjectDisposedException){} }
 public async Task Open(string peer,string id,string world){if(id.Length!=32||!id.All(Uri.IsHexDigit)||streams.Count>=64)throw new IOException("Неверный поток");if(!game.Running.ContainsKey(world)||!game.Activities.TryGetValue(world,out var activity)||activity.LanPort<1)throw new IOException("LAN-мир уже закрыт");var client=new TcpClient();await client.ConnectAsync(IPAddress.Loopback,activity.LanPort,cancel.Token);var state=new StreamState(client,peer,id);if(!streams.TryAdd(id,state)){client.Dispose();throw new IOException("Повторный поток");}_=Read(state);}
 async Task Read(StreamState state){try{var buffer=new byte[12288];int count;while((count=await state.Client.GetStream().ReadAsync(buffer,cancel.Token))>0){var ack=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);state.Ack=ack;emit(new{type="partyTcp",peer=state.Peer,id=state.Id,operation="data",data=Convert.ToBase64String(buffer,0,count)});await ack.Task.WaitAsync(TimeSpan.FromSeconds(20),cancel.Token);}}catch(Exception e) when(e is IOException or SocketException or OperationCanceledException or TimeoutException or ObjectDisposedException){}finally{Close(state.Peer,state.Id);emit(new{type="partyTcp",peer=state.Peer,id=state.Id,operation="close"});}}
 public async Task Write(string peer,string id,string data){if(!streams.TryGetValue(id,out var state)||state.Peer!=peer)throw new IOException("Поток закрыт");if(data.Length>16384)throw new IOException("Слишком большой пакет");var bytes=Convert.FromBase64String(data);await state.Write.WaitAsync(cancel.Token);try{using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);deadline.CancelAfter(TimeSpan.FromSeconds(20));await state.Client.GetStream().WriteAsync(bytes,deadline.Token);}finally{state.Write.Release();}}
 public void Ack(string peer,string id){if(streams.TryGetValue(id,out var state)&&state.Peer==peer)state.Ack?.TrySetResult(true);}
 public void Close(string peer,string id){if(streams.TryGetValue(id,out var state)&&state.Peer==peer&&streams.TryRemove(id,out _)){state.Ack?.TrySetCanceled();state.Client.Dispose();}}
 public void DropPeer(string peer){foreach(var pair in streams.ToArray())if(pair.Value.Peer==peer)Close(peer,pair.Key);foreach(var item in listeners.ToArray())if(item.Value.Peer==peer&&listeners.TryRemove(item.Key,out var entry))entry.Listener.Stop();}
 public void DropAll(){foreach(var item in listeners.Values)item.Listener.Stop();listeners.Clear();foreach(var item in streams.Values)item.Client.Dispose();streams.Clear();}
 public void Dispose(){cancel.Cancel();DropAll();}
}
