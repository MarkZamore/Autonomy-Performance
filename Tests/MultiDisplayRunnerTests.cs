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
    static readonly MethodInfo applyConfiguration=typeof(Runner).GetMethod("ApplyConfiguration",BindingFlags.Static|BindingFlags.NonPublic);
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
    static RunReport RunConfiguration(ModeConfiguration configuration,bool skipFan)
    {
        configuration.Validate();RunReport report=new RunReport();FakeActions.Calls.Clear();
        Assert(applyConfiguration!=null,"The production configured-action entry point is missing.");
        applyConfiguration.Invoke(null,new object[]{report,configuration,skipFan});
        return report;
    }
    static ModeConfiguration Mixed(int application,int audio,int playback,int saver)
    {
        return new ModeConfiguration {
            WallpaperApplication=application,WallpaperAudio=audio,WallpaperPlayback=playback,
            BatteryPercentage=1,ClockSeconds=2,BatteryCare=1,FanProfile=2,
            AdaptiveBrightness=1,DarkTheme=1,EnergySaver=saver,
            Displays=new[]{All("screen1"),All("screen2")},Brightness=42,NightLight=2,NightLightStrength=73
        };
    }
    static string[] WallpaperCalls(int application,int audio,int playback)
    {
        List<string> expected=new List<string>();
        if(application==1)expected.Add("wallpaper:start");
        if(audio!=0)expected.Add(audio==1?"wallpaper:unmute":"wallpaper:mute");
        if(playback!=0)expected.Add(playback==1?"wallpaper:play":"wallpaper:pause");
        if(application==2)expected.Add("wallpaper:exit");
        return expected.ToArray();
    }
    static void AssertSequence(IList<string> actual,IList<string> expected,string message)
    {
        Assert(actual.Count==expected.Count,message+" (call count: "+String.Join(",",new List<string>(actual).ToArray())+")");
        for(int i=0;i<expected.Count;i++)Assert(actual[i]==expected[i],message+" (index "+i+": "+actual[i]+" != "+expected[i]+")");
    }
    static void ConfiguredActionOrder()
    {
        string[] other={"taskbar:battery:True","taskbar:seconds:False","batteryCare:True",
            "readPower:ac","readPower:dc","fan:2:False","additional:True","setPower:ac","setPower:dc",
            "energy:False","mode:screen1","hdr:screen1","content:screen1","scale:screen1","dynamic:screen1","brightness:screen1",
            "mode:screen2","hdr:screen2","content:screen2","scale:screen2","dynamic:screen2","brightness:screen2",
            "brightnessGlobal:42","night:False:73"};
        for(int application=0;application<3;application++)for(int audio=0;audio<3;audio++)for(int playback=0;playback<3;playback++) {
            FakeActions.Reset();RunReport report=RunConfiguration(Mixed(application,audio,playback,2),false);
            List<string> expected=new List<string>(WallpaperCalls(application,audio,playback));expected.AddRange(other);
            AssertSequence(FakeActions.Calls,expected,"Wallpaper actions must finish before every other action/read for "+application+"/"+audio+"/"+playback);
            string[] wallpaper=WallpaperCalls(application,audio,playback);
            Assert(report.Steps.Count==wallpaper.Length+22,"Selected actions must each retain their report entry.");
            for(int i=0;i<wallpaper.Length;i++)Assert(report.Steps[i].Name.StartsWith("Wallpaper",StringComparison.Ordinal),"Report order must match execution order.");
            Assert(report.Steps.TrueForAll(delegate(StepResult result){return result.Status=="ok";}),"Successful Wallpaper adapters must not alter other results.");
        }
        FakeActions.Reset();RunReport empty=RunConfiguration(new ModeConfiguration(),false);
        Assert(empty.Steps.Count==0,"An empty configuration must not execute selected settings.");
        AssertSequence(FakeActions.Calls,new[]{"additional:True"},"Unselected Wallpaper and other actions must not invoke adapters.");

        // The adapters return skipped exactly as an absent application does. A
        // skipped launch/IPC must neither delay reordering nor cancel settings.
        FakeActions.Reset();FakeActions.WallpaperAvailable=false;FakeActions.WallpaperRunning=false;
        RunReport missing=RunConfiguration(Mixed(1,2,2,1),false);
        AssertSequence(FakeActions.Calls.GetRange(0,3),new[]{"wallpaper:start","wallpaper:mute","wallpaper:pause"},"Absent Wallpaper Engine still owns only the selected prefix.");
        Assert(missing.Steps.GetRange(0,3).TrueForAll(delegate(StepResult result){return result.Status=="skipped";}),"Unavailable Wallpaper commands must report skipped.");
        Assert(FakeActions.Calls.Contains("mode:screen2")&&FakeActions.Calls[FakeActions.Calls.Count-1]=="energy:True","A skipped Wallpaper prefix must not cancel later monitors or energy saving.");

        FakeActions.Reset();FakeActions.WallpaperRunning=false;
        RunReport stopped=RunConfiguration(new ModeConfiguration{WallpaperAudio=2,WallpaperPlayback=2,ClockSeconds=1},false);
        AssertSequence(FakeActions.Calls,new[]{"wallpaper:mute","wallpaper:pause","taskbar:seconds:True","additional:True"},"Optional pause/mute must not implicitly launch a stopped application.");
        Assert(stopped.Steps.Count==3&&stopped.Steps[0].Status=="skipped"&&stopped.Steps[1].Status=="skipped"&&stopped.Steps[2].Status=="ok","Skipped Wallpaper commands must leave a later selected setting runnable.");

        FakeActions.Reset();RunReport skippedFan=RunConfiguration(new ModeConfiguration{WallpaperApplication=2,FanProfile=3,PowerMode=3},true);
        AssertSequence(FakeActions.Calls,new[]{"wallpaper:exit","fan:0:True","additional:True","setPower:ac","setPower:dc"},"Explicit power mode and standard/skip-fan mapping must remain after Wallpaper exit.");
        Assert(skippedFan.Steps[0].Name=="WallpaperApplication","A selected exit must be the first report step.");
        FakeActions.Reset();
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
            ConfiguredActionOrder();
            Console.WriteLine("PASS: "+checks+" production runner isolation/order checks with controlled adapters; no system settings changed.");
            return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
}

