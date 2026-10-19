using System;
using System.Collections.Generic;
using System.Reflection;
using System.Web.Script.Serialization;

// Uses synthetic capabilities only. No native settings API or exported EXE is run.
static class AdaptivePresetTests
{
    static int checks;
    static void Assert(bool value,string message) { checks++;if(!value)throw new InvalidOperationException(message); }
    static DisplayResolution Resolution(uint width,uint height,params uint[] rates)
    {
        return new DisplayResolution { Width=width,Height=height,Rates=new List<uint>(rates) };
    }
    static PresetDisplayCapabilities Display(int number,uint recommendedWidth,uint recommendedHeight,uint width,uint height,bool hdr,bool dynamic,int[] scales,params DisplayResolution[] modes)
    {
        DisplayInfo display=new DisplayInfo { MonitorId="synthetic-screen-"+number,DeviceName=@"\\.\DISPLAY"+number,Label="Экран "+number,Width=width,Height=height,CurrentRate=60,RecommendedWidth=recommendedWidth,RecommendedHeight=recommendedHeight,Resolutions=new List<DisplayResolution>(modes),Rates=new List<uint>() };
        foreach(DisplayResolution mode in modes) { mode.IsRecommended=mode.Width==recommendedWidth&&mode.Height==recommendedHeight;if(mode.Width==width&&mode.Height==height)display.Rates=new List<uint>(mode.Rates); }
        return new PresetDisplayCapabilities { Display=display,HdrSupported=hdr,DynamicRefreshSupported=dynamic,Scales=new List<int>(scales) };
    }
    static PresetCapabilities Complete()
    {
        return new PresetCapabilities { EnergySaver=true,PowerMode=true,DarkTheme=true,TaskbarRepair=true,FanProfile=true,WallpaperPlayback=true,WallpaperAudio=true,WallpaperApplication=true };
    }
    static DisplayModeConfiguration Find(ModeConfiguration configuration,string id)
    {
        foreach(DisplayModeConfiguration display in configuration.GetDisplayActions())if(display.DisplayId==id)return display;
        throw new InvalidOperationException("Missing preset target: "+id);
    }
    static void Unrequested(ModeConfiguration configuration,string scenario)
    {
        foreach(string field in new[]{"BatteryCare","SleepTimeout","ScreenTimeout","PresenceSleep","PresenceWake","PresenceDim","PresenceOnlooker","AdaptiveBrightness","ScreenSaver","UsbPower","NightLight","BatteryPercentage","ClockSeconds","Hdr","DynamicRefresh","DisplayScale","ContentBrightness"})
            Assert((int)typeof(ModeConfiguration).GetField(field).GetValue(configuration)==0,scenario+" adds unrelated "+field);
        Assert(configuration.Brightness==-1&&configuration.RefreshRate==0&&configuration.DisplayWidth==0&&configuration.DisplayHeight==0&&String.IsNullOrEmpty(configuration.DisplayId),scenario+" uses legacy/global display actions");
        foreach(DisplayModeConfiguration display in configuration.GetDisplayActions())Assert(display.Brightness==-1&&display.ContentBrightness==0,scenario+" changes brightness/CABC");
    }
    static bool Same(ModeConfiguration first,ModeConfiguration second)
    {
        foreach(FieldInfo field in typeof(ModeConfiguration).GetFields())if(field.Name!="Displays"&&!Object.Equals(field.GetValue(first),field.GetValue(second)))return false;
        if(first.Displays.Length!=second.Displays.Length)return false;
        for(int i=0;i<first.Displays.Length;i++)foreach(FieldInfo field in typeof(DisplayModeConfiguration).GetFields())if(!Object.Equals(field.GetValue(first.Displays[i]),field.GetValue(second.Displays[i])))return false;
        return true;
    }
    static void MainPresets()
    {
        PresetCapabilities capabilities=Complete();
        // Deliberately use current resolutions of a different aspect ratio and an unrelated global maximum Hz.
        capabilities.Displays.Add(Display(10,1920,1200,1920,1200,true,true,new[]{100,200},Resolution(1920,1200,59,120)));
        capabilities.Displays.Add(Display(2,3840,2160,1920,1200,false,false,new[]{100,150},Resolution(3840,2160,60,120),Resolution(1920,1080,60,165),Resolution(1920,1200,60,240)));
        capabilities.Displays.Add(Display(1,2880,1800,1920,1080,true,true,new[]{100,150,200},Resolution(2880,1800,60,144),Resolution(1920,1200,60,120),Resolution(1920,1080,60,240)));
        capabilities.Displays.Add(Display(3,2560,1440,2560,1440,false,false,new int[0],Resolution(2560,1440,75,165)));
        JavaScriptSerializer serializer=new JavaScriptSerializer();string before=serializer.Serialize(capabilities);
        foreach(bool performance in new[]{false,true}) {
            ModeConfiguration configuration=PresetBuilder.Build(performance,capabilities);configuration.Validate();
            Assert(configuration.EnergySaver==(performance?2:1),"Wrong energy-saver direction");
            Assert(configuration.PowerMode==(performance?3:1),"Wrong Windows power mode");
            Assert(configuration.DarkTheme==1&&configuration.TaskbarRepair==2,"Shared dark theme/taskbar repair missing");
            Assert(configuration.FanProfile==(performance?2:1),"Wrong ASUS fan mode");
            Assert(configuration.WallpaperPlayback==(performance?1:2)&&configuration.WallpaperAudio==(performance?1:2),"Wrong wallpaper playback/audio direction");
            Assert(configuration.WallpaperApplication==(performance?1:0),"Wallpaper launch belongs only to performance");
            Assert(configuration.GetDisplayActions().Length==(performance?4:3),"Configure every capable monitor; omit a monitor whose requested actions are all unavailable");
            string[] ordered=performance?new[]{"synthetic-screen-1","synthetic-screen-2","synthetic-screen-3","synthetic-screen-10"}:new[]{"synthetic-screen-1","synthetic-screen-2","synthetic-screen-10"};
            for(int i=0;i<ordered.Length;i++)Assert(configuration.Displays[i].DisplayId==ordered[i],"Displays must be ordered numerically, independent of capability input order");
            DisplayModeConfiguration one=Find(configuration,"synthetic-screen-1"),two=Find(configuration,"synthetic-screen-2"),three=PresetBuilder.BuildDisplay(performance,capabilities.Displays[3]),ten=Find(configuration,"synthetic-screen-10");
            Assert(one.DisplayWidth==(performance?2880U:1920U)&&one.DisplayHeight==(performance?1800U:1200U),"Native 16:10 aspect must win over current 16:9 mode");
            Assert(two.DisplayWidth==(performance?3840U:1920U)&&two.DisplayHeight==(performance?2160U:1080U),"Native 16:9 aspect must win over current 16:10 mode");
            Assert(one.RefreshRate==(performance?144U:60U),"Hz must be selected from the chosen resolution, not global 240 Hz");
            Assert(two.RefreshRate==(performance?120U:60U),"Second display must use its own resolution/refresh pair");
            Assert(three.DisplayWidth==(performance?2560U:0U)&&three.DisplayHeight==(performance?1440U:0U),"Missing 1920x1080 must not be invented");
            Assert(three.RefreshRate==(performance?165U:0U),"Unavailable 60 Hz must not be invented from a different mode");
            Assert(ten.DisplayWidth==1920&&ten.DisplayHeight==1200&&ten.RefreshRate==(performance?120U:0U),"An available autonomy resolution must not imply unsupported 60 Hz");
            Assert(one.Hdr==(performance?1:2)&&ten.Hdr==(performance?1:2),"Supported HDR action missing");
            Assert(two.Hdr==0&&three.Hdr==0,"Unsupported HDR must be omitted");
            Assert(one.DynamicRefresh==(performance?1:2)&&ten.DynamicRefresh==(performance?1:2),"Supported DRR action missing");
            Assert(two.DynamicRefresh==0&&three.DynamicRefresh==0,"Unsupported DRR must be omitted");
            Assert(one.DisplayScale==(performance?200:0)&&ten.DisplayScale==(performance?200:0)&&two.DisplayScale==0&&three.DisplayScale==0,"200% scale must only be selected when Windows offers it");
            Unrequested(configuration,performance?"Performance":"Autonomy");
            Assert(serializer.Serialize(capabilities)==before,"Building a preset mutated capability data");
            Assert(Same(configuration,PresetBuilder.Build(performance,capabilities)),"Repeated builds must be deterministic");
            configuration.Displays[0].Brightness=12;
            Assert(PresetBuilder.Build(performance,capabilities).Displays[0].Brightness==-1,"Returned configurations must not share mutable display actions");
        }
    }
    static void MissingFeatures()
    {
        PresetCapabilities absent=new PresetCapabilities();
        foreach(bool performance in new[]{false,true}) {
            ModeConfiguration empty=PresetBuilder.Build(performance,absent);empty.Validate();
            Assert(!empty.HasActions&&empty.GetDisplayActions().Length==0,"A machine with no available features must remain empty");
            Unrequested(empty,"Unavailable capabilities");
        }
        foreach(string feature in new[]{"EnergySaver","PowerMode","DarkTheme","TaskbarRepair","FanProfile","WallpaperPlayback","WallpaperAudio","WallpaperApplication"}) {
            PresetCapabilities available=new PresetCapabilities();typeof(PresetCapabilities).GetField(feature).SetValue(available,true);
            foreach(bool performance in new[]{false,true}) {
                ModeConfiguration configuration=PresetBuilder.Build(performance,available);configuration.Validate();
                foreach(string action in new[]{"EnergySaver","PowerMode","DarkTheme","TaskbarRepair","FanProfile","WallpaperPlayback","WallpaperAudio","WallpaperApplication"}) {
                    int expected=action!=feature?0:action=="EnergySaver"?(performance?2:1):action=="PowerMode"?(performance?3:1):action=="TaskbarRepair"?2:action=="FanProfile"?(performance?2:1):action=="WallpaperPlayback"||action=="WallpaperAudio"?(performance?1:2):action=="WallpaperApplication"?(performance?1:0):1;
                    Assert((int)typeof(ModeConfiguration).GetField(action).GetValue(configuration)==expected,"Capability gating failed: "+feature+" / "+action);
                }
                Unrequested(configuration,"Single capability "+feature);
            }
        }
        PresetDisplayCapabilities none=Display(4,0,0,2560,1440,false,false,new int[0]);
        foreach(bool performance in new[]{false,true}) {
            DisplayModeConfiguration display=PresetBuilder.BuildDisplay(performance,none);
            Assert(display!=null&&!display.HasActions,"No supported display features must not produce actions");
        }
        PresetDisplayCapabilities missingPreferred=Display(5,0,0,2560,1440,false,false,new int[0],Resolution(2560,1440,60,144),Resolution(1920,1080,60,120));
        DisplayModeConfiguration noRecommendation=PresetBuilder.BuildDisplay(true,missingPreferred);
        Assert(noRecommendation.DisplayWidth==0&&noRecommendation.DisplayHeight==0,"Current resolution must never masquerade as a Windows recommendation");
        Assert(noRecommendation.RefreshRate==144,"Without a recommendation, use the maximum valid Hz for the retained current resolution");
        PresetDisplayCapabilities missingPair=Display(6,2880,1800,1920,1080,false,false,new int[0],Resolution(1920,1080,60,165));
        DisplayModeConfiguration invalidRecommendation=PresetBuilder.BuildDisplay(true,missingPair);
        Assert(invalidRecommendation.DisplayWidth==0&&invalidRecommendation.DisplayHeight==0&&invalidRecommendation.RefreshRate==165,"Unavailable recommended mode must be skipped while retaining a valid current-resolution Hz action");
        Assert(PresetBuilder.BuildDisplay(false,new PresetDisplayCapabilities())==null,"Missing display identity must be ignored");
    }
    static void ManyDisplays()
    {
        PresetCapabilities capabilities=new PresetCapabilities();
        for(int i=1;i<=32;i++)capabilities.Displays.Add(Display(i,1920,1080,1920,1080,false,false,new int[0],Resolution(1920,1080,60,(uint)(60+i))));
        foreach(bool performance in new[]{false,true}) {
            ModeConfiguration configuration=PresetBuilder.Build(performance,capabilities);configuration.Validate();
            Assert(configuration.GetDisplayActions().Length==32,"All supported monitor slots must be covered");
            for(int i=1;i<=32;i++) {
                DisplayModeConfiguration target=Find(configuration,"synthetic-screen-"+i);
                Assert(target.DisplayWidth==1920&&target.DisplayHeight==1080&&target.RefreshRate==(performance?(uint)(60+i):60U),"A monitor after the first two was skipped or given another monitor's Hz");
            }
        }
    }
    static int Run()
    {
        MainPresets();MissingFeatures();ManyDisplays();
        Console.WriteLine("PASS: "+checks+" synthetic adaptive-preset checks; no settings changed and no exported EXE executed.");return 0;
    }
    static int Main(string[] args)
    {
        try {
            if(args.Length!=1)throw new ArgumentException("Pass the full builder path.");
            string builder=System.IO.Path.GetFullPath(args[0]);
            AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e) { return Assembly.LoadFrom(builder); };
            Assembly.LoadFrom(builder);return Run();
        } catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
}
