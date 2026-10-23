using Microsoft.Win32;
using WindowsPreferences;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Web.Script.Serialization;

public sealed class ModeSnapshot
{
    public WindowsState Windows;
    public FanState Fan;
}

public sealed class RunReport
{
    public string Mode;
    public string StartedUtc;
    public string CompletedUtc;
    public int ProcessId;
    public List<StepResult> Steps = new List<StepResult>();
    public ModeSnapshot State;
}



public sealed class DisplayBrightnessState
{
    public string InstanceName;
    public byte CurrentBrightness;
}

public sealed class WindowsState
{
    public uint? EnergySaverOverride;
    public int? EnergySaverStatus;
    public string ACPowerMode;
    public string DCPowerMode;
    public byte? PowerACLine;
    public List<DisplayBrightnessState> Brightness = new List<DisplayBrightnessState>();
    public float? Volume;
    public bool? Muted;
    public string AudioEndpointId;
    public List<string> Errors = new List<string>();
}

public static class WindowsControl
{
    private const ulong SaverOverrideName = 0x41C6013DA3BC3075UL;
    private static readonly Guid SaverStatusGuid = new Guid("550e8400-e29b-41d4-a716-446655440000");
    private static readonly Guid BestPerformanceGuid = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");
    private static readonly Guid EndpointVolumeGuid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly TimeSpan WmiTimeout = TimeSpan.FromSeconds(3);

    public static WindowsState ReadState()
    {
        WindowsState state = new WindowsState();
        try { state.EnergySaverOverride = ReadSaverOverride(); }
        catch (Exception ex) { state.Errors.Add("EnergySaverOverride: " + ErrorText(ex)); }
        try
        {
            using (SaverObserver observer = new SaverObserver())
            {
                observer.WaitForInitial(1000);
                state.EnergySaverStatus = observer.Current;
                if (!state.EnergySaverStatus.HasValue)
                    state.Errors.Add("EnergySaverStatus: no initial notification");
            }
        }
        catch (Exception ex) { state.Errors.Add("EnergySaverStatus: " + ErrorText(ex)); }
        try { state.ACPowerMode = ReadPowerMode(true).ToString(); }
        catch (Exception ex) { state.Errors.Add("ACPowerMode: " + ErrorText(ex)); }
        try { state.DCPowerMode = ReadPowerMode(false).ToString(); }
        catch (Exception ex) { state.Errors.Add("DCPowerMode: " + ErrorText(ex)); }
        try
        {
            SystemPowerStatus status;
            if (!GetSystemPowerStatus(out status)) throw new Win32Exception(Marshal.GetLastWin32Error());
            state.PowerACLine = status.ACLineStatus;
        }
        catch (Exception ex) { state.Errors.Add("PowerACLine: " + ErrorText(ex)); }
        try
        {
            using (ManagementObjectSearcher searcher = Searcher("SELECT InstanceName, CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE"))
            using (ManagementObjectCollection collection = searcher.Get())
                foreach (ManagementObject monitor in collection)
                    using (monitor)
                        state.Brightness.Add(new DisplayBrightnessState {
                            InstanceName = Convert.ToString(monitor["InstanceName"]),
                            CurrentBrightness = Convert.ToByte(monitor["CurrentBrightness"])
                        });
        }
        catch (Exception ex) { state.Errors.Add("Brightness: " + ErrorText(ex)); }
        try
        {
            using (AudioEndpoint endpoint = new AudioEndpoint(null))
            {
                state.AudioEndpointId = endpoint.Id;
                state.Volume = endpoint.ReadVolume();
                state.Muted = endpoint.ReadMute();
            }
        }
        catch (Exception ex) { state.Errors.Add("Audio: " + ErrorText(ex)); }
        return state;
    }

