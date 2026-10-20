using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

public sealed class DisplayModeConfiguration
{
    public string DisplayId;
    public uint DisplayWidth, DisplayHeight, RefreshRate;
    public int DisplayScale, Hdr, DynamicRefresh, ContentBrightness;
    public int Brightness = -1; // -1 unchanged, 0..100 percent on this display only
    public bool HasActions { get { return DisplayWidth != 0 || DisplayHeight != 0 || RefreshRate != 0 || DisplayScale != 0 || Hdr != 0 || DynamicRefresh != 0 || ContentBrightness != 0 || Brightness >= 0; } }
    public void Validate()
    {
        if (String.IsNullOrEmpty(DisplayId) || DisplayId.Length > 1024)
            throw new InvalidDataException("Invalid display target.");
        if (RefreshRate != 0 && RefreshRate < 2)
            throw new InvalidDataException("Invalid display refresh rate.");
        if ((DisplayWidth == 0) != (DisplayHeight == 0) || DisplayWidth > 65535 || DisplayHeight > 65535)
            throw new InvalidDataException("Invalid display resolution.");
        if (DisplayScale < 0 || DisplayScale > 500 || Hdr < 0 || Hdr > 2 || DynamicRefresh < 0 || DynamicRefresh > 2 || ContentBrightness < 0 || ContentBrightness > 3 || Brightness < -1 || Brightness > 100)
            throw new InvalidDataException("Invalid display action.");
    }
}

public sealed class ModeConfiguration
{
    public int Version = 2;
    public int EnergySaver; // 0 unchanged, 1 on, 2 off
    public int FanProfile;  // 0 unchanged, 1 Whisper, 2 Performance, 3 Standard (ASUS native 0)
    public int WallpaperPlayback; // 0 unchanged, 1 play, 2 pause
    public int WallpaperAudio; // 0 unchanged, 1 unmute, 2 mute (wallpapers only)
    public int BatteryCare; // 0 unchanged, 1 enabled, 2 disabled
    public int WallpaperApplication; // 0 unchanged, 1 start, 2 exit
    public int NightLight,DynamicRefresh,DisplayScale;
    public int BatteryPercentage,ClockSeconds;
    public int PresenceWake,PresenceOnlooker;
    public int NightLightStrength=50;
    public int PowerMode;   // 0 unchanged, 1 efficiency, 2 balanced, 3 performance
    public int Brightness = -1; // -1 unchanged, 0..100 percent
    public int DarkTheme, SleepTimeout, ScreenTimeout, Hdr, PresenceSleep, PresenceDim;
    public int ContentBrightness, AdaptiveBrightness, ScreenSaver, UsbPower; // 0 unchanged, 1 on, 2 off
    public string DisplayId;
    public uint RefreshRate;
    public uint DisplayWidth,DisplayHeight; // 0/0 unchanged
    // Older exports use the single-display fields above; new exports use this array.
    public DisplayModeConfiguration[] Displays;
    public bool HasActions { get { return EnergySaver != 0 || FanProfile != 0 || PowerMode != 0 || Brightness >= 0 || RefreshRate != 0 || DarkTheme != 0 || SleepTimeout != 0 || ScreenTimeout != 0 || Hdr != 0 || PresenceSleep != 0 || PresenceDim != 0 || ContentBrightness != 0 || AdaptiveBrightness != 0 || ScreenSaver != 0 || UsbPower != 0 || WallpaperPlayback != 0 || WallpaperAudio != 0 || BatteryCare != 0 || DisplayWidth != 0 || WallpaperApplication != 0 || NightLight != 0 || DynamicRefresh != 0 || DisplayScale != 0 || BatteryPercentage != 0 || ClockSeconds != 0 || PresenceWake != 0 || PresenceOnlooker != 0 || HasDisplayArrayActions(); } }
    bool HasDisplayArrayActions()
    {
        if (Displays != null) foreach (DisplayModeConfiguration display in Displays)
            if (display != null && display.HasActions) return true;
        return false;
    }
    DisplayModeConfiguration LegacyDisplay()
    {
        return new DisplayModeConfiguration { DisplayId = DisplayId, DisplayWidth = DisplayWidth, DisplayHeight = DisplayHeight, RefreshRate = RefreshRate, DisplayScale = DisplayScale, Hdr = Hdr, DynamicRefresh = DynamicRefresh, ContentBrightness = ContentBrightness };
    }
    public DisplayModeConfiguration[] GetDisplayActions()
    {
        List<DisplayModeConfiguration> result = new List<DisplayModeConfiguration>();
        if (Displays != null) foreach (DisplayModeConfiguration display in Displays)
            if (display != null && display.HasActions) result.Add(display);
        DisplayModeConfiguration legacy = LegacyDisplay();
        if (legacy.HasActions) result.Add(legacy);
        return result.ToArray();
    }
    public void Validate()
    {
        if ((Version != 1 && Version != 2) || EnergySaver < 0 || EnergySaver > 2 || FanProfile < 0 || FanProfile > 3 || PowerMode < 0 || PowerMode > 3 || Brightness < -1 || Brightness > 100)
            throw new InvalidDataException("Invalid mode configuration.");
        foreach(int value in new[]{DarkTheme,SleepTimeout,ScreenTimeout,Hdr,PresenceSleep,PresenceDim,AdaptiveBrightness,ScreenSaver,UsbPower,WallpaperPlayback,WallpaperAudio,BatteryCare,WallpaperApplication,NightLight,DynamicRefresh,BatteryPercentage,ClockSeconds,PresenceWake,PresenceOnlooker})
            if(value<0||value>2)throw new InvalidDataException("Invalid additional action.");
        if(ContentBrightness<0||ContentBrightness>3||NightLightStrength<0||NightLightStrength>100||DisplayScale<0||DisplayScale>500)throw new InvalidDataException("Invalid display action.");
        if(RefreshRate!=0&&(RefreshRate<2||String.IsNullOrEmpty(DisplayId)||DisplayId.Length>1024))throw new InvalidDataException("Invalid display configuration.");
        if((DisplayWidth==0)!=(DisplayHeight==0)||DisplayWidth>65535||DisplayHeight>65535||(DisplayWidth!=0&&(String.IsNullOrEmpty(DisplayId)||DisplayId.Length>1024)))throw new InvalidDataException("Invalid display resolution.");
        if((Hdr!=0||ContentBrightness!=0||DynamicRefresh!=0||DisplayScale!=0)&&(String.IsNullOrEmpty(DisplayId)||DisplayId.Length>1024))throw new InvalidDataException("Missing display-feature target.");
        HashSet<string> displayIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DisplayModeConfiguration legacy = LegacyDisplay();
        if (legacy.HasActions) { legacy.Validate(); displayIds.Add(legacy.DisplayId); }
        if (Displays != null) {
            if (Displays.Length > 32) throw new InvalidDataException("Too many display targets.");
            foreach (DisplayModeConfiguration display in Displays) {
                if (display == null) throw new InvalidDataException("Empty display configuration.");
                display.Validate();
                if (!displayIds.Add(display.DisplayId)) throw new InvalidDataException("Duplicate display target.");
            }
        }
    }
    public static Guid PowerGuid(int mode)
    {
        if (mode == 1) return new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        if (mode == 2) return Guid.Empty;
        if (mode == 3) return new Guid("ded574b5-45a0-4f42-8737-46345c09c238");
        throw new ArgumentOutOfRangeException("mode");
    }
}

