using System;
using System.Collections.Generic;
using System.Reflection;

// Compile the production Runner with controlled action adapters. No native
// settings API is linked into this test executable.
static class MultiDisplayRunnerTests
{
    static int checks;
    static void Assert(bool value,string message) { checks++;if(!value)throw new InvalidOperationException(message); }
    static readonly MethodInfo apply=typeof(Runner).GetMethod("ApplyDisplay",BindingFlags.Static|BindingFlags.NonPublic);
    static RunReport Run(ModeConfiguration configuration)
    {
        configuration.Validate();RunReport report=new RunReport();FakeActions.Calls.Clear();
        foreach(DisplayModeConfiguration display in configuration.GetDisplayActions())apply.Invoke(null,new object[]{report,display});
        return report;
    }
    static DisplayModeConfiguration All(string id)
    {
        return new DisplayModeConfiguration { DisplayId=id,RefreshRate=60,DisplayWidth=1920,DisplayHeight=1080,Hdr=1,ContentBrightness=3,DisplayScale=150,DynamicRefresh=2,Brightness=37 };
    }
    static int Main()
    {
        try {
            ModeConfiguration configuration=new ModeConfiguration { Displays=new[]{All("screen1"),All("screen2")} };
            RunReport report=Run(configuration);
            Assert(report.Steps.Count==12,"Each monitor must receive all six selected actions.");
            Assert(String.Join(",",FakeActions.Calls.ToArray())=="mode:screen1,hdr:screen1,content:screen1,scale:screen1,dynamic:screen1,brightness:screen1,mode:screen2,hdr:screen2,content:screen2,scale:screen2,dynamic:screen2,brightness:screen2","The supplied monitor/action order must be preserved.");
            Assert(report.Steps.TrueForAll(delegate(StepResult result){return result.Status=="ok";}),"All successful adapters must report success.");
            FakeActions.ThrowOn="hdr:screen1";
            report=Run(configuration);
            Assert(report.Steps.Count==12&&report.Steps[1].Status=="error","One throwing adapter must become an individual failed step.");
            Assert(FakeActions.Calls.Contains("brightness:screen1")&&FakeActions.Calls.Contains("brightness:screen2"),"A failure must not cancel later settings or the second monitor.");
            FakeActions.ThrowOn="brightness:screen1";report=Run(configuration);
            Assert(report.Steps.Count==12&&report.Steps[5].Status=="error"&&report.Steps[11].Status=="ok","A throwing brightness adapter must not cancel another monitor.");
            FakeActions.ThrowOn=null;FakeActions.Missing="screen1";report=Run(configuration);
            Assert(report.Steps.Count==12&&report.Steps.GetRange(0,6).TrueForAll(delegate(StepResult value){return value.Status=="skipped";}),"Disconnected monitors must be skipped action by action.");
            Assert(report.Steps.GetRange(6,6).TrueForAll(delegate(StepResult value){return value.Status=="ok";}),"A disconnected first monitor must not prevent the connected second monitor.");
            FakeActions.Missing=null;
            report=Run(new ModeConfiguration { Displays=new[]{new DisplayModeConfiguration { DisplayId="unchanged" },new DisplayModeConfiguration { DisplayId="brightnessOnly",Brightness=0 }} });
            Assert(report.Steps.Count==1&&FakeActions.Calls[0]=="brightness:brightnessOnly","Unselected actions must not run; zero percent brightness is selected.");
            report=Run(new ModeConfiguration { DisplayId="oldProfile",RefreshRate=144,Brightness=100 });
            Assert(report.Steps.Count==1&&FakeActions.Calls[0]=="mode:oldProfile","Old single-display mode settings must remain runnable without duplicating global brightness.");
            Console.WriteLine("PASS: "+checks+" production runner isolation/order checks with controlled adapters; no system settings changed.");
            return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
}

static class FakeActions
{
    public static string ThrowOn,Missing;
    public static readonly List<string> Calls=new List<string>();
    public static DisplayChangeResult Apply(string action,string id)
    {
        string name=action+":"+id;Calls.Add(name);
        if(name==ThrowOn)throw new InvalidOperationException("Controlled adapter failure.");
        return new DisplayChangeResult(id==Missing?"skipped":"ok",id);
    }
    public static StepResult Ok() { return new StepResult("unused","ok",null); }
    public static List<StepResult> Empty() { return new List<StepResult>(); }
}
public sealed class DisplayChangeResult
{
    public string Status,Detail;
    public DisplayChangeResult(string status,string detail){Status=status;Detail=detail;}
}
public static class DisplayControl { public static DisplayChangeResult Apply(string id,uint rate,uint width,uint height){return FakeActions.Apply("mode",id);} }
public static class HdrControl
{
    public static DisplayChangeResult Apply(string id,bool enabled){return FakeActions.Apply("hdr",id);}
    public static DisplayChangeResult ApplyContentBrightness(string id,int mode){return FakeActions.Apply("content",id);}
    public static DisplayChangeResult ApplyScale(string id,int scale){return FakeActions.Apply("scale",id);}
    public static DisplayChangeResult ApplyDynamic(string id,bool enabled){return FakeActions.Apply("dynamic",id);}
}
public static class DisplayBrightnessControl
{
    public static StepResult Apply(string id,int value){DisplayChangeResult result=FakeActions.Apply("brightness",id);return new StepResult("DisplayBrightness",result.Status,result.Detail);}
}
public sealed class WindowsState { }
public sealed class FanState { }
public sealed class ModeSnapshot { public WindowsState Windows;public FanState Fan; }
public sealed class RunReport
{
    public string Mode,StartedUtc,CompletedUtc;public int ProcessId;public ModeSnapshot State;
    public List<StepResult> Steps=new List<StepResult>();
}
public static class WindowsControl
{
    public static List<StepResult> Restore(WindowsState state){return FakeActions.Empty();}
    public static Guid ReadPowerMode(bool ac){return Guid.Empty;}
    public static StepResult SetPowerMode(bool ac,Guid mode){return FakeActions.Ok();}
    public static List<StepResult> SetEnergySaver(bool value){return FakeActions.Empty();}
    public static List<StepResult> SetBrightness(int value){throw new InvalidOperationException("Global brightness is forbidden in per-display tests.");}
    public static WindowsState ReadState(){return new WindowsState();}
}
public static class FanControl
{
    public static List<StepResult> Restore(FanState state){return FakeActions.Empty();}
    public static StepResult SetProfile(int mode,bool skip){return FakeActions.Ok();}
    public static FanState ReadState(){return new FanState();}
}
public static class TaskbarControl
{
    public static StepResult SetPreference(bool battery,bool enabled){return FakeActions.Ok();}
    public static StepResult Apply(int mode){return FakeActions.Ok();}
}
public static class WallpaperControl
{
    public static StepResult SetApplication(bool value){return FakeActions.Ok();}
    public static StepResult SetAudio(bool value){return FakeActions.Ok();}
    public static StepResult SetPlayback(bool value){return FakeActions.Ok();}
}
public static class BatteryCareControl { public static StepResult Set(bool value){return FakeActions.Ok();} }
public static class AdditionalControl { public static List<StepResult> Apply(ModeConfiguration configuration,bool allow){return FakeActions.Empty();} }
public static class NightLightControl { public static StepResult Apply(bool value,int strength){return FakeActions.Ok();} }
