using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

// Wallpaper Engine's documented CLI talks to its already running instance.
// Paths are discovered on the destination machine, never embedded in a mode.
public static class WallpaperControl
{
    delegate bool WindowCallback(IntPtr hwnd,IntPtr parameter);
    [DllImport("user32.dll")]static extern bool EnumWindows(WindowCallback callback,IntPtr parameter);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr hwnd,StringBuilder text,int count);
    [DllImport("user32.dll")]static extern bool PostThreadMessage(uint thread,uint message,IntPtr w,IntPtr l);
    static bool ValidPath(string path){try{return File.Exists(path)&&String.Equals(FileVersionInfo.GetVersionInfo(path).ProductName,"Wallpaper Engine",StringComparison.OrdinalIgnoreCase);}catch{return false;}}
    public static string FindInstalled()
    {
        List<string> folders=new List<string>();List<string> steamFolders=new List<string>();
        foreach(RegistryHive hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(RegistryView view in new[]{RegistryView.Registry64,RegistryView.Registry32}){
            try{using(RegistryKey root=RegistryKey.OpenBaseKey(hive,view)){
                using(RegistryKey key=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 431960")){if(key!=null){string location=key.GetValue("InstallLocation") as string;if(!String.IsNullOrEmpty(location))folders.Add(location);}}
                using(RegistryKey key=root.OpenSubKey(@"SOFTWARE\Valve\Steam")){if(key!=null)foreach(string name in new[]{"SteamPath","InstallPath"}){string location=key.GetValue(name) as string;if(!String.IsNullOrEmpty(location))steamFolders.Add(location);}}
            }}catch{}
        }
        steamFolders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));
        foreach(string steam in steamFolders){
            List<string> libraries=new List<string>{steam};
            try{string vdf=Path.Combine(steam,@"steamapps\libraryfolders.vdf");if(File.Exists(vdf))foreach(Match m in Regex.Matches(File.ReadAllText(vdf),"\"path\"\\s*\"([^\"]+)\""))libraries.Add(m.Groups[1].Value.Replace(@"\\",@"\"));}catch{}
            foreach(string library in libraries){
                string install="wallpaper_engine";
                try{string manifest=Path.Combine(library,@"steamapps\appmanifest_431960.acf");if(File.Exists(manifest)){Match m=Regex.Match(File.ReadAllText(manifest),"\"installdir\"\\s*\"([^\"]+)\"");if(m.Success&&Path.GetFileName(m.Groups[1].Value)==m.Groups[1].Value)install=m.Groups[1].Value;}}catch{}
                folders.Add(Path.Combine(library,@"steamapps\common",install));
            }
        }
        foreach(string folder in folders)foreach(string file in new[]{"wallpaper64.exe","wallpaper32.exe"}){string path=Path.Combine(folder,file);if(ValidPath(path))return path;}
        return null;
    }
    public static StepResult SetApplication(bool enabled)
    {
        const string step="WallpaperApplication";List<Process> all=new List<Process>();
        try{
            int session;using(Process self=Process.GetCurrentProcess())session=self.SessionId;
            List<Process> running=new List<Process>();
            foreach(string name in new[]{"wallpaper64","wallpaper32"})foreach(Process p in Process.GetProcessesByName(name)){all.Add(p);try{if(p.SessionId==session&&!p.HasExited&&ValidPath(p.MainModule.FileName))running.Add(p);}catch{}}
            if(enabled){
                if(running.Count>0)return new StepResult(step,"ok","Wallpaper Engine уже запущен.");
                string path=FindInstalled();if(path==null)return new StepResult(step,"skipped","Установка Wallpaper Engine не найдена.");
                using(Process p=Process.Start(new ProcessStartInfo(path,"-silent"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(path)})){
                    if(p==null)return new StepResult(step,"skipped","Wallpaper Engine не запущен.");
                    for(int i=0;i<40;i++){IntPtr ready=IntPtr.Zero;EnumWindows(delegate(IntPtr h,IntPtr ignored){uint pid;GetWindowThreadProcessId(h,out pid);if(pid==(uint)p.Id){StringBuilder cls=new StringBuilder(128);GetClassName(h,cls,128);if(cls.ToString()=="WPETrayWindow")ready=h;}return true;},IntPtr.Zero);if(ready!=IntPtr.Zero)return new StepResult(step,"ok","Wallpaper Engine запущен напрямую, без запуска Steam.");if(p.HasExited)break;System.Threading.Thread.Sleep(100);}
                }
                return new StepResult(step,"skipped","Запуск Wallpaper Engine не подтверждён вовремя.");
            }
            if(running.Count==0)return new StepResult(step,"ok","Wallpaper Engine уже завершён.");
            if(running.Count!=1)return new StepResult(step,"skipped","Несколько экземпляров Wallpaper Engine; завершение пропущено.");
            Process engine=running[0];uint eventThread=0;
            EnumWindows(delegate(IntPtr h,IntPtr ignored){uint pid;uint thread=GetWindowThreadProcessId(h,out pid);if(pid==(uint)engine.Id){StringBuilder cls=new StringBuilder(128);GetClassName(h,cls,128);if(cls.ToString()=="WPEEventWindow")eventThread=thread;}return true;},IntPtr.Zero);
            // WM_CLOSE merely destroys the tray window. Request normal termination
            // of the application's event loop instead; never force-kill the engine.
            if(eventThread==0||!PostThreadMessage(eventThread,0x12,IntPtr.Zero,IntPtr.Zero))return new StepResult(step,"skipped","Получатель команды завершения Wallpaper Engine недоступен.");
            return engine.WaitForExit(4000)?new StepResult(step,"ok","Wallpaper Engine штатно завершён."):new StepResult(step,"skipped","Wallpaper Engine не завершился вовремя; принудительное завершение не выполнялось.");
        }catch{return new StepResult(step,"skipped","Запуск или завершение Wallpaper Engine недоступны.");}
        finally{foreach(Process p in all)p.Dispose();}
    }
    public static StepResult SetPlayback(bool play)
    {
        return SendCommand("WallpaperPlayback",play?"play":"pause");
    }
    public static StepResult SetAudio(bool enabled)
    {
        return SendCommand("WallpaperAudio",enabled?"unmute":"mute");
    }
    static StepResult SendCommand(string step,string command)
    {
        List<Process> candidates=new List<Process>();
        Process client=null;
        try{
            int session;using(Process self=Process.GetCurrentProcess())session=self.SessionId;
            string path=null;Process running=null;
            foreach(string name in new[]{"wallpaper32","wallpaper64"})foreach(Process process in Process.GetProcessesByName(name)){
                candidates.Add(process);
                try{
                    if(process.SessionId!=session||process.HasExited)continue;
                    string executable=process.MainModule.FileName;
                    if(!String.Equals(FileVersionInfo.GetVersionInfo(executable).ProductName,"Wallpaper Engine",StringComparison.OrdinalIgnoreCase))continue;
                    if(path!=null&&!String.Equals(path,executable,StringComparison.OrdinalIgnoreCase))return new StepResult(step,"skipped","Несколько экземпляров Wallpaper Engine; команда пропущена.");
                    path=executable;running=process;
                }catch{}
            }
            if(running==null||running.HasExited||!File.Exists(path))return new StepResult(step,"skipped","Wallpaper Engine не запущен в текущем сеансе.");
            ProcessStartInfo start=new ProcessStartInfo(path,"-control "+command);
            start.UseShellExecute=false;start.CreateNoWindow=true;start.WindowStyle=ProcessWindowStyle.Hidden;start.WorkingDirectory=Path.GetDirectoryName(path);
            // Check again immediately before sending. Never start an absent app
            // from a registry/Steam path just to apply an optional action.
            if(running.HasExited)return new StepResult(step,"skipped","Wallpaper Engine завершился до отправки команды.");
            client=Process.Start(start);
            if(client==null)return new StepResult(step,"skipped","Не удалось отправить команду Wallpaper Engine.");
            if(!client.WaitForExit(2500)){
                try{client.Kill();client.WaitForExit(500);}catch{}
                return new StepResult(step,"skipped","Wallpaper Engine не завершил обработку команды вовремя.");
            }
            if(client.ExitCode!=0)return new StepResult(step,"skipped","Wallpaper Engine отклонил команду; код "+client.ExitCode+".");
            return new StepResult(step,"ok","Отправлена команда Wallpaper Engine: "+command+".");
        }catch{return new StepResult(step,"skipped","Управление Wallpaper Engine недоступно.");}
        finally{if(client!=null)client.Dispose();foreach(Process process in candidates)process.Dispose();}
    }
}