public static class PeConfiguration
{
    public const string ResourceName = "AP_CONFIG";
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr FindResource(IntPtr module,string name,IntPtr type);
    [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module,IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module,IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
    public static ModeConfiguration Read(string path)
    {
        IntPtr module=LoadLibraryEx(path,IntPtr.Zero,0x22);
        if(module==IntPtr.Zero) throw new IOException("Cannot read executable resources.");
        try {
            IntPtr resource=FindResource(module,ResourceName,new IntPtr(10));
            if(resource==IntPtr.Zero) throw new InvalidDataException("Mode configuration is missing.");
            uint size=SizeofResource(module,resource);
            if(size==0||size>65536) throw new InvalidDataException("Invalid configuration size.");
            byte[] data=new byte[size];Marshal.Copy(LockResource(LoadResource(module,resource)),data,0,data.Length);
            string json=Encoding.UTF8.GetString(data);JavaScriptSerializer serializer=new JavaScriptSerializer();
            ModeConfiguration c=serializer.Deserialize<ModeConfiguration>(json);
            if(c!=null&&c.Version==1){
                System.Collections.Generic.Dictionary<string,object> old=serializer.DeserializeObject(json) as System.Collections.Generic.Dictionary<string,object>;
                object value;if(old!=null&&old.TryGetValue("Brightness100",out value)&&value is bool&&(bool)value)c.Brightness=100;
                // Legacy volume actions are deliberately not accepted by the current runner.
            }
            if(c==null) throw new InvalidDataException("Empty mode configuration.");c.Validate();return c;
        } finally {FreeLibrary(module);}
    }
}
