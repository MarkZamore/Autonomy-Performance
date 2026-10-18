using System;using System.Collections.Generic;using System.Runtime.InteropServices;
public sealed class DisplayInfo {
 public string MonitorId;public string DeviceName;public string Label;public uint Width,Height,CurrentRate;public List<uint> Rates=new List<uint>();
 public override string ToString(){return Label;}
}
public sealed class DisplayChangeResult {public string Status,Detail;public DisplayChangeResult(string status,string detail){Status=status;Detail=detail;}}
public static class DisplayControl {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Device{public uint Size;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Description;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Id;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Key;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Mode{[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;public ushort Spec,Driver,Size,Extra;public uint Fields;public int X,Y;public uint Orientation,Fixed;public short Color,Duplex,Resolution,Option,Collate;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Form;public ushort LogPixels;public uint Bits,Width,Height,Flags,Frequency,ICM,Intent,Media,Dither,Reserved1,Reserved2,PanningWidth,PanningHeight;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool EnumDisplayDevices(string name,uint index,ref Device device,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool EnumDisplaySettings(string name,int index,ref Mode mode);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int ChangeDisplaySettingsEx(string name,ref Mode mode,IntPtr hwnd,uint flags,IntPtr parameter);
 static Device NewDevice(){Device d=new Device();d.Size=(uint)Marshal.SizeOf(typeof(Device));return d;}
 static Mode NewMode(){Mode m=new Mode();m.Size=(ushort)Marshal.SizeOf(typeof(Mode));return m;}
 public static List<DisplayInfo> Enumerate(){
  List<DisplayInfo> result=new List<DisplayInfo>();
  for(uint i=0;i<32;i++){
   Device adapter=NewDevice();if(!EnumDisplayDevices(null,i,ref adapter,0))break;if((adapter.Flags&1)==0||(adapter.Flags&8)!=0)continue;
   Device monitor=new Device();int count=0;
   for(uint j=0;j<32;j++){Device found=NewDevice();if(!EnumDisplayDevices(adapter.Name,j,ref found,1))break;if((found.Flags&1)!=0&&!String.IsNullOrEmpty(found.Id)){monitor=found;count++;}}
   // A cloned source drives more than one monitor: do not affect an unintended screen.
   if(count!=1)continue;
   Mode current=NewMode();if(!EnumDisplaySettings(adapter.Name,-1,ref current))continue;
   SortedSet<uint> rates=new SortedSet<uint>();
   for(int j=0;j<10000;j++){Mode mode=NewMode();if(!EnumDisplaySettings(adapter.Name,j,ref mode))break;if(mode.Width==current.Width&&mode.Height==current.Height&&mode.Bits==current.Bits&&mode.Orientation==current.Orientation&&mode.Flags==current.Flags&&mode.Frequency>1)rates.Add(mode.Frequency);}
   if(rates.Count==0)continue;
   int screenNumber;string suffix=adapter.Name.Substring(adapter.Name.LastIndexOf("DISPLAY",StringComparison.OrdinalIgnoreCase)+7);
   if(!Int32.TryParse(suffix,out screenNumber))screenNumber=result.Count+1;
   result.Add(new DisplayInfo{MonitorId=monitor.Id,DeviceName=adapter.Name,Label="Экран "+screenNumber+" — "+current.Width+" × "+current.Height,Width=current.Width,Height=current.Height,CurrentRate=current.Frequency,Rates=new List<uint>(rates)});
  }
  return result;
 }
 public static DisplayChangeResult Apply(string monitorId,uint rate){
  try{
   List<DisplayInfo> matches=Enumerate().FindAll(delegate(DisplayInfo d){return String.Equals(d.MonitorId,monitorId,StringComparison.OrdinalIgnoreCase);});
   if(matches.Count!=1)return new DisplayChangeResult("skipped","Selected monitor is disconnected or ambiguous.");
   DisplayInfo display=matches[0];if(!display.Rates.Contains(rate))return new DisplayChangeResult("skipped","Refresh rate unavailable at the current resolution.");
   Mode current=NewMode();if(!EnumDisplaySettings(display.DeviceName,-1,ref current))return new DisplayChangeResult("skipped","Current display mode unavailable.");
   if(current.Frequency==rate)return new DisplayChangeResult("ok",rate+" Hz already selected.");
   current.Frequency=rate;current.Fields=0x400000;
   int test=ChangeDisplaySettingsEx(display.DeviceName,ref current,IntPtr.Zero,2,IntPtr.Zero);
   if(test!=0)return new DisplayChangeResult("skipped","Driver rejected refresh rate; code="+test);
   int applied=ChangeDisplaySettingsEx(display.DeviceName,ref current,IntPtr.Zero,1,IntPtr.Zero);
   if(applied!=0)return new DisplayChangeResult("error","Display mode could not be applied; code="+applied);
   Mode observed=NewMode();if(!EnumDisplaySettings(display.DeviceName,-1,ref observed)||observed.Frequency!=rate)return new DisplayChangeResult("error","Refresh rate read-back mismatch.");
   return new DisplayChangeResult("ok",display.Label+": "+rate+" Hz verified.");
  }catch(Exception ex){return new DisplayChangeResult("error",ex.Message);}
 }
}