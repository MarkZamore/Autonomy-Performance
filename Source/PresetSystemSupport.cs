using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;

// Captured when choosing a preset. Every probe only reads an existing Windows
// setting or installed backend; it never applies the actions being offered.
public sealed class PresetSystemSnapshot
{
    public bool EnergySaver;
    public bool PowerMode;
    public bool DarkTheme;
    public bool FanProfile;
}

public static class PresetSystemSupport
{
    const string PersonalizePath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";


    public static PresetSystemSnapshot Read()
    {
        PresetSystemSnapshot result = new PresetSystemSnapshot();
        result.EnergySaver = WindowsControl.SupportsEnergySaver();
        try
        {
            // Read the same two independent user preferences that the runner
            // changes. Reading active power schemes would test a different API.
            WindowsControl.ReadPowerMode(true);
            WindowsControl.ReadPowerMode(false);
            result.PowerMode = true;
        }
        catch { }
        try { result.DarkTheme = CanReadExistingKey(PersonalizePath); }
        catch { }
        try { result.FanProfile = FanControl.ReadState().Available; }
        catch { }
        return result;
    }

    static bool CanReadExistingKey(string path)
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path, false))
        {
            // OpenSubKey can return a handle without reading values. Actually
            // enumerate the names so an unreadable key is excluded as well.
            return key != null && key.GetValueNames() != null;
        }
    }
}
