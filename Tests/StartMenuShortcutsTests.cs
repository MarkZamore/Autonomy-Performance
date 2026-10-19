using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

static class StartMenuShortcutsTests
{
    static int checks;
    static void Assert(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Edit(string path, string method, string value)
    {
        object link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046")));
        try {
            ((IPersistFile)link).Load(path, 0);
            Type contract = typeof(StartMenuShortcuts).GetNestedType("IShellLink", BindingFlags.NonPublic);
            contract.GetMethod(method).Invoke(link, new object[] { value });
            ((IPersistFile)link).Save(path, true);
        } finally { Marshal.FinalReleaseComObject(link); }
    }
    [STAThread] static int Main(string[] args)
    {
        try {
            string root = Path.GetFullPath(args[0]), folder = Path.Combine(root, "Programs"), missing = Path.Combine(root, "Missing");
            Assert(StartMenuShortcuts.CleanupInFolder(missing) == 0 && !Directory.Exists(missing), "Startup cleanup creates a missing folder");
            Directory.CreateDirectory(Path.Combine(root, "First")); Directory.CreateDirectory(Path.Combine(root, "Second"));
            string first = Path.Combine(root, "First", "Режим.exe"), second = Path.Combine(root, "Second", "Режим.exe");
            File.WriteAllBytes(first, new byte[] { 77, 90 }); File.WriteAllBytes(second, new byte[] { 77, 90 });
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
