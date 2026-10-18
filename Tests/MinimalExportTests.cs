using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;

public static class MinimalExportTests
{
    static string builder;
    static readonly List<ObservedEvent> events = new List<ObservedEvent>();
    sealed class ObservedEvent { public string Kind; public string Name; public string OldName; }
    delegate bool ResourceNameCallback(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool EnumResourceNames(IntPtr module, IntPtr type, ResourceNameCallback callback, IntPtr parameter);
    public static int Main(string[] args)
    {
        builder = Path.GetFullPath(args[0]);
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e) {
            return new AssemblyName(e.Name).Name == "AutonomyPerformance" ? Assembly.LoadFrom(builder) : null;
        };
        try { Run(Path.GetFullPath(args[1])); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string target = Path.Combine(output, "MinimalMode.exe");
        // This fixed name belongs to the harness and is reset before observation.
        if (File.Exists(target)) File.Delete(target);
        Assert(Directory.GetFiles(output, ".ap-*.tmp").Length == 0, "Test folder already contains export temporary files");
        ModeConfiguration original = new ModeConfiguration {
            EnergySaver=2, FanProfile=1, PowerMode=2, Brightness100=true, Volume100=true,
            DisplayId="TEST\\DISPLAY\\MINIMAL-EXPORT", RefreshRate=60
        };
        string watcherError = null;
        using (ManualResetEvent renamed = new ManualResetEvent(false))
        using (FileSystemWatcher watcher = new FileSystemWatcher(output)) {
            watcher.NotifyFilter = NotifyFilters.FileName;
            watcher.Created += delegate(object sender, FileSystemEventArgs e) {
                lock(events) events.Add(new ObservedEvent { Kind="Created", Name=e.Name });
            };
            watcher.Deleted += delegate(object sender, FileSystemEventArgs e) {
                lock(events) events.Add(new ObservedEvent { Kind="Deleted", Name=e.Name });
            };
            watcher.Renamed += delegate(object sender, RenamedEventArgs e) {
                lock(events) events.Add(new ObservedEvent { Kind="Renamed", Name=e.Name, OldName=e.OldName });
                if (e.FullPath == target) renamed.Set();
            };
            watcher.Error += delegate(object sender, ErrorEventArgs e) { watcherError=e.GetException().ToString(); };
            watcher.EnableRaisingEvents = true;
            ExecutableExporter.Save(target, original, EmbeddedAssets.Read("Icon0"));
            Assert(renamed.WaitOne(2000), "No rename from export temporary file to final EXE was observed");
            // FileSystemWatcher callbacks run asynchronously; let the finite queue drain.
            Thread.Sleep(200);
            watcher.EnableRaisingEvents = false;
        }
        Assert(watcherError == null, "File-system observation lost events: " + watcherError);
        ObservedEvent[] observed; lock(events) observed=events.ToArray();
        ObservedEvent[] created=observed.Where(e=>e.Kind=="Created").ToArray();
        Assert(created.Length==1, "Export created more than one file: " + new JavaScriptSerializer().Serialize(observed));
        string temporary=created[0].Name;
        Assert(temporary.StartsWith(".ap-",StringComparison.Ordinal)&&temporary.EndsWith(".tmp",StringComparison.Ordinal), "Only expected .ap-*.tmp may be created");
        Assert(observed.Count(e=>e.Kind=="Renamed"&&e.OldName==temporary&&e.Name==Path.GetFileName(target))==1, "Temporary file was not renamed exactly once to target");
        Assert(observed.Count(e=>e.Kind=="Renamed")==1&&observed.Count(e=>e.Kind=="Deleted")==0, "Unexpected rename/delete activity");
        Assert(!observed.Any(e=>e.Name.IndexOf("RCX",StringComparison.OrdinalIgnoreCase)>=0||(e.OldName??"").IndexOf("RCX",StringComparison.OrdinalIgnoreCase)>=0), "RCX resource-update file was observed");
        Assert(Directory.GetFiles(output,".ap-*.tmp").Length==0&&Directory.GetFiles(output,"*RCX*").Length==0, "Temporary files remained after export");
        ModeConfiguration roundtrip=PeConfiguration.Read(target);
        string expected=new JavaScriptSerializer().Serialize(original),actual=new JavaScriptSerializer().Serialize(roundtrip);
        Assert(expected==actual, "Exported configuration did not round-trip");
        IntPtr module=LoadLibraryEx(target,IntPtr.Zero,0x22);
        Assert(module!=IntPtr.Zero,"Exported PE could not be loaded for data-only resource inspection");
        bool hasVersion=false;
        ResourceNameCallback callback=delegate(IntPtr h,IntPtr type,IntPtr name,IntPtr parameter){hasVersion=true;return true;};
        try{EnumResourceNames(module,new IntPtr(16),callback,IntPtr.Zero);GC.KeepAlive(callback);}finally{FreeLibrary(module);}
        FileVersionInfo version=FileVersionInfo.GetVersionInfo(target);
        Assert(!hasVersion&&String.IsNullOrEmpty(version.FileVersion)&&String.IsNullOrEmpty(version.ProductVersion), "Exported executable retained version metadata");
        string json=new JavaScriptSerializer().Serialize(new {
            Passed=true, Builder=builder, BuilderBytes=new FileInfo(builder).Length,
            Exported=target, CreatedFiles=created.Length, RenamedFiles=1, DeletedFiles=0,
            NoRcxFiles=true, NoTemporaryFilesRemain=true, ConfigurationRoundtrip=true,
            VersionMetadataAbsent=true, ExportedExecutableWasRun=false, Events=observed
        });
        File.WriteAllText(Path.Combine(output,"result.json"),json,new System.Text.UTF8Encoding(false));
        Console.WriteLine(json);
    }
}