    public static List<StepResult> SetEnergySaver(bool enabled)
    {
        return SetEnergySaver(enabled,value=>{StepResult result;return BrokerWindowsControl.Boolean(WindowsPreferenceId.EnergySaver,value,"EnergySaver",out result)?result:null;},
            ReadSaverOverride,SetSaverOverride,value=>{using(var observer=new SaverObserver())return observer.WaitForDesired(value,3000);});
    }
    internal static List<StepResult> SetEnergySaver(bool enabled,Func<bool,StepResult> brokerApply,Func<uint> readOverride,
        Func<uint,bool?,List<StepResult>> forceOverride,Func<bool,bool> observe)
    {
        try {
            uint desired=enabled?1u:2u;
            var broker=brokerApply(enabled);
            if(broker==null)return forceOverride(desired,enabled);
            if(broker.Status!="ok")return new List<StepResult>{broker};
            // The Boolean model can match an automatic saver. A preset means
            // forced On/Off; avoid issuing a matching toggle, and preserve the
            // explicit override through the scoped native API if necessary.
            if(readOverride()!=desired)return forceOverride(desired,enabled);
            if(!observe(enabled))throw new InvalidOperationException("Effective energy saver status did not converge.");
            return new List<StepResult>{broker};
        }catch(Exception ex){return new List<StepResult>{new StepResult("EnergySaver","error",ErrorText(ex))};}
    }

    public static List<StepResult> SetBestPerformance()
    {
        List<StepResult> results = new List<StepResult>();
        results.Add(SetPowerMode(true, BestPerformanceGuid));
        results.Add(SetPowerMode(false, BestPerformanceGuid));
        return results;
    }

    public static List<StepResult> SetBrightness(int percent)
    {
        if(percent<0||percent>100)throw new ArgumentOutOfRangeException("percent");
        return SetBrightness(null,(byte)percent);
    }

    public static List<StepResult> Restore(WindowsState state)
    {
        List<StepResult> results = new List<StepResult>();
        if (state == null)
        {
            results.Add(new StepResult("Restore", "error", "Saved Windows state is missing"));
            return results;
        }
        // Clear the active saver before restoring the saved power modes. Restore
        // the original saver override last, including automatic (0) if saved.
        // A partial diagnostic snapshot must not change a saver state that could
        // not be captured. Normal fully captured snapshots restore the override
        // last, including the automatic state (0).
        if (state.EnergySaverOverride.HasValue) results.AddRange(SetEnergySaver(false));
        RestorePowerMode(results, true, state.ACPowerMode);
        RestorePowerMode(results, false, state.DCPowerMode);
        if (state.Brightness != null && state.Brightness.Count != 0)
            results.AddRange(SetBrightness(state.Brightness));
        else
            results.Add(new StepResult("Brightness", "skipped", "No saved brightness values"));
        if (state.Volume.HasValue || state.Muted.HasValue)
        {
            try
            {
                if (String.IsNullOrEmpty(state.AudioEndpointId))
                    throw new InvalidOperationException("Saved audio endpoint ID is missing");
                using (AudioEndpoint endpoint = new AudioEndpoint(state.AudioEndpointId))
                {
                    if (state.Volume.HasValue) endpoint.SetVolume(state.Volume.Value);
                    if (state.Muted.HasValue) endpoint.SetMute(state.Muted.Value);
                    float actualVolume = endpoint.ReadVolume();
                    bool actualMute = endpoint.ReadMute();
                    if ((state.Volume.HasValue && Math.Abs(actualVolume - state.Volume.Value) > 0.001f) ||
                        (state.Muted.HasValue && actualMute != state.Muted.Value))
                        throw new InvalidOperationException("Restored audio did not match saved values");
                    results.Add(new StepResult("AudioRestore", "ok", "Restored saved volume/mute; endpoint=" + endpoint.Id));
                }
            }
            catch (Exception ex) { results.Add(new StepResult("AudioRestore", "error", ErrorText(ex))); }
        }
        else results.Add(new StepResult("AudioRestore", "skipped", "No saved audio values"));
        if (state.EnergySaverOverride.HasValue)
        {
            uint value = state.EnergySaverOverride.Value;
            bool? desired = value == 1 ? (bool?)true : value == 2 ? (bool?)false : null;
            results.AddRange(SetSaverOverride(value, desired));
        }
        else results.Add(new StepResult("EnergySaver", "skipped", "No saved saver override"));
        return results;
    }

