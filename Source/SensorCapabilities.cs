using System;using System.Runtime.InteropServices;using System.Management;
// Read-only probes through Windows' sensor APIs, not PnP name heuristics.
public static class SensorCapabilities{
 [StructLayout(LayoutKind.Sequential)]struct PowerStatus{public byte AC,Flag,Percent,Saver;public uint Life,FullLife;}
 [DllImport("kernel32.dll")]static extern bool GetSystemPowerStatus(out PowerStatus status);
 public static bool HasBattery(){PowerStatus status;return GetSystemPowerStatus(out status)&&status.Flag!=255&&(status.Flag&128)==0;}
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int GetObject(IntPtr self,out IntPtr value);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int GetFeatures(IntPtr self,IntPtr id,out IntPtr value);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int GetBoolean(IntPtr self,out byte value);
 [DllImport("combase.dll")]static extern int RoInitialize(uint type);
 [DllImport("combase.dll")]static extern void RoUninitialize();
 [DllImport("combase.dll")]static extern int RoGetActivationFactory(IntPtr name,ref Guid iid,out IntPtr factory);
 [DllImport("combase.dll",CharSet=CharSet.Unicode)]static extern int WindowsCreateString(string value,int length,out IntPtr name);
 [DllImport("combase.dll")]static extern int WindowsDeleteString(IntPtr value);
 [DllImport("combase.dll")]static extern uint WindowsGetStringLen(IntPtr value);
 static T Method<T>(IntPtr value,int slot)where T:class{return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value),slot*IntPtr.Size),typeof(T)) as T;}
 static void Check(int hr){if(hr<0)Marshal.ThrowExceptionForHR(hr);}
 public static bool HasAmbientLight(){IntPtr name=IntPtr.Zero,factory=IntPtr.Zero,sensor=IntPtr.Zero;int init=RoInitialize(0);try{if(init<0&&init!=unchecked((int)0x80010106))return false;string cls="Windows.Devices.Sensors.LightSensor";Check(WindowsCreateString(cls,cls.Length,out name));Guid iid=new Guid("45db8c84-c3a8-471e-9a53-6457fad87c0e");Check(RoGetActivationFactory(name,ref iid,out factory));Check(Method<GetObject>(factory,6)(factory,out sensor));return sensor!=IntPtr.Zero;}catch{return false;}finally{if(sensor!=IntPtr.Zero)Marshal.Release(sensor);if(factory!=IntPtr.Zero)Marshal.Release(factory);if(name!=IntPtr.Zero)WindowsDeleteString(name);if(init>=0)RoUninitialize();}}
 public static bool HasBrightness(){try{using(ManagementObjectSearcher query=new ManagementObjectSearcher(@"root\wmi","SELECT Active FROM WmiMonitorBrightness"))using(ManagementObjectCollection rows=query.Get()){foreach(ManagementObject row in rows)using(row){if(row["Active"] is bool&&(bool)row["Active"])return true;}}}catch{}return false;}
 public static bool HasPresence(bool dimming){return HasPresence(dimming?1:0);}
 public static bool HasPresence(int feature){bool dimming=feature==1;IntPtr name=IntPtr.Zero,factory=IntPtr.Zero,settings=IntPtr.Zero,id=IntPtr.Zero,features=IntPtr.Zero,extra=IntPtr.Zero;int init=RoInitialize(0);try{
  if(init<0&&init!=unchecked((int)0x80010106))return false;string cls="Windows.Devices.Sensors.HumanPresenceSettings";Check(WindowsCreateString(cls,cls.Length,out name));Guid iid=new Guid("7f343202-e010-52c4-af0c-04a8f1e033da");Check(RoGetActivationFactory(name,ref iid,out factory));Check(Method<GetObject>(factory,7)(factory,out settings));if(settings==IntPtr.Zero)return false;Check(Method<GetObject>(settings,6)(settings,out id));if(id==IntPtr.Zero||WindowsGetStringLen(id)==0)return false;Check(Method<GetFeatures>(factory,11)(factory,id,out features));if(features==IntPtr.Zero)return false;
  IntPtr target=features;int slot=feature==2?8:9;if(dimming||feature==3){iid=new Guid(feature==3?"ed4e03aa-5ca2-5c02-b783-262e91295619":"08a9cdda-d929-5ec2-81e2-940bafa089cf");Check(Marshal.QueryInterface(features,ref iid,out extra));target=extra;slot=6;}
  byte supported;Check(Method<GetBoolean>(target,slot)(target,out supported));return supported!=0;
 }catch{return false;}finally{foreach(IntPtr value in new[]{extra,features,settings,factory})if(value!=IntPtr.Zero)Marshal.Release(value);if(id!=IntPtr.Zero)WindowsDeleteString(id);if(name!=IntPtr.Zero)WindowsDeleteString(name);if(init>=0)RoUninitialize();}}
}
