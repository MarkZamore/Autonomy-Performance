using System;
using System.Collections.Generic;

// These inputs describe capabilities only. Building a preset never applies it.
public sealed class PresetCapabilities
{
    public bool EnergySaver, PowerMode, DarkTheme, TaskbarRepair, FanProfile;
    public bool WallpaperPlayback, WallpaperAudio, WallpaperApplication;
    public List<PresetDisplayCapabilities> Displays = new List<PresetDisplayCapabilities>();
}

public sealed class PresetDisplayCapabilities
{
    public DisplayInfo Display;
    public bool HdrSupported, DynamicRefreshSupported;
    public int RecommendedScale;
    public List<int> Scales = new List<int>();
}

public static class PresetBuilder
{
    public static ModeConfiguration Build(bool performance, PresetCapabilities capabilities)
    {
        if (capabilities == null) throw new ArgumentNullException("capabilities");
        ModeConfiguration result = new ModeConfiguration {
            EnergySaver = capabilities.EnergySaver ? (performance ? 2 : 1) : 0,
            PowerMode = capabilities.PowerMode ? (performance ? 3 : 1) : 0,
            DarkTheme = capabilities.DarkTheme ? 1 : 0,
            TaskbarRepair = capabilities.TaskbarRepair ? 2 : 0,
            FanProfile = capabilities.FanProfile ? (performance ? 2 : 1) : 0,
            WallpaperPlayback = capabilities.WallpaperPlayback ? (performance ? 1 : 2) : 0,
            WallpaperAudio = capabilities.WallpaperAudio ? (performance ? 1 : 2) : 0,
            WallpaperApplication = performance && capabilities.WallpaperApplication ? 1 : 0
        };
        List<DisplayModeConfiguration> displays = new List<DisplayModeConfiguration>();
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<PresetDisplayCapabilities> available = capabilities.Displays == null ? new List<PresetDisplayCapabilities>() : new List<PresetDisplayCapabilities>(capabilities.Displays);
        available.Sort(delegate(PresetDisplayCapabilities a, PresetDisplayCapabilities b) {
            int order = DisplayNumber(a).CompareTo(DisplayNumber(b));
            return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(a == null || a.Display == null ? null : a.Display.MonitorId, b == null || b.Display == null ? null : b.Display.MonitorId);
        });
        foreach (PresetDisplayCapabilities display in available) {
            DisplayModeConfiguration action = BuildDisplay(performance, display);
            if (action != null && action.HasActions && seen.Add(action.DisplayId)) displays.Add(action);
        }
        result.Displays = displays.ToArray();
        result.Validate();
        return result;
    }

    public static DisplayModeConfiguration BuildDisplay(bool performance, PresetDisplayCapabilities capabilities)
    {
        DisplayInfo display = capabilities == null ? null : capabilities.Display;
        if (display == null || String.IsNullOrEmpty(display.MonitorId)) return null;
        DisplayModeConfiguration action = new DisplayModeConfiguration { DisplayId = display.MonitorId };
        uint referenceWidth = display.RecommendedWidth != 0 ? display.RecommendedWidth : display.Width;
        uint referenceHeight = display.RecommendedHeight != 0 ? display.RecommendedHeight : display.Height;
        uint width = performance ? display.RecommendedWidth : 1920;
        uint height = performance ? display.RecommendedHeight : ((ulong)referenceWidth * 10 == (ulong)referenceHeight * 16 ? 1200u : 1080u);
        DisplayResolution resolution = FindResolution(display, width, height);
        if (resolution != null) { action.DisplayWidth = resolution.Width; action.DisplayHeight = resolution.Height; }
        // If a requested resolution is unavailable, frequency is considered only
        // for the unchanged resolution; never export an unsupported combination.
        List<uint> rates = resolution == null ? display.Rates : resolution.Rates;
        if (rates != null) foreach (uint rate in rates) {
            if (performance) { if (rate >= 2 && rate > action.RefreshRate) action.RefreshRate = rate; }
            else if (rate == 60) action.RefreshRate = rate;
        }
        if (performance && capabilities.RecommendedScale > 0 && capabilities.Scales != null && capabilities.Scales.Contains(capabilities.RecommendedScale)) action.DisplayScale = capabilities.RecommendedScale;
        if (capabilities.HdrSupported) action.Hdr = performance ? 1 : 2;
        if (capabilities.DynamicRefreshSupported) action.DynamicRefresh = performance ? 1 : 2;
        return action;
    }

    static DisplayResolution FindResolution(DisplayInfo display, uint width, uint height)
    {
        if (width == 0 || height == 0 || display.Resolutions == null) return null;
        return display.Resolutions.Find(delegate(DisplayResolution value) { return value != null && value.Width == width && value.Height == height; });
    }

    static int DisplayNumber(PresetDisplayCapabilities capabilities)
    {
        string name = capabilities == null || capabilities.Display == null ? null : capabilities.Display.DeviceName;
        if (String.IsNullOrEmpty(name)) return Int32.MaxValue;
        int start = name.LastIndexOf("DISPLAY", StringComparison.OrdinalIgnoreCase), number;
        return start >= 0 && Int32.TryParse(name.Substring(start + 7), out number) ? number : Int32.MaxValue;
    }
}