    private static void RestorePowerMode(List<StepResult> results, bool ac, string saved)
    {
        string name = ac ? "PowerModeAC" : "PowerModeDC";
        try
        {
            Guid desired;
            if (String.IsNullOrEmpty(saved))
            {
                results.Add(new StepResult(name, "skipped", "No saved power mode"));
                return;
            }
            if (!Guid.TryParse(saved, out desired)) throw new InvalidOperationException("Invalid saved power mode GUID");
            results.Add(SetPowerMode(ac, desired));
        }
        catch (Exception ex) { results.Add(new StepResult(name, "error", ErrorText(ex))); }
    }

    public static StepResult SetPowerMode(bool ac, Guid desired)
    {
        string name = ac ? "PowerModeAC" : "PowerModeDC";
        try
        {
            if(ReadPowerMode(ac)==desired)return new StepResult(name,"ok",desired+" already selected.");
            StepResult broker;
            int choice=desired==BestPerformanceGuid?2:desired==Guid.Empty?1:desired==new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a")?0:-1;
            if(choice>=0 && BrokerWindowsControl.Choice(ac?WindowsPreferenceId.PowerModeAC:WindowsPreferenceId.PowerModeDC,choice,3,name,true,out broker)) {
                if(broker.Status!="ok")return broker;
                return new StepResult(name,ReadPowerMode(ac)==desired?"ok":"error","Broker power mode verified through the AC/DC power API.");
            }
            uint result = ac ? PowerSetUserConfiguredACPowerMode(ref desired) : PowerSetUserConfiguredDCPowerMode(ref desired);
            CheckWin32(result);
            Guid actual = ReadPowerMode(ac);
            if (actual != desired) throw new InvalidOperationException("Power mode read-back mismatch: " + actual);
            return new StepResult(name, "ok", actual.ToString());
        }
        catch (Exception ex) { return new StepResult(name, "error", ErrorText(ex)); }
    }

    public static Guid ReadPowerMode(bool ac)
    {
        Guid value;
        uint result = ac ? PowerGetUserConfiguredACPowerMode(out value) : PowerGetUserConfiguredDCPowerMode(out value);
        CheckWin32(result);
        return value;
    }

    // A preset may offer this internal Windows mechanism only when its exact
    // existing payload can be read. This does not subscribe, publish or create
    // WNF state and deliberately avoids the broader diagnostic ReadState().
    public static bool SupportsEnergySaver()
    {
        try { return ReadSaverOverride() <= 2; }
        catch { return false; }
    }

    private static uint ReadSaverOverride()
    {
        ulong name = SaverOverrideName;
        byte[] data = new byte[64];
        uint size = (uint)data.Length, stamp;
        CheckNt(NtQueryWnfStateData(ref name, IntPtr.Zero, IntPtr.Zero, out stamp, data, ref size));
        if (size != 4) throw new InvalidOperationException("Unexpected saver override payload length: " + size);
        return BitConverter.ToUInt32(data, 0);
    }