static class FakeActions
{
    public static string ThrowOn,Missing;
    public static bool WallpaperAvailable=true,WallpaperRunning=true;
    public static readonly List<string> Calls=new List<string>();
    public static void Reset(){Calls.Clear();ThrowOn=null;Missing=null;WallpaperAvailable=true;WallpaperRunning=true;}
    public static DisplayChangeResult Apply(string action,string id)
    {
        string name=action+":"+id;Calls.Add(name);
        if(name==ThrowOn)throw new InvalidOperationException("Controlled adapter failure.");
        return new DisplayChangeResult(id==Missing?"skipped":"ok",id);
    }
    public static StepResult Ok() { return new StepResult("unused","ok",null); }
    public static StepResult Step(string call,string name){Calls.Add(call);return new StepResult(name,"ok",null);}
    public static StepResult Wallpaper(string call,string name){Calls.Add("wallpaper:"+call);return new StepResult(name,WallpaperRunning?"ok":"skipped",null);}
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
    public static Guid ReadPowerMode(bool ac){FakeActions.Calls.Add("readPower:"+(ac?"ac":"dc"));return Guid.Empty;}
    public static StepResult SetPowerMode(bool ac,Guid mode){return FakeActions.Step("setPower:"+(ac?"ac":"dc"),"PowerMode");}
    public static List<StepResult> SetEnergySaver(bool value){return new List<StepResult>{FakeActions.Step("energy:"+value,"EnergySaver")};}
    public static List<StepResult> SetBrightness(int value){return new List<StepResult>{FakeActions.Step("brightnessGlobal:"+value,"Brightness")};}
    public static WindowsState ReadState(){return new WindowsState();}
}
public static class FanControl
{
    public static List<StepResult> Restore(FanState state){return FakeActions.Empty();}
    public static StepResult SetProfile(int mode,bool skip){return FakeActions.Step("fan:"+mode+":"+skip,"FanProfile");}
    public static FanState ReadState(){return new FanState();}
}
public static class TaskbarControl
{
    public static StepResult SetPreference(bool battery,bool enabled){return FakeActions.Step("taskbar:"+(battery?"battery":"seconds")+":"+enabled,battery?"BatteryPercentage":"ClockSeconds");}
    public static StepResult Apply(int mode){return FakeActions.Ok();}
}
public static class WallpaperControl
{
    public static StepResult SetApplication(bool value){
        if(value)FakeActions.WallpaperRunning=FakeActions.WallpaperAvailable;
        StepResult result=FakeActions.Wallpaper(value?"start":"exit","WallpaperApplication");
        if(!value)FakeActions.WallpaperRunning=false;
        return result;
    }
    public static StepResult SetAudio(bool value){return FakeActions.Wallpaper(value?"unmute":"mute","WallpaperAudio");}
    public static StepResult SetPlayback(bool value){return FakeActions.Wallpaper(value?"play":"pause","WallpaperPlayback");}
}
public static class BatteryCareControl { public static StepResult Set(bool value){return FakeActions.Step("batteryCare:"+value,"BatteryCare");} }
public static class AdditionalControl { public static List<StepResult> Apply(ModeConfiguration configuration,bool allow){FakeActions.Calls.Add("additional:"+allow);return configuration.DarkTheme!=0?new List<StepResult>{new StepResult("DarkTheme","ok",null)}:FakeActions.Empty();} }
public static class NightLightControl { public static StepResult Apply(bool value,int strength){return FakeActions.Step("night:"+value+":"+strength,"NightLight");} }
