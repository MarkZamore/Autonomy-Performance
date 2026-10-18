using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

public sealed class ModeConfiguration
{
    public int Version = 1;
    public int EnergySaver; // 0 unchanged, 1 on, 2 off
    public int FanProfile;  // 0 unchanged, 1 Whisper, 2 Performance
    public int PowerMode;   // 0 unchanged, 1 efficiency, 2 balanced, 3 performance
    public bool Brightness100;
    public bool Volume100;
    public string DisplayId;
    public uint RefreshRate;
    public bool HasActions { get { return EnergySaver != 0 || FanProfile != 0 || PowerMode != 0 || Brightness100 || Volume100 || RefreshRate != 0; } }
    public void Validate()
    {
        if (Version != 1 || EnergySaver < 0 || EnergySaver > 2 || FanProfile < 0 || FanProfile > 2 || PowerMode < 0 || PowerMode > 3)
            throw new InvalidDataException("Invalid mode configuration.");
        if(RefreshRate!=0&&(RefreshRate<2||String.IsNullOrEmpty(DisplayId)||DisplayId.Length>1024))throw new InvalidDataException("Invalid display configuration.");
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
            ModeConfiguration c=new JavaScriptSerializer().Deserialize<ModeConfiguration>(Encoding.UTF8.GetString(data));
            if(c==null) throw new InvalidDataException("Empty mode configuration.");c.Validate();return c;
        } finally {FreeLibrary(module);}
    }
}