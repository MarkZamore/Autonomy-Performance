using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

// Only this application's marked links in its own Programs subfolder are managed.
// No registration, scheduled task, background process or target execution is used.
public static class StartMenuShortcuts
{
    const string Marker = "Autonomy&Performance.ScriptShortcut.v1";
    static readonly Guid ShellLinkClass = new Guid("00021401-0000-0000-C000-000000000046");

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, IntPtr data, uint flags);
        void GetIDList(out IntPtr value);
        void SetIDList(IntPtr value);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int length);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int length);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetHotkey(out short value);
        void SetHotkey(short value);
        void GetShowCmd(out int value);
        void SetShowCmd(int value);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    static string Folder()
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (String.IsNullOrEmpty(programs)) throw new DirectoryNotFoundException();
        return Path.Combine(programs, "Autonomy&Performance");
    }

    public static string Create(string executable) { return CreateInFolder(executable, Folder()); }

    internal static string CreateInFolder(string executable, string folder)
    {
        executable = Path.GetFullPath(executable);
        if (!String.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException();
        if (!File.Exists(executable)) throw new FileNotFoundException();
        if (!Vacant(folder) && IsReparsePoint(folder)) throw new IOException();
        Directory.CreateDirectory(folder);
        string destination = null;
        foreach (string path in Directory.GetFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly)) {
            string target;
            if (ReadOwnedTarget(path, out target) && String.Equals(target, executable, StringComparison.OrdinalIgnoreCase)) { destination = path; break; }
        }
        if (destination == null) {
            string name = Path.GetFileNameWithoutExtension(executable);
            for (int number = 1; number <= 10000; number++) {
                string candidate = Path.Combine(folder, name + (number == 1 ? "" : " (" + number + ")") + ".lnk");
                if (Vacant(candidate)) { destination = candidate; break; }
            }
            if (destination == null) throw new IOException();
        }
        object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(ShellLinkClass));
        try {
            IShellLink link = (IShellLink)instance;
            link.SetPath(executable);
            link.SetWorkingDirectory(Path.GetDirectoryName(executable));
            link.SetArguments("");
            link.SetDescription(Marker);
            link.SetIconLocation(executable, 0);
            link.SetShowCmd(1);
            ((IPersistFile)instance).Save(destination, true);
        } finally { Marshal.FinalReleaseComObject(instance); }
        return destination;
    }

    public static void Cleanup()
    {
        try { CleanupInFolder(Folder()); } catch { }
    }

    internal static int CleanupInFolder(string folder)
    {
        int removed = 0;
        if (!Directory.Exists(folder) || IsReparsePoint(folder)) return removed;
        foreach (string path in Directory.GetFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly)) {
            try {
                string target;
                if (ReadOwnedTarget(path, out target) && DefinitelyMissing(target)) { File.Delete(path); removed++; }
            } catch { }
        }
        if (removed > 0) { try { Directory.Delete(folder, false); } catch { } }
        return removed;
    }

    static bool ReadOwnedTarget(string path, out string target)
    {
        target = null;
        object instance = null;
        try {
            if (IsReparsePoint(path)) return false;
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(ShellLinkClass));
            ((IPersistFile)instance).Load(path, 0);
            IShellLink link = (IShellLink)instance;
            StringBuilder description = new StringBuilder(256), arguments = new StringBuilder(32768), value = new StringBuilder(32768);
            link.GetDescription(description, description.Capacity);
            if (description.ToString() != Marker) return false;
            link.GetArguments(arguments, arguments.Capacity);
            if (arguments.Length != 0) return false;
            // GetPath alone never resolves or launches the shortcut's target.
            link.GetPath(value, value.Capacity, IntPtr.Zero, 4);
            string raw = value.ToString();
            if (String.IsNullOrEmpty(raw) || !Path.IsPathRooted(raw) || raw.IndexOf('%') >= 0 || !String.Equals(Path.GetExtension(raw), ".exe", StringComparison.OrdinalIgnoreCase)) return false;
            target = Path.GetFullPath(raw);
            return true;
        } catch { return false; }
        finally { if (instance != null) Marshal.FinalReleaseComObject(instance); }
    }

    static bool DefinitelyMissing(string target)
    {
        // A disconnected drive or inaccessible share does not prove deletion.
        if (target.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        try {
            DriveInfo drive = new DriveInfo(Path.GetPathRoot(target));
            if (!drive.IsReady || drive.DriveType == DriveType.Network) return false;
            try { File.GetAttributes(target); return false; }
            catch (FileNotFoundException) { return true; }
            catch (DirectoryNotFoundException) { return true; }
        } catch { return false; }
    }

    static bool IsReparsePoint(string path) { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
    static bool Vacant(string path)
    {
        try { File.GetAttributes(path); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
    }
}
