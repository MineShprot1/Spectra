using System.Runtime.InteropServices;
namespace Spectra;
/// <summary>Only listeners owned by the Java processes launched by this launcher.</summary>
public static class LanDiscovery
{
 [DllImport("iphlpapi.dll",SetLastError=true)]static extern uint GetExtendedTcpTable(nint table,ref int size,bool order,int family,int tableClass,uint reserved);
 public static Dictionary<int,List<int>> Listeners(IEnumerable<int> processIds)
 {
  var ids=processIds.ToHashSet();var found=new Dictionary<int,List<int>>();if(!OperatingSystem.IsWindows())return found;
  foreach(var family in new[]{2,23}){int size=0;GetExtendedTcpTable(0,ref size,false,family,5,0);if(size<4||size>16*1024*1024)continue;var table=Marshal.AllocHGlobal(size);try{if(GetExtendedTcpTable(table,ref size,false,family,5,0)!=0)continue;var bytes=new byte[size];Marshal.Copy(table,bytes,0,size);foreach(var (pid,port) in Parse(bytes,family==23))if(ids.Contains(pid)){if(!found.TryGetValue(pid,out var list))found[pid]=list=[];if(!list.Contains(port))list.Add(port);}}finally{Marshal.FreeHGlobal(table);}}
  return found;
 }
 internal static IEnumerable<(int Pid,int Port)> Parse(byte[] bytes,bool ipv6)
 {
  if(bytes.Length<4)yield break;var length=ipv6?56:24;var count=BitConverter.ToUInt32(bytes,0);if(count>(bytes.Length-4)/length)yield break;
  for(var index=0;index<count;index++){int offset=4+index*length,state=ipv6?48:0,port=ipv6?20:8,pid=ipv6?52:20;if(BitConverter.ToUInt32(bytes,offset+state)!=2)continue;var number=(bytes[offset+port]<<8)|bytes[offset+port+1];if(number>0)yield return(BitConverter.ToInt32(bytes,offset+pid),number);}
 }
}