    private static List<StepResult> SetSaverOverride(uint value, bool? desiredEnabled)
    {
        List<StepResult> results = new List<StepResult>();
        SaverObserver observer = null;
        string observationError = null;
        try
        {
            if (value > 2) throw new InvalidOperationException("Unsupported saved saver override: " + value);
            try { observer = new SaverObserver(); }
            catch (Exception ex) { observationError = ErrorText(ex); }
            uint command = value;
            CheckNt(RtlPublishWnfStateData(SaverOverrideName, IntPtr.Zero, ref command, 4, IntPtr.Zero));
            uint actualOverride = ReadSaverOverride();
            if (actualOverride != value) throw new InvalidOperationException("Saver override read-back mismatch: " + actualOverride);
            if (desiredEnabled.HasValue)
            {
                if (observer == null)
                    throw new InvalidOperationException("Saver override applied, but actual state verification failed: " + observationError);
                if (!observer.WaitForDesired(desiredEnabled.Value, 3000))
                    throw new InvalidOperationException("Saver override applied, but actual EnergySaverStatus did not converge within 3 seconds; actual=" +
                        (observer.Current.HasValue ? observer.Current.Value.ToString() : "unavailable"));
            }
            results.Add(new StepResult("EnergySaver", "ok", "override=" + value + "; status=" +
                (observer != null && observer.Current.HasValue ? observer.Current.Value.ToString() : "unavailable")));
        }
        catch (Exception ex) { results.Add(new StepResult("EnergySaver", "error", ErrorText(ex))); }
        finally { if (observer != null) observer.Dispose(); }
        return results;
    }

    private static List<StepResult> SetBrightness(List<DisplayBrightnessState> saved, byte percent = 100)
    {
        List<StepResult> results = new List<StepResult>();
        Dictionary<string, byte> targets = null;
        HashSet<string> found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (saved != null)
            {
                targets = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
                foreach (DisplayBrightnessState item in saved)
                    if (item != null && !String.IsNullOrEmpty(item.InstanceName)) targets[item.InstanceName] = item.CurrentBrightness;
            }
            using (ManagementObjectSearcher searcher = Searcher("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE"))
            using (ManagementObjectCollection collection = searcher.Get())
            {
                foreach (ManagementObject monitor in collection)
                {
                    using (monitor)
                    {
                        string instance = null;
                        try
                        {
                            instance = Convert.ToString(monitor["InstanceName"]);
                            byte desired = percent;
                            if (targets != null && !targets.TryGetValue(instance, out desired)) continue;
                            found.Add(instance);
                            using (ManagementBaseObject parameters = monitor.GetMethodParameters("WmiSetBrightness"))
                            {
                                parameters["Timeout"] = 1U;
                                parameters["Brightness"] = desired;
                                using (ManagementBaseObject output = monitor.InvokeMethod("WmiSetBrightness", parameters,
                                    new InvokeMethodOptions { Timeout = WmiTimeout }))
                                {
                                    if (output != null && output["ReturnValue"] != null)
                                    {
                                        uint code = Convert.ToUInt32(output["ReturnValue"]);
                                        if (code != 0) throw new InvalidOperationException("WmiSetBrightness returned " + code);
                                    }
                                }
                            }
                            Stopwatch brightnessWait = Stopwatch.StartNew();
                            byte actual = ReadBrightness(instance);
                            while (actual != desired && brightnessWait.ElapsedMilliseconds < 2000)
                            {
                                Thread.Sleep(100);
                                actual = ReadBrightness(instance);
                            }
                            if (actual != desired) throw new InvalidOperationException("Brightness read-back mismatch after 2 seconds: requested=" + desired + "; actual=" + actual);
                            results.Add(new StepResult("Brightness", "ok", instance + "=" + actual + "%"));
                        }
                        catch (Exception ex) { results.Add(new StepResult("Brightness", "error", (instance ?? "unknown display") + ": " + ErrorText(ex))); }
                    }
                }
            }
            if (targets != null)
            {
                foreach (string instance in targets.Keys)
                    if (!found.Contains(instance)) results.Add(new StepResult("Brightness", "skipped", "Saved display is unavailable: " + instance));
            }
            if (results.Count == 0) results.Add(new StepResult("Brightness", "skipped", "No active WMI brightness-capable displays"));
        }
        catch (Exception ex) { results.Add(new StepResult("Brightness", "error", ErrorText(ex))); }
        return results;
    }

    private static byte ReadBrightness(string instance)
    {
        using (ManagementObjectSearcher searcher = Searcher("SELECT InstanceName, CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE"))
        using (ManagementObjectCollection collection = searcher.Get())
            foreach (ManagementObject monitor in collection)
                using (monitor)
                    if (String.Equals(Convert.ToString(monitor["InstanceName"]), instance, StringComparison.OrdinalIgnoreCase))
                        return Convert.ToByte(monitor["CurrentBrightness"]);
        throw new InvalidOperationException("Display disappeared during verification");
    }

    private static ManagementObjectSearcher Searcher(string query)
    {
        ManagementScope scope = new ManagementScope(@"\\.\root\wmi", new ConnectionOptions { Timeout = WmiTimeout });
        return new ManagementObjectSearcher(scope, new ObjectQuery(query), new EnumerationOptions {
            ReturnImmediately = false, Timeout = WmiTimeout, Rewindable = false
        });
    }

    private sealed class SaverObserver : IDisposable
    {
        private readonly ManualResetEvent changed = new ManualResetEvent(false);
        private readonly NotifyCallback callback;
        private IntPtr handle;
        private volatile int current = -1;
        public int? Current { get { int value = current; return value < 0 ? (int?)null : value; } }

        public SaverObserver()
        {
            callback = OnNotify;
            Guid setting = SaverStatusGuid;
            NotifyParameters recipient = new NotifyParameters {
                Callback = Marshal.GetFunctionPointerForDelegate(callback), Context = IntPtr.Zero
            };
            uint result = PowerSettingRegisterNotification(ref setting, 2, ref recipient, out handle);
            if (result != 0)
            {
                changed.Dispose();
                CheckWin32(result);
            }
        }

        private uint OnNotify(IntPtr context, uint type, IntPtr setting)
        {
            try
            {
                if (setting != IntPtr.Zero && Marshal.ReadInt32(setting, 16) == 4)
                {
                    int value = Marshal.ReadInt32(setting, 20);
                    if (value >= 0 && value <= 2) current = value;
                    changed.Set();
                }
            }
            catch { }
            return 0;
        }

        public void WaitForInitial(int timeout)
        {
            if (!Current.HasValue) changed.WaitOne(timeout);
        }

        public bool WaitForDesired(bool enabled, int timeout)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (true)
            {
                changed.Reset();
                int? value = Current;
                if (value.HasValue && ((value.Value != 0) == enabled)) return true;
                int remaining = timeout - (int)timer.ElapsedMilliseconds;
                if (remaining <= 0) return false;
                changed.WaitOne(remaining);
            }
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                try { PowerSettingUnregisterNotification(handle); } catch { }
                handle = IntPtr.Zero;
            }
            GC.KeepAlive(callback);
            changed.Dispose();
        }
    }

    private sealed class AudioEndpoint : IDisposable
    {
        private IMMDeviceEnumerator enumerator;
        private IMMDevice device;
        private IAudioEndpointVolume volume;
        public string Id;

        public AudioEndpoint(string endpointId)
        {
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                if (String.IsNullOrEmpty(endpointId)) CheckHr(enumerator.GetDefaultAudioEndpoint(0, 0, out device));
                else CheckHr(enumerator.GetDevice(endpointId, out device));
                CheckHr(device.GetId(out Id));
                Guid iid = EndpointVolumeGuid;
                object activated;
                CheckHr(device.Activate(ref iid, 23, IntPtr.Zero, out activated));
                volume = (IAudioEndpointVolume)activated;
            }
            catch { Dispose(); throw; }
        }

        public float ReadVolume() { float result; CheckHr(volume.GetMasterVolumeLevelScalar(out result)); return result; }
        public bool ReadMute() { bool result; CheckHr(volume.GetMute(out result)); return result; }
        public void SetVolume(float value)
        {
            if (Single.IsNaN(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException("value");
            Guid context = Guid.Empty;
            CheckHr(volume.SetMasterVolumeLevelScalar(value, ref context));
        }
        public void SetMute(bool value) { Guid context = Guid.Empty; CheckHr(volume.SetMute(value, ref context)); }
        public void Dispose()
        {
            ReleaseCom(volume); volume = null;
            ReleaseCom(device); device = null;
            ReleaseCom(enumerator); enumerator = null;
        }
    }

    private static void ReleaseCom(object value)
    {
        if (value != null) try { Marshal.ReleaseComObject(value); } catch { }
    }

    private static void CheckWin32(uint error) { if (error != 0) throw new Win32Exception(unchecked((int)error)); }
    private static void CheckHr(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
    private static void CheckNt(int result)
    {
        if (result < 0) throw new InvalidOperationException("NTSTATUS 0x" + unchecked((uint)result).ToString("X8"));
    }
    private static string ErrorText(Exception ex) { return ex.GetType().Name + ": " + ex.Message; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyParameters { public IntPtr Callback, Context; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint NotifyCallback(IntPtr context, uint type, IntPtr setting);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    [DllImport("powrprof.dll")]
    private static extern uint PowerGetUserConfiguredACPowerMode(out Guid mode);
    [DllImport("powrprof.dll")]
    private static extern uint PowerGetUserConfiguredDCPowerMode(out Guid mode);
    [DllImport("powrprof.dll")]
    private static extern uint PowerSetUserConfiguredACPowerMode(ref Guid mode);
    [DllImport("powrprof.dll")]
    private static extern uint PowerSetUserConfiguredDCPowerMode(ref Guid mode);
    [DllImport("powrprof.dll")]
    private static extern uint PowerSettingRegisterNotification(ref Guid setting, uint flags, ref NotifyParameters recipient, out IntPtr handle);
    [DllImport("powrprof.dll")]
    private static extern uint PowerSettingUnregisterNotification(IntPtr handle);
    [DllImport("ntdll.dll")]
    private static extern int NtQueryWnfStateData(ref ulong name, IntPtr type, IntPtr scope, out uint stamp, [Out] byte[] buffer, ref uint size);
    [DllImport("ntdll.dll")]
    private static extern int RtlPublishWnfStateData(ulong name, IntPtr type, ref uint buffer, uint size, IntPtr scope);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float decibels, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float value, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float decibels);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float value);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float decibels, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float value, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float decibels);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float value);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}


