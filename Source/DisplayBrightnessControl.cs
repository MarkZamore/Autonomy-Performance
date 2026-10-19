using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Threading;

// WMI exposes brightness on only some panels. Never redirect a missing target
// to another monitor or to the global brightness action used by old exports.
public static class DisplayBrightnessControl
{
    static readonly TimeSpan WmiTimeout = TimeSpan.FromSeconds(2);
    sealed class Endpoint
    {
        public string InstanceName, MethodPath;
        public byte Brightness;
    }
    sealed class WmiInstance
    {
        public string Name, Path;
        public byte Brightness;
    }
    static ManagementScope Scope()
    {
        return new ManagementScope(@"\\.\root\wmi",new ConnectionOptions { Timeout=WmiTimeout });
    }
    static List<WmiInstance> ReadInstances(string query,string target,bool includeBrightness)
    {
        List<WmiInstance> result=new List<WmiInstance>();
        using(ManagementObjectSearcher searcher=new ManagementObjectSearcher(Scope(),new ObjectQuery(query),new EnumerationOptions { ReturnImmediately=false,Timeout=WmiTimeout,Rewindable=false }))
        using(ManagementObjectCollection objects=searcher.Get())
            foreach(ManagementObject value in objects)
                using(value) {
                    string name=Convert.ToString(value["InstanceName"]);
                    if(!String.Equals(NormalizeInstance(name),target,StringComparison.OrdinalIgnoreCase))continue;
                    result.Add(new WmiInstance { Name=name,Path=value.Path.Path,Brightness=includeBrightness?Convert.ToByte(value["CurrentBrightness"]):(byte)0 });
                }
        return result;
    }
    static string NormalizeMonitor(string path)
    {
        if(String.IsNullOrEmpty(path)||!path.StartsWith(@"\\?\",StringComparison.Ordinal))return null;
        int suffix=path.LastIndexOf("#{",StringComparison.Ordinal);
        Guid interfaceClass;
        if(suffix<4||!Guid.TryParse(path.Substring(suffix+1),out interfaceClass))return null;
        string instance=path.Substring(4,suffix-4).Replace('#','\\');
        return instance.StartsWith(@"DISPLAY\",StringComparison.OrdinalIgnoreCase)?instance:null;
    }
    static string NormalizeInstance(string value)
    {
        if(String.IsNullOrEmpty(value)||!value.StartsWith(@"DISPLAY\",StringComparison.OrdinalIgnoreCase))return null;
        int suffix=value.LastIndexOf('_');
        if(suffix<0||suffix==value.Length-1)return null;
        for(int i=suffix+1;i<value.Length;i++)if(value[i]<'0'||value[i]>'9')return null;
        return value.Substring(0,suffix);
    }
    static Endpoint ReadTarget(string monitorId)
    {
        List<DisplayInfo> matches=DisplayControl.Enumerate().FindAll(delegate(DisplayInfo value) { return String.Equals(value.MonitorId,monitorId,StringComparison.OrdinalIgnoreCase); });
        if(matches.Count!=1)return null;
        string target=NormalizeMonitor(matches[0].MonitorId);
        if(target==null)return null;
        List<WmiInstance> values=ReadInstances("SELECT InstanceName,CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE",target,true);
        List<WmiInstance> methods=ReadInstances("SELECT InstanceName FROM WmiMonitorBrightnessMethods WHERE Active=TRUE",target,false);
        if(values.Count!=1||methods.Count!=1||!String.Equals(values[0].Name,methods[0].Name,StringComparison.OrdinalIgnoreCase))return null;
        return new Endpoint { InstanceName=values[0].Name,MethodPath=methods[0].Path,Brightness=values[0].Brightness };
    }
    public static bool Available(string monitorId)
    {
        try { return ReadTarget(monitorId)!=null; } catch { return false; }
    }
    public static StepResult Apply(string monitorId,int percent)
    {
        const string step="DisplayBrightness";
        try {
            if(percent<0||percent>100)return new StepResult(step,"skipped","Недопустимое значение яркости.");
            Endpoint target=ReadTarget(monitorId);
            if(target==null)return new StepResult(step,"skipped","Яркость выбранного дисплея недоступна.");
            if(target.Brightness==percent)return new StepResult(step,"ok","Яркость выбранного дисплея уже установлена.");
            using(ManagementObject monitor=new ManagementObject(Scope(),new ManagementPath(target.MethodPath),new ObjectGetOptions { Timeout=WmiTimeout }))
            using(ManagementBaseObject parameters=monitor.GetMethodParameters("WmiSetBrightness")) {
                // Recheck the exact active endpoint before invoking its method.
                Endpoint current=ReadTarget(monitorId);
                if(current==null||!String.Equals(current.InstanceName,target.InstanceName,StringComparison.OrdinalIgnoreCase)||!String.Equals(current.MethodPath,target.MethodPath,StringComparison.OrdinalIgnoreCase))
                    return new StepResult(step,"skipped","Выбранный дисплей изменился во время подготовки.");
                parameters["Timeout"]=0U;parameters["Brightness"]=(byte)percent;
                using(ManagementBaseObject output=monitor.InvokeMethod("WmiSetBrightness",parameters,new InvokeMethodOptions { Timeout=WmiTimeout }))
                    if(output!=null&&output["ReturnValue"]!=null&&Convert.ToUInt32(output["ReturnValue"])!=0)
                        return new StepResult(step,"skipped","Дисплей не принял выбранную яркость.");
            }
            Stopwatch wait=Stopwatch.StartNew();
            do {
                Endpoint observed=ReadTarget(monitorId);
                if(observed==null)return new StepResult(step,"skipped","Выбранный дисплей отключён во время проверки.");
                if(observed.Brightness==percent)return new StepResult(step,"ok",monitorId+": "+percent+"%.");
                Thread.Sleep(50);
            } while(wait.ElapsedMilliseconds<1500);
            return new StepResult(step,"skipped","Изменение яркости выбранного дисплея не подтверждено.");
        } catch { return new StepResult(step,"skipped","Управление яркостью выбранного дисплея недоступно."); }
    }
}
