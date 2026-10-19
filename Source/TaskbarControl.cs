using System;using System.Runtime.InteropServices;using System.Threading;using Microsoft.Win32;
public static class TaskbarControl{
 const string Path=@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string cls,string title);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint message,UIntPtr w,IntPtr data,uint flags,uint timeout,out UIntPtr result);
 static bool Notify(IntPtr window){IntPtr text=Marshal.StringToHGlobalUni("TraySettings");try{UIntPtr result;return SendMessageTimeout(window,0x1A,UIntPtr.Zero,text,2,500,out result)!=IntPtr.Zero;}finally{Marshal.FreeHGlobal(text);}}
 public static StepResult SetPreference(bool battery,bool enabled){string name=battery?"IsBatteryPercentageEnabled":"ShowSecondsInSystemClock";try{IntPtr window=FindWindow("Shell_TrayWnd",null);if(window==IntPtr.Zero)return new StepResult(name,"skipped","Панель задач недоступна.");using(RegistryKey key=Registry.CurrentUser.OpenSubKey(Path,true)){if(key==null)return new StepResult(name,"skipped","Настройки панели задач недоступны.");int target=enabled?1:0;if(Object.Equals(key.GetValue(name),target))return new StepResult(name,"ok","Параметр панели задач уже выбран.");key.SetValue(name,target,RegistryValueKind.DWord);bool notified=Notify(window);return new StepResult(name,notified?"ok":"skipped",notified?"Параметр панели задач изменён.":"Настройка сохранена; панель задач не ответила вовремя.");}}catch{return new StepResult(name,"skipped","Настройка панели задач недоступна.");}}
 public static StepResult Apply(int mode){
  const string step="TaskbarRepair";if(mode<1||mode>2)return new StepResult(step,"skipped","Режим исправления не выбран.");
  IntPtr window=FindWindow("Shell_TrayWnd",null);if(window==IntPtr.Zero)return new StepResult(step,"skipped","Панель задач недоступна.");
  try{using(RegistryKey key=Registry.CurrentUser.OpenSubKey(Path,true)){
   if(key==null)return new StepResult(step,"skipped","Настройки панели задач недоступны.");
   object old=key.GetValue("MMTaskbarMode",null);RegistryValueKind kind=old==null?RegistryValueKind.DWord:key.GetValueKind("MMTaskbarMode");bool completed=false;
   try{key.SetValue("MMTaskbarMode",0,RegistryValueKind.DWord);if(!Notify(window))return new StepResult(step,"skipped","Панель задач не ответила вовремя.");
    Thread.Sleep(60);key.SetValue("MMTaskbarMode",mode,RegistryValueKind.DWord);if(!Notify(window))return new StepResult(step,"skipped","Панель задач не подтвердила конечный режим.");
    completed=true;return new StepResult(step,"ok","Все панели задач -> режим "+mode+".");
   }finally{if(!completed){if(old==null)key.DeleteValue("MMTaskbarMode",false);else key.SetValue("MMTaskbarMode",old,kind);Notify(window);}}
  }}catch{return new StepResult(step,"skipped","Изменение режима панели задач недоступно.");}
 }
}
