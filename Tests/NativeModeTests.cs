using System;using System.Diagnostics;using Microsoft.Win32;
class NativeModeTests{
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static void Ok(StepResult step){Console.WriteLine(step.Name+": "+step.Status+" / "+step.Detail);Check(step.Status=="ok",step.Detail);}
 static string Payload(byte[] record){for(int i=4;i<record.Length-4;i++)if(record[i]==67&&record[i+1]==66&&record[i+2]==1&&record[i+3]==0)return BitConverter.ToString(record,i,record.Length-i-3);throw new Exception("No payload");}
 static int Main(){try{
  if(NightLightControl.Available()){
   byte[][] original=NightLightControl.Capture();try{Ok(NightLightControl.Apply(true,25));Ok(NightLightControl.Apply(true,75));Ok(NightLightControl.Apply(false,50));}finally{NightLightControl.Restore(original);}
   byte[][] after=NightLightControl.Capture();Check(Payload(original[0])==Payload(after[0])&&Payload(original[1])==Payload(after[1]),"Night Light settings/schedule not restored");Console.WriteLine("Night Light payloads restored.");
  }
  int rate=BatteryCareControl.ReadRate();if(rate==100){try{for(int i=0;i<2;i++){Ok(BatteryCareControl.Set(true));Check(BatteryCareControl.ReadRate()==80,"Wrong charging limit");Ok(BatteryCareControl.Set(false));}}finally{Ok(BatteryCareControl.Set(false));}Check(BatteryCareControl.ReadRate()==rate,"Charging state not restored");}
  using(RegistryKey key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",false)){
   int mode=Convert.ToInt32(key.GetValue("MMTaskbarMode",0));if(mode==1||mode==2){int pid=Process.GetProcessesByName("explorer")[0].Id;try{foreach(int target in new[]{1,1,2,2}){Ok(TaskbarControl.Apply(target));Check(Convert.ToInt32(key.GetValue("MMTaskbarMode"))==target,"Wrong taskbar mode");}}finally{Ok(TaskbarControl.Apply(mode));}Check(pid==Process.GetProcessesByName("explorer")[0].Id,"Explorer restarted");}
   foreach(bool battery in new[]{false,true}){string name=battery?"IsBatteryPercentageEnabled":"ShowSecondsInSystemClock";object original=key.GetValue(name,null);if(!(original is int))continue;bool enabled=(int)original!=0;try{Ok(TaskbarControl.SetPreference(battery,!enabled));Check(Convert.ToInt32(key.GetValue(name))==(enabled?0:1),"Taskbar preference did not change");Ok(TaskbarControl.SetPreference(battery,enabled));}finally{Ok(TaskbarControl.SetPreference(battery,enabled));}}
  }
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
