using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

public sealed class ProgramAvailabilitySnapshot
{
    public readonly bool ScreenXpert,WallpaperEngine,ArmouryCrate;
    public ProgramAvailabilitySnapshot(bool screenXpert,bool wallpaperEngine,bool armouryCrate){ScreenXpert=screenXpert;WallpaperEngine=wallpaperEngine;ArmouryCrate=armouryCrate;}
}

// Installation queries only: no launches, service changes or private settings.
// Registered current-user MSIX packages and validated desktop binaries count,
// regardless of whether an application happens to be running.
public static class ProgramAvailability
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern int GetPackagesByPackageFamily(string family,ref uint count,IntPtr names,ref uint bufferLength,IntPtr buffer);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern int GetPackagePathByFullName(string fullName,ref uint length,StringBuilder path);

    public static ProgramAvailabilitySnapshot Read()
    {
        bool screen=DesktopInstalled("ScreenXpert",new[]{"AsusScreenXpertUI.exe","ScreenXpert.exe"},@"ASUS\AsusScreenXpert")||PackageInstalled("B9ECED6F.ScreenPadMaster_qmba6cd70vzyy");
        bool armoury=PackageInstalled("B9ECED6F.ArmouryCrate_qmba6cd70vzyy")||PackageInstalled("B9ECED6F.ArmouryCrateSE_qmba6cd70vzyy")||DesktopInstalled("ArmouryCrate",new[]{"ArmouryCrate.exe","ArmouryCrateSE.exe"},@"ASUS\ARMOURY CRATE");
        bool wallpaper=false;try{wallpaper=WallpaperControl.FindInstalled()!=null;}catch{}
        return new ProgramAvailabilitySnapshot(screen,wallpaper,armoury);
    }

    static bool PackageInstalled(string family)
    {
        IntPtr names=IntPtr.Zero,buffer=IntPtr.Zero;
        try{
            uint count=0,length=0;int result=GetPackagesByPackageFamily(family,ref count,IntPtr.Zero,ref length,IntPtr.Zero);
            if(result!=122||count==0||count>128||length==0||length>1048576)return false;
            names=Marshal.AllocHGlobal(checked((int)count*IntPtr.Size));buffer=Marshal.AllocHGlobal(checked((int)length*2));
            if(GetPackagesByPackageFamily(family,ref count,names,ref length,buffer)!=0)return false;
            for(int i=0;i<count;i++){
                string fullName=Marshal.PtrToStringUni(Marshal.ReadIntPtr(names,i*IntPtr.Size));uint pathLength=0;
                if(String.IsNullOrEmpty(fullName)||GetPackagePathByFullName(fullName,ref pathLength,null)!=122||pathLength==0||pathLength>32768)continue;
                StringBuilder path=new StringBuilder((int)pathLength);
                if(GetPackagePathByFullName(fullName,ref pathLength,path)==0&&File.Exists(Path.Combine(path.ToString(),"AppxManifest.xml")))return true;
            }
        }catch{}
        finally{if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);if(names!=IntPtr.Zero)Marshal.FreeHGlobal(names);}
        return false;
    }

    static string Compact(string value){StringBuilder result=new StringBuilder();foreach(char c in value??"")if(Char.IsLetterOrDigit(c))result.Append(Char.ToUpperInvariant(c));return result.ToString();}
    static bool ExpectedExecutable(string path,string product)
    {
        try{
            if(!File.Exists(path))return false;
            FileVersionInfo version=FileVersionInfo.GetVersionInfo(path);
            return Compact(version.ProductName).Contains(Compact(product))&&Compact(version.CompanyName).Contains("ASUS");
        }catch{return false;}
    }
    static bool InFolder(string folder,string[] executables,string product)
    {
        if(String.IsNullOrWhiteSpace(folder))return false;
        foreach(string executable in executables)try{if(ExpectedExecutable(Path.Combine(folder,executable),product))return true;}catch{}
        return false;
    }
    static bool DesktopInstalled(string product,string[] executables,string relativeFolder)
    {
        foreach(Environment.SpecialFolder location in new[]{Environment.SpecialFolder.ProgramFiles,Environment.SpecialFolder.ProgramFilesX86})
            if(InFolder(Path.Combine(Environment.GetFolderPath(location),relativeFolder),executables,product))return true;
        foreach(RegistryHive hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(RegistryView view in new[]{RegistryView.Registry64,RegistryView.Registry32}){
            try{using(RegistryKey root=RegistryKey.OpenBaseKey(hive,view)){
                foreach(string executable in executables)using(RegistryKey key=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\"+executable,false)){
                    string path=key==null?null:key.GetValue(null) as string;
                    if(!String.IsNullOrWhiteSpace(path)&&ExpectedExecutable(path.Trim().Trim('"'),product))return true;
                }
                using(RegistryKey uninstall=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",false)){
                    if(uninstall==null)continue;
                    foreach(string subkey in uninstall.GetSubKeyNames())try{using(RegistryKey key=uninstall.OpenSubKey(subkey,false)){
                        string name=key==null?null:key.GetValue("DisplayName") as string;
                        if(!Compact(name).Contains(Compact(product)))continue;
                        if(InFolder(key.GetValue("InstallLocation") as string,executables,product))return true;
                    }}catch{}
                }
            }}catch{}
        }
        return false;
    }
}
