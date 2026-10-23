using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

public sealed class ArmouryCrateSnapshot
{
    public bool Installed,ReceiverAvailable,StateKnown,Locked,PowerKnown,PowerOnline,ManualConfigured;
    public int SupportedModes=-1,EnabledModes=-1,SelectedMode=-1;
    public string ReceiverPath,ActivePowerScheme;
    internal IntPtr ReceiverWindow;
}

public static class ArmouryCrateControl
{
    const string RegistryPath=@"SOFTWARE\ASUS\Armoury Crate Service\ThrottlePlugin\External";
    const string ReceiverClass="UserSessionManagerEventClass",ReceiverTitle="User Session Helper Window";
    const string MessageName="Operating Mode Notification from External Program";
    const int PollMilliseconds=50,MaximumPolls=50,SettledPolls=11,MaximumProfileBytes=524288;
    [StructLayout(LayoutKind.Sequential)] struct PowerStatus { public byte ACLineStatus,BatteryFlag,BatteryLifePercent,SystemStatusFlag;public uint BatteryLifeTime,BatteryFullLifeTime; }
    sealed class ProfileValue { public string Text;public int Offset,Length;public bool Map; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string className,string title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder value,int size);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string value);
    [DllImport("user32.dll",SetLastError=true)] static extern bool PostMessage(IntPtr window,uint message,UIntPtr parameter,IntPtr data);
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,uint processId);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref uint size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern bool ProcessIdToSessionId(uint processId,out uint sessionId);
    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root,out IntPtr scheme);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);

    public static int ModeBit(int mode){return mode>=1&&mode<=5?1<<(mode-1):0;}

    public static ArmouryCrateSnapshot Read()
    {
        ArmouryCrateSnapshot state=new ArmouryCrateSnapshot();
        try{state.Installed=ProgramAvailability.IsArmouryCrateInstalled();}catch{}
        if(!state.Installed)return state;
        try{
            PowerStatus power;
            if(GetSystemPowerStatus(out power)&&power.ACLineStatus<=1){state.PowerKnown=true;state.PowerOnline=power.ACLineStatus==1;}
        }catch{}
        try{
            using(RegistryKey root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))using(RegistryKey key=root.OpenSubKey(RegistryPath,false)){
                if(key!=null){
                    state.SupportedModes=ReadNumber(key,"DisplayModes");state.EnabledModes=ReadNumber(key,"EnabledModes");state.SelectedMode=ReadNumber(key,"SelectedMode");
                    int locked=ReadNumber(key,"IsLocked");state.Locked=locked!=0;
                    state.StateKnown=state.SupportedModes>0&&state.SupportedModes<=31&&state.EnabledModes>=0&&state.EnabledModes<=31&&(state.EnabledModes&~state.SupportedModes)==0&&state.SelectedMode>0&&state.SelectedMode<=16&&(state.SelectedMode&(state.SelectedMode-1))==0&&(state.SelectedMode&state.SupportedModes)!=0&&(locked==0||locked==1);
                }
            }
        }catch{}
        try{state.ReceiverWindow=FindReceiver(out state.ReceiverPath);state.ReceiverAvailable=state.ReceiverWindow!=IntPtr.Zero;}catch{}
        try{
            if(state.PowerKnown&&(state.EnabledModes&16)!=0){
                string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),@"ASUS\ARMOURY CRATE Config\Manual\ThrottlePlugin."+(state.PowerOnline?"AC":"DC")+"ManualConfig.bin");
                using(FileStream stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
                    if(stream.Length>0&&stream.Length<=MaximumProfileBytes){
                        byte[] data=new byte[(int)stream.Length];int offset=0,count;
                        while(offset<data.Length&&(count=stream.Read(data,offset,data.Length-offset))>0)offset+=count;
                        if(offset==data.Length)state.ManualConfigured=HasConfiguredManualProfile(data);
                    }
                }
            }
        }catch{}
        state.ActivePowerScheme=ReadPowerScheme();
        return state;
    }

    static int ReadNumber(RegistryKey key,string name){object value=key.GetValue(name);return value is int?(int)value:-1;}

    static string ReadPowerScheme()
    {
        IntPtr memory=IntPtr.Zero;
        try{if(PowerGetActiveScheme(IntPtr.Zero,out memory)==0&&memory!=IntPtr.Zero)return ((Guid)Marshal.PtrToStructure(memory,typeof(Guid))).ToString("D");}catch{}
        finally{if(memory!=IntPtr.Zero)LocalFree(memory);}
        return null;
    }

    static IntPtr FindReceiver(out string imagePath)
    {
        imagePath=null;IntPtr window=FindWindow(ReceiverClass,ReceiverTitle);if(window==IntPtr.Zero)return IntPtr.Zero;
        StringBuilder className=new StringBuilder(256);
        if(GetClassName(window,className,className.Capacity)==0||className.ToString()!=ReceiverClass)return IntPtr.Zero;
        uint processId,sessionId,selfSessionId;GetWindowThreadProcessId(window,out processId);
        using(Process self=Process.GetCurrentProcess())if(processId==0||!ProcessIdToSessionId(processId,out sessionId)||!ProcessIdToSessionId((uint)self.Id,out selfSessionId)||sessionId!=selfSessionId)return IntPtr.Zero;
        IntPtr process=OpenProcess(0x1000,false,processId);if(process==IntPtr.Zero)return IntPtr.Zero;
        try{
            uint size=32768;StringBuilder path=new StringBuilder((int)size);
            if(!QueryFullProcessImageName(process,0,path,ref size))return IntPtr.Zero;
            string actual=Path.GetFullPath(path.ToString());bool expected=false;
            foreach(Environment.SpecialFolder folder in new[]{Environment.SpecialFolder.ProgramFiles,Environment.SpecialFolder.ProgramFilesX86}){
                string parent=Environment.GetFolderPath(folder);if(String.IsNullOrEmpty(parent))continue;
                string candidate=Path.Combine(parent,@"ASUS\Armoury Crate Service\ArmouryCrate.UserSessionHelper.exe");
                if(String.Equals(actual,Path.GetFullPath(candidate),StringComparison.OrdinalIgnoreCase))expected=true;
            }
            if(!expected||!File.Exists(actual))return IntPtr.Zero;
            FileVersionInfo version=FileVersionInfo.GetVersionInfo(actual);
            if(String.IsNullOrEmpty(version.CompanyName)||version.CompanyName.IndexOf("ASUS",StringComparison.OrdinalIgnoreCase)<0)return IntPtr.Zero;
            imagePath=actual;return window;
        }finally{CloseHandle(process);}
    }

    static bool Send(int modeBit)
    {
        string path;IntPtr window=FindReceiver(out path);if(window==IntPtr.Zero)return false;
        uint message=RegisterWindowMessage(MessageName);
        return message!=0&&PostMessage(window,message,new UIntPtr(2),new IntPtr(modeBit));
    }

    public static StepResult Apply(int mode){return Apply(mode,Read,Send,Thread.Sleep);}

    public static StepResult Apply(int mode,Func<ArmouryCrateSnapshot> read,Func<int,bool> send,Action<int> wait)
    {
        const string step="ArmouryMode";
        try{
            if(mode==0)return new StepResult(step,"skipped","Режим работы Armoury Crate не изменяется.");
            int target=ModeBit(mode);if(target==0)return new StepResult(step,"skipped","Неизвестный режим работы Armoury Crate.");
            if(read==null||send==null||wait==null)return new StepResult(step,"skipped","Управление Armoury Crate недоступно.");
            ArmouryCrateSnapshot initial=read();string reason=Unavailable(initial,mode,target);
            if(reason!=null)return new StepResult(step,"skipped",reason);
            bool initiallyOnline=initial.PowerOnline;
            ArmouryCrateSnapshot current=read();reason=Unavailable(current,mode,target);
            if(reason!=null)return new StepResult(step,"skipped",reason);
            if(current.PowerOnline!=initiallyOnline)return new StepResult(step,"skipped","Источник питания изменился; режим Armoury Crate пропущен.");
            if(!send(target))return new StepResult(step,"error","Не удалось доставить команду службе Armoury Crate.");
            int stable=0;string scheme=null;
            for(int poll=0;poll<MaximumPolls;poll++){
                current=read();reason=Unavailable(current,mode,target);
                if(current!=null&&current.PowerKnown&&current.PowerOnline!=initiallyOnline)return new StepResult(step,"error","Источник питания изменился во время переключения Armoury Crate.");
                if(reason==null&&current.SelectedMode==target&&!String.IsNullOrEmpty(current.ActivePowerScheme)){
                    stable=String.Equals(scheme,current.ActivePowerScheme,StringComparison.OrdinalIgnoreCase)?stable+1:1;scheme=current.ActivePowerScheme;
                    if(stable>=SettledPolls)return new StepResult(step,"ok","Выбор режима Armoury Crate подтверждён.");
                }else{stable=0;scheme=null;}
                if(poll+1<MaximumPolls)wait(PollMilliseconds);
            }
            return new StepResult(step,"error","Служба Armoury Crate не подтвердила устойчивое переключение режима.");
        }catch(Exception exception){return new StepResult(step,"error","Управление Armoury Crate недоступно: "+exception.GetType().Name+".");}
    }

    static string Unavailable(ArmouryCrateSnapshot state,int mode,int target)
    {
        if(state==null)return "Не удалось прочитать состояние Armoury Crate.";
        if(!state.Installed)return "Armoury Crate не установлен.";
        if(!state.ReceiverAvailable)return "Служебный получатель Armoury Crate недоступен.";
        if(!state.StateKnown)return "Состояние режимов Armoury Crate недоступно.";
        if(!state.PowerKnown)return "Не удалось определить источник питания для Armoury Crate.";
        if(state.Locked)return "Изменение режима Armoury Crate заблокировано.";
        if((state.SupportedModes&target)==0)return "Выбранный режим не поддерживается Armoury Crate.";
        if((state.EnabledModes&target)==0)return "Выбранный режим Armoury Crate сейчас недоступен.";
        if(mode==4&&!state.PowerOnline)return "Турбо Armoury Crate недоступен при питании от батареи.";
        if(mode==5&&!state.ManualConfigured)return "Сохранённый применённый ручной профиль Armoury Crate не найден.";
        return null;
    }

    public static bool HasConfiguredManualProfile(byte[] data)
    {
        try{
            if(data==null||data.Length==0||data.Length>MaximumProfileBytes)return false;
            Dictionary<string,ProfileValue> root=ReadProfileMap(data,0,data.Length);ProfileValue applied,list,profile;Guid identifier;
            if(!root.TryGetValue("Apply",out applied)||applied.Map||String.IsNullOrEmpty(applied.Text)||applied.Text.Length!=38||!Guid.TryParse(applied.Text,out identifier))return false;
            if(!root.TryGetValue("ProfileList",out list)||list.Map||String.IsNullOrEmpty(list.Text)||list.Text.IndexOf(identifier.ToString("B"),StringComparison.OrdinalIgnoreCase)<0)return false;
            return root.TryGetValue(applied.Text,out profile)&&profile.Map&&profile.Length>0;
        }catch{return false;}
    }

    static Dictionary<string,ProfileValue> ReadProfileMap(byte[] data,int start,int length)
    {
        Dictionary<string,ProfileValue> values=new Dictionary<string,ProfileValue>(StringComparer.Ordinal);int end=checked(start+length),position=start;
        if(start<0||length<0||end>data.Length)throw new InvalidDataException();
        while(position<end){
            if(values.Count>=512)throw new InvalidDataException();
            int keyLength=data[position++];if(keyLength==0||keyLength>end-position)throw new InvalidDataException();
            string key=new UTF8Encoding(false,true).GetString(data,position,keyLength);position+=keyLength;
            if(position>=end)throw new InvalidDataException();int kind=data[position++];
            if((kind!=0x10&&kind!=0x20)||end-position<4)throw new InvalidDataException();
            uint byteLength=BitConverter.ToUInt32(data,position);position+=4;
            if(byteLength>(uint)(end-position))throw new InvalidDataException();
            ProfileValue value=new ProfileValue{Map=kind==0x20,Offset=position,Length=(int)byteLength};
            if(!value.Map){if((byteLength&1)!=0)throw new InvalidDataException();value.Text=new UnicodeEncoding(false,false,true).GetString(data,position,(int)byteLength);}
            values.Add(key,value);position+=(int)byteLength;
        }
        return values;
    }
}