public sealed class FanState
{
    public bool Available { get; set; }
    public string Reason { get; set; }
    public string InspectionError { get; set; }
    public bool ScreenXpertInstalled { get; set; }
    public string ScreenXpertPath { get; set; }
    public int? NRModelFlag { get; set; }
    public int? Capability { get; set; }
    public bool SeparateACDC { get; set; }
    public int? ACProfile { get; set; }
    public int? DCProfile { get; set; }
    public int? ActiveProfile { get; set; }
    public string ActiveProfileKey { get; set; }
    public string PowerSource { get; set; }
    public bool HControlFound { get; set; }
    public uint HControlProcessId { get; set; }
    public int? PowerModeSynchronization { get; set; }
}

public static class FanControl
{
    private const string HotkeysPath = @"SOFTWARE\ASUS\ASUS System Control Interface\AsusOptimization\ASUS Keyboard Hotkeys";
    private const string CapabilityPath = @"SOFTWARE\ASUS\ASUS System Control Interface\AsusOptimization\ScreenXpert";
    private const string ScreenXpertRegistryPath = @"SOFTWARE\ASUS\ASUS ScreenXpert";

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr FindWindowW(string className, string windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint message, UIntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out UIntPtr result);

    private static int? ReadDword(RegistryKey machine, string path, string name)
    {
        using (RegistryKey key = machine.OpenSubKey(path, false))
        {
            if (key == null) return null;
            object value = key.GetValue(name, null);
            if (value is int) return (int)value;
            if (value is uint) return unchecked((int)(uint)value);
            int parsed;
            return value is string && Int32.TryParse((string)value, out parsed)
                ? (int?)parsed : null;
        }
    }

    private static int? ValidProfile(int? value)
    {
        return value.HasValue && value.Value >= 0 && value.Value <= 3 ? value : null;
    }

    private static IntPtr FindHControl()
    {
        return FindWindowW("HCONTROL", "HControl");
    }

