using System;
using System.Runtime.InteropServices;
using System.Threading;

// CCD targets are matched by their monitor interface path, never by screen number.
// Native layouts and packet IDs: Microsoft's WinSDK wingdi.h (win32metadata).
public static class HdrControl {
 const uint ActivePaths=0x2,VirtualModeAware=0x10,VirtualRefreshAware=0x40;
 const int InsufficientBuffer=122,NotSupported=50,InvalidParameter=87;
 [StructLayout(LayoutKind.Sequential)] struct Luid {public uint Low;public int High;}
 [StructLayout(LayoutKind.Sequential)] struct Rational {public uint Numerator,Denominator;}
 [StructLayout(LayoutKind.Sequential)] struct Source {public Luid Adapter;public uint Id,ModeIndex,Flags;}
 [StructLayout(LayoutKind.Sequential)] struct Target {public Luid Adapter;public uint Id,ModeIndex,Technology,Rotation,Scaling;public Rational Refresh;public uint ScanLine;public int Available;public uint Flags;}
 [StructLayout(LayoutKind.Sequential)] struct Path {public Source Source;public Target Target;public uint Flags;}
 [StructLayout(LayoutKind.Sequential)] struct Header {public uint Type,Size;public Luid Adapter;public uint Id;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct TargetName {
  public Header Header;public uint Flags,Technology;public ushort Manufacturer,Product;public uint Connector;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string FriendlyName;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string MonitorPath;
 }
 [StructLayout(LayoutKind.Sequential)] struct ColorInfo {public Header Header;public uint Flags,Encoding,BitsPerChannel;}
 [StructLayout(LayoutKind.Sequential)] struct ColorInfo2 {public Header Header;public uint Flags,Encoding,BitsPerChannel,ActiveColorMode;}
 [StructLayout(LayoutKind.Sequential)] struct ColorState {public Header Header;public uint Enabled;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct OsVersion {
  public uint Size,Major,Minor,Build,Platform;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string ServicePack;
 }
 sealed class Snapshot {public Target Target;public bool Modern,Supported,Enabled,Limited;}
 [DllImport("user32.dll")]static extern int GetDisplayConfigBufferSizes(uint flags,out uint paths,out uint modes);
 [DllImport("user32.dll")]static extern int QueryDisplayConfig(uint flags,ref uint paths,[Out]Path[] pathArray,ref uint modes,IntPtr modeArray,IntPtr topology);
 [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo",CharSet=CharSet.Unicode)]static extern int GetName(ref TargetName packet);
 [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo")]static extern int GetColor(ref ColorInfo packet);
 [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo")]static extern int GetColor2(ref ColorInfo2 packet);
 [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo")]static extern int GetContentBrightness(ref ColorState packet);
 [DllImport("user32.dll",EntryPoint="DisplayConfigSetDeviceInfo")]static extern int SetColor(ref ColorState packet);
 [DllImport("ntdll.dll",CharSet=CharSet.Unicode)]static extern int RtlGetVersion(ref OsVersion version);

 static Header Packet(uint type,Type layout,Target target) {return new Header{Type=type,Size=(uint)Marshal.SizeOf(layout),Adapter=target.Adapter,Id=target.Id};}
 static bool LegacyWindows11() {
  OsVersion version=new OsVersion();version.Size=(uint)Marshal.SizeOf(typeof(OsVersion));
  return RtlGetVersion(ref version)==0&&version.Major==10&&version.Build>=22000&&version.Build<26100;
 }
 static bool NameMatches(Target target,string monitorId) {
  TargetName name=new TargetName();name.Header=Packet(2,typeof(TargetName),target);
  return GetName(ref name)==0&&String.Equals(name.MonitorPath,monitorId,StringComparison.OrdinalIgnoreCase);
 }
 static bool FindTarget(string monitorId,out Target target) {
  target=new Target();if(String.IsNullOrWhiteSpace(monitorId))return false;
  uint flags=ActivePaths|VirtualModeAware|VirtualRefreshAware;
  for(int attempt=0;attempt<4;attempt++) {
   uint pathCount,modeCount;int result=GetDisplayConfigBufferSizes(flags,out pathCount,out modeCount);
   if(result==InvalidParameter&&(flags&VirtualRefreshAware)!=0){flags&=~VirtualRefreshAware;continue;}
   if(result!=0||pathCount==0||pathCount>4096||modeCount>16384)return false;
   Path[] paths=new Path[pathCount];IntPtr modes=Marshal.AllocHGlobal(checked((int)Math.Max(1,modeCount)*64));
   try {
    result=QueryDisplayConfig(flags,ref pathCount,paths,ref modeCount,modes,IntPtr.Zero);
    if(result==InsufficientBuffer)continue;
    if(result==InvalidParameter&&(flags&VirtualRefreshAware)!=0){flags&=~VirtualRefreshAware;continue;}
    if(result!=0)return false;
    int matches=0;
    for(int i=0;i<pathCount;i++)if((paths[i].Flags&1)!=0&&paths[i].Target.Available!=0&&NameMatches(paths[i].Target,monitorId)){target=paths[i].Target;matches++;}
    return matches==1;
   }finally{Marshal.FreeHGlobal(modes);}
  }
  return false;
 }
 static bool Read(Target target,out Snapshot snapshot) {
  snapshot=new Snapshot();snapshot.Target=target;
  ColorInfo2 modern=new ColorInfo2();modern.Header=Packet(15,typeof(ColorInfo2),target);
  int result=GetColor2(ref modern);
  if(result==0){snapshot.Modern=true;snapshot.Supported=(modern.Flags&0x10)!=0;snapshot.Enabled=(modern.Flags&0x20)!=0;snapshot.Limited=(modern.Flags&8)!=0;return true;}
  // On recent Windows the legacy packet also controls WCG. Never use it there.
  if((result!=InvalidParameter&&result!=NotSupported)||!LegacyWindows11())return false;
  ColorInfo legacy=new ColorInfo();legacy.Header=Packet(9,typeof(ColorInfo),target);
  if(GetColor(ref legacy)!=0||(legacy.Flags&4)!=0)return false;
  snapshot.Supported=(legacy.Flags&1)!=0;snapshot.Enabled=(legacy.Flags&2)!=0;snapshot.Limited=(legacy.Flags&8)!=0;return true;
 }
 static DisplayChangeResult Skip(string detail){return new DisplayChangeResult("skipped",detail);}

 // Read-only capability probe. It never activates HDR or changes display modes.
 public static bool TryInspect(string monitorId,out bool supported,out bool enabled) {
  supported=false;enabled=false;
  try{Target target;Snapshot state;if(!FindTarget(monitorId,out target)||!Read(target,out state))return false;supported=state.Supported;enabled=state.Enabled;return true;}
  catch{return false;}
 }
 // Private CCD CABC packets used by this Windows installation's display Settings.
 // GET=-36, SET=-35; HEADER20 + DWORD24. Option 0=off, 1=always, 2=battery only.
 // This changes content-adaptive brightness, independently of ambient-light sensing.
 static readonly bool KnownContentBrightnessApi=CheckContentBrightnessApi();
 static bool CheckContentBrightnessApi() {
  try {
   System.Diagnostics.FileVersionInfo version=System.Diagnostics.FileVersionInfo.GetVersionInfo(System.IO.Path.Combine(Environment.SystemDirectory,"SettingsHandlers_PCDisplay.dll"));
   return version.FileMajorPart==10&&version.FileMinorPart==0&&version.FileBuildPart==26100;
  }catch{return false;}
 }
 static bool ReadContentBrightness(Target target,out uint option) {
  option=0;if(!KnownContentBrightnessApi)return false;
  ColorState packet=new ColorState();packet.Header=Packet(unchecked((uint)-36),typeof(ColorState),target);
  int result=GetContentBrightness(ref packet);option=packet.Enabled;
  return result==0&&option<=2;
 }
 public static bool TryInspectContentBrightness(string monitorId,out int option) {
  option=-1;
  try{Target target;uint value;if(!FindTarget(monitorId,out target)||!ReadContentBrightness(target,out value))return false;option=(int)value;return true;}
  catch{return false;}
 }
 public static DisplayChangeResult ApplyContentBrightness(string monitorId,bool enabled) {
  try {
   Target target;if(!FindTarget(monitorId,out target))return Skip("Выбранный дисплей отключён или не определён однозначно.");
   uint option,desired=enabled?1u:0u;
   if(!ReadContentBrightness(target,out option))return Skip("Оптимизация яркости по содержимому для выбранного дисплея недоступна.");
   if(option==desired)return new DisplayChangeResult("ok",enabled?"Оптимизация яркости по содержимому уже включена.":"Оптимизация яркости по содержимому уже выключена.");
   Target current;
   if(!FindTarget(monitorId,out current)||current.Id!=target.Id||current.Adapter.Low!=target.Adapter.Low||current.Adapter.High!=target.Adapter.High)return Skip("Подключение выбранного дисплея изменилось.");
   ColorState request=new ColorState();request.Header=Packet(unchecked((uint)-35),typeof(ColorState),target);request.Enabled=desired;
   int result=SetColor(ref request);
   if(result!=0)return Skip("Драйвер не применил оптимизацию яркости по содержимому; код "+result+".");
   for(int attempt=0;attempt<5;attempt++) {
    if(attempt>0)Thread.Sleep(50);
    uint observed;
    if(!NameMatches(target,monitorId))return Skip("Выбранный дисплей отключён во время изменения яркости по содержимому.");
    if(ReadContentBrightness(target,out observed)&&observed==desired)return new DisplayChangeResult("ok",enabled?"Оптимизация яркости по содержимому включена.":"Оптимизация яркости по содержимому выключена.");
   }
   return new DisplayChangeResult("error","Не удалось подтвердить выбранную оптимизацию яркости по содержимому.");
  }catch{return Skip("Оптимизация яркости по содержимому для выбранного дисплея недоступна.");}
 }
 public static DisplayChangeResult Apply(string monitorId,bool enabled) {
  try {
   Target target;if(!FindTarget(monitorId,out target))return Skip("Выбранный дисплей отключён или не определён однозначно.");
   Snapshot state;if(!Read(target,out state))return Skip("Управление HDR для выбранного дисплея недоступно.");
   if(!state.Supported)return Skip("Выбранный дисплей не поддерживает HDR.");
   if(state.Enabled==enabled)return new DisplayChangeResult("ok",enabled?"HDR уже включён.":"HDR уже выключен.");
   if(enabled&&state.Limited)return Skip("Включение HDR ограничено политикой Windows.");
   Target current;
   if(!FindTarget(monitorId,out current)||current.Id!=target.Id||current.Adapter.Low!=target.Adapter.Low||current.Adapter.High!=target.Adapter.High)return Skip("Подключение выбранного дисплея изменилось.");
   ColorState request=new ColorState();request.Header=Packet(state.Modern?16u:10u,typeof(ColorState),target);request.Enabled=enabled?1u:0u;
   int result=SetColor(ref request);
   if(result!=0)return Skip("Драйвер не применил HDR; код "+result+".");
   for(int attempt=0;attempt<5;attempt++) {
    if(attempt>0)Thread.Sleep(50);
    Snapshot observed;
    if(!NameMatches(target,monitorId))return Skip("Выбранный дисплей отключён во время изменения HDR.");
    if(Read(target,out observed)&&observed.Enabled==enabled)return new DisplayChangeResult("ok",enabled?"HDR включён.":"HDR выключен.");
   }
   return new DisplayChangeResult("error","Не удалось подтвердить выбранное состояние HDR.");
  }catch{return Skip("Управление HDR для выбранного дисплея недоступно.");}
 }
}
