using System;using System.Collections.Generic;using System.IO;using System.Reflection;using System.Runtime.InteropServices;using System.Windows;using System.Windows.Controls;using System.Windows.Controls.Primitives;using System.Windows.Input;using System.Windows.Markup;using System.Windows.Media;using System.Windows.Media.Imaging;using Microsoft.Win32;

using System.Windows.Interop;using System.Windows.Threading;

public sealed class ConstructorWindow
{
    public Window Window {get;private set;}
    public readonly IconSelection IconChoice=new IconSelection();
    readonly CheckBox[] powerChecks,fanChecks,switches;readonly Button save,iconButton;readonly Image currentIcon;readonly Popup popup;readonly UniformGrid icons;readonly TextBlock status;readonly TextBox preview;
    readonly ToggleButton brightnessToggle;readonly Slider brightnessSlider,nightLightSlider;readonly ComboBox displaySelector,resolutionSelector,refreshSelector,scaleSelector,contentBrightnessSelector;readonly TextBlock displayUnavailable;
    static readonly string[] switchNames={"EnergySaverSwitch","DarkThemeSwitch","HdrSwitch","PresenceSleepSwitch","PresenceDimSwitch","AdaptiveBrightnessSwitch","ScreenSaverSwitch","UsbPowerSwitch","WallpaperPlaybackSwitch","WallpaperAudioSwitch","BatteryCareSwitch","WallpaperApplicationSwitch","TaskbarRepairSwitch","NightLightSwitch","DynamicRefreshSwitch","BatteryPercentageSwitch","ClockSecondsSwitch","PresenceWakeSwitch","PresenceOnlookerSwitch"};
    bool updating,updatingDisplays;
    readonly DispatcherTimer displayRefreshTimer;HwndSource displayMessageSource;bool displayRefreshRunning,displayRefreshPending,displayObserverClosed;
    string savedScriptState;
    readonly Dictionary<string,DisplayModeConfiguration> displaySettings=new Dictionary<string,DisplayModeConfiguration>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,DisplayInfo> knownDisplays=new Dictionary<string,DisplayInfo>(StringComparer.OrdinalIgnoreCase);
    string editingDisplayId;
    readonly List<PreviewAction> previewActions=new List<PreviewAction>();
    sealed class PreviewAction {public string Key,DisplayId,Text;public int Start,Length;}
    Point previewDown;int previewDownAction=-1;bool previewDragged,previewCanRemove;
    readonly Dictionary<Expander,int> categoryAnimationVersions=new Dictionary<Expander,int>();
    public sealed class RefreshChoice {public uint Rate;public override string ToString(){return Rate==0?"Не менять частоту обновления":Rate+" Гц";}}
    public sealed class ScaleChoice {public int Percent;public bool Recommended;public override string ToString(){return Percent==0?"Не менять масштаб":Percent+"%"+(Recommended?" (рекомендуется)":"");}}
    public sealed class ContentChoice {public int Value;public string Label;public override string ToString(){return Label;}}
    CheckBox Action(string name){return (CheckBox)Window.FindName(name);}
    static readonly string[] iconNames={"Autonomy&Performance","Батарея","Экономия энергии","Производительность","Скорость","Своя иконка ICO"};
    public ConstructorWindow()
    {
        using(Stream theme=Assembly.GetExecutingAssembly().GetManifestResourceStream("Theme"))Application.Current.Resources=(ResourceDictionary)XamlReader.Load(theme);
        using(Stream categoryIcons=Assembly.GetExecutingAssembly().GetManifestResourceStream("CategoryIcons"))Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(categoryIcons));
        WindowsTheme.Initialize(Application.Current);
        using(Stream content=Assembly.GetExecutingAssembly().GetManifestResourceStream("Window"))Window=(Window)XamlReader.Load(content);
        Window.Icon=ImageSource(EmbeddedAssets.Read("Icon0"));
        powerChecks=WireExclusiveChoices(new[]{"Efficiency","Balanced","BestPerformance"});
        fanChecks=WireExclusiveChoices(new[]{"FanWhisper","FanPerformance","FanStandard"});
        switches=new CheckBox[switchNames.Length];for(int i=0;i<switchNames.Length;i++){
            switches[i]=(CheckBox)Window.FindName(switchNames[i]);switches[i].Checked+=SelectionChanged;switches[i].Unchecked+=SelectionChanged;switches[i].Indeterminate+=SelectionChanged;
            switches[i].PreviewMouseLeftButtonDown+=SwitchPointerDown;switches[i].PreviewKeyDown+=SwitchKeyDown;
            ControlMotion.AttachSwitch(switches[i]);
            System.Windows.Automation.AutomationProperties.SetHelpText(switches[i],"Нажатие: не менять - включить - выключить - не менять. Стрелки влево и вправо выбирают состояние; стрелка вниз - не менять.");
            if(switches[i].Name=="TaskbarRepairSwitch")System.Windows.Automation.AutomationProperties.SetHelpText(switches[i],"Слева - не менять, в центре - режим 1, справа - режим 2.");
        }
        save=(Button)Window.FindName("SaveButton");iconButton=(Button)Window.FindName("IconButton");currentIcon=(Image)Window.FindName("CurrentIcon");popup=(Popup)Window.FindName("IconPopup");icons=(UniformGrid)Window.FindName("IconGrid");status=(TextBlock)Window.FindName("Status");preview=(TextBox)Window.FindName("ScriptPreview");
        save.Click+=SaveClicked;iconButton.Click+=delegate{RefreshIcons();popup.IsOpen=!popup.IsOpen;};
        ((Button)Window.FindName("ResetScriptButton")).Click+=delegate{ResetScript();};
        preview.PreviewMouseLeftButtonDown+=PreviewPointerDown;preview.PreviewMouseMove+=PreviewPointerMove;preview.PreviewMouseLeftButtonUp+=PreviewPointerUp;
        preview.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Delete&&preview.SelectionLength==0){int index=PreviewIndexAt(preview.CaretIndex);if(index>=0){RemovePreviewAction(index);e.Handled=true;}}};
        popup.Opened+=delegate{int selected=IconChoice.CustomPath==null?IconChoice.BuiltInIndex:5;((Button)icons.Children[selected]).Focus();};
        icons.PreviewKeyDown+=PopupKeyDown;
        Window.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape&&popup.IsOpen){popup.IsOpen=false;iconButton.Focus();e.Handled=true;}};
        WindowsTheme.Attach(Window);
        SmoothScrollingController settingsScrolling=new SmoothScrollingController((ScrollViewer)Window.FindName("SettingsScroll"));
        Window.Closed+=delegate{settingsScrolling.Dispose();};
        displaySelector=(ComboBox)Window.FindName("DisplaySelector");refreshSelector=(ComboBox)Window.FindName("RefreshSelector");displayUnavailable=(TextBlock)Window.FindName("DisplayUnavailable");
        resolutionSelector=(ComboBox)Window.FindName("ResolutionSelector");
        scaleSelector=(ComboBox)Window.FindName("ScaleSelector");contentBrightnessSelector=(ComboBox)Window.FindName("ContentBrightnessSelector");
        contentBrightnessSelector.ItemsSource=new[]{new ContentChoice{Value=0,Label="Не менять яркость с учётом содержимого"},new ContentChoice{Value=1,Label="Всегда"},new ContentChoice{Value=3,Label="Только при питании от батареи"}};contentBrightnessSelector.SelectedIndex=0;
        nightLightSlider=(Slider)Window.FindName("NightLightSlider");nightLightSlider.ValueChanged+=delegate{SelectionChanged(null,null);};
        brightnessToggle=(ToggleButton)Window.FindName("BrightnessToggleButton");brightnessSlider=(Slider)Window.FindName("BrightnessSlider");
        ControlMotion.AttachSwitch((CheckBox)brightnessToggle);
        brightnessToggle.PreviewMouseLeftButtonDown+=SwitchPointerDown;brightnessToggle.PreviewKeyDown+=SwitchKeyDown;
        brightnessToggle.Checked+=SelectionChanged;brightnessToggle.Unchecked+=SelectionChanged;brightnessSlider.ValueChanged+=delegate{SelectionChanged(null,null);};
        displaySelector.SelectionChanged+=delegate{if(!updatingDisplays)LoadDisplayEditor();};
        displaySelector.DropDownOpened+=delegate{RefreshDisplays();};
        resolutionSelector.SelectionChanged+=delegate{if(!updatingDisplays){RefreshRates();DisplaySelectionChanged(null,null);}};
        refreshSelector.SelectionChanged+=delegate{DisplaySelectionChanged(null,null);};
        scaleSelector.SelectionChanged+=delegate{DisplaySelectionChanged(null,null);};contentBrightnessSelector.SelectionChanged+=delegate{DisplaySelectionChanged(null,null);};
        ((Expander)Window.FindName("DisplaySection")).Expanded+=delegate{RefreshDisplays();};
        ((Button)Window.FindName("PresetPerformanceButton")).Click+=delegate{ApplyPreset(true);};
        ((Button)Window.FindName("PresetAutonomyButton")).Click+=delegate{ApplyPreset(false);};
        foreach(string categoryName in new[]{"PerformanceSection","WallpaperSection","ArmourySection","PowerSection","DisplaySection","AdditionalSection"}){
            Expander category=(Expander)Window.FindName(categoryName);
            category.Expanded+=CategoryStateChanged;category.Collapsed+=CategoryStateChanged;
            category.IsEnabledChanged+=delegate(object sender,DependencyPropertyChangedEventArgs e){Expander section=(Expander)sender;if(!section.IsEnabled)section.IsExpanded=false;};
        }
        RefreshProgramAvailability();
        displayRefreshTimer=new DispatcherTimer(DispatcherPriority.Background,Window.Dispatcher){Interval=TimeSpan.FromMilliseconds(150)};
        displayRefreshTimer.Tick+=delegate{displayRefreshTimer.Stop();RefreshDisplays();};
        Window.SourceInitialized+=delegate{displayMessageSource=HwndSource.FromHwnd(new WindowInteropHelper(Window).Handle);if(displayMessageSource!=null)displayMessageSource.AddHook(DisplayMessage);};
        Window.Closed+=delegate{displayObserverClosed=true;displayRefreshTimer.Stop();if(displayMessageSource!=null){displayMessageSource.RemoveHook(DisplayMessage);displayMessageSource=null;}};
        Window.Activated+=delegate{RefreshProgramAvailability();QueueDisplayRefresh();};
        RefreshDisplays();
        RefreshHardware();
        ((TextBlock)Window.FindName("BatteryWear")).Text=BatteryHealth.Read();
        RefreshIcons();UpdatePreview();
    }
    void CategoryStateChanged(object sender,RoutedEventArgs e)
    {
        if(e.OriginalSource!=sender)return;
        Expander category=(Expander)sender;
        if(!category.IsEnabled&&category.IsExpanded){category.IsExpanded=false;return;}
        AnimateCategory(category,Window.IsLoaded&&SystemParameters.ClientAreaAnimation);
    }
    void AnimateCategory(Expander category,bool animate)
    {
        category.ApplyTemplate();Border content=category.Template.FindName("SectionContent",category) as Border;if(content==null)return;
        double shownHeight=Double.IsNaN(content.Height)?content.ActualHeight:content.Height,shownOpacity=content.Opacity;
        bool reversing=content.HasAnimatedProperties;
        int version;categoryAnimationVersions.TryGetValue(category,out version);categoryAnimationVersions[category]=++version;
        content.BeginAnimation(FrameworkElement.HeightProperty,null);content.ClearValue(FrameworkElement.HeightProperty);
        content.BeginAnimation(UIElement.OpacityProperty,null);
        content.Visibility=Visibility.Visible;
        if(!animate){if(!category.IsExpanded)content.ClearValue(UIElement.VisibilityProperty);return;}
        content.Measure(new Size(Math.Max(1,category.ActualWidth-2),Double.PositiveInfinity));double height=content.DesiredSize.Height;
        double from=reversing?Math.Max(0,shownHeight):(category.IsExpanded?0:(shownHeight>0?shownHeight:height)),target=category.IsExpanded?height:0;
        if(height<=0&&!category.IsExpanded){content.ClearValue(UIElement.VisibilityProperty);return;}
        content.ClipToBounds=true;content.Height=target;
        System.Windows.Media.Animation.DoubleAnimation reveal=new System.Windows.Media.Animation.DoubleAnimation(from,target,TimeSpan.FromMilliseconds(category.IsExpanded?200:180));
        reveal.EasingFunction=new System.Windows.Media.Animation.CubicEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseInOut};
        reveal.Completed+=delegate{int current;if(categoryAnimationVersions.TryGetValue(category,out current)&&current==version){content.BeginAnimation(FrameworkElement.HeightProperty,null);content.ClearValue(FrameworkElement.HeightProperty);content.BeginAnimation(UIElement.OpacityProperty,null);if(!category.IsExpanded)content.ClearValue(UIElement.VisibilityProperty);}};
        content.BeginAnimation(FrameworkElement.HeightProperty,reveal);
        content.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(category.IsExpanded&&!reversing?0:shownOpacity,category.IsExpanded?1:0,TimeSpan.FromMilliseconds(160)));
    }
    public ModeConfiguration Configuration {
        get{
            CaptureDisplayEditor();
            List<DisplayModeConfiguration> displays=new List<DisplayModeConfiguration>();
            foreach(DisplayModeConfiguration action in displaySettings.Values)if(action.HasActions)displays.Add(CloneDisplay(action));
            displays.Sort(delegate(DisplayModeConfiguration a,DisplayModeConfiguration b){int order=DisplayNumber(a.DisplayId).CompareTo(DisplayNumber(b.DisplayId));return order!=0?order:StringComparer.OrdinalIgnoreCase.Compare(a.DisplayId,b.DisplayId);});
            return new ModeConfiguration{Displays=displays.ToArray(),
                EnergySaver=SwitchValue(Action("EnergySaverSwitch")),FanProfile=SelectedMode(fanChecks),
                PowerMode=SelectedMode(powerChecks),
                DarkTheme=SwitchValue(Action("DarkThemeSwitch")),SleepTimeout=0,ScreenTimeout=0,
                PresenceSleep=SwitchValue(Action("PresenceSleepSwitch")),PresenceDim=SwitchValue(Action("PresenceDimSwitch")),
                AdaptiveBrightness=SwitchValue(Action("AdaptiveBrightnessSwitch")),ScreenSaver=SwitchValue(Action("ScreenSaverSwitch")),UsbPower=SwitchValue(Action("UsbPowerSwitch")),WallpaperPlayback=SwitchValue(Action("WallpaperPlaybackSwitch")),WallpaperAudio=SwitchValue(Action("WallpaperAudioSwitch")),BatteryCare=SwitchValue(Action("BatteryCareSwitch")),WallpaperApplication=SwitchValue(Action("WallpaperApplicationSwitch")),TaskbarRepair=Action("TaskbarRepairSwitch").IsChecked==false?0:Action("TaskbarRepairSwitch").IsChecked==true?2:1,
                PresenceWake=SwitchValue(Action("PresenceWakeSwitch")),PresenceOnlooker=SwitchValue(Action("PresenceOnlookerSwitch")),BatteryPercentage=SwitchValue(Action("BatteryPercentageSwitch")),ClockSeconds=SwitchValue(Action("ClockSecondsSwitch")),NightLight=SwitchValue(Action("NightLightSwitch")),NightLightStrength=(int)Math.Round(nightLightSlider.Value)};
        }
    }
    static DisplayModeConfiguration CloneDisplay(DisplayModeConfiguration d){return new DisplayModeConfiguration{DisplayId=d.DisplayId,DisplayWidth=d.DisplayWidth,DisplayHeight=d.DisplayHeight,RefreshRate=d.RefreshRate,DisplayScale=d.DisplayScale,Hdr=d.Hdr,DynamicRefresh=d.DynamicRefresh,ContentBrightness=d.ContentBrightness,Brightness=d.Brightness};}
    int DisplayNumber(string id){DisplayInfo display;if(knownDisplays.TryGetValue(id,out display)){string name=display.DeviceName;int start=name.LastIndexOf("DISPLAY",StringComparison.OrdinalIgnoreCase),number;if(start>=0&&Int32.TryParse(name.Substring(start+7),out number))return number;}return Int32.MaxValue;}
    string ScreenName(string id){int number=DisplayNumber(id);return number==Int32.MaxValue?"Отключённый экран":"Экран "+number;}
    void CaptureDisplayEditor(){
        if(updating||updatingDisplays||String.IsNullOrEmpty(editingDisplayId)||displaySelector==null)return;
        DisplayInfo display=displaySelector.SelectedItem as DisplayInfo;if(display==null||!String.Equals(display.MonitorId,editingDisplayId,StringComparison.OrdinalIgnoreCase))return;
        DisplayResolution resolution=resolutionSelector.SelectedItem as DisplayResolution;RefreshChoice rate=refreshSelector.SelectedItem as RefreshChoice;ScaleChoice scale=scaleSelector.SelectedItem as ScaleChoice;ContentChoice content=contentBrightnessSelector.SelectedItem as ContentChoice;
        DisplayModeConfiguration action=new DisplayModeConfiguration{DisplayId=editingDisplayId,DisplayWidth=resolution==null?0:resolution.Width,DisplayHeight=resolution==null?0:resolution.Height,RefreshRate=rate==null?0:rate.Rate,DisplayScale=scale==null?0:scale.Percent,Hdr=SwitchValue(Action("HdrSwitch")),DynamicRefresh=SwitchValue(Action("DynamicRefreshSwitch")),ContentBrightness=contentBrightnessSelector.IsEnabled&&content!=null?content.Value:0,Brightness=brightnessToggle.IsEnabled&&brightnessToggle.IsChecked==true?(int)Math.Round(brightnessSlider.Value):-1};
        if(action.HasActions)displaySettings[editingDisplayId]=action;else displaySettings.Remove(editingDisplayId);
    }
    static int SwitchValue(CheckBox control){return !control.IsEnabled?0:control.IsChecked.HasValue?(control.IsChecked.Value?1:2):0;}
    void SetAvailable(string name,bool available){CheckBox control=Action(name);control.IsEnabled=available;if(!available)control.IsChecked=null;}
    void RefreshProgramAvailability(){ProgramAvailabilitySnapshot programs=ProgramAvailability.Read();ApplyProgramAvailability(programs.ScreenXpert,programs.WallpaperEngine,programs.ArmouryCrate);}
    void ApplyProgramAvailability(bool screenXpert,bool wallpaperEngine,bool armouryCrate)
    {
        bool previous=updating;updating=true;
        try{
            SetProgramCategoryAvailable("PerformanceSection",screenXpert,new[]{"FanWhisper","FanPerformance","FanStandard","BatteryCareSwitch"});
            SetProgramCategoryAvailable("WallpaperSection",wallpaperEngine,new[]{"WallpaperPlaybackSwitch","WallpaperAudioSwitch","WallpaperApplicationSwitch"});
            SetProgramCategoryAvailable("ArmourySection",armouryCrate,new string[0]);
        }finally{updating=previous;}
        SelectionChanged(null,null);
    }
    void SetProgramCategoryAvailable(string name,bool available,string[] actions)
    {
        Expander category=(Expander)Window.FindName(name);category.IsEnabled=available;
        if(!available)category.IsExpanded=false;
        foreach(string action in actions){CheckBox control=Action(action);control.IsEnabled=available;if(!available)control.IsChecked=control.IsThreeState?(bool?)null:false;}
    }
    void RefreshHardware(){bool previous=updating;updating=true;try{SetAvailable("PresenceWakeSwitch",SensorCapabilities.HasPresence(2));SetAvailable("PresenceOnlookerSwitch",SensorCapabilities.HasPresence(3));SetAvailable("PresenceSleepSwitch",SensorCapabilities.HasPresence(false));SetAvailable("PresenceDimSwitch",SensorCapabilities.HasPresence(true));SetAvailable("AdaptiveBrightnessSwitch",SensorCapabilities.HasAmbientLight()&&SensorCapabilities.HasBrightness());SetAvailable("NightLightSwitch",NightLightControl.Available());SetAvailable("BatteryPercentageSwitch",SensorCapabilities.HasBattery());}finally{updating=previous;}SelectionChanged(null,null);}
    CheckBox[] WireExclusiveChoices(string[] names){CheckBox[] result=new CheckBox[names.Length];for(int i=0;i<names.Length;i++){result[i]=(CheckBox)Window.FindName(names[i]);result[i].Checked+=ExclusiveSelectionChanged;result[i].Unchecked+=ExclusiveSelectionChanged;}return result;}
    static int SelectedMode(CheckBox[] choices){for(int i=0;i<choices.Length;i++)if(choices[i].IsEnabled&&choices[i].IsChecked==true)return i+1;return 0;}
    static void CycleSwitch(CheckBox control)
    {
        if(!control.IsThreeState)control.IsChecked=control.IsChecked!=true;
        else if(control.Name=="TaskbarRepairSwitch")control.IsChecked=control.IsChecked==false?null:control.IsChecked==null?(bool?)true:false;
        else control.IsChecked=control.IsChecked==null?true:control.IsChecked==true?(bool?)false:null;
    }
    void SwitchPointerDown(object sender,MouseButtonEventArgs e)
    {
        CheckBox control=(CheckBox)sender;if(!control.IsEnabled)return;
        FrameworkElement track=control.Template.FindName("SwitchTrack",control) as FrameworkElement;bool trackHit=false;
        // Sliders handle their own pointer input, including clicks on the track and thumb.
        for(DependencyObject source=e.OriginalSource as DependencyObject;source!=null&&source!=control;){
            if(source is Slider)return;
            if(source==track)trackHit=true;
            FrameworkContentElement content=source as FrameworkContentElement;
            source=source is Visual?VisualTreeHelper.GetParent(source):content!=null?content.Parent:null;
        }
        Point p=track==null?new Point(-1,-1):e.GetPosition(track);
        control.Focus();
        if(!trackHit||track==null||p.X<0||p.X>track.ActualWidth||p.Y<0||p.Y>track.ActualHeight)CycleSwitch(control);
        else control.IsChecked=SwitchPosition(control.IsThreeState,p.X/track.ActualWidth);
        e.Handled=true;
    }
    static bool? SwitchPosition(bool threeState,double position){return !threeState?(bool?)(position>=0.5):position<1.0/3?false:position>2.0/3?(bool?)true:null;}
    void SwitchKeyDown(object sender,KeyEventArgs e)
    {
        CheckBox control=(CheckBox)sender;
        if(!control.IsEnabled)return;
        if(control.Name=="NightLightSwitch"&&nightLightSlider.IsKeyboardFocusWithin)return;
        if(control.Name=="BrightnessToggleButton"&&brightnessSlider.IsKeyboardFocusWithin)return;
        if(e.Key==Key.Space){CycleSwitch(control);e.Handled=true;return;}
        if(control.Name=="BrightnessToggleButton"){
            if(e.Key==Key.Left||e.Key==Key.Home||e.Key==Key.Delete)control.IsChecked=false;
            else if(e.Key==Key.Right||e.Key==Key.End)control.IsChecked=true;else return;e.Handled=true;return;
        }
        if(control.Name=="TaskbarRepairSwitch"){
            int value=control.IsChecked==false?0:control.IsChecked==true?2:1;
            if(e.Key==Key.Left||e.Key==Key.Down)value=Math.Max(0,value-1);
            else if(e.Key==Key.Right||e.Key==Key.Up)value=Math.Min(2,value+1);
            else if(e.Key==Key.Home||e.Key==Key.Delete)value=0;else if(e.Key==Key.End)value=2;else return;
            control.IsChecked=value==0?(bool?)false:value==2?(bool?)true:null;e.Handled=true;return;
        }
        if(e.Key==Key.Left||e.Key==Key.Home)control.IsChecked=false;
        else if(e.Key==Key.Right||e.Key==Key.End)control.IsChecked=true;
        else if(e.Key==Key.Down||e.Key==Key.Up||e.Key==Key.Delete)control.IsChecked=null;
        else return;e.Handled=true;
    }
    public void ApplyPreset(bool performance)
    {
        RefreshProgramAvailability();RefreshDisplays();RefreshHardware();
        ModeConfiguration preset=PresetBuilder.Build(performance,ReadPresetCapabilities(performance));
        bool previousUpdating=updating,previousDisplays=updatingDisplays;
        updating=true;updatingDisplays=true;
        try{
            foreach(CheckBox check in switches)check.IsChecked=check.Name=="TaskbarRepairSwitch"?(bool?)false:null;
            for(int i=0;i<powerChecks.Length;i++)powerChecks[i].IsChecked=preset.PowerMode==i+1;
            for(int i=0;i<fanChecks.Length;i++)fanChecks[i].IsChecked=preset.FanProfile==i+1;
            SetPresetSwitch("EnergySaverSwitch",preset.EnergySaver);
            SetPresetSwitch("DarkThemeSwitch",preset.DarkTheme);
            SetPresetSwitch("WallpaperPlaybackSwitch",preset.WallpaperPlayback);
            SetPresetSwitch("WallpaperAudioSwitch",preset.WallpaperAudio);
            SetPresetSwitch("WallpaperApplicationSwitch",preset.WallpaperApplication);
            Action("TaskbarRepairSwitch").IsChecked=preset.TaskbarRepair==0?(bool?)false:preset.TaskbarRepair==2?(bool?)true:null;
            displaySettings.Clear();nightLightSlider.Value=50;
            foreach(DisplayModeConfiguration display in preset.Displays)displaySettings[display.DisplayId]=CloneDisplay(display);
            LoadDisplayEditor();
        }finally{updating=previousUpdating;updatingDisplays=previousDisplays;}
        SelectionChanged(null,null);
    }
    void SetPresetSwitch(string name,int value){Action(name).IsChecked=value==0?(bool?)null:value==1;}
    PresetCapabilities ReadPresetCapabilities(bool performance)
    {
        PresetSystemSnapshot system=PresetSystemSupport.Read(displaySelector.Items.Count>1);
        PresetCapabilities capabilities=new PresetCapabilities{
            EnergySaver=system.EnergySaver&&Action("EnergySaverSwitch").IsEnabled,
            PowerMode=system.PowerMode&&powerChecks[performance?2:0].IsEnabled,
            DarkTheme=system.DarkTheme&&Action("DarkThemeSwitch").IsEnabled,
            TaskbarRepair=system.TaskbarRepair&&Action("TaskbarRepairSwitch").IsEnabled,
            FanProfile=system.FanProfile&&fanChecks[performance?1:0].IsEnabled,
            WallpaperPlayback=Action("WallpaperPlaybackSwitch").IsEnabled,
            WallpaperAudio=Action("WallpaperAudioSwitch").IsEnabled,
            WallpaperApplication=Action("WallpaperApplicationSwitch").IsEnabled
        };
        foreach(DisplayInfo display in displaySelector.Items){
            bool enabled;
            PresetDisplayCapabilities target=new PresetDisplayCapabilities{Display=display,Scales=HdrControl.InspectScales(display.MonitorId),HdrSupported=HdrControl.CanChange(display.MonitorId,performance)};
            DisplayModeConfiguration mode=PresetBuilder.BuildDisplay(performance,target);
            target.DynamicRefreshSupported=performance?HdrControl.InspectDynamic(display.MonitorId,mode.DisplayWidth,mode.DisplayHeight,mode.RefreshRate,out enabled):HasDynamicRefreshCapability(display);
            capabilities.Displays.Add(target);
        }
        return capabilities;
    }
    static bool HasDynamicRefreshCapability(DisplayInfo display)
    {
        if(display==null)return false;bool enabled;
        if(HdrControl.InspectDynamic(display.MonitorId,out enabled))return true;
        DisplayModeConfiguration recommended=PresetBuilder.BuildDisplay(true,new PresetDisplayCapabilities{Display=display});
        return HdrControl.InspectDynamic(display.MonitorId,recommended.DisplayWidth,recommended.DisplayHeight,recommended.RefreshRate,out enabled);
    }
    public void ResetScript(){
        bool previous=updating,previousDisplays=updatingDisplays;updating=true;updatingDisplays=true;
        try{foreach(CheckBox check in switches)check.IsChecked=check.Name=="TaskbarRepairSwitch"?(bool?)false:null;foreach(CheckBox check in powerChecks)check.IsChecked=false;foreach(CheckBox check in fanChecks)check.IsChecked=false;displaySettings.Clear();nightLightSlider.Value=50;LoadDisplayEditor();}
        finally{updating=previous;updatingDisplays=previousDisplays;}
        SelectionChanged(null,null);
    }
    void SelectionChanged(object sender,RoutedEventArgs e)
    {
        if(updating)return;
        brightnessSlider.IsEnabled=brightnessToggle.IsEnabled&&brightnessToggle.IsChecked==true;
        nightLightSlider.IsEnabled=Action("NightLightSwitch").IsChecked==true;
        status.Text="";UpdatePreview();
    }
    void ExclusiveSelectionChanged(object sender,RoutedEventArgs e)
    {
        if(updating)return;updating=true;
        try{
            CheckBox selected=(CheckBox)sender;
            CheckBox[] choices=Array.IndexOf(powerChecks,selected)>=0?powerChecks:fanChecks;
            if(selected.IsChecked==true){foreach(CheckBox check in choices)if(check!=selected)check.IsChecked=false;}
        }finally{updating=false;}
        SelectionChanged(null,null);
    }
    void RefreshDisplays()
    {
        if(displayObserverClosed)return;
        if(displayRefreshRunning){displayRefreshPending=true;return;}
        displayRefreshRunning=true;displayRefreshPending=false;
        try{
            List<DisplayInfo> displays;
            try{displays=DisplayControl.Enumerate();}catch{displays=new List<DisplayInfo>();}
            ApplyDisplayList(displays);
        }finally{displayRefreshRunning=false;if(displayRefreshPending)QueueDisplayRefresh();}
    }
    void QueueDisplayRefresh(){if(displayObserverClosed)return;if(displayRefreshRunning){displayRefreshPending=true;return;}displayRefreshTimer.Stop();displayRefreshTimer.Start();}
    IntPtr DisplayMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        // Display mode changes also arrive when a screen is disabled in Windows.
        // Device notifications cover monitor connection changes with unchanged desktop size.
        long deviceEvent=wParam.ToInt64();
        if(message==0x7E||(message==0x219&&(deviceEvent==7||deviceEvent==0x8000||deviceEvent==0x8004)))QueueDisplayRefresh();
        return IntPtr.Zero;
    }
    static bool SameRates(List<uint> left,List<uint> right){if(left.Count!=right.Count)return false;for(int i=0;i<left.Count;i++)if(left[i]!=right[i])return false;return true;}
    bool SameDisplayList(List<DisplayInfo> displays)
    {
        if(displaySelector.Items.Count!=displays.Count)return false;
        for(int i=0;i<displays.Count;i++){
            DisplayInfo a=displaySelector.Items[i] as DisplayInfo,b=displays[i];
            if(a==null||!String.Equals(a.MonitorId,b.MonitorId,StringComparison.OrdinalIgnoreCase)||a.DeviceName!=b.DeviceName||a.Label!=b.Label||a.Width!=b.Width||a.Height!=b.Height||a.CurrentRate!=b.CurrentRate||a.RecommendedWidth!=b.RecommendedWidth||a.RecommendedHeight!=b.RecommendedHeight||!SameRates(a.Rates,b.Rates)||a.Resolutions.Count!=b.Resolutions.Count)return false;
            for(int j=0;j<a.Resolutions.Count;j++){DisplayResolution x=a.Resolutions[j],y=b.Resolutions[j];if(x.Width!=y.Width||x.Height!=y.Height||x.IsRecommended!=y.IsRecommended||!SameRates(x.Rates,y.Rates))return false;}
        }
        return true;
    }
    void ApplyDisplayList(List<DisplayInfo> displays)
    {
        if(SameDisplayList(displays)&&displaySelector.ItemsSource!=null)return;
        CaptureDisplayEditor();DisplayInfo old=displaySelector.SelectedItem as DisplayInfo;
        bool previous=updatingDisplays;updatingDisplays=true;
        try{
            displaySelector.IsDropDownOpen=false;
            foreach(DisplayInfo display in displays)knownDisplays[display.MonitorId]=display;
            displaySelector.ItemsSource=displays;DisplayInfo selected=old==null?null:displays.Find(delegate(DisplayInfo d){return String.Equals(d.MonitorId,old.MonitorId,StringComparison.OrdinalIgnoreCase);});
            displaySelector.SelectedItem=selected??(displays.Count==0?null:displays[0]);
            displayUnavailable.Visibility=displays.Count==0?Visibility.Visible:Visibility.Collapsed;
        }finally{updatingDisplays=previous;}
        LoadDisplayEditor();
    }
    void LoadDisplayEditor(){
        bool previous=updating,previousDisplays=updatingDisplays;updating=true;updatingDisplays=true;
        try{
            DisplayInfo display=displaySelector.SelectedItem as DisplayInfo;editingDisplayId=display==null?null:display.MonitorId;
            DisplayModeConfiguration saved;DisplayModeConfiguration d=!String.IsNullOrEmpty(editingDisplayId)&&displaySettings.TryGetValue(editingDisplayId,out saved)?CloneDisplay(saved):new DisplayModeConfiguration{DisplayId=editingDisplayId};
            RefreshResolutions();foreach(DisplayResolution choice in resolutionSelector.Items)if(choice.Width==d.DisplayWidth&&choice.Height==d.DisplayHeight)resolutionSelector.SelectedItem=choice;
            RefreshScales();foreach(ScaleChoice choice in scaleSelector.Items)if(choice.Percent==d.DisplayScale)scaleSelector.SelectedItem=choice;
            RefreshRates();foreach(RefreshChoice choice in refreshSelector.Items)if(choice.Rate==d.RefreshRate)refreshSelector.SelectedItem=choice;
            Action("HdrSwitch").IsChecked=CheckedValue(d.Hdr);Action("DynamicRefreshSwitch").IsChecked=CheckedValue(d.DynamicRefresh);
            foreach(ContentChoice choice in contentBrightnessSelector.Items)if(choice.Value==d.ContentBrightness)contentBrightnessSelector.SelectedItem=choice;
            brightnessToggle.IsChecked=d.Brightness>=0;brightnessSlider.Value=d.Brightness>=0?d.Brightness:100;
            UpdateDisplayAvailability();
        }finally{updating=previous;updatingDisplays=previousDisplays;}
        if(!updating)SelectionChanged(null,null);
    }
    static bool? CheckedValue(int value){return value==1?(bool?)true:value==2?(bool?)false:null;}
    void RefreshScales(){bool previous=updatingDisplays;updatingDisplays=true;try{DisplayInfo d=displaySelector.SelectedItem as DisplayInfo;List<ScaleChoice> choices=new List<ScaleChoice>{new ScaleChoice()};if(d!=null){int recommended=HdrControl.InspectRecommendedScale(d.MonitorId);foreach(int value in HdrControl.InspectScales(d.MonitorId))choices.Add(new ScaleChoice{Percent=value,Recommended=value==recommended});}scaleSelector.ItemsSource=choices;scaleSelector.SelectedIndex=0;}finally{updatingDisplays=previous;}}
    void RefreshResolutions()
    {
        bool previous=updatingDisplays;updatingDisplays=true;
        try{DisplayInfo display=displaySelector.SelectedItem as DisplayInfo;List<DisplayResolution> list=new List<DisplayResolution>{new DisplayResolution()};if(display!=null)list.AddRange(display.Resolutions);resolutionSelector.ItemsSource=list;resolutionSelector.SelectedIndex=0;}
        finally{updatingDisplays=previous;}
    }
    void RefreshRates()
    {
        bool previous=updatingDisplays;updatingDisplays=true;
        try{
            DisplayInfo display=displaySelector.SelectedItem as DisplayInfo;List<RefreshChoice> rates=new List<RefreshChoice>{new RefreshChoice{Rate=0}};
            DisplayResolution resolution=resolutionSelector.SelectedItem as DisplayResolution;
            if(display!=null)foreach(uint rate in resolution!=null&&resolution.Width!=0?resolution.Rates:display.Rates)rates.Add(new RefreshChoice{Rate=rate});
            refreshSelector.ItemsSource=rates;refreshSelector.SelectedIndex=0;
        }finally{updatingDisplays=previous;}
    }
    void DisplaySelectionChanged(object sender,RoutedEventArgs e)
    {
        if(updating||updatingDisplays)return;
        UpdateDisplayAvailability();SelectionChanged(null,null);
    }
    void UpdateDisplayAvailability(){
        displaySelector.IsEnabled=displaySelector.Items.Count>0;resolutionSelector.IsEnabled=displaySelector.Items.Count>0&&resolutionSelector.Items.Count>1;refreshSelector.IsEnabled=displaySelector.Items.Count>0&&refreshSelector.Items.Count>1;
        DisplayInfo display=displaySelector.SelectedItem as DisplayInfo;bool supported=false,enabled=false;int option;
        bool hdr=display!=null&&HdrControl.TryInspect(display.MonitorId,out supported,out enabled)&&supported;
        bool dynamic=HasDynamicRefreshCapability(display);
        bool content=display!=null&&HdrControl.TryInspectContentBrightness(display.MonitorId,out option);
        bool previous=updating;updating=true;try{Action("HdrSwitch").IsEnabled=hdr;if(!hdr)Action("HdrSwitch").IsChecked=null;Action("DynamicRefreshSwitch").IsEnabled=dynamic;if(!dynamic)Action("DynamicRefreshSwitch").IsChecked=null;contentBrightnessSelector.IsEnabled=content;if(!content)contentBrightnessSelector.SelectedIndex=0;scaleSelector.IsEnabled=scaleSelector.Items.Count>1;brightnessToggle.IsEnabled=display!=null&&DisplayBrightnessControl.Available(display.MonitorId);if(!brightnessToggle.IsEnabled)brightnessToggle.IsChecked=false;}finally{updating=previous;}
    }
    void UpdatePreview()
    {
        ModeConfiguration c=Configuration;previewActions.Clear();
        PreviewSwitch("EnergySaverSwitch",c.EnergySaver,"Включить энергосбережение","Выключить энергосбережение");
        if(c.PowerMode!=0)AddPreview("PowerMode",null,c.PowerMode==1?"Максимальная эффективность энергосбережения":c.PowerMode==2?"Сбалансированный":"Оптимальная производительность");
        foreach(DisplayModeConfiguration d in c.Displays){
            string screen=ScreenName(d.DisplayId)+" - ";
            if(d.DisplayScale!=0)AddPreview("DisplayScale",d.DisplayId,screen+"масштаб "+d.DisplayScale+"%");
            if(d.DisplayWidth!=0)AddPreview("DisplayWidth",d.DisplayId,screen+"разрешение "+d.DisplayWidth+" × "+d.DisplayHeight);
            if(d.RefreshRate!=0)AddPreview("RefreshRate",d.DisplayId,screen+"частота обновления "+d.RefreshRate+" Гц");
            if(d.Hdr!=0)AddPreview("Hdr",d.DisplayId,screen+(d.Hdr==1?"включить HDR":"выключить HDR"));
            if(d.DynamicRefresh!=0)AddPreview("DynamicRefresh",d.DisplayId,screen+(d.DynamicRefresh==1?"включить динамическую частоту обновления":"выключить динамическую частоту обновления"));
            if(d.Brightness>=0)AddPreview("Brightness",d.DisplayId,screen+"яркость "+d.Brightness+"%");
            if(d.ContentBrightness!=0)AddPreview("ContentBrightness",d.DisplayId,screen+"яркость с учётом содержимого - "+(d.ContentBrightness==1?"всегда":d.ContentBrightness==2?"откл.":"только при питании от батареи"));
        }
        PreviewSwitch("NightLightSwitch",c.NightLight,"Включить ночной свет - интенсивность "+c.NightLightStrength+"%","Выключить ночной свет");
        PreviewSwitch("AdaptiveBrightnessSwitch",c.AdaptiveBrightness,"Автоматически менять яркость при изменении освещения","Не менять яркость при изменении освещения");
        PreviewSwitch("DarkThemeSwitch",c.DarkTheme,"Включить тёмную тему Windows","Выключить тёмную тему Windows");
        PreviewSwitch("ScreenSaverSwitch",c.ScreenSaver,"Включить заставку","Выключить заставку");
        PreviewSwitch("UsbPowerSwitch",c.UsbPower,"Останавливать USB-устройства, когда экран выключен, для экономии заряда батареи","Не останавливать USB-устройства, когда экран выключен");
        PreviewSwitch("BatteryPercentageSwitch",c.BatteryPercentage,"Показывать процент заряда батареи на панели задач","Скрыть процент заряда батареи на панели задач");
        PreviewSwitch("PresenceSleepSwitch",c.PresenceSleep,"Отключать экран, когда я ухожу","Не отключать экран, когда я ухожу");
        PreviewSwitch("PresenceWakeSwitch",c.PresenceWake,"Пробуждать мое устройство, когда я приближаюсь","Не пробуждать мое устройство, когда я приближаюсь");
        PreviewSwitch("PresenceDimSwitch",c.PresenceDim,"Автоматически уменьшать яркость, когда я отвожу взгляд","Не уменьшать яркость, когда я отвожу взгляд");
        PreviewSwitch("PresenceOnlookerSwitch",c.PresenceOnlooker,"Обнаружить, когда другие люди смотрят на мой экран","Выключить обнаружение других людей, смотрящих на экран");
        PreviewSwitch("ClockSecondsSwitch",c.ClockSeconds,"Показывать секунды в часах на панели задач","Скрыть секунды в часах на панели задач");
        if(c.TaskbarRepair!=0)AddPreview("TaskbarRepairSwitch",null,"Исправление панели задач - Все панели задач, затем "+(c.TaskbarRepair==1?"Главная панель задач и панель задач, в которой открыто окно":"Панель задач, в которой открыто окно"));
        PreviewSwitch("BatteryCareSwitch",c.BatteryCare,"Включить режим бережной зарядки","Выключить режим бережной зарядки");
        if(c.FanProfile!=0)AddPreview("FanProfile",null,c.FanProfile==1?"Тихий режим вентилятора":c.FanProfile==2?"Производительный режим вентилятора":"Стандартный режим вентилятора");
        PreviewSwitch("WallpaperPlaybackSwitch",c.WallpaperPlayback,"Продолжить воспроизведение обоев Wallpaper Engine","Поставить обои Wallpaper Engine на паузу");
        PreviewSwitch("WallpaperAudioSwitch",c.WallpaperAudio,"Включить звук обоев Wallpaper Engine","Выключить звук обоев Wallpaper Engine");
        PreviewSwitch("WallpaperApplicationSwitch",c.WallpaperApplication,"Запустить Wallpaper Engine","Завершить Wallpaper Engine");
        System.Text.StringBuilder text=new System.Text.StringBuilder();
        foreach(PreviewAction entry in previewActions){if(text.Length>0)text.Append(Environment.NewLine+Environment.NewLine);entry.Start=text.Length;entry.Length=entry.Text.Length;text.Append(entry.Text);}
        string value=previewActions.Count==0?"Действия не выбраны.":text.ToString();
        if(preview.Text!=value){double offset=preview.VerticalOffset;int start=preview.SelectionStart,length=preview.SelectionLength;preview.Text=value;start=Math.Min(start,value.Length);preview.Select(start,Math.Min(length,value.Length-start));preview.ScrollToVerticalOffset(offset);}
        ((Button)Window.FindName("ResetScriptButton")).IsEnabled=c.HasActions;
        UpdateSaveState();
    }
    void AddPreview(string key,string displayId,string text){previewActions.Add(new PreviewAction{Key=key,DisplayId=displayId,Text=text});}
    void PreviewSwitch(string key,int value,string on,string off){if(value!=0)AddPreview(key,null,value==1?on:off);}
    int PreviewIndexAt(int character){for(int i=0;i<previewActions.Count;i++){PreviewAction entry=previewActions[i];if(character>=entry.Start&&character<entry.Start+entry.Length)return i;}return -1;}
    void PreviewPointerDown(object sender,MouseButtonEventArgs e){previewDown=e.GetPosition(preview);previewDownAction=PreviewIndexAt(preview.GetCharacterIndexFromPoint(previewDown,false));previewDragged=false;previewCanRemove=e.ClickCount==1&&Keyboard.Modifiers==ModifierKeys.None&&preview.SelectionLength==0;}
    void PreviewPointerMove(object sender,MouseEventArgs e){if(e.LeftButton==MouseButtonState.Pressed){Point point=e.GetPosition(preview);if(Math.Abs(point.X-previewDown.X)>=SystemParameters.MinimumHorizontalDragDistance||Math.Abs(point.Y-previewDown.Y)>=SystemParameters.MinimumVerticalDragDistance)previewDragged=true;}}
    void PreviewPointerUp(object sender,MouseButtonEventArgs e){
        int index=PreviewIndexAt(preview.GetCharacterIndexFromPoint(e.GetPosition(preview),false));
        bool remove=previewCanRemove&&!previewDragged&&preview.SelectionLength==0&&index>=0&&index==previewDownAction;previewCanRemove=false;
        if(remove){RemovePreviewAction(index);e.Handled=true;}
    }
    public void RemovePreviewAction(int index){
        if(index<0||index>=previewActions.Count)return;PreviewAction entry=previewActions[index];
        bool previous=updating;updating=true;
        try{
            if(entry.DisplayId!=null){DisplayModeConfiguration d;if(displaySettings.TryGetValue(entry.DisplayId,out d)){
                switch(entry.Key){case "DisplayWidth":d.DisplayWidth=0;d.DisplayHeight=0;break;case "DisplayScale":d.DisplayScale=0;break;case "RefreshRate":d.RefreshRate=0;break;case "Hdr":d.Hdr=0;break;case "DynamicRefresh":d.DynamicRefresh=0;break;case "ContentBrightness":d.ContentBrightness=0;break;case "Brightness":d.Brightness=-1;break;}
                if(!d.HasActions)displaySettings.Remove(entry.DisplayId);
            }if(String.Equals(entry.DisplayId,editingDisplayId,StringComparison.OrdinalIgnoreCase))LoadDisplayEditor();}
            else if(entry.Key=="PowerMode")foreach(CheckBox check in powerChecks)check.IsChecked=false;
            else if(entry.Key=="FanProfile")foreach(CheckBox check in fanChecks)check.IsChecked=false;
            else Action(entry.Key).IsChecked=entry.Key=="TaskbarRepairSwitch"?(bool?)false:null;
        }finally{updating=previous;}
        SelectionChanged(null,null);
    }

    string ScriptState()
    {
        using(System.Security.Cryptography.SHA256 hash=System.Security.Cryptography.SHA256.Create())return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(Configuration)+":"+Convert.ToBase64String(hash.ComputeHash(IconChoice.GetBytes()));
    }
    void UpdateSaveState(){save.IsEnabled=Configuration.HasActions&&ScriptState()!=savedScriptState;}
    public static BitmapSource ImageSource(byte[] data){using(MemoryStream s=new MemoryStream(data)){BitmapDecoder decoder=BitmapDecoder.Create(s,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);BitmapFrame chosen=decoder.Frames[0];foreach(BitmapFrame f in decoder.Frames)if(f.PixelWidth>chosen.PixelWidth)chosen=f;chosen.Freeze();return chosen;}}
    void PopupKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape){popup.IsOpen=false;iconButton.Focus();e.Handled=true;return;}
        Button focused=Keyboard.FocusedElement as Button;if(focused==null)return;
        int index=icons.Children.IndexOf(focused),next=index;
        if(e.Key==Key.Left)next=(index+5)%6;else if(e.Key==Key.Right)next=(index+1)%6;
        else if(e.Key==Key.Up||e.Key==Key.Down)next=(index+3)%6;
        else if(e.Key==Key.Home)next=0;else if(e.Key==Key.End)next=5;
        else if(e.Key==Key.Enter){focused.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));e.Handled=true;return;}
        else return;
        ((Button)icons.Children[next]).Focus();e.Handled=true;
    }
    public void RefreshIcons()
    {
        currentIcon.Source=ImageSource(IconChoice.GetBytes());icons.Children.Clear();
        for(int i=0;i<6;i++){
            int choice=i;Button button=new Button{Margin=(Thickness)Window.FindResource("Gap.Tile"),Height=(double)Window.FindResource("Size.Tile"),Tag=i};
            System.Windows.Automation.AutomationProperties.SetName(button,iconNames[i]);
            if(i<5)button.ToolTip=iconNames[i];
            bool selected=i<5&&IconChoice.CustomPath==null&&IconChoice.BuiltInIndex==i||i==5&&IconChoice.CustomPath!=null;
            if(selected)button.Background=(Brush)Window.FindResource("Brush.SurfaceHover");
            if(i<5||IconChoice.CustomPath!=null){byte[] bytes=i<5?EmbeddedAssets.Read("Icon"+i):IconChoice.GetBytes();button.Content=new Image{Source=ImageSource(bytes),Width=(double)Window.FindResource("Size.Icon"),Height=(double)Window.FindResource("Size.Icon")};}
            else {
                Grid unknown=new Grid{Width=(double)Window.FindResource("Size.Icon"),Height=(double)Window.FindResource("Size.Icon")};
                unknown.Children.Add(new System.Windows.Shapes.Rectangle{Stroke=(Brush)Window.FindResource("Brush.TextMuted"),StrokeThickness=1,StrokeDashArray=new DoubleCollection{2,2},RadiusX=4,RadiusY=4});
                unknown.Children.Add(new TextBlock{Text="?",FontSize=(double)Window.FindResource("Font.Display"),Foreground=(Brush)Window.FindResource("Brush.TextMuted"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});button.Content=unknown;
            }
            button.Click+=delegate{
                popup.IsOpen=false;
                if(choice<5){IconChoice.SelectBuiltIn(choice);RefreshIcons();iconButton.Focus();return;}
                try{
                    string path=NativeFileDialogs.OpenIcon(new System.Windows.Interop.WindowInteropHelper(Window).Handle);
                    if(path!=null){ImageSource(File.ReadAllBytes(path));IconChoice.SelectCustom(path);RefreshIcons();status.Text="";}
                }catch(Exception ex){status.Text="Не удалось выбрать иконку: "+FormatUserError(ex);status.Foreground=(Brush)Window.FindResource("Brush.TextMuted");}
                iconButton.Focus();
            };
            icons.Children.Add(button);
        }
        UpdateSaveState();
    }
    void SaveToPath(string path)
    {
        ExecutableExporter.Save(path,Configuration,IconChoice.GetBytes());IconChoice.Saved();savedScriptState=ScriptState();RefreshIcons();status.Text="";
    }
    void SaveClicked(object sender,RoutedEventArgs e)
    {
        if(!save.IsEnabled)return;
        try{
            string path=NativeFileDialogs.SaveExecutable(new System.Windows.Interop.WindowInteropHelper(Window).Handle);
            if(path==null)return;
            SaveToPath(path);
        }catch(Exception ex){status.Text="Не удалось сохранить: "+FormatUserError(ex);status.Foreground=(Brush)Window.FindResource("Brush.TextMuted");}
    }
    public static string FormatUserError(Exception error)
    {
        if(error is UnauthorizedAccessException||error is System.Security.SecurityException)return "Нет доступа к выбранному файлу или папке.";
        if(error is PathTooLongException)return "Слишком длинный путь. Выберите другую папку или более короткое имя.";
        if(error is DirectoryNotFoundException)return "Выбранная папка не найдена.";
        if(error is FileNotFoundException)return "Выбранный файл не найден.";
        if(error is InvalidDataException||error is FileFormatException||error is NotSupportedException)return "Файл или выбранные параметры имеют неверный формат.";
        System.ComponentModel.Win32Exception systemError=error as System.ComponentModel.Win32Exception;
        if(systemError!=null){
            if(systemError.NativeErrorCode==5)return "Нет доступа к выбранному файлу или папке.";
            if(systemError.NativeErrorCode==32||systemError.NativeErrorCode==33)return "Файл занят другой программой. Закройте её и повторите попытку.";
            if(systemError.NativeErrorCode==112)return "Недостаточно свободного места на диске.";
            return "Windows не удалось выполнить операцию. Проверьте доступ к файлу и повторите попытку.";
        }
        if(error is IOException)return "Файл недоступен. Проверьте папку, свободное место и отсутствие открытой копии программы.";
        if(error is ArgumentException)return "Проверьте имя файла и выбранные параметры.";
        return "Операция не выполнена. Проверьте выбранный файл и повторите попытку.";
    }
}
public static class ConstructorApp
{
    [STAThread] public static int Main()
    {
        // Set before WPF initializes its rendering system. This affects this process only.
        AppContext.SetSwitch("Switch.System.Windows.Media.MediaContext.DisableDirtyRectangles",true);
        return Run();
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Run(){try{Application app=new Application();app.ShutdownMode=ShutdownMode.OnMainWindowClose;ConstructorWindow ui=new ConstructorWindow();return app.Run(ui.Window);}catch{return 1;}}
}
