using Microsoft.Win32;
using WindowsPreferences;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;

// Only selected Windows preferences are written. No application settings, logs,
// temporary files, services, scheduled tasks or background processes are created.
public static class AdditionalControl
{
    static readonly Guid SleepSubgroup = new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    static readonly Guid SleepIdle = new Guid("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
    static readonly Guid DisplaySubgroup = new Guid("7516b95f-f776-4464-8c53-06167f40cc99");
    static readonly Guid DisplayIdle = new Guid("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
    static readonly Guid AdaptiveBrightness = new Guid("fbd9aa66-9553-4097-ba44-ed6e9d65eab8");
    const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string UsbKey = @"SYSTEM\CurrentControlSet\Control\USB\AutomaticSurpriseRemoval";

    public static List<StepResult> Apply(ModeConfiguration c, bool allowSchemeRefresh = true)
    {
        List<StepResult> results = new List<StepResult>();
        if (c == null) return results;
        Run(results, "DarkTheme", c.DarkTheme, delegate { return SetDarkTheme(c.DarkTheme == 1); });
        Guid scheme = Guid.Empty; bool activate = false;
        if (!allowSchemeRefresh)
        {
            if(c.SleepTimeout!=0)results.Add(new StepResult("SleepTimeout","skipped","Cannot preserve unreadable unselected AC/DC power modes safely."));
            if(c.ScreenTimeout!=0)results.Add(new StepResult("ScreenTimeout","skipped","Cannot preserve unreadable unselected AC/DC power modes safely."));
            if(c.AdaptiveBrightness!=0)results.Add(new StepResult("AdaptiveBrightness","skipped","Cannot preserve unreadable unselected AC/DC power modes safely."));
        }
        else if (c.SleepTimeout != 0 || c.ScreenTimeout != 0 || c.AdaptiveBrightness != 0)
        {
            IntPtr pointer = IntPtr.Zero;
            try
            {
                Check(PowerGetActiveScheme(IntPtr.Zero, out pointer));
                scheme = (Guid)Marshal.PtrToStructure(pointer, typeof(Guid));
            }
            catch (Exception ex)
            {
                if (c.SleepTimeout != 0) results.Add(Failed("SleepTimeout", ex));
                if (c.ScreenTimeout != 0) results.Add(Failed("ScreenTimeout", ex));
                if (c.AdaptiveBrightness != 0) results.Add(Failed("AdaptiveBrightness", ex));
            }
            finally { if (pointer != IntPtr.Zero) LocalFree(pointer); }
            if (scheme != Guid.Empty)
            {
                Run(results, "SleepTimeout", c.SleepTimeout, delegate { return SetPowerValue("SleepTimeout", scheme, SleepSubgroup, SleepIdle, c.SleepTimeout == 1 ? 180u : 0u, ref activate); });
                Run(results, "ScreenTimeout", c.ScreenTimeout, delegate { return SetPowerValue("ScreenTimeout", scheme, DisplaySubgroup, DisplayIdle, c.ScreenTimeout == 1 ? 180u : 0u, ref activate); });
                Run(results, "AdaptiveBrightness", c.AdaptiveBrightness, delegate { return SensorCapabilities.HasAmbientLight()&&SensorCapabilities.HasBrightness()?SetPowerValue("AdaptiveBrightness", scheme, DisplaySubgroup, AdaptiveBrightness, c.AdaptiveBrightness == 1 ? 1u : 0u, ref activate):new StepResult("AdaptiveBrightness","skipped","Ambient-light sensor or brightness control unavailable."); });
                if (activate)
                {
                    try { Check(PowerSetActiveScheme(IntPtr.Zero, ref scheme)); }
                    catch (Exception ex) { results.Add(Failed("PowerSettingsActivation", ex)); }
                }
            }
        }
        Run(results, "PresenceScreenOff", c.PresenceSleep, delegate { return Presence.Set(false, c.PresenceSleep == 1); });
        Run(results, "PresenceDimming", c.PresenceDim, delegate { return Presence.Set(true, c.PresenceDim == 1); });
        Run(results, "PresenceWake", c.PresenceWake, delegate { return Presence.SetFeature(2, c.PresenceWake == 1); });
        Run(results, "PresenceOnlooker", c.PresenceOnlooker, delegate { return Presence.SetFeature(3, c.PresenceOnlooker == 1); });
        // HDR and content-adaptive brightness are handled by HdrControl, which
        // resolves the selected display and uses its native display packets.
        Run(results, "ScreenSaver", c.ScreenSaver, delegate { return SetScreenSaver(c.ScreenSaver == 1); });
        Run(results, "UsbPower", c.UsbPower, delegate { return SetExistingMachinePreference("UsbPower", UsbKey, "AttemptRecoveryFromUsbPowerDrain", c.UsbPower == 1 ? 1 : 0, 1); });
        return results;
    }

    static void Run(List<StepResult> results, string name, int selected, Func<StepResult> action)
    {
        if (selected == 0) return;
        try { results.Add(action()); }
        catch (Exception ex) { results.Add(Failed(name, ex)); }
    }

    static StepResult Failed(string name, Exception ex)
    {
        int code = Marshal.GetHRForException(ex);
        Win32Exception native=ex as Win32Exception;
        bool unavailable = ex is UnauthorizedAccessException || ex is SecurityException || ex is NotSupportedException || ex is DllNotFoundException || ex is EntryPointNotFoundException || (native!=null&&(native.NativeErrorCode==5||native.NativeErrorCode==50||native.NativeErrorCode==2||native.NativeErrorCode==120||native.NativeErrorCode==1168)) || code == unchecked((int)0x80070005) || code == unchecked((int)0x80040154) || code == unchecked((int)0x80004002) || code == unchecked((int)0x8000000F) || code == unchecked((int)0x80070490) || code == unchecked((int)0x80070032) || code == unchecked((int)0x80070002) || code == unchecked((int)0x80070078);
        return new StepResult(name, unavailable ? "skipped" : "error", "HRESULT=0x" + code.ToString("X8") + "; " + ex.GetType().Name);
    }

    static StepResult SetDarkTheme(bool enabled)
    {
        StepResult broker;
        if(BrokerWindowsControl.Choice(WindowsPreferenceId.DarkColorMode,enabled?1:0,3,"DarkTheme",false,out broker))return broker;
        bool changed=false;
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Personalize, true))
        {
            if (key == null) return new StepResult("DarkTheme", "skipped", "Windows personalization settings are unavailable.");
            int value = enabled ? 0 : 1;
            if (!Object.Equals(key.GetValue("AppsUseLightTheme"), value)) {key.SetValue("AppsUseLightTheme", value, RegistryValueKind.DWord);changed=true;}
            if (!Object.Equals(key.GetValue("SystemUsesLightTheme"), value)) {key.SetValue("SystemUsesLightTheme", value, RegistryValueKind.DWord);changed=true;}
        }
        UIntPtr ignored;
        if(changed)SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, UIntPtr.Zero, "ImmersiveColorSet", 0x0002, 200, out ignored);
        return new StepResult("DarkTheme", "ok", enabled ? "Dark Windows and application color mode selected." : "Light Windows and application color mode selected.");
    }

    static StepResult SetPowerValue(string name, Guid scheme, Guid subgroup, Guid setting, uint value, ref bool activate)
    {
        uint ac, dc;
        uint first = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out ac);
        uint second = PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out dc);
        if (first == 2 || first == 50 || second == 2 || second == 50)
            return new StepResult(name, "skipped", "The power setting is unavailable.");
        Check(first); Check(second);
        if(BrokerWindowsControl.Certified && (setting==SleepIdle || setting==DisplayIdle)) {
            StepResult acResult,dcResult;
            var acId=setting==SleepIdle?WindowsPreferenceId.SleepAC:WindowsPreferenceId.ScreenAC;
            var dcId=setting==SleepIdle?WindowsPreferenceId.SleepDC:WindowsPreferenceId.ScreenDC;
            bool acHandled=BrokerWindowsControl.Choice(acId,value==0?-1:2,16,name+"AC",true,out acResult);
            bool dcHandled=BrokerWindowsControl.Choice(dcId,value==0?-1:2,16,name+"DC",true,out dcResult);
            if(acHandled && acResult.Status!="ok" || dcHandled && dcResult.Status!="ok")
                return new StepResult(name,"skipped","Windows did not confirm both power sources.");
            if(!acHandled && ac!=value){Check(PowerWriteACValueIndex(IntPtr.Zero,ref scheme,ref subgroup,ref setting,value));activate=true;}
            if(!dcHandled && dc!=value){Check(PowerWriteDCValueIndex(IntPtr.Zero,ref scheme,ref subgroup,ref setting,value));activate=true;}
            Check(PowerReadACValueIndex(IntPtr.Zero,ref scheme,ref subgroup,ref setting,out ac));
            Check(PowerReadDCValueIndex(IntPtr.Zero,ref scheme,ref subgroup,ref setting,out dc));
            return new StepResult(name,ac==value&&dc==value?"ok":"error","Verified separate AC/DC timeout values.");
        }
        if(BrokerWindowsControl.Certified && setting==AdaptiveBrightness) {
            StepResult broker;
            if(BrokerWindowsControl.Boolean(WindowsPreferenceId.AdaptiveBrightness,value!=0,name,out broker)) {
                if(broker.Status!="ok")return broker;
                // This Windows control changes the active source. Preserve the
                // preset's explicit AC/DC scope for the other source as well.
                Check(PowerReadACValueIndex(IntPtr.Zero,ref scheme,ref subgroup,ref setting,out ac));
                Check(PowerReadDCValueIndex(IntPtr.Zero,ref scheme,ref subgroup,ref setting,out dc));
            }
        }
        if (ac != value) { Check(PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value)); activate = true; }
        if (dc != value) { Check(PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value)); activate = true; }
        return new StepResult(name, "ok", "AC/DC=" + value + "; scheme=" + scheme);
    }

    static StepResult SetExistingMachinePreference(string name, string path, string valueName, int value, int max)
    {
        StepResult broker;
        if(name=="UsbPower"&&BrokerWindowsControl.Boolean(WindowsPreferenceId.UsbPower,value!=0,name,out broker))return broker;
        // These are Windows-owned settings. Never create an absent feature/key or
        // change permissions. Machines whose ACL requires elevation silently skip.
        using (RegistryKey read = Registry.LocalMachine.OpenSubKey(path, false))
        {
            object existing = read == null ? null : read.GetValue(valueName, null);
            if (!(existing is int) || (int)existing < 0 || (int)existing > max)
                return new StepResult(name, "skipped", "The Windows setting is unavailable or unrecognized.");
            if ((int)existing == value) return new StepResult(name, "ok", valueName + "=" + value + "; already selected.");
        }
        using (RegistryKey write = Registry.LocalMachine.OpenSubKey(path, true))
        {
            if (write == null) return new StepResult(name, "skipped", "The Windows setting is unavailable.");
            write.SetValue(valueName, value, RegistryValueKind.DWord);
            if (!Object.Equals(write.GetValue(valueName), value)) throw new IOException("The selected Windows preference did not verify.");
        }
        return new StepResult(name, "ok", valueName + "=" + value + "; driver applies its preference on the next policy refresh.");
    }

    static StepResult SetScreenSaver(bool enabled)
    {
        if (enabled)
        {
            using (RegistryKey desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", false))
            {
                string configured = desktop == null ? null : desktop.GetValue("SCRNSAVE.EXE") as string;
                if (String.IsNullOrWhiteSpace(configured)) return new StepResult("ScreenSaver", "skipped", "No screensaver is configured.");
                configured = Environment.ExpandEnvironmentVariables(configured).Trim('"');
                if (!Path.IsPathRooted(configured)) configured = Path.Combine(Environment.SystemDirectory, configured);
                if (!File.Exists(configured) || !String.Equals(Path.GetExtension(configured), ".scr", StringComparison.OrdinalIgnoreCase))
                    return new StepResult("ScreenSaver", "skipped", "The configured screensaver is unavailable.");
            }
        }
        int actual;
        if (!GetSystemParametersInfo(0x0010, 0, out actual, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if((actual!=0)==enabled)return new StepResult("ScreenSaver","ok","Requested screensaver state is already selected.");
        if (!SystemParametersInfo(0x0011, enabled ? 1u : 0u, IntPtr.Zero, 0x0003)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!GetSystemParametersInfo(0x0010, 0, out actual, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new StepResult("ScreenSaver", actual != 0 == enabled ? "ok" : "error", "ScreenSaveActive=" + (actual != 0));
    }

    static void Check(uint result) { if (result != 0) throw new Win32Exception((int)result); }

    // Native WinRT ABI from Windows SDK windows.devices.sensors.h. No .winmd,
    // SDK or additional assembly is required on the target computer.
    static class Presence
    {
        static readonly Guid FactoryId = new Guid("7f343202-e010-52c4-af0c-04a8f1e033da");
        static readonly Guid Settings2Id = new Guid("a26f705e-8696-5eb4-b9e1-26a508de1cd4");
        static readonly Guid Features2Id = new Guid("08a9cdda-d929-5ec2-81e2-940bafa089cf");
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetObject(IntPtr self, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetFeatures(IntPtr self, IntPtr sensor, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetBoolean(IntPtr self, out byte value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetBoolean(IntPtr self, byte value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Update(IntPtr self, IntPtr value);
        static T Method<T>(IntPtr instance, int slot) where T : class
        {
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size), typeof(T)) as T;
        }
        static void Hr(int value) { if (value < 0) Marshal.ThrowExceptionForHR(value); }
        static IntPtr Query(IntPtr instance, Guid iid) { IntPtr result; Hr(Marshal.QueryInterface(instance, ref iid, out result)); return result; }
        static bool Get(IntPtr instance, int slot) { byte result; Hr(Method<GetBoolean>(instance, slot)(instance, out result)); return result != 0; }

        public static StepResult Set(bool dimming, bool enabled)
        {return SetFeature(dimming?1:0,enabled);}
        public static StepResult SetFeature(int feature,bool enabled)
        {
            bool dimming=feature==1,extended=dimming||feature==3;
            string name = dimming ? "PresenceDimming" : feature==2?"PresenceWake":feature==3?"PresenceOnlooker":"PresenceScreenOff";
            StepResult broker;
            var preference=feature==0?WindowsPreferenceId.PresenceSleep:feature==1?WindowsPreferenceId.PresenceDim:feature==2?WindowsPreferenceId.PresenceWake:WindowsPreferenceId.PresenceOnlooker;
            if(BrokerWindowsControl.Boolean(preference,enabled,name,out broker))return broker;
            Guid settingsId=feature==3?new Guid("89757226-acc1-4f58-81df-47f1d69537f2"):Settings2Id;
            IntPtr factory = IntPtr.Zero, settings = IntPtr.Zero, settings2 = IntPtr.Zero, features = IntPtr.Zero, features2 = IntPtr.Zero, sensor = IntPtr.Zero, className = IntPtr.Zero;
            int initialized = RoInitialize(0);
            try
            {
                if (initialized < 0 && initialized != unchecked((int)0x80010106)) Hr(initialized);
                string runtimeClass = "Windows.Devices.Sensors.HumanPresenceSettings";
                Hr(WindowsCreateString(runtimeClass, runtimeClass.Length, out className));
                Guid iid = FactoryId; Hr(RoGetActivationFactory(className, ref iid, out factory));
                Hr(Method<GetObject>(factory, 7)(factory, out settings));
                if (settings == IntPtr.Zero) return new StepResult(name, "skipped", "Human-presence settings are unavailable.");
                Hr(Method<GetObject>(settings, 6)(settings, out sensor));
                if (sensor == IntPtr.Zero || WindowsGetStringLen(sensor) == 0) return new StepResult(name, "skipped", "No configured Windows human-presence sensor.");
                Hr(Method<GetFeatures>(factory, 11)(factory, sensor, out features));
                if (features == IntPtr.Zero) return new StepResult(name, "skipped", "Human-presence capabilities are unavailable.");
                IntPtr target; int getSlot, setSlot;
                if (extended)
                {
                    features2 = Query(features,feature==3?new Guid("ed4e03aa-5ca2-5c02-b783-262e91295619"):Features2Id);
                    if (!Get(features2, 6)) return new StepResult(name, "skipped", "Adaptive dimming is unsupported by the sensor.");
                    settings2 = Query(settings, settingsId); target = settings2; getSlot = 6; setSlot = 7;
                }
                else
                {
                    if (!Get(features,feature==2?8:9)) return new StepResult(name, "skipped", "Presence feature is unsupported by the sensor.");
                    target = settings; getSlot = feature==2?8:12; setSlot = feature==2?9:13;
                }
                if (Get(target, getSlot) == enabled) return new StepResult(name, "ok", "Requested presence setting is already selected.");
                Hr(Method<SetBoolean>(target, setSlot)(target, enabled ? (byte)1 : (byte)0));
                Hr(Method<Update>(factory, 9)(factory, settings));
                // UpdateSettings may be denied to unpackaged apps by Windows.
                // Do not fall back to OEM provisioning/policy keys or request UAC.
                IntPtr verified=IntPtr.Zero, verifiedTarget=IntPtr.Zero;
                try
                {
                    Hr(Method<GetObject>(factory,7)(factory,out verified));
                    if(verified==IntPtr.Zero)throw new IOException("Presence settings could not be verified.");
                    verifiedTarget=extended?Query(verified,settingsId):verified;
                    if(Get(verifiedTarget,getSlot)!=enabled)throw new IOException("Presence settings did not verify.");
                }
                finally
                {
                    if(extended&&verifiedTarget!=IntPtr.Zero)Marshal.Release(verifiedTarget);
                    if(verified!=IntPtr.Zero)Marshal.Release(verified);
                }
                return new StepResult(name, "ok", enabled ? "Enabled through Windows HumanPresenceSettings." : "Disabled through Windows HumanPresenceSettings.");
            }
            finally
            {
                foreach (IntPtr pointer in new[] { features2, features, settings2, settings, factory }) if (pointer != IntPtr.Zero) Marshal.Release(pointer);
                if (sensor != IntPtr.Zero) WindowsDeleteString(sensor);
                if (className != IntPtr.Zero) WindowsDeleteString(className);
                if (initialized >= 0) RoUninitialize();
            }
        }
    }

    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr pointer);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SystemParametersInfo(uint action, uint param, IntPtr data, uint flags);
    [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", SetLastError=true)] static extern bool GetSystemParametersInfo(uint action, uint param, out int data, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
    [DllImport("combase.dll")] static extern int RoInitialize(uint type);
    [DllImport("combase.dll")] static extern void RoUninitialize();
    [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr name, ref Guid iid, out IntPtr factory);
    [DllImport("combase.dll", CharSet=CharSet.Unicode)] static extern int WindowsCreateString(string value, int length, out IntPtr text);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr text);
    [DllImport("combase.dll")] static extern uint WindowsGetStringLen(IntPtr text);
}
