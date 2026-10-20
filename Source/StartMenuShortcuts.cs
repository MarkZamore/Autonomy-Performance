using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

// Cleanup manages only marked links in our Programs folder and Taskbar copies. Icon refresh is
// limited to existing links to the exact exported EXE, keeping their other properties.
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
    internal static string CreateWithIcon(string executable,int iconIndex) { return CreateInFolderWithIcon(executable, Folder(),iconIndex); }

    internal static string CreateInFolder(string executable, string folder)
    { return CreateInFolderWithIcon(executable,folder,0); }

    static string CreateInFolderWithIcon(string executable, string folder,int iconIndex)
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
            link.SetIconLocation(executable, iconIndex);
            link.SetShowCmd(1);
            ((IPersistFile)instance).Save(destination, true);
        } finally { Marshal.FinalReleaseComObject(instance); }
        return destination;
    }

    public static void Cleanup()
    {
        try { CleanupInFolder(Folder()); } catch { }
        try {
            string roaming=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if(!String.IsNullOrEmpty(roaming))CleanupInFolder(Path.Combine(roaming,@"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),false,ShellUnpin.TryUnpin);
        }catch{ }
    }

    internal static void RefreshIcons(string executable,int previousIndex,int newIndex)
    {
        try { RefreshIconsInFolder(executable, Folder(),previousIndex,newIndex); } catch { }
        try {
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!String.IsNullOrEmpty(roaming)) RefreshIconsInFolder(executable, Path.Combine(roaming, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),previousIndex,newIndex);
        } catch { }
    }

    [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
    static extern void SHChangeNotify(uint change, uint flags, string first, IntPtr second);

    internal static int RefreshIconsInFolder(string executable, string folder,int previousIndex=-1,int newIndex=-1)
    {
        int refreshed = 0;
        executable = Path.GetFullPath(executable);
        if (!Directory.Exists(folder) || IsReparsePoint(folder)) return refreshed;
        foreach (string path in Directory.GetFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly)) {
            object instance = null;
            try {
                if (IsReparsePoint(path)) continue;
                instance = Activator.CreateInstance(Type.GetTypeFromCLSID(ShellLinkClass));
                ((IPersistFile)instance).Load(path, 2);
                IShellLink link = (IShellLink)instance;
                string target;
                if (!ReadTarget(link, false, out target) || !String.Equals(target, executable, StringComparison.OrdinalIgnoreCase)) continue;
                StringBuilder icon = new StringBuilder(32768); int index;
                link.GetIconLocation(icon, icon.Capacity, out index);
                // Preserve a user-selected external icon or another resource index.
                if ((icon.Length != 0 && !String.Equals(icon.ToString(), executable, StringComparison.OrdinalIgnoreCase)) || (index != 0 && index != -1 && index != previousIndex)) continue;
                // Re-save the LOADED link: arguments, AppUserModelID, description,
                // working directory and other Windows pin metadata are preserved.
                // The alias has the same artwork as the default icon, with an ID derived
                // from its content. Reusing an icon therefore reuses the correct cached art.
                link.SetIconLocation(executable, newIndex);
                ((IPersistFile)instance).Save(path, true);
                SHChangeNotify(0x2000, 0x1005, path, IntPtr.Zero);
                refreshed++;
            } catch { }
            finally { if (instance != null) Marshal.FinalReleaseComObject(instance); }
        }
        if (refreshed > 0) SHChangeNotify(0x1000, 0x1005, folder, IntPtr.Zero);
        return refreshed;
    }

    internal static int CleanupInFolder(string folder)
    { return CleanupInFolder(folder,true,ShellUnpin.TryUnpin); }

    internal static int CleanupInFolder(string folder,bool removeEmptyFolder,Func<string,bool> unpin)
    {
        int removed = 0;
        if (!Directory.Exists(folder) || IsReparsePoint(folder)) return removed;
        foreach (string path in Directory.GetFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly)) {
            try {
                string target;
                if (ReadCleanupTarget(path, out target) && DefinitelyMissing(target)) {
                    // Unpin BEFORE deleting the shortcut, as required by Windows. Keep it
                    // for a later retry if the Shell cannot perform its part of cleanup.
                    if(!unpin(path))continue;
                    // Recheck after the Shell call: a disconnected/restored target must
                    // never be removed merely because an earlier observation was stale.
                    string current;
                    if(!ReadCleanupTarget(path,out current)||!String.Equals(current,target,StringComparison.OrdinalIgnoreCase)||!DefinitelyMissing(target))continue;
                    File.Delete(path);removed++;
                    SHChangeNotify(4,0x1005,path,IntPtr.Zero);
                }
            } catch { }
        }
        if(removed>0){SHChangeNotify(0x1000,0x1005,folder,IntPtr.Zero);if(removeEmptyFolder){try{Directory.Delete(folder,false);SHChangeNotify(0x10,0x1005,folder,IntPtr.Zero);}catch{ }}}
        return removed;
    }

    static bool ReadOwnedTarget(string path, out string target)
    { return ReadLinkTarget(path,false,out target); }

    static bool ReadCleanupTarget(string path,out string target)
    { return ReadLinkTarget(path,true,out target); }

    static bool ReadLinkTarget(string path,bool checkIcon,out string target)
    {
        target = null;
        object instance = null;
        try {
            if (IsReparsePoint(path)) return false;
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(ShellLinkClass));
            ((IPersistFile)instance).Load(path, 0);
            IShellLink link=(IShellLink)instance;
            if(!ReadTarget(link,true,out target))return false;
            if(checkIcon){StringBuilder icon=new StringBuilder(32768);int index;link.GetIconLocation(icon,icon.Capacity,out index);if(!String.Equals(icon.ToString(),target,StringComparison.OrdinalIgnoreCase)||index>0||index < -65535)return false;}
            return true;
        } catch { return false; }
        finally { if (instance != null) Marshal.FinalReleaseComObject(instance); }
    }

    static bool ReadTarget(IShellLink link, bool requireMarker, out string target)
    {
        target = null;
        StringBuilder description = new StringBuilder(256), arguments = new StringBuilder(32768), value = new StringBuilder(32768);
        link.GetDescription(description, description.Capacity);
        if (requireMarker && description.ToString() != Marker) return false;
        link.GetArguments(arguments, arguments.Capacity);
        if (arguments.Length != 0) return false;
        // GetPath alone never resolves or launches the shortcut's target.
        link.GetPath(value, value.Capacity, IntPtr.Zero, 4);
        string raw = value.ToString();
        if (String.IsNullOrEmpty(raw) || !Path.IsPathRooted(raw) || raw.IndexOf('%') >= 0 || !String.Equals(Path.GetExtension(raw), ".exe", StringComparison.OrdinalIgnoreCase)) return false;
        target = Path.GetFullPath(raw);
        return true;
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

// Windows' supported shortcut-uninstallation API, without invoking shortcut verbs.
internal static class ShellUnpin
{
    [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem { }
    [ComImport,Guid("4CD19ADA-25A5-4A32-B3B7-347BEE5BE36B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IStartMenuPinnedList { void RemoveFromList(IShellItem item); }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)]
    static extern void SHCreateItemFromParsingName(string path,IntPtr context,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IShellItem item);
    [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved,uint flags);
    [DllImport("ole32.dll")] static extern void CoUninitialize();
    internal static bool TryUnpin(string path)
    {
        IShellItem item=null;object instance=null;int initialized=CoInitializeEx(IntPtr.Zero,2);
        try {
            Guid id=typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(path,IntPtr.Zero,ref id,out item);
            instance=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("A2A9545D-A0C2-42B4-9708-A0B2BADD77C8")));
            ((IStartMenuPinnedList)instance).RemoveFromList(item);return true;
        }catch{return false;}
        finally{if(instance!=null)Marshal.FinalReleaseComObject(instance);if(item!=null)Marshal.FinalReleaseComObject(item);if(initialized>=0)CoUninitialize();}
    }
}