    public static FanState ReadState()
    {
        FanState state = new FanState();
        state.Reason = "FanProfile is unavailable.";
        state.PowerSource = "unknown";
        try
        {
            string programFiles = Environment.GetEnvironmentVariable("ProgramW6432");
            if (String.IsNullOrEmpty(programFiles))
                programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            state.ScreenXpertPath = Path.Combine(programFiles, "ASUS", "AsusScreenXpert");
            state.ScreenXpertInstalled = File.Exists(Path.Combine(state.ScreenXpertPath, "AsusScreenXpertUI.exe"))
                || File.Exists(Path.Combine(state.ScreenXpertPath, "AsusScreenXpertReunion.exe"));

            using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                state.NRModelFlag = ReadDword(machine, ScreenXpertRegistryPath, "isNRModel");
                state.Capability = ReadDword(machine, CapabilityPath, "FanMode");
                state.SeparateACDC = state.Capability.HasValue && (state.Capability.Value & 4) != 0;
                state.ACProfile = ValidProfile(ReadDword(machine, HotkeysPath, "QuietFan"));
                state.DCProfile = ValidProfile(ReadDword(machine, HotkeysPath, "QuietFanDC"));
                state.PowerModeSynchronization = ReadDword(machine, HotkeysPath, "QuietFanPowerMode");
            }

            SystemPowerStatus power;
            if (GetSystemPowerStatus(out power))
                state.PowerSource = power.ACLineStatus == 0 ? "DC" : power.ACLineStatus == 1 ? "AC" : "unknown";
            state.ActiveProfileKey = state.SeparateACDC && state.PowerSource == "DC" ? "QuietFanDC" : "QuietFan";
            state.ActiveProfile = state.ActiveProfileKey == "QuietFanDC" ? state.DCProfile : state.ACProfile;

            IntPtr hwnd = FindHControl();
            state.HControlFound = hwnd != IntPtr.Zero;
            if (state.HControlFound)
            {
                uint processId;
                GetWindowThreadProcessId(hwnd, out processId);
                state.HControlProcessId = processId;
            }

            if (!state.ScreenXpertInstalled) state.Reason = "ScreenXpert is not installed; FanProfile skipped.";
            else if (state.NRModelFlag.GetValueOrDefault() != 0) state.Reason = "ScreenXpert reports FanProfile unsupported on this model.";
            else if (!state.Capability.HasValue || state.Capability.Value == 0) state.Reason = "ScreenXpert FanProfile capability is unavailable.";
            else if (state.SeparateACDC && state.PowerSource == "unknown") state.Reason = "Cannot determine the active AC/DC FanProfile safely.";
            else if (!state.HControlFound) state.Reason = "ASUS FanProfile backend is unavailable.";
            else if (!state.ActiveProfile.HasValue) state.Reason = "The active ASUS FanProfile cannot be read.";
            else
            {
                state.Available = true;
                state.Reason = "Available through the installed ASUS system backend.";
            }
        }
        catch (Exception ex)
        {
            state.Available = false;
            state.InspectionError = ex.Message;
            state.Reason = "FanProfile inspection failed: " + ex.Message;
        }
        return state;
    }

    private static string ProfileName(int mode)
    {
        switch (mode)
        {
            case 0: return "Standard";
            case 1: return "Whisper";
            case 2: return "Performance";
            case 3: return "FullSpeed";
            default: return mode.ToString();
        }
    }

    public static StepResult SetProfile(int mode, bool simulateUnavailable)
    {
        const string name = "FanProfile";
        if (mode < 0 || mode > 3) return new StepResult(name, "error", "Invalid ASUS fan profile: " + mode);
        if (simulateUnavailable) return new StepResult(name, "skipped", "Simulated unavailable ScreenXpert/FanProfile.");
        try
        {
            FanState before = ReadState();
            if (!before.Available) return new StepResult(name, before.InspectionError == null ? "skipped" : "error", before.Reason);
            if (before.ActiveProfile == mode)
                return new StepResult(name, "ok", ProfileName(mode) + " already selected (" + before.ActiveProfileKey + "); no command sent.");

            IntPtr hwnd = FindHControl();
            if (hwnd == IntPtr.Zero) return new StepResult(name, "skipped", "ASUS FanProfile backend disappeared.");
            uint message = RegisterWindowMessageW("Application Notification to ATKHotkey");
            if (message == 0) return new StepResult(name, "error", "ASUS IPC message registration failed: " + Marshal.GetLastWin32Error());
            UIntPtr result;
            // Installed ScreenXpert sends 0x10200+mode. The ASUS backend's 0x10080+mode
            // path applies the same absolute profile but omits its OSD notification.
            IntPtr sent = SendMessageTimeoutW(hwnd, message, new UIntPtr(0xAE5),
                new IntPtr(0x10080 + mode), 0x22, 500, out result);
            int senderError = sent == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;

            Stopwatch timer = Stopwatch.StartNew();
            FanState observed = before;
            while (timer.ElapsedMilliseconds < 2000)
            {
                observed = ReadState();
                if (before.SeparateACDC && observed.ActiveProfileKey != before.ActiveProfileKey)
                    return new StepResult(name, "error", "Power source changed while applying FanProfile; verification stopped without sending another command.");
                if (observed.Available && observed.ActiveProfile == mode)
                {
                    // Registry updates precede the ASUS Windows-power synchronization.
                    // Bound this wait so explicit Windows settings can be applied afterward.
                    Thread.Sleep(400);
                    FanState stable = ReadState();
                    if (stable.Available && stable.ActiveProfileKey == before.ActiveProfileKey && stable.ActiveProfile == mode)
                        return new StepResult(name, "ok", ProfileName(mode) + " verified through " + stable.ActiveProfileKey + " (" + stable.PowerSource + ").");
                    observed = stable;
                }
                Thread.Sleep(75);
            }
            return new StepResult(name, "error", "ASUS FanProfile did not verify as " + ProfileName(mode)
                + "; observed=" + (observed.ActiveProfile.HasValue ? observed.ActiveProfile.Value.ToString() : "unknown")
                + ", source=" + observed.PowerSource + (senderError != 0 ? ", IPC error=" + senderError : "") + ".");
        }
        catch (Exception ex)
        {
            return new StepResult(name, "error", "FanProfile application failed: " + ex.Message);
        }
    }

    public static List<StepResult> Restore(FanState saved)
    {
        List<StepResult> steps = new List<StepResult>();
        if (saved == null)
        {
            steps.Add(new StepResult("FanProfileRestore", "error", "No original FanProfile snapshot."));
            return steps;
        }
        if (!saved.Available)
        {
            steps.Add(new StepResult("FanProfileRestore", "skipped", "Original FanProfile was unavailable; nothing to restore."));
            return steps;
        }
        FanState current = ReadState();
        if (!current.Available)
        {
            steps.Add(new StepResult("FanProfileRestore", current.InspectionError == null ? "skipped" : "error", current.Reason));
            return steps;
        }
        int? target = current.ActiveProfileKey == "QuietFanDC" ? saved.DCProfile : saved.ACProfile;
        if (target.HasValue) steps.Add(SetProfile(target.Value, false));
        else steps.Add(new StepResult("FanProfileRestore", "skipped", "The original value for the current power source was not readable."));

        FanState after = ReadState();
        if (saved.SeparateACDC && after.SeparateACDC && after.ActiveProfileKey != saved.ActiveProfileKey)
        {
            int? inactiveNow = saved.ActiveProfileKey == "QuietFanDC" ? after.DCProfile : after.ACProfile;
            if (inactiveNow != saved.ActiveProfile)
                steps.Add(new StepResult("FanProfileRestoreInactive", "skipped",
                    "Power source changed; the original " + saved.ActiveProfileKey
                    + " is inactive and cannot be restored through this IPC without changing the power source."));
        }
        return steps;
    }
}
