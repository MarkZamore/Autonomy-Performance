using System;using System.Diagnostics;using System.Runtime.InteropServices;using System.Threading;
public sealed class CancelNativeDialog {
[DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string cls,string title);
[DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
[DllImport("user32.dll")]static extern bool PostMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
public bool Found;public bool Canceled;Thread worker;
public CancelNativeDialog(string title){worker=new Thread(delegate(){Stopwatch clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<12000){IntPtr h=FindWindow("#32770",title);uint pid=0;if(h!=IntPtr.Zero)GetWindowThreadProcessId(h,out pid);if(h!=IntPtr.Zero&&pid==(uint)Process.GetCurrentProcess().Id){Found=true;Canceled=PostMessage(h,0x111,new IntPtr(2),IntPtr.Zero);return;}Thread.Sleep(100);}});worker.IsBackground=true;worker.Start();}
public bool Wait(){return worker.Join(13000)&&Found&&Canceled;}
}