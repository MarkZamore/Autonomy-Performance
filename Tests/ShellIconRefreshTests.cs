using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

public static class ShellIconRefreshTests
{
    static string builder;
    static int checks;
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct FileInfo {
        public IntPtr Icon; public int Index; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path,uint attributes,out FileInfo info,uint size,uint flags);
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
    static extern uint ExtractIconEx(string path,int index,out IntPtr large,out IntPtr small,uint count);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved,uint flags);
    [DllImport("ole32.dll")] static extern void CoUninitialize();
    static void Assert(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static string Fingerprint(IntPtr handle) {
        Assert(handle!=IntPtr.Zero,"Native icon missing");
        using(Icon icon=(Icon)Icon.FromHandle(handle).Clone())using(Bitmap bitmap=icon.ToBitmap())using(MemoryStream data=new MemoryStream())using(SHA256 hash=SHA256.Create()) {
            bitmap.Save(data,System.Drawing.Imaging.ImageFormat.Png);
            return Convert.ToBase64String(hash.ComputeHash(data.ToArray()));
        }
    }
    static string ShellIcon(string path,bool small) {
        FileInfo info;
        Assert(SHGetFileInfo(path,0,out info,(uint)Marshal.SizeOf(typeof(FileInfo)),0x4100u|(small?1u:0u))!=IntPtr.Zero,"Shell icon lookup failed");
        try{return Fingerprint(info.Icon);}finally{if(info.Icon!=IntPtr.Zero)DestroyIcon(info.Icon);}
    }
    static string[] EmbeddedIcons(string path) {
        IntPtr large,small;
        Assert(ExtractIconEx(path,0,out large,out small,1)>0,"Fresh EXE icon extraction failed");
        try{return new string[]{Fingerprint(large),Fingerprint(small)};}
        finally{if(large!=IntPtr.Zero)DestroyIcon(large);if(small!=IntPtr.Zero)DestroyIcon(small);}
    }
    [STAThread] public static int Main(string[] args) {
        if(args[0]=="--probe") {
            int com=CoInitializeEx(IntPtr.Zero,2);
            try{Assert(ShellIcon(args[1],false)==args[2],"Large Shell icon is stale");Assert(ShellIcon(args[1],true)==args[3],"Small Shell icon is stale");return 0;}
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
            finally{if(com>=0)CoUninitialize();}
        }
        builder=Path.GetFullPath(args[0]);
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){return new AssemblyName(e.Name).Name=="AutonomyPerformance"?Assembly.LoadFrom(builder):null;};
        int initialized=CoInitializeEx(IntPtr.Zero,2);
        try{Run(Path.GetFullPath(args[1]));Console.WriteLine("PASS: "+checks+" checks; replaced icon resources, new native Shell consumers, unchanged-icon detection and no export leftovers; no exported EXE executed.");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{if(initialized>=0)CoUninitialize();}
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static void Run(string folder) {
        Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"Режим "+Guid.NewGuid().ToString("N")+".exe");
        try {
            string[] previous=null;
            foreach(int index in new int[]{1,3,2,4,0,1}) {
                byte[] replacement=PortableResources.CreateModeExecutable(EmbeddedAssets.Read("RunnerTemplate"),System.Text.Encoding.UTF8.GetBytes("{\"DarkTheme\":1}"),EmbeddedAssets.Read("Icon"+index));
                MethodInfo needsRefresh=typeof(ExecutableExporter).Assembly.GetType("ShellIconRefresh").GetMethod("NeedsRefresh",BindingFlags.Static|BindingFlags.NonPublic);
                Assert((bool)needsRefresh.Invoke(null,new object[]{path,replacement})==(previous!=null),"Icon change detection failed before replacement");
                ExecutableExporter.Save(path,new ModeConfiguration{DarkTheme=index%2+1},EmbeddedAssets.Read("Icon"+index));
                string[] expected=EmbeddedIcons(path);
                if(previous!=null)Assert(previous[0]!=expected[0]&&previous[1]!=expected[1],"Fixture icons must differ");
                // Shell image lists are per-process. A console does not handle Shell change
                // messages like Explorer; use a fresh Shell consumer rather than its old list.
                ProcessStartInfo probe=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--probe \""+path+"\" "+expected[0]+" "+expected[1]);
                probe.UseShellExecute=false;probe.CreateNoWindow=true;
                using(Process process=Process.Start(probe)){Assert(process.WaitForExit(5000),"Native Shell probe did not finish");Assert(process.ExitCode==0,"Fresh Shell consumer returned a stale icon");}
                Assert(!(bool)needsRefresh.Invoke(null,new object[]{path,replacement}),"Configuration-only replacement triggers global icon refresh");
                Assert(PeConfiguration.Read(path).DarkTheme==index%2+1,"Replacement lost its new configuration");
                Assert(Directory.GetFiles(folder,".ap-*.tmp").Length==0,"Save left temporary files");
                previous=expected;
            }
        } finally {if(File.Exists(path))File.Delete(path);}
    }
}
