using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

public static class EmbeddedAssets
{
    public static byte[] Read(string name){using(Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)){if(s==null)throw new InvalidDataException("Missing embedded asset: "+name);using(MemoryStream m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}}}
}
public sealed class IconSelection
{
    public int BuiltInIndex {get;private set;}
    public string CustomPath {get;private set;}
    public void SelectBuiltIn(int index){if(index<0||index>4)throw new ArgumentOutOfRangeException("index");BuiltInIndex=index;CustomPath=null;}
    public void SelectCustom(string path){IconFile.Validate(File.ReadAllBytes(path));CustomPath=path;}
    public byte[] GetBytes(){
        if(CustomPath!=null){try{return IconFile.Validate(File.ReadAllBytes(CustomPath));}catch{SelectBuiltIn(0);}}
        return EmbeddedAssets.Read("Icon"+BuiltInIndex);
    }
    public void Saved(){if(CustomPath!=null)SelectBuiltIn(0);}
}
public static class IconFile
{
    public static byte[] Validate(byte[] bytes)
    {
        if(bytes==null||bytes.Length<22||bytes.Length>16*1024*1024)throw new InvalidDataException("Некорректный файл ICO.");
        using(BinaryReader r=new BinaryReader(new MemoryStream(bytes))){
            if(r.ReadUInt16()!=0||r.ReadUInt16()!=1)throw new InvalidDataException("Нужен файл иконки ICO.");
            int count=r.ReadUInt16();if(count<1||count>256||bytes.Length<6+16*count)throw new InvalidDataException("Некорректная таблица ICO.");
            for(int i=0;i<count;i++){
                r.BaseStream.Position=6+16*i+8;uint size=r.ReadUInt32(),offset=r.ReadUInt32();
                if(size<4||offset<6+16*count||(long)offset+size>bytes.Length)throw new InvalidDataException("Повреждённое изображение ICO.");
            }
        }
        using(System.Drawing.Icon icon=new System.Drawing.Icon(new MemoryStream(bytes))){if(icon.Width==0)throw new InvalidDataException("Пустая иконка.");}
        return bytes;
    }
}
// Ask the Shell to reread changed EXE icons without deleting caches or restarting Explorer.
internal static class ShellIconRefresh
{
    const uint IconsChanged=0x08000000,ItemChanged=0x2000,DirectoryChanged=0x1000;
    const uint Flush=0x1000,UnicodePathAndFlush=0x1005;
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]
    static extern void SHChangeNotify(uint change,uint flags,string first,IntPtr second);
    internal static bool NeedsRefresh(string path,byte[] replacement)
    {
        if(!File.Exists(path))return false;
        try{return !PortableResources.HaveSameIcon(File.ReadAllBytes(path),replacement);}
        catch{return true;}
    }
    internal static void Changed(string path,bool iconChanged)
    {
        try {
            // UPDATEITEM alone leaves Explorer's same-path icon cache stale. Only an icon
            // change triggers Shell cache invalidation; configuration-only saves do not.
            if(iconChanged)SHChangeNotify(IconsChanged,Flush,null,IntPtr.Zero);
            // FLUSH waits for notification delivery, unlike the former FLUSHNOWAIT.
            SHChangeNotify(ItemChanged,UnicodePathAndFlush,path,IntPtr.Zero);
            SHChangeNotify(DirectoryChanged,UnicodePathAndFlush,Path.GetDirectoryName(path),IntPtr.Zero);
        }catch{ /* A Shell refresh failure must not invalidate a successful export. */ }
    }
}
public static class ExecutableExporter
{
    static void Commit(string temp,string destination)
    {
        for(int attempt=0;;attempt++){
            try{if(File.Exists(destination))File.Replace(temp,destination,null);else File.Move(temp,destination);return;}
            catch(IOException error){
                int code=Marshal.GetHRForException(error)&0xffff;
                if(attempt>=5||(code!=32&&code!=33&&code!=1175&&code!=1176))throw;
                // A just-created executable can briefly be held by the Windows scanner.
                System.Threading.Thread.Sleep(40);
            }
        }
    }
    public static bool RemoveVersionMetadata(string executable)
    {
        executable=Path.GetFullPath(executable);
        if(String.Equals(executable,Assembly.GetExecutingAssembly().Location,StringComparison.OrdinalIgnoreCase))throw new IOException("Нельзя заменить работающий конструктор.");
        byte[] input=File.ReadAllBytes(executable),configuration=PortableResources.ReadConfiguration(input);
        ModeConfiguration original=new JavaScriptSerializer().Deserialize<ModeConfiguration>(Encoding.UTF8.GetString(configuration));original.Validate();
        bool changed;byte[] output=PortableResources.RemoveVersion(input,out changed);if(!changed)return false;
        if(!Equal(configuration,PortableResources.ReadConfiguration(output)))throw new InvalidDataException("Не удалось проверить сведения о создаваемом файле.");
        string temp=Path.Combine(Path.GetDirectoryName(executable),".ap-"+Guid.NewGuid().ToString("N")+".tmp");
        try{
            File.WriteAllBytes(temp,output);
            Commit(temp,executable);
            ShellIconRefresh.Changed(executable,false);
            return true;
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    static bool Equal(byte[] first,byte[] second){if(first.Length!=second.Length)return false;for(int i=0;i<first.Length;i++)if(first[i]!=second[i])return false;return true;}
    public static void Save(string destination,ModeConfiguration config,byte[] icon)
    {
        config.Validate();if(!config.HasActions)throw new InvalidDataException("Выберите хотя бы одно действие.");IconFile.Validate(icon);
        destination=Path.GetFullPath(destination);
        if(String.Equals(destination,Assembly.GetExecutingAssembly().Location,StringComparison.OrdinalIgnoreCase))throw new IOException("Нельзя заменить работающий конструктор.");
        byte[] data=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(config));
        byte[] executable=PortableResources.CreateModeExecutable(EmbeddedAssets.Read("RunnerTemplate"),data,icon);
        if(!Equal(data,PortableResources.ReadConfiguration(executable)))throw new InvalidDataException("Не удалось проверить настройки создаваемого файла.");
        bool iconChanged=ShellIconRefresh.NeedsRefresh(destination,executable);
        string temp=Path.Combine(Path.GetDirectoryName(destination),".ap-"+Guid.NewGuid().ToString("N")+".tmp");
        try{
            File.WriteAllBytes(temp,executable);
            Commit(temp,destination);
            ShellIconRefresh.Changed(destination,iconChanged);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
