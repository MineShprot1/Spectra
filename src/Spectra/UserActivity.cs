using System.Runtime.InteropServices;
namespace Spectra;
public static class UserActivity
{
 [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint Size, Tick; }
 [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput info);
 public static bool IsAway { get { var input=new LastInput {Size=(uint)Marshal.SizeOf<LastInput>()};return GetLastInputInfo(ref input)&&unchecked((uint)Environment.TickCount-input.Tick)>=300000; } }
}
