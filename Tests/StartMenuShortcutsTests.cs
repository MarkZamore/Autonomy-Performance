using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

static class StartMenuShortcutsTests
{
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size=24)] struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Text; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore {
        void GetCount(out uint count); void GetAt(uint index,out PropertyKey key);
        void GetValue(ref PropertyKey key,out PropVariant value); void SetValue(ref PropertyKey key,ref PropVariant value); void Commit();
    }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
    static string AppId(string path, string set)
    {
        object link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046")));
        try {
            ((IPersistFile)link).Load(path, set == null ? 0 : 2);
            IPropertyStore store = (IPropertyStore)link;
            PropertyKey key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
            if (set != null) {
                PropVariant input = new PropVariant { Type = 31, Text = Marshal.StringToCoTaskMemUni(set) };
                try { store.SetValue(ref key, ref input); store.Commit(); ((IPersistFile)link).Save(path, true); }
                finally { PropVariantClear(ref input); }
            }
            PropVariant result; store.GetValue(ref key, out result);
            try { return result.Type == 31 ? Marshal.PtrToStringUni(result.Text) : null; }
            finally { PropVariantClear(ref result); }
        } finally { Marshal.FinalReleaseComObject(link); }
    }
    static int checks;
    static void Assert(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Edit(string path, string method, string value)
    { EditValues(path, method, new object[] { value }); }
    static void EditValues(string path, string method, object[] values)
    {
        object link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046")));
        try {
            ((IPersistFile)link).Load(path, 0);
            Type contract = typeof(StartMenuShortcuts).GetNestedType("IShellLink", BindingFlags.NonPublic);
            contract.GetMethod(method).Invoke(link, values);
            ((IPersistFile)link).Save(path, true);
        } finally { Marshal.FinalReleaseComObject(link); }
    }
    static object[] ReadValues(string path, string method, object[] values)
    {
        object link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046")));
        try {
            ((IPersistFile)link).Load(path, 0);
            typeof(StartMenuShortcuts).GetNestedType("IShellLink", BindingFlags.NonPublic).GetMethod(method).Invoke(link, values);
            return values;
        } finally { Marshal.FinalReleaseComObject(link); }
    }
    static string Text(string path, string method)
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder(32768);
        ReadValues(path, method, new object[] { text, text.Capacity }); return text.ToString();
    }
    static int IconIndex(string path)
    {
        System.Text.StringBuilder text = new System.Text.StringBuilder(32768);
        return (int)ReadValues(path, "GetIconLocation", new object[] { text, text.Capacity, 0 })[2];
    }
    static void VerifyPinRefresh(string root, string first, string second)
    {
        string folder = Path.Combine(root, "PinRefresh"), absent = Path.Combine(root, "NoPins");
        Assert(StartMenuShortcuts.RefreshIconsInFolder(first, absent) == 0 && !Directory.Exists(absent), "Refresh creates a missing shortcut folder");
        string link = StartMenuShortcuts.CreateInFolder(first, folder);
        string windows = Path.Combine(folder, "WindowsPin.lnk"); File.Copy(link, windows);
        Edit(windows, "SetDescription", "Windows pin metadata");
        Edit(windows, "SetWorkingDirectory", root);
        EditValues(windows, "SetHotkey", new object[] { (short)321 });
        EditValues(windows, "SetShowCmd", new object[] { 7 });
        AppId(windows, "AutonomyPerformance.PinRegression");
        string other = StartMenuShortcuts.CreateInFolder(second, folder);
        string custom = Path.Combine(folder, "CustomIcon.lnk"); File.Copy(link, custom);
        EditValues(custom, "SetIconLocation", new object[] { Path.Combine(root, "User.ico"), 0 });
        string index = Path.Combine(folder, "CustomIndex.lnk"); File.Copy(link, index);
        EditValues(index, "SetIconLocation", new object[] { first, 2 });
        string args = Path.Combine(folder, "Arguments.lnk"); File.Copy(link, args); Edit(args, "SetArguments", "--user-option");
        string corrupt = Path.Combine(folder, "Corrupt.lnk"); File.WriteAllText(corrupt, "Keep");
        string nested = Path.Combine(folder, "Nested"); Directory.CreateDirectory(nested);
        string child = Path.Combine(nested, "Nested.lnk"); File.Copy(link, child);
        string[] untouched = new string[] { other, custom, index, args, corrupt, child };
        string[] snapshots = Array.ConvertAll(untouched, delegate(string p) { return Convert.ToBase64String(File.ReadAllBytes(p)); });
        Assert(StartMenuShortcuts.RefreshIconsInFolder(first, folder) == 2, "Refresh does not select exactly matching default-icon links");
        Assert(IconIndex(link) == -1 && IconIndex(windows) == -1, "Pinned icon reference was not invalidated");
        Assert(Text(windows, "GetDescription") == "Windows pin metadata" && Text(windows, "GetArguments") == "", "Refresh loses pin description or arguments");
        Assert(AppId(windows, null) == "AutonomyPerformance.PinRegression", "Refresh loses the pinned AppUserModelID");
        System.Text.StringBuilder work = new System.Text.StringBuilder(32768);
        ReadValues(windows, "GetWorkingDirectory", new object[] { work, work.Capacity });
        Assert(work.ToString() == root, "Refresh changes the working directory");
        Assert((short)ReadValues(windows, "GetHotkey", new object[] { (short)0 })[0] == 321 && (int)ReadValues(windows, "GetShowCmd", new object[] { 0 })[0] == 7, "Refresh changes pin launch properties");
        for (int i = 0; i < untouched.Length; i++) Assert(Convert.ToBase64String(File.ReadAllBytes(untouched[i])) == snapshots[i], "Refresh modifies unrelated/custom/argument/corrupt/nested shortcut");
        Assert(StartMenuShortcuts.RefreshIconsInFolder(first, folder,-1,-1234) == 2 && IconIndex(link) == -1234 && IconIndex(windows) == -1234, "Repeated refresh does not set the new content-specific icon reference");
        Assert(Directory.GetFiles(folder).Length == 7, "Refresh creates new shortcut or icon files");
    }
    [STAThread] static int Main(string[] args)
    {
        try {
            string root = Path.GetFullPath(args[0]), folder = Path.Combine(root, "Programs"), missing = Path.Combine(root, "Missing");
            Assert(StartMenuShortcuts.CleanupInFolder(missing) == 0 && !Directory.Exists(missing), "Startup cleanup creates a missing folder");
            Directory.CreateDirectory(Path.Combine(root, "First")); Directory.CreateDirectory(Path.Combine(root, "Second"));
            string first = Path.Combine(root, "First", "Режим.exe"), second = Path.Combine(root, "Second", "Режим.exe");
            File.WriteAllBytes(first, new byte[] { 77, 90 }); File.WriteAllBytes(second, new byte[] { 77, 90 });
            VerifyPinRefresh(root, first, second);
            string link = StartMenuShortcuts.CreateInFolder(first, folder);
            Assert(File.Exists(link) && Path.GetFileName(link) == "Режим.lnk", "A named Start menu link was not created");
            byte[] bytes = File.ReadAllBytes(link);
            Assert(bytes.Length > 76 && BitConverter.ToInt32(bytes, 0) == 76, "Native .lnk header is invalid");
            Assert(StartMenuShortcuts.CleanupInFolder(folder) == 0 && File.Exists(link), "A valid shortcut is deleted");
            Assert(StartMenuShortcuts.CreateInFolder(first, folder) == link && Directory.GetFiles(folder).Length == 1, "Saving twice duplicates its shortcut");
            string other = StartMenuShortcuts.CreateInFolder(second, folder);
            Assert(other != link && File.Exists(link) && File.Exists(other), "Identical EXE names overwrite a shortcut to another file");
            string foreign = Path.Combine(folder, "Foreign.lnk"); File.Copy(link, foreign);
            Edit(foreign, "SetDescription", "User-created shortcut");
            string arguments = Path.Combine(folder, "Edited.lnk"); File.Copy(link, arguments); Edit(arguments, "SetArguments", "--user-option");
            string network = Path.Combine(folder, "Network.lnk"); File.Copy(link, network); Edit(network, "SetPath", @"\\offline.invalid\share\Missing.exe");
            string corrupt = Path.Combine(folder, "Corrupt.lnk"); File.WriteAllText(corrupt, "User data");
            string text = Path.Combine(folder, "Readme.txt"); File.WriteAllText(text, "Keep");
            string nested = Path.Combine(folder, "Nested"); Directory.CreateDirectory(nested); string nestedLink = Path.Combine(nested, "Nested.lnk"); File.Copy(link, nestedLink);
            File.Delete(first);
            Assert(StartMenuShortcuts.CleanupInFolder(folder) == 1, "Cleanup did not remove exactly the owned missing target link");
            Assert(!File.Exists(link) && File.Exists(other) && File.Exists(second), "Cleanup changes a valid link or target");
            Assert(File.Exists(foreign), "Cleanup deletes a foreign valid shortcut");
            Assert(File.Exists(arguments), "Cleanup deletes a user-edited argument shortcut");
            Assert(File.Exists(network), "Cleanup treats an unavailable share as deletion");
            Assert(File.Exists(corrupt) && File.ReadAllText(corrupt) == "User data", "Cleanup deletes corrupt or unrelated user data");
            Assert(File.Exists(text) && File.Exists(nestedLink), "Cleanup modifies other files or walks subfolders");
            string recreated = StartMenuShortcuts.CreateInFolder(second, folder);
            Assert(recreated == other, "Existing target identity was lost after cleanup");
            File.Delete(second);
            Assert(StartMenuShortcuts.CleanupInFolder(folder) == 1 && !File.Exists(other), "A second stale link was not removed");
            Assert(Directory.Exists(folder), "Nonempty folder with user files was deleted");
            string collisionFolder = Path.Combine(root, "Collision"); Directory.CreateDirectory(collisionFolder);
            File.WriteAllBytes(first, new byte[] {77, 90}); string occupied = Path.Combine(collisionFolder, "Режим.lnk"); File.Copy(foreign, occupied);
            string separate = StartMenuShortcuts.CreateInFolder(first, collisionFolder);
            Assert(separate != occupied && File.Exists(occupied), "Creation overwrites an existing foreign shortcut");
            string emptyFolder = Path.Combine(root, "Empty"); string single = StartMenuShortcuts.CreateInFolder(first, emptyFolder); File.Delete(first);
            Assert(StartMenuShortcuts.CleanupInFolder(emptyFolder) == 1 && !File.Exists(single) && !Directory.Exists(emptyFolder), "An empty owned folder is left behind");
            Console.WriteLine("PASS: " + checks + " native shortcut checks; no real Start menu paths or exported EXEs were used.");
            return 0;
        } catch(Exception error) { Console.WriteLine(error); return 1; }
    }
}
