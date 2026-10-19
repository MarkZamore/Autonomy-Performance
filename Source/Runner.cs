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
                Guid? savedAC=null,savedDC=null;
                bool preservePower=true,fanMayHaveChanged=false;
                bool additionalSchemeRefresh=c.SleepTimeout!=0||c.ScreenTimeout!=0||c.AdaptiveBrightness!=0;
                if(c.PowerMode==0&&((c.FanProfile!=0&&!skipFan)||additionalSchemeRefresh)){
                    try{savedAC=WindowsControl.ReadPowerMode(true);}catch{preservePower=false;}
                    try{savedDC=WindowsControl.ReadPowerMode(false);}catch{preservePower=false;}
                }
                if(c.FanProfile!=0) {
                    if(preservePower){
                        StepResult fanResult=FanControl.SetProfile(c.FanProfile,skipFan);
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
                if(c.RefreshRate!=0){DisplayChangeResult display=DisplayControl.Apply(c.DisplayId,c.RefreshRate);report.Steps.Add(new StepResult("DisplayRefreshRate",display.Status,display.Detail));}
                if(c.Hdr!=0){DisplayChangeResult hdr=HdrControl.Apply(c.DisplayId,c.Hdr==1);report.Steps.Add(new StepResult("DisplayHdr",hdr.Status,hdr.Detail));}
                if(c.ContentBrightness!=0){DisplayChangeResult cabc=HdrControl.ApplyContentBrightness(c.DisplayId,c.ContentBrightness==1);report.Steps.Add(new StepResult("ContentBrightness",cabc.Status,cabc.Detail));}
                if(c.Brightness>=0)report.Steps.AddRange(WindowsControl.SetBrightness(c.Brightness));
                if(c.EnergySaver==1)report.Steps.AddRange(WindowsControl.SetEnergySaver(true));
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
}
