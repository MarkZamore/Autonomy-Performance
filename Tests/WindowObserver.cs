using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Text;
public sealed class ObservedWindow{public uint ProcessId;public string ClassName;public string Title;public uint Event;}
public sealed class WindowObserver:IDisposable{
[DllImport("user32.dll")]static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,Callback cb,uint pid,uint thread,uint flags);
[DllImport("user32.dll")]static extern bool UnhookWinEvent(IntPtr h);
[DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr h,uint flag);
[DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
[DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder text,int max);
[DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr h,StringBuilder text,int max);
delegate void Callback(IntPtr hook,uint ev,IntPtr h,int obj,int child,uint thread,uint time);
Callback callback;IntPtr hook;public readonly List<ObservedWindow> Events=new List<ObservedWindow>();
public WindowObserver(){callback=OnEvent;hook=SetWinEventHook(0x8000,0x8002,IntPtr.Zero,callback,0,0,0);if(hook==IntPtr.Zero)throw new Exception("WinEvent hook unavailable");}
void OnEvent(IntPtr hook,uint ev,IntPtr h,int obj,int child,uint thread,uint time){if(h==IntPtr.Zero||obj!=0||child!=0||!IsWindowVisible(h)||GetAncestor(h,2)!=h)return;uint pid;GetWindowThreadProcessId(h,out pid);StringBuilder cls=new StringBuilder(128),title=new StringBuilder(512);GetClassName(h,cls,128);GetWindowText(h,title,512);Events.Add(new ObservedWindow{ProcessId=pid,ClassName=cls.ToString(),Title=title.ToString(),Event=ev});}
public void Dispose(){if(hook!=IntPtr.Zero){UnhookWinEvent(hook);hook=IntPtr.Zero;}GC.KeepAlive(callback);}
}