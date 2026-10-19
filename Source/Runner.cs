using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Web.Script.Serialization;

internal static class Runner
{
    [STAThread] static int Main(string[] args)
    {
        string reportPath=null,restorePath=null;bool status=false,skipFan=false;
        RunReport report=new RunReport();report.StartedUtc=DateTime.UtcNow.ToString("o");report.ProcessId=System.Diagnostics.Process.GetCurrentProcess().Id;
        Mutex gate=null;bool acquired=false;Timer deadline=null;
        try {
            for(int i=0;i<args.Length;i++) {
                if(args[i]=="--report"&&i+1<args.Length) reportPath=Path.GetFullPath(args[++i]);
                else if(args[i]=="--status") status=true;
                else if(args[i]=="--restore"&&i+1<args.Length) restorePath=Path.GetFullPath(args[++i]);
                else if(args[i]=="--skip-fan") skipFan=true;
                else return 1;
            }
            gate=new Mutex(false,"Local\\AutonomyPerformance."+WindowsIdentity.GetCurrent().User.Value);
            try{acquired=gate.WaitOne(10000);}catch(AbandonedMutexException){acquired=true;}
            if(!acquired)return 2;
            deadline=new Timer(delegate{Environment.Exit(3);},null,20000,Timeout.Infinite);
            if(restorePath!=null){
                report.Mode="restore";
                RunReport saved=new JavaScriptSerializer().Deserialize<RunReport>(File.ReadAllText(restorePath));
                if(saved==null||saved.State==null)throw new InvalidDataException("Invalid saved state.");
                report.Steps.AddRange(FanControl.Restore(saved.State.Fan));
                report.Steps.AddRange(WindowsControl.Restore(saved.State.Windows));
            }else if(!status){
                report.Mode="configured";
                ModeConfiguration c=PeConfiguration.Read(Assembly.GetExecutingAssembly().Location);
                if(c.BatteryPercentage!=0)report.Steps.Add(TaskbarControl.SetPreference(true,c.BatteryPercentage==1));
                if(c.ClockSeconds!=0)report.Steps.Add(TaskbarControl.SetPreference(false,c.ClockSeconds==1));
                if(c.TaskbarRepair!=0)report.Steps.Add(TaskbarControl.Apply(c.TaskbarRepair));
                if(c.WallpaperApplication==1)report.Steps.Add(WallpaperControl.SetApplication(true));
                if(c.BatteryCare!=0)report.Steps.Add(BatteryCareControl.Set(c.BatteryCare==1));
                Guid? savedAC=null,savedDC=null;
                bool preservePower=true,fanMayHaveChanged=false;
                bool additionalSchemeRefresh=c.SleepTimeout!=0||c.ScreenTimeout!=0||c.AdaptiveBrightness!=0;
                if(c.PowerMode==0&&((c.FanProfile!=0&&!skipFan)||additionalSchemeRefresh)){
                    try{savedAC=WindowsControl.ReadPowerMode(true);}catch{preservePower=false;}
                    try{savedDC=WindowsControl.ReadPowerMode(false);}catch{preservePower=false;}
                }
                if(c.FanProfile!=0) {
                    if(preservePower){
                        StepResult fanResult=FanControl.SetProfile(c.FanProfile==3?0:c.FanProfile,skipFan);
                        report.Steps.Add(fanResult);
                        fanMayHaveChanged=fanResult.Status!="skipped";
                    }
                    else report.Steps.Add(new StepResult("FanProfile","skipped","Cannot preserve unreadable unselected AC/DC power modes safely."));
                }
                report.Steps.AddRange(AdditionalControl.Apply(c,preservePower||c.PowerMode!=0));
                if(c.PowerMode!=0){
                    report.Steps.Add(WindowsControl.SetPowerMode(true,ModeConfiguration.PowerGuid(c.PowerMode)));
                    report.Steps.Add(WindowsControl.SetPowerMode(false,ModeConfiguration.PowerGuid(c.PowerMode)));
                }else if(fanMayHaveChanged||additionalSchemeRefresh){
                    if(savedAC.HasValue)report.Steps.Add(WindowsControl.SetPowerMode(true,savedAC.Value));
                    if(savedDC.HasValue)report.Steps.Add(WindowsControl.SetPowerMode(false,savedDC.Value));
                }
                // Clear a selected saver override before HDR; brightness follows
                // display reconfiguration so a driver transition cannot reset it.
                if(c.EnergySaver==2)report.Steps.AddRange(WindowsControl.SetEnergySaver(false));
                foreach(DisplayModeConfiguration display in c.GetDisplayActions()) ApplyDisplay(report,display);
                if(c.Brightness>=0)report.Steps.AddRange(WindowsControl.SetBrightness(c.Brightness));
                if(c.NightLight!=0)report.Steps.Add(NightLightControl.Apply(c.NightLight==1,c.NightLightStrength));
                if(c.EnergySaver==1)report.Steps.AddRange(WindowsControl.SetEnergySaver(true));
                // Set a selected mute state before resuming playback.
                if(c.WallpaperAudio!=0)report.Steps.Add(WallpaperControl.SetAudio(c.WallpaperAudio==1));
                if(c.WallpaperPlayback!=0)report.Steps.Add(WallpaperControl.SetPlayback(c.WallpaperPlayback==1));
                if(c.WallpaperApplication==2)report.Steps.Add(WallpaperControl.SetApplication(false));
            }else report.Mode="status";
            if(reportPath!=null)report.State=new ModeSnapshot{Windows=WindowsControl.ReadState(),Fan=FanControl.ReadState()};
            foreach(StepResult step in report.Steps)if(step.Status=="error")return 1;
            return 0;
        }catch(Exception ex){report.Steps.Add(new StepResult("Runner","error",ex.Message));return 1;}
        finally{
            try{if(reportPath!=null){report.CompletedUtc=DateTime.UtcNow.ToString("o");File.WriteAllText(reportPath,new JavaScriptSerializer().Serialize(report));}}catch{}
            try{if(deadline!=null)deadline.Dispose();}catch{}
            try{if(acquired&&gate!=null)gate.ReleaseMutex();}catch{}
            try{if(gate!=null)gate.Dispose();}catch{}
        }
    }
    static void ApplyDisplay(RunReport report,DisplayModeConfiguration display)
    {
        if(display.RefreshRate!=0||display.DisplayWidth!=0)
            AddDisplayStep(report,display.DisplayId,"DisplayRefreshRate",delegate{return DisplayControl.Apply(display.DisplayId,display.RefreshRate,display.DisplayWidth,display.DisplayHeight);});
        if(display.Hdr!=0)
            AddDisplayStep(report,display.DisplayId,"DisplayHdr",delegate{return HdrControl.Apply(display.DisplayId,display.Hdr==1);});
        if(display.ContentBrightness!=0)
            AddDisplayStep(report,display.DisplayId,"ContentBrightness",delegate{return HdrControl.ApplyContentBrightness(display.DisplayId,display.ContentBrightness);});
        if(display.DisplayScale!=0)
            AddDisplayStep(report,display.DisplayId,"DisplayScale",delegate{return HdrControl.ApplyScale(display.DisplayId,display.DisplayScale);});
        if(display.DynamicRefresh!=0)
            AddDisplayStep(report,display.DisplayId,"DynamicRefresh",delegate{return HdrControl.ApplyDynamic(display.DisplayId,display.DynamicRefresh==1);});
        if(display.Brightness>=0) {
            try { StepResult result=DisplayBrightnessControl.Apply(display.DisplayId,display.Brightness);result.Detail="["+display.DisplayId+"] "+result.Detail;report.Steps.Add(result); }
            catch(Exception ex) { report.Steps.Add(new StepResult("DisplayBrightness","error","["+display.DisplayId+"] "+ex.Message)); }
        }
    }
    static void AddDisplayStep(RunReport report,string displayId,string name,Func<DisplayChangeResult> action)
    {
        // A failed operation on one screen must not discard the remaining screens/actions.
        try { DisplayChangeResult result=action(); report.Steps.Add(new StepResult(name,result.Status,"["+displayId+"] "+result.Detail)); }
        catch(Exception ex) { report.Steps.Add(new StepResult(name,"error","["+displayId+"] "+ex.Message)); }
    }
}
