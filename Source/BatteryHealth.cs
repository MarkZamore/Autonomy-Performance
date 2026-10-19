using System;using System.Globalization;using System.Runtime.InteropServices;
// Windows.Devices.Power.Battery.GetReport: no powercfg report, temporary file,
// registry cache, polling timer or background service.
public static class BatteryHealth{
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int GetObject(IntPtr self,out IntPtr value);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int GetInt(IntPtr self,out int value);
 [DllImport("combase.dll")]static extern int RoInitialize(uint type);
 [DllImport("combase.dll")]static extern void RoUninitialize();
 [DllImport("combase.dll")]static extern int RoGetActivationFactory(IntPtr name,ref Guid iid,out IntPtr factory);
 [DllImport("combase.dll",CharSet=CharSet.Unicode)]static extern int WindowsCreateString(string value,int length,out IntPtr name);
 [DllImport("combase.dll")]static extern int WindowsDeleteString(IntPtr value);
 static T Method<T>(IntPtr instance,int slot)where T:class{return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),slot*IntPtr.Size),typeof(T)) as T;}
 static void Check(int value){if(value<0)Marshal.ThrowExceptionForHR(value);}
 static int Capacity(IntPtr report,int slot){IntPtr optional=IntPtr.Zero;try{Check(Method<GetObject>(report,slot)(report,out optional));if(optional==IntPtr.Zero)return -1;int value;Check(Method<GetInt>(optional,6)(optional,out value));return value;}finally{if(optional!=IntPtr.Zero)Marshal.Release(optional);}}
 public static string Format(int full,int design){if(full<=0||design<=0)return "Износ батареи: нет данных";double wear=Math.Max(0,100.0*(1.0-(double)full/design));return String.Format(CultureInfo.InvariantCulture,"Износ батареи: {0:0.000} Wh / {1:0.000} Wh | {2:0.0}%",full/1000.0,design/1000.0,wear);}
 public static string Read(){IntPtr name=IntPtr.Zero,factory=IntPtr.Zero,battery=IntPtr.Zero,report=IntPtr.Zero;int init=RoInitialize(0);try{if(init<0&&init!=unchecked((int)0x80010106))return Format(0,0);string cls="Windows.Devices.Power.Battery";Check(WindowsCreateString(cls,cls.Length,out name));Guid iid=new Guid("79cd72b6-9e5e-4452-bea6-dfcd541e597f");Check(RoGetActivationFactory(name,ref iid,out factory));Check(Method<GetObject>(factory,6)(factory,out battery));if(battery==IntPtr.Zero)return Format(0,0);Check(Method<GetObject>(battery,7)(battery,out report));if(report==IntPtr.Zero)return Format(0,0);return Format(Capacity(report,8),Capacity(report,7));}catch{return Format(0,0);}finally{foreach(IntPtr value in new[]{report,battery,factory})if(value!=IntPtr.Zero)Marshal.Release(value);if(name!=IntPtr.Zero)WindowsDeleteString(name);if(init>=0)RoUninitialize();}}
}
