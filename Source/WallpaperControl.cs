using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

// Wallpaper Engine's documented CLI talks to its already running instance.
// Paths are discovered on the destination machine, never embedded in a mode.
public static class WallpaperControl
{
    public static StepResult SetPlayback(bool play)
    {
        const string step="WallpaperPlayback";
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
            ProcessStartInfo start=new ProcessStartInfo(path,play?"-control play":"-control pause");
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
            return new StepResult(step,"ok",play?"Отправлена команда продолжения всех обоев.":"Отправлена команда паузы всех обоев.");
        }catch{return new StepResult(step,"skipped","Управление Wallpaper Engine недоступно.");}
        finally{if(client!=null)client.Dispose();foreach(Process process in candidates)process.Dispose();}
    }
}
