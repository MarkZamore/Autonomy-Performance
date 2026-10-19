using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

// Read-only native validation. The only SetDisplayConfig route exercised by
// these probes is SDC_VALIDATE; no test requests SDC_APPLY or changes Windows.
static class PresetDisplayCapabilitiesTests {
 static int checks;
 static void Assert(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
 static BindingFlags PrivateStatic=BindingFlags.Static|BindingFlags.NonPublic;
 static MethodInfo Topology=typeof(HdrControl).GetMethod("GetTopology",PrivateStatic);
 static MethodInfo Prepare=typeof(HdrControl).GetMethod("PrepareDynamicMode",PrivateStatic);
 static object Field(object value,string field){return value.GetType().GetField(field).GetValue(value);}
 static string Snapshot(DisplayInfo display){
  bool hdrSupported,hdrEnabled,drrEnabled;HdrControl.TryInspect(display.MonitorId,out hdrSupported,out hdrEnabled);HdrControl.InspectDynamic(display.MonitorId,out drrEnabled);
  return display.Width+"x"+display.Height+"@"+display.CurrentRate+"/"+HdrControl.InspectCurrentScale(display.MonitorId)+"/HDR="+hdrSupported+":"+hdrEnabled+"/DRR="+drrEnabled;
 }
 static void TestCopiedLowRate(DisplayInfo display,uint width,uint height,uint rate,bool expected,bool lowResolution){
  object topology=Topology.Invoke(null,new object[]{display.MonitorId});if(topology==null)return;
  try{
   Array paths=(Array)Field(topology,"Paths");int index=(int)Field(topology,"Index");object path=paths.GetValue(index),target=Field(path,"Target");
   uint modeIndex=(uint)Field(target,"ModeIndex")>>16;IntPtr mode=IntPtr.Add((IntPtr)Field(topology,"Modes"),checked((int)modeIndex*64));
   // Corrupt only this private snapshot to represent a former 60 Hz physical
   // mode. Preferred mode timings must replace it before validating 144 Hz.
   Marshal.WriteInt32(mode,32,60);Marshal.WriteInt32(mode,36,1);
   if(lowResolution){
    object source=Field(path,"Source");uint sourceIndex=(uint)Field(source,"ModeIndex")>>16,desktopIndex=(uint)Field(target,"ModeIndex")&0xffff;
    IntPtr sourceMode=IntPtr.Add((IntPtr)Field(topology,"Modes"),checked((int)sourceIndex*64));
    uint oldWidth=unchecked((uint)Marshal.ReadInt32(sourceMode,16)),oldHeight=unchecked((uint)Marshal.ReadInt32(sourceMode,20));
    if(desktopIndex!=0xffff){
     IntPtr desktop=IntPtr.Add((IntPtr)Field(topology,"Modes"),checked((int)desktopIndex*64));
     MethodInfo resize=typeof(HdrControl).GetMethod("ResizeDesktopImage",PrivateStatic);
     Assert((bool)resize.Invoke(null,new object[]{desktop,oldWidth,oldHeight,1920u,1200u}),"Could not build an isolated low-resolution desktop snapshot.");
    }
    Marshal.WriteInt32(sourceMode,16,1920);Marshal.WriteInt32(sourceMode,20,1200);
   }
   bool actual=(bool)Prepare.Invoke(null,new object[]{topology,width,height,rate});
   Assert(actual==expected,"Preferred-mode DRR probe depends on the current physical Hz for "+display.Label);
  }finally{((IDisposable)topology).Dispose();}
 }
 static int Main(){
  List<DisplayInfo> displays=DisplayControl.Enumerate();Dictionary<string,string> before=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  foreach(DisplayInfo display in displays)before.Add(display.MonitorId,Snapshot(display));
  bool enabled;Assert(!HdrControl.InspectDynamic("AP_TEST_MISSING",1920,1200,144,out enabled),"An absent monitor was advertised.");
  Assert(!HdrControl.CanChange("AP_TEST_MISSING",true)&&!HdrControl.CanChange("AP_TEST_MISSING",false),"An absent HDR monitor was advertised.");
  foreach(DisplayInfo display in displays){
   Assert(!HdrControl.InspectDynamic(display.MonitorId,1920,0,144,out enabled),"An incomplete resolution was accepted.");
   Assert(!HdrControl.InspectDynamic(display.MonitorId,1,1,144,out enabled),"A driver-unlisted resolution was accepted.");
   Assert(!HdrControl.InspectDynamic(display.MonitorId,display.Width,display.Height,60,out enabled),"60 Hz was advertised as a DRR activation mode.");
   Assert(!HdrControl.InspectDynamicMode(display.MonitorId,display.Width,display.Height,65535),"An unavailable refresh rate was accepted.");
   bool supported,hdr;bool read=HdrControl.TryInspect(display.MonitorId,out supported,out hdr);
   if(read){Assert(!HdrControl.CanChange(display.MonitorId,false)||supported,"HDR OFF was advertised without HDR support.");Assert(!HdrControl.CanChange(display.MonitorId,true)||supported,"HDR ON was advertised without HDR support.");}
   DisplayResolution native=display.Resolutions.Find(delegate(DisplayResolution r){return r.IsRecommended;});
   if(native!=null&&native.Rates.Count>0){
    uint maximum=native.Rates[native.Rates.Count-1];bool capable=HdrControl.InspectDynamicMode(display.MonitorId,native.Width,native.Height,maximum);
    Console.WriteLine(display.Label+": recommended="+native.Width+"x"+native.Height+"@"+maximum+", future DRR="+capable+", HDR on/off="+HdrControl.CanChange(display.MonitorId,true)+"/"+HdrControl.CanChange(display.MonitorId,false));
    if(maximum>=119){TestCopiedLowRate(display,native.Width,native.Height,maximum,capable,false);TestCopiedLowRate(display,native.Width,native.Height,maximum,capable,true);}
   }
  }
  List<DisplayInfo> after=DisplayControl.Enumerate();Assert(after.Count==before.Count,"Read-only probes changed the connected display count.");
  foreach(DisplayInfo display in after){Assert(before.ContainsKey(display.MonitorId),"Read-only probes changed monitor identity.");Assert(before[display.MonitorId]==Snapshot(display),"Read-only probes changed display settings: "+display.Label);}
  Console.WriteLine("PASS: "+checks+" read-only preset display capability checks.");return 0;
 }
}
