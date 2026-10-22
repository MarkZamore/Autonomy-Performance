using System;using System.Diagnostics;using Microsoft.Win32;
class NativeModeTests{
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static void Ok(StepResult step){Console.WriteLine(step.Name+": "+step.Status+" / "+step.Detail);Check(step.Status=="ok",step.Detail);}
 static int Main(){try{
  if(NightLightControl.Available()){
   NightLightSnapshot current=NightLightControl.Capture();
   Console.WriteLine("Night Light read-only: manual mode="+current.Enabled+" / strength="+current.Strength+". No toggle or schedule/override restoration is attempted.");
  }
  int rate=BatteryCareControl.ReadRate();if(rate==100){try{for(int i=0;i<2;i++){Ok(BatteryCareControl.Set(true));Check(BatteryCareControl.ReadRate()==80,"Wrong charging limit");Ok(BatteryCareControl.Set(false));}}finally{Ok(BatteryCareControl.Set(false));}Check(BatteryCareControl.ReadRate()==rate,"Charging state not restored");}
  // Registry values no longer represent the effective broker state. Tray
  // changes are checked by substituted sessions and a separate approved live check.
  foreach(bool battery in new[]{false,true})Console.WriteLine("Tray preference available: "+battery+" / "+TaskbarControl.Available(battery));
  foreach(DisplayInfo d in DisplayControl.Enumerate()){
   Check(DisplayControl.Apply(d.MonitorId,0,d.Width,d.Height).Status=="ok","Current resolution rejected");
   int scale=HdrControl.InspectCurrentScale(d.MonitorId);if(scale!=0)Check(HdrControl.ApplyScale(d.MonitorId,scale).Status=="ok","Current scale rejected");
   bool supported,enabled;Console.WriteLine(d.Label+" / resolutions="+d.Resolutions.Count+" / scale="+scale+" / HDR read="+HdrControl.TryInspect(d.MonitorId,out supported,out enabled)+" supported="+supported+" / DRR="+HdrControl.InspectDynamic(d.MonitorId,out enabled));
  }
  Check(DisplayControl.Apply("TEST\\MISSING",60,1920,1080).Status=="skipped","Missing monitor not skipped");Check(HdrControl.ApplyScale("TEST\\MISSING",150).Status=="skipped","Missing scale target not skipped");Check(HdrControl.ApplyDynamic("TEST\\MISSING",true).Status=="skipped","Missing DRR target not skipped");
  Console.WriteLine("Sensors: leave="+SensorCapabilities.HasPresence(0)+", gaze="+SensorCapabilities.HasPresence(1)+", wake="+SensorCapabilities.HasPresence(2)+", onlooker="+SensorCapabilities.HasPresence(3)+", ambient="+SensorCapabilities.HasAmbientLight());
  Console.WriteLine("PASS: native actions and restoration; missing hardware skips. Resolution/scale tests use current values; DRR activation is not tested on unavailable hardware.");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
