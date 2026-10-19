using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;

// Explicit development check. No application path invokes this program.
// Pass --live to exercise DRR, with an exact CCD topology rollback in finally.
static class DisplayCapabilitiesTest {
 static void Assert(bool value,string message){if(!value)throw new InvalidOperationException(message);}
 static int Main(string[] args){
  bool live=Array.IndexOf(args,"--live")>=0;
  BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
  MethodInfo topology=typeof(HdrControl).GetMethod("GetTopology",flags),set=typeof(HdrControl).GetMethod("SetDisplayConfig",flags);
  List<DisplayInfo> displays=DisplayControl.Enumerate();
  foreach(DisplayInfo display in displays){
   bool original;bool supported=HdrControl.InspectDynamic(display.MonitorId,out original);
   int scale=HdrControl.InspectRecommendedScale(display.MonitorId);
   Console.WriteLine(display.Label+": DRR="+supported+", enabled="+original+", native="+display.RecommendedWidth+"x"+display.RecommendedHeight+", recommendedScale="+scale);
   Assert(display.RecommendedWidth>0&&display.RecommendedHeight>0,"Preferred mode missing.");
   Assert(display.Resolutions.Exists(delegate(DisplayResolution r){return r.IsRecommended&&r.Width==display.RecommendedWidth&&r.Height==display.RecommendedHeight;}),"Recommended mode not marked.");
   Assert(scale==0||HdrControl.InspectScales(display.MonitorId).Contains(scale),"Recommended scale unavailable.");
   if(!live||!supported)continue;
   object saved=topology.Invoke(null,new object[]{display.MonitorId});
   Assert(saved!=null,"Topology snapshot unavailable.");
   try{
    foreach(bool requested in new[]{!original,!original,original,original}){
     DisplayChangeResult result=HdrControl.ApplyDynamic(display.MonitorId,requested);
     Console.WriteLine("  DRR "+requested+": "+result.Status+"; "+result.Detail);
     Assert(result.Status=="ok","Dynamic change failed.");
     bool enabled;Assert(HdrControl.InspectDynamic(display.MonitorId,out enabled)&&enabled==requested,"DRR read-back mismatch.");
     foreach(DisplayInfo before in displays){
      DisplayInfo after=DisplayControl.Enumerate().Find(delegate(DisplayInfo d){return d.MonitorId==before.MonitorId;});
      Assert(after!=null&&after.Width==before.Width&&after.Height==before.Height,"Display topology or resolution changed.");
     }
    }
   }finally{
    Type layout=saved.GetType();
    int result=(int)set.Invoke(null,new object[]{layout.GetField("Count").GetValue(saved),layout.GetField("Paths").GetValue(saved),layout.GetField("ModeCount").GetValue(saved),layout.GetField("Modes").GetValue(saved),0x282A0u});
    ((IDisposable)saved).Dispose();
    Assert(result==0,"Exact topology restore failed, native code "+result+".");
    bool restored;Assert(HdrControl.InspectDynamic(display.MonitorId,out restored)&&restored==original,"Original DRR state did not restore.");
    Console.WriteLine("  Exact topology restored.");
   }
  }
  foreach(DisplayInfo before in displays){DisplayInfo after=DisplayControl.Enumerate().Find(delegate(DisplayInfo d){return d.MonitorId==before.MonitorId;});Assert(after!=null&&after.Width==before.Width&&after.Height==before.Height&&after.CurrentRate==before.CurrentRate,"Final original display mode did not restore.");}
  Console.WriteLine("PASS: preferred resolutions, recommended scales and "+(live?"DRR transitions/repeats/restoration.":"read-only DRR validation."));return 0;
 }
}
