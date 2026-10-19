using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

// Matches BatteryCarePage in the installed ScreenXpert: WM_COPYDATA,
// function 700, BatteryCareMode=80/100 (256 for supported smart charging).
public static class BatteryCareControl
{
    const string BasePath=@"SOFTWARE\ASUS\ASUS System Control Interface\AsusOptimization";
    [StructLayout(LayoutKind.Sequential)] struct CopyData { public IntPtr Id;public int Size;public IntPtr Data; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string cls,string title);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr window,uint message,UIntPtr parameter,ref CopyData data,uint flags,uint timeout,out UIntPtr result);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenEvent(uint access,bool inherit,string name);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static int Read(string path,string name){using(RegistryKey root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))using(RegistryKey key=root.OpenSubKey(path,false)){object v=key==null?null:key.GetValue(name);return v is int?(int)v:-1;}}
    public static int ReadRate(){try{return Read(BasePath+@"\ASUS Keyboard Hotkeys","ChargingRate");}catch{return -1;}}
    static bool? EventState(string suffix){IntPtr h=OpenEvent(0x100000,false,@"Global\ASUS ASLDRSrv Battery Health Charging "+suffix+" Event");if(h==IntPtr.Zero)return null;try{uint v=WaitForSingleObject(h,0);return v==0?true:v==258?(bool?)false:null;}finally{CloseHandle(h);}}
    public static StepResult Set(bool enabled)
    {
        const string step="BatteryCare";IntPtr memory=IntPtr.Zero;
        try{
            int support=Read(BasePath+@"\ScreenXpert","BatteryCare");
            if(support!=1&&support!=2)return new StepResult(step,"skipped","Бережная зарядка ScreenXpert недоступна.");
            int rate=ReadRate();if(rate<40||rate>100)return new StepResult(step,"skipped","Не удалось прочитать состояние зарядки ASUS.");
            // Do not overwrite a temporary full-charge exception unknowingly.
            if(EventState("Deferred")==true)return new StepResult(step,"skipped","Действует временная полная зарядка ASUS.");
            int target=enabled?80:100;
            if(enabled&&support==2){bool? smart=EventState("Smart");if(!smart.HasValue)return new StepResult(step,"skipped","Состояние адаптивной зарядки ASUS недоступно.");if(smart.Value)target=256;}
            IntPtr window=FindWindow("ASUSSCREENXPERTUI",null);if(window==IntPtr.Zero)return new StepResult(step,"skipped","Служебный получатель ScreenXpert недоступен.");
            uint pid;GetWindowThreadProcessId(window,out pid);
            using(Process process=Process.GetProcessById((int)pid))using(Process self=Process.GetCurrentProcess()){
                string expected=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),@"ASUS\AsusScreenXpert\AsusScreenXpertUI.exe");
                // The ASUS UIAccess process denies even limited image-path queries.
                // Check its session, executable name, registered class and installation.
                if(process.SessionId!=self.SessionId||!File.Exists(expected)||!String.Equals(process.ProcessName,"AsusScreenXpertUI",StringComparison.OrdinalIgnoreCase))return new StepResult(step,"skipped","Неизвестный получатель команды ASUS.");
            }
            string command="BatteryCareMode="+target;memory=Marshal.StringToCoTaskMemUni(command);
            CopyData data=new CopyData{Id=IntPtr.Zero,Size=command.Length*2+1,Data=memory};UIntPtr result;
            if(SendMessageTimeout(window,0x4A,new UIntPtr(700),ref data,2,500,out result)==IntPtr.Zero)return new StepResult(step,"skipped","Получатель ASUS не ответил вовремя.");
            for(int i=0;i<20;i++){rate=ReadRate();if(enabled?rate>=40&&rate<100:rate==100)return new StepResult(step,"ok",enabled?"Бережная зарядка ASUS включена.":"Бережная зарядка ASUS выключена.");Thread.Sleep(50);}
            return new StepResult(step,"skipped","Команда отправлена, изменение зарядки не подтверждено.");
        }catch{return new StepResult(step,"skipped","Управление бережной зарядкой недоступно.");}
        finally{if(memory!=IntPtr.Zero)Marshal.FreeCoTaskMem(memory);}
    }
}
