using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

public sealed class ModeConfiguration
{
    public int Version = 2;
    public int EnergySaver; // 0 unchanged, 1 on, 2 off
    public int FanProfile;  // 0 unchanged, 1 Whisper, 2 Performance
    public int PowerMode;   // 0 unchanged, 1 efficiency, 2 balanced, 3 performance
    public int Brightness = -1; // -1 unchanged, 0..100 percent
    public int DarkTheme, SleepTimeout, ScreenTimeout, Hdr, PresenceSleep, PresenceDim;
    public int ContentBrightness, AdaptiveBrightness, ScreenSaver, UsbPower; // 0 unchanged, 1 on, 2 off
    public string DisplayId;
    public uint RefreshRate;
    public bool HasActions { get { return EnergySaver != 0 || FanProfile != 0 || PowerMode != 0 || Brightness >= 0 || RefreshRate != 0 || DarkTheme != 0 || SleepTimeout != 0 || ScreenTimeout != 0 || Hdr != 0 || PresenceSleep != 0 || PresenceDim != 0 || ContentBrightness != 0 || AdaptiveBrightness != 0 || ScreenSaver != 0 || UsbPower != 0; } }
    public void Validate()
    {
        if ((Version != 1 && Version != 2) || EnergySaver < 0 || EnergySaver > 2 || FanProfile < 0 || FanProfile > 2 || PowerMode < 0 || PowerMode > 3 || Brightness < -1 || Brightness > 100)
            throw new InvalidDataException("Invalid mode configuration.");
        foreach(int value in new[]{DarkTheme,SleepTimeout,ScreenTimeout,Hdr,PresenceSleep,PresenceDim,ContentBrightness,AdaptiveBrightness,ScreenSaver,UsbPower})
            if(value<0||value>2)throw new InvalidDataException("Invalid additional action.");
        if(RefreshRate!=0&&(RefreshRate<2||String.IsNullOrEmpty(DisplayId)||DisplayId.Length>1024))throw new InvalidDataException("Invalid display configuration.");
        if((Hdr!=0||ContentBrightness!=0)&&(String.IsNullOrEmpty(DisplayId)||DisplayId.Length>1024))throw new InvalidDataException("Missing display-feature target.");
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
            if(size==0||size>4096) throw new InvalidDataException("Invalid configuration size.");
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
