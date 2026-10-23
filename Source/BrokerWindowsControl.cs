using System;
using Microsoft.Win32;
using WindowsPreferences;

// The installed Windows model owns transitions and notifications. The exact
// choice order is enabled only for the build validated with native readback.
internal static class BrokerWindowsControl
{
    static readonly bool certified=CertifiedBuild();
    internal static bool Certified {get{return certified;}}
    static bool CertifiedBuild()
    {
        try {using(var k=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            return k!=null&&Convert.ToString(k.GetValue("CurrentBuildNumber"))=="26300"&&Equals(k.GetValue("UBR"),9711);}
        catch{return false;}
    }
    internal static bool Boolean(WindowsPreferenceId id,bool value,string name,out StepResult result)
    {
        result=null;if(!certified)return false;
        return Apply(id,s=>value,name,()=>NativeWindowsPreferences.Open(id),false,out result);
    }
    internal static bool Choice(WindowsPreferenceId id,int index,int expectedCount,string name,bool nativeContextFallback,out StepResult result)
    {
        result=null;if(!certified)return false;
        return Apply(id,s=>{
            if(s.Choices.Length!=expectedCount)throw new InvalidOperationException("Unexpected Windows choices.");
            int selected=index<0?s.Choices.Length-1:index;
            if(selected<0||selected>=s.Choices.Length)throw new InvalidOperationException("Unavailable Windows choice.");
            return s.Choices[selected];
        },name,()=>NativeWindowsPreferences.Open(id),nativeContextFallback,out result);
    }
    internal static bool Apply(WindowsPreferenceId id,Func<WindowsPreferenceSnapshot,object> target,string name,
        Func<IWindowsPreferenceSession> open,bool nativeContextFallback,out StepResult result)
    {
        result=null;
        try {
            using(var session=open()) {
                var before=session.Read();
                if(before==null)throw new InvalidOperationException("Missing Windows state.");
                if(before.IsSetByGroupPolicy||before.IsUpdating){result=Skipped(name);return true;}
                if(!before.IsApplicable||!before.IsEnabled){
                    if(nativeContextFallback)return false;
                    result=Skipped(name);return true;
                }
                object desired=target(before);WindowsPreferenceCatalog.Get(id).Validate(desired);
                if(!Equals(before.Value,desired))session.Set(desired);
                var after=session.Read();
                if(after==null||!after.IsApplicable||after.IsSetByGroupPolicy||after.IsUpdating||!Equals(after.Value,desired))
                    {result=Skipped(name);return true;}
                result=new StepResult(name,"ok","Windows preference applied and verified.");return true;
            }
        }catch {result=Skipped(name);return true;}
    }
    static StepResult Skipped(string name){return new StepResult(name,"skipped","Windows did not confirm an available preference.");}
}
