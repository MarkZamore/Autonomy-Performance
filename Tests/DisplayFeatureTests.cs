using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

// Explicit development test. Never called by the constructor or an exported mode.
static class DisplayFeatureTests
{
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
    static readonly List<object> results=new List<object>();
    static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static void CheckBrightness(List<DisplayBrightnessState> expected)
    {
        WindowsState observed=WindowsControl.ReadState();
        foreach(DisplayBrightnessState before in expected){DisplayBrightnessState after=observed.Brightness.Find(delegate(DisplayBrightnessState d){return d.InstanceName==before.InstanceName;});Assert(after!=null&&after.CurrentBrightness==before.CurrentBrightness,"Original panel brightness did not restore.");}
    }
    static void RestoreBrightness(List<DisplayBrightnessState> saved)
    {
        MethodInfo method=typeof(WindowsControl).GetMethod("SetBrightness",BindingFlags.NonPublic|BindingFlags.Static,null,new[]{typeof(List<DisplayBrightnessState>),typeof(byte)},null);
        foreach(StepResult step in (List<StepResult>)method.Invoke(null,new object[]{saved,(byte)100}))Assert(step.Status!="error",step.Detail);
        CheckBrightness(saved);
    }
    [STAThread] static int Main(string[] args)
    {
        WindowsState original=null;List<DisplayInfo> screens=null;Exception failure=null;
        try{
            Assert(args.Length==1,"An explicit report destination is required.");
            SetThreadExecutionState(0x80000003);
            original=WindowsControl.ReadState();screens=DisplayControl.Enumerate();
            if(original.Brightness.Count>0){
                try{foreach(StepResult step in WindowsControl.SetBrightness(37))Assert(step.Status!="error",step.Detail);foreach(DisplayBrightnessState value in WindowsControl.ReadState().Brightness)Assert(value.CurrentBrightness==37,"Requested 37% brightness did not verify.");results.Add(new{Name="Brightness37",Status="ok"});}
                finally{RestoreBrightness(original.Brightness);}
            }
            if(screens.Count>0){
                string id=screens[0].MonitorId;int cabc;
                bool cabcReadable=HdrControl.TryInspectContentBrightness(id,out cabc)&&cabc>=0&&cabc<=1;
                if(cabcReadable){
                    try{
                        DisplayChangeResult applied=HdrControl.ApplyContentBrightness(id,cabc==0);
                        results.Add(new{Name="ContentBrightness",Status=applied.Status,Detail=applied.Detail});
                        Assert(applied.Status!="error",applied.Detail);
                        if(applied.Status=="ok"){int after;Assert(HdrControl.TryInspectContentBrightness(id,out after)&&after==(cabc==0?1:0),"Content brightness did not change.");}
                    }finally{
                        DisplayChangeResult restored=HdrControl.ApplyContentBrightness(id,cabc!=0);Assert(restored.Status=="ok","CABC restore failed: "+restored.Detail);
                        int after;Assert(HdrControl.TryInspectContentBrightness(id,out after)&&after==cabc,"Original CABC did not restore.");
                    }
                }else results.Add(new{Name="ContentBrightness",Status="skipped",Detail="No supported reversible 0/1 target."});
                bool supported,hdr;
                if(HdrControl.TryInspect(id,out supported,out hdr)&&supported){
                    try{
                        DisplayChangeResult applied=HdrControl.Apply(id,!hdr);
                        results.Add(new{Name="Hdr",Status=applied.Status,Detail=applied.Detail});
                        Assert(applied.Status!="error",applied.Detail);
                        if(applied.Status=="ok"){bool afterSupport,after;Assert(HdrControl.TryInspect(id,out afterSupport,out after)&&after!=hdr,"HDR did not change.");}
                    }finally{
                        DisplayChangeResult restored=HdrControl.Apply(id,hdr);Assert(restored.Status=="ok","HDR restore failed: "+restored.Detail);
                        bool afterSupport,after;Assert(HdrControl.TryInspect(id,out afterSupport,out after)&&after==hdr,"Original HDR did not restore.");
                    }
                }else results.Add(new{Name="Hdr",Status="skipped",Detail="HDR is unavailable."});
                if(cabcReadable){int after;Assert(HdrControl.TryInspectContentBrightness(id,out after),"CABC unavailable after HDR restore.");if(after!=cabc)Assert(HdrControl.ApplyContentBrightness(id,cabc!=0).Status=="ok","CABC policy changed during HDR test and could not restore.");}
            }
        }catch(Exception ex){failure=ex;}
        finally{
            try{
                if(screens!=null)foreach(DisplayInfo before in screens){DisplayInfo after=DisplayControl.Enumerate().Find(delegate(DisplayInfo d){return d.MonitorId==before.MonitorId;});Assert(after!=null&&after.Width==before.Width&&after.Height==before.Height,"Display connection or resolution changed.");if(after.CurrentRate!=before.CurrentRate)Assert(DisplayControl.Apply(before.MonitorId,before.CurrentRate).Status=="ok","Original refresh rate did not restore.");}
                if(original!=null&&original.Brightness.Count>0)RestoreBrightness(original.Brightness);
            }catch(Exception ex){failure=failure??ex;}
            SetThreadExecutionState(0x80000000);
        }
        if(args.Length==1)File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{Passed=failure==null,Restored=failure==null,Results=results,Error=failure==null?null:failure.ToString()}));
        Console.WriteLine(failure==null?"PASS: brightness/HDR/content-brightness live transitions and original states restored.":failure.ToString());
        return failure==null?0:1;
    }
}
