using System;using System.Collections.Generic;using System.IO;using System.Reflection;using System.Runtime.InteropServices;using System.Windows;using System.Windows.Controls;using System.Windows.Controls.Primitives;using System.Windows.Input;using System.Windows.Markup;using System.Windows.Media;using System.Windows.Media.Imaging;using Microsoft.Win32;

public sealed class ConstructorWindow
{
    public Window Window {get;private set;}
    public readonly IconSelection IconChoice=new IconSelection();
    readonly CheckBox[] powerChecks,fanChecks,switches;readonly Button save,iconButton;readonly Image currentIcon;readonly Popup popup;readonly UniformGrid icons;readonly TextBlock status;readonly TextBox preview;
    readonly ToggleButton brightnessToggle;readonly Slider brightnessSlider,nightLightSlider;readonly ComboBox displaySelector,resolutionSelector,refreshSelector,scaleSelector,contentBrightnessSelector;readonly TextBlock displayUnavailable;
    static readonly string[] switchNames={"EnergySaverSwitch","DarkThemeSwitch","HdrSwitch","PresenceSleepSwitch","PresenceDimSwitch","AdaptiveBrightnessSwitch","ScreenSaverSwitch","UsbPowerSwitch","WallpaperPlaybackSwitch","WallpaperAudioSwitch","BatteryCareSwitch","WallpaperApplicationSwitch","TaskbarRepairSwitch","NightLightSwitch","DynamicRefreshSwitch","BatteryPercentageSwitch","ClockSecondsSwitch","PresenceWakeSwitch","PresenceOnlookerSwitch"};
    bool updating,updatingDisplays;
    string savedScriptState;
    readonly Dictionary<Expander,int> categoryAnimationVersions=new Dictionary<Expander,int>();
    public sealed class RefreshChoice {public uint Rate;public override string ToString(){return Rate==0?"Не менять частоту обновления":Rate+" Гц";}}
    public sealed class ScaleChoice {public int Percent;public override string ToString(){return Percent==0?"Не менять масштаб":Percent+"%";}}
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
            System.Windows.Automation.AutomationProperties.SetHelpText(switches[i],"Слева - выключить, в центре - не менять, справа - включить. Стрелки влево и вправо выбирают состояние; стрелка вниз - не менять.");
            if(switches[i].Name=="TaskbarRepairSwitch")System.Windows.Automation.AutomationProperties.SetHelpText(switches[i],"Слева - не менять, в центре - режим 1, справа - режим 2.");
        }
        save=(Button)Window.FindName("SaveButton");iconButton=(Button)Window.FindName("IconButton");currentIcon=(Image)Window.FindName("CurrentIcon");popup=(Popup)Window.FindName("IconPopup");icons=(UniformGrid)Window.FindName("IconGrid");status=(TextBlock)Window.FindName("Status");preview=(TextBox)Window.FindName("ScriptPreview");
        save.Click+=SaveClicked;iconButton.Click+=delegate{RefreshIcons();popup.IsOpen=!popup.IsOpen;};
        popup.Opened+=delegate{int selected=IconChoice.CustomPath==null?IconChoice.BuiltInIndex:5;((Button)icons.Children[selected]).Focus();};
        icons.PreviewKeyDown+=PopupKeyDown;
        Window.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape&&popup.IsOpen){popup.IsOpen=false;iconButton.Focus();e.Handled=true;}};
        WindowsTheme.Attach(Window);
        SmoothScrollingController settingsScrolling=new SmoothScrollingController((ScrollViewer)Window.FindName("SettingsScroll"));
        Window.Closed+=delegate{settingsScrolling.Dispose();};
        displaySelector=(ComboBox)Window.FindName("DisplaySelector");refreshSelector=(ComboBox)Window.FindName("RefreshSelector");displayUnavailable=(TextBlock)Window.FindName("DisplayUnavailable");
        resolutionSelector=(ComboBox)Window.FindName("ResolutionSelector");
        scaleSelector=(ComboBox)Window.FindName("ScaleSelector");contentBrightnessSelector=(ComboBox)Window.FindName("ContentBrightnessSelector");
        contentBrightnessSelector.ItemsSource=new[]{new ContentChoice{Value=0,Label="Не менять"},new ContentChoice{Value=2,Label="Откл."},new ContentChoice{Value=1,Label="Всегда"},new ContentChoice{Value=3,Label="Только при питании от батареи"}};contentBrightnessSelector.SelectedIndex=0;
        nightLightSlider=(Slider)Window.FindName("NightLightSlider");nightLightSlider.ValueChanged+=delegate{SelectionChanged(null,null);};
        brightnessToggle=(ToggleButton)Window.FindName("BrightnessToggleButton");brightnessSlider=(Slider)Window.FindName("BrightnessSlider");
        ControlMotion.AttachBrightness(brightnessToggle);
        brightnessToggle.Checked+=SelectionChanged;brightnessToggle.Unchecked+=SelectionChanged;brightnessSlider.ValueChanged+=delegate{SelectionChanged(null,null);};
        displaySelector.SelectionChanged+=delegate{if(!updatingDisplays){RefreshResolutions();RefreshRates();RefreshScales();DisplaySelectionChanged(null,null);}};
        resolutionSelector.SelectionChanged+=delegate{if(!updatingDisplays){RefreshRates();DisplaySelectionChanged(null,null);}};
        refreshSelector.SelectionChanged+=delegate{DisplaySelectionChanged(null,null);};
        scaleSelector.SelectionChanged+=delegate{DisplaySelectionChanged(null,null);};contentBrightnessSelector.SelectionChanged+=delegate{SelectionChanged(null,null);};
        ((Expander)Window.FindName("DisplaySection")).Expanded+=delegate{RefreshDisplays();};
        ((Button)Window.FindName("PresetPerformanceButton")).Click+=delegate{ApplyPreset(true);};
        ((Button)Window.FindName("PresetAutonomyButton")).Click+=delegate{ApplyPreset(false);};
        foreach(string categoryName in new[]{"PerformanceSection","WallpaperSection","ArmourySection","PowerSection","DisplaySection","AdditionalSection"}){
            Expander category=(Expander)Window.FindName(categoryName);
            category.Expanded+=CategoryStateChanged;category.Collapsed+=CategoryStateChanged;
        }
        RefreshDisplays();
        RefreshHardware();
        ((TextBlock)Window.FindName("BatteryWear")).Text=BatteryHealth.Read();
        RefreshIcons();UpdatePreview();
    }
    void CategoryStateChanged(object sender,RoutedEventArgs e)
    {
        if(e.OriginalSource!=sender)return;
        AnimateCategory((Expander)sender,Window.IsLoaded&&SystemParameters.ClientAreaAnimation);
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
            DisplayInfo display=displaySelector.SelectedItem as DisplayInfo;RefreshChoice rate=refreshSelector.SelectedItem as RefreshChoice;
            DisplayResolution resolution=resolutionSelector.SelectedItem as DisplayResolution;
            bool change=display!=null&&rate!=null&&rate.Rate!=0;
            bool changeSize=display!=null&&resolution!=null&&resolution.Width!=0;
            int hdr=display==null||!Action("HdrSwitch").IsEnabled?0:SwitchValue(Action("HdrSwitch"));
            int contentBrightness=display==null||!contentBrightnessSelector.IsEnabled?0:((ContentChoice)contentBrightnessSelector.SelectedItem).Value;
            bool scale=display!=null&&scaleSelector.SelectedItem!=null&&((ScaleChoice)scaleSelector.SelectedItem).Percent!=0;
            bool dynamicRate=display!=null&&Action("DynamicRefreshSwitch").IsEnabled&&SwitchValue(Action("DynamicRefreshSwitch"))!=0;
            return new ModeConfiguration{DisplayId=change||changeSize||hdr!=0||contentBrightness!=0||scale||dynamicRate?display.MonitorId:null,RefreshRate=change?rate.Rate:0,DisplayWidth=changeSize?resolution.Width:0,DisplayHeight=changeSize?resolution.Height:0,
                EnergySaver=SwitchValue(Action("EnergySaverSwitch")),FanProfile=SelectedMode(fanChecks),
                Brightness=brightnessToggle.IsEnabled&&brightnessToggle.IsChecked==true?(int)Math.Round(brightnessSlider.Value):-1,
                PowerMode=SelectedMode(powerChecks),
                DarkTheme=SwitchValue(Action("DarkThemeSwitch")),SleepTimeout=0,ScreenTimeout=0,Hdr=hdr,
                PresenceSleep=SwitchValue(Action("PresenceSleepSwitch")),PresenceDim=SwitchValue(Action("PresenceDimSwitch")),ContentBrightness=contentBrightness,
                AdaptiveBrightness=SwitchValue(Action("AdaptiveBrightnessSwitch")),ScreenSaver=SwitchValue(Action("ScreenSaverSwitch")),UsbPower=SwitchValue(Action("UsbPowerSwitch")),WallpaperPlayback=SwitchValue(Action("WallpaperPlaybackSwitch")),WallpaperAudio=SwitchValue(Action("WallpaperAudioSwitch")),BatteryCare=SwitchValue(Action("BatteryCareSwitch")),WallpaperApplication=SwitchValue(Action("WallpaperApplicationSwitch")),TaskbarRepair=Action("TaskbarRepairSwitch").IsChecked==false?0:Action("TaskbarRepairSwitch").IsChecked==true?2:1,
                PresenceWake=SwitchValue(Action("PresenceWakeSwitch")),PresenceOnlooker=SwitchValue(Action("PresenceOnlookerSwitch")),BatteryPercentage=SwitchValue(Action("BatteryPercentageSwitch")),ClockSeconds=SwitchValue(Action("ClockSecondsSwitch")),NightLight=SwitchValue(Action("NightLightSwitch")),NightLightStrength=(int)Math.Round(nightLightSlider.Value),DynamicRefresh=display!=null&&Action("DynamicRefreshSwitch").IsEnabled?SwitchValue(Action("DynamicRefreshSwitch")):0,DisplayScale=display!=null&&scaleSelector.SelectedItem!=null?((ScaleChoice)scaleSelector.SelectedItem).Percent:0};
        }
    }
    static int SwitchValue(CheckBox control){return !control.IsEnabled?0:control.IsChecked.HasValue?(control.IsChecked.Value?1:2):0;}
    void SetAvailable(string name,bool available){CheckBox control=Action(name);control.IsEnabled=available;if(!available)control.IsChecked=null;}
    void RefreshHardware(){bool previous=updating;updating=true;try{SetAvailable("PresenceWakeSwitch",SensorCapabilities.HasPresence(2));SetAvailable("PresenceOnlookerSwitch",SensorCapabilities.HasPresence(3));SetAvailable("PresenceSleepSwitch",SensorCapabilities.HasPresence(false));SetAvailable("PresenceDimSwitch",SensorCapabilities.HasPresence(true));SetAvailable("AdaptiveBrightnessSwitch",SensorCapabilities.HasAmbientLight()&&SensorCapabilities.HasBrightness());SetAvailable("NightLightSwitch",NightLightControl.Available());SetAvailable("BatteryPercentageSwitch",SensorCapabilities.HasBattery());brightnessToggle.IsEnabled=SensorCapabilities.HasBrightness();if(!brightnessToggle.IsEnabled)brightnessToggle.IsChecked=false;}finally{updating=previous;}SelectionChanged(null,null);}
    CheckBox[] WireExclusiveChoices(string[] names){CheckBox[] result=new CheckBox[names.Length];for(int i=0;i<names.Length;i++){result[i]=(CheckBox)Window.FindName(names[i]);result[i].Checked+=ExclusiveSelectionChanged;result[i].Unchecked+=ExclusiveSelectionChanged;}return result;}
    static int SelectedMode(CheckBox[] choices){for(int i=0;i<choices.Length;i++)if(choices[i].IsChecked==true)return i+1;return 0;}
    void SwitchPointerDown(object sender,MouseButtonEventArgs e)
    {
        CheckBox control=(CheckBox)sender;FrameworkElement track=control.Template.FindName("SwitchTrack",control) as FrameworkElement;
        if(track==null)return;Point p=e.GetPosition(track);
        if(p.X<0||p.X>track.ActualWidth||p.Y<0||p.Y>track.ActualHeight){if(control.Name=="TaskbarRepairSwitch"){control.Focus();control.IsChecked=control.IsChecked==false?null:control.IsChecked==null?(bool?)true:false;e.Handled=true;}return;}
        control.Focus();control.IsChecked=p.X<track.ActualWidth/3?false:p.X>track.ActualWidth*2/3?(bool?)true:null;e.Handled=true;
    }
    void SwitchKeyDown(object sender,KeyEventArgs e)
    {
        CheckBox control=(CheckBox)sender;
        if(control.Name=="NightLightSwitch"&&nightLightSlider.IsKeyboardFocusWithin)return;
        if(control.Name=="TaskbarRepairSwitch"){
            int value=control.IsChecked==false?0:control.IsChecked==true?2:1;
            if(e.Key==Key.Space)value=(value+1)%3;
            else if(e.Key==Key.Left||e.Key==Key.Down)value=Math.Max(0,value-1);
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
        bool previousUpdating=updating,previousDisplays=updatingDisplays;
        updating=true;updatingDisplays=true;
        try{
            foreach(CheckBox check in switches)check.IsChecked=check.Name=="TaskbarRepairSwitch"?(bool?)false:null;
            for(int i=0;i<powerChecks.Length;i++)powerChecks[i].IsChecked=performance&&i==2;
            for(int i=0;i<fanChecks.Length;i++)fanChecks[i].IsChecked=i==(performance?1:0);
            Action("EnergySaverSwitch").IsChecked=!performance;
            brightnessToggle.IsChecked=performance;brightnessSlider.Value=100;
            if(refreshSelector.Items.Count>0)refreshSelector.SelectedIndex=0;
            if(resolutionSelector.Items.Count>0)resolutionSelector.SelectedIndex=0;
            if(scaleSelector.Items.Count>0)scaleSelector.SelectedIndex=0;contentBrightnessSelector.SelectedIndex=0;nightLightSlider.Value=50;
        }finally{updating=previousUpdating;updatingDisplays=previousDisplays;}
        SelectionChanged(null,null);DisplaySelectionChanged(null,null);
    }
    void SelectionChanged(object sender,RoutedEventArgs e)
    {
        if(updating)return;
        brightnessSlider.IsEnabled=brightnessToggle.IsChecked==true;
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
        DisplayInfo old=displaySelector.SelectedItem as DisplayInfo;RefreshChoice oldRate=refreshSelector.SelectedItem as RefreshChoice;DisplayResolution oldResolution=resolutionSelector.SelectedItem as DisplayResolution;ScaleChoice oldScale=scaleSelector.SelectedItem as ScaleChoice;
        updatingDisplays=true;
        try{
            List<DisplayInfo> displays;
            try{displays=DisplayControl.Enumerate();}catch{displays=new List<DisplayInfo>();}
            displaySelector.ItemsSource=displays;DisplayInfo selected=old==null?null:displays.Find(delegate(DisplayInfo d){return String.Equals(d.MonitorId,old.MonitorId,StringComparison.OrdinalIgnoreCase);});
            if(old!=null&&selected==null){Action("HdrSwitch").IsChecked=null;contentBrightnessSelector.SelectedIndex=0;Action("DynamicRefreshSwitch").IsChecked=null;}
            displaySelector.SelectedItem=selected??(displays.Count==0?null:displays[0]);RefreshResolutions();
            if(selected!=null&&oldResolution!=null)foreach(DisplayResolution r in resolutionSelector.Items)if(r.Width==oldResolution.Width&&r.Height==oldResolution.Height)resolutionSelector.SelectedItem=r;
            RefreshRates();
            RefreshScales();if(selected!=null&&oldScale!=null)foreach(ScaleChoice scale in scaleSelector.Items)if(scale.Percent==oldScale.Percent)scaleSelector.SelectedItem=scale;
            if(selected!=null&&oldRate!=null)foreach(RefreshChoice rate in refreshSelector.Items)if(rate.Rate==oldRate.Rate)refreshSelector.SelectedItem=rate;
            displayUnavailable.Visibility=displays.Count==0?Visibility.Visible:Visibility.Collapsed;
        }finally{updatingDisplays=false;}
        DisplaySelectionChanged(null,null);
    }
    void RefreshScales(){bool previous=updatingDisplays;updatingDisplays=true;try{DisplayInfo d=displaySelector.SelectedItem as DisplayInfo;List<ScaleChoice> choices=new List<ScaleChoice>{new ScaleChoice()};if(d!=null)foreach(int value in HdrControl.InspectScales(d.MonitorId))choices.Add(new ScaleChoice{Percent=value});scaleSelector.ItemsSource=choices;scaleSelector.SelectedIndex=0;}finally{updatingDisplays=previous;}}
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
        displaySelector.IsEnabled=displaySelector.Items.Count>0;resolutionSelector.IsEnabled=displaySelector.Items.Count>0&&resolutionSelector.Items.Count>1;refreshSelector.IsEnabled=displaySelector.Items.Count>0&&refreshSelector.Items.Count>1;
        DisplayInfo display=displaySelector.SelectedItem as DisplayInfo;bool supported=false,enabled=false;int option;
        bool hdr=display!=null&&HdrControl.TryInspect(display.MonitorId,out supported,out enabled)&&supported;
        bool dynamic=display!=null&&HdrControl.InspectDynamic(display.MonitorId,out enabled);
        bool content=display!=null&&HdrControl.TryInspectContentBrightness(display.MonitorId,out option);
        bool previous=updating;updating=true;try{Action("HdrSwitch").IsEnabled=hdr;if(!hdr)Action("HdrSwitch").IsChecked=null;Action("DynamicRefreshSwitch").IsEnabled=dynamic;if(!dynamic)Action("DynamicRefreshSwitch").IsChecked=null;contentBrightnessSelector.IsEnabled=content;if(!content)contentBrightnessSelector.SelectedIndex=0;scaleSelector.IsEnabled=scaleSelector.Items.Count>1;}finally{updating=previous;}
        save.IsEnabled=Configuration.HasActions;status.Text="";UpdatePreview();
    }
    void UpdatePreview()
    {
        ModeConfiguration c=Configuration;List<string> actions=new List<string>();
        if(c.EnergySaver==1)actions.Add("Включить энергосбережение");else if(c.EnergySaver==2)actions.Add("Выключить энергосбережение");
        AddSwitchAction(actions,c.BatteryCare,"Включить режим бережной зарядки","Выключить режим бережной зарядки");
        if(c.FanProfile==1)actions.Add("Тихий режим вентиляторов");else if(c.FanProfile==2)actions.Add("Производительный режим вентиляторов");
        else if(c.FanProfile==3)actions.Add("Стандартный режим вентиляторов");
        AddSwitchAction(actions,c.WallpaperPlayback,"Продолжить воспроизведение обоев Wallpaper Engine","Поставить обои Wallpaper Engine на паузу");
        AddSwitchAction(actions,c.WallpaperApplication,"Запустить Wallpaper Engine без Steam","Завершить Wallpaper Engine");
        AddSwitchAction(actions,c.WallpaperAudio,"Включить звук обоев Wallpaper Engine","Выключить звук обоев Wallpaper Engine");
        if(c.Brightness>=0)actions.Add("Яркость "+c.Brightness+"%");
        if(c.PowerMode==1)actions.Add("Максимальная эффективность энергосбережения");else if(c.PowerMode==2)actions.Add("Сбалансированный");else if(c.PowerMode==3)actions.Add("Оптимальная производительность");
        if(c.RefreshRate!=0){DisplayInfo d=(DisplayInfo)displaySelector.SelectedItem;actions.Add(d.Label+" - "+c.RefreshRate+" Гц");}
        if(c.DisplayWidth!=0){DisplayInfo d=(DisplayInfo)displaySelector.SelectedItem;actions.Add(d.Label+" - разрешение "+c.DisplayWidth+" × "+c.DisplayHeight);}
        if(c.DisplayScale!=0){DisplayInfo d=(DisplayInfo)displaySelector.SelectedItem;actions.Add(d.Label+" - масштаб "+c.DisplayScale+"%");}
        AddSwitchAction(actions,c.DynamicRefresh,"Включить динамическую частоту обновления выбранного дисплея","Выключить динамическую частоту обновления выбранного дисплея");
        AddSwitchAction(actions,c.NightLight,"Включить ночной свет - интенсивность "+c.NightLightStrength+"%","Выключить ночной свет");
        AddSwitchAction(actions,c.DarkTheme,"Включить тёмную тему Windows и приложений","Выключить тёмную тему Windows и приложений");
        AddSwitchAction(actions,c.SleepTimeout,"Переходить в спящий режим через 3 минуты бездействия","Не переходить в спящий режим по таймеру");
        AddSwitchAction(actions,c.ScreenTimeout,"Выключать экран через 3 минуты бездействия","Не выключать экран по таймеру");
        if(c.Hdr!=0){DisplayInfo d=(DisplayInfo)displaySelector.SelectedItem;actions.Add((c.Hdr==1?"Включить HDR - ":"Выключить HDR - ")+d.Label);}
        AddSwitchAction(actions,c.PresenceSleep,"Выключать экран при уходе от устройства","Выключить отключение экрана при уходе");
        AddSwitchAction(actions,c.PresenceWake,"Пробуждать устройство при приближении","Не пробуждать устройство при приближении");
        AddSwitchAction(actions,c.PresenceOnlooker,"Обнаруживать посторонние взгляды на экран","Выключить обнаружение посторонних взглядов на экран");
        AddSwitchAction(actions,c.PresenceDim,"Затемнять экран при отведении взгляда","Не затемнять экран при отведении взгляда");
        if(c.ContentBrightness!=0){DisplayInfo d=(DisplayInfo)displaySelector.SelectedItem;actions.Add((c.ContentBrightness==1?"Включить оптимизацию яркости по содержимому - ":"Выключить оптимизацию яркости по содержимому - ")+d.Label);}
        if(c.ContentBrightness==3){actions.RemoveAt(actions.Count-1);DisplayInfo d=(DisplayInfo)displaySelector.SelectedItem;actions.Add("Оптимизация яркости по содержимому только при питании от батареи - "+d.Label);}
        AddSwitchAction(actions,c.AdaptiveBrightness,"Включить автояркость по освещению","Выключить автояркость по освещению");
        AddSwitchAction(actions,c.ScreenSaver,"Включить заставку","Выключить заставку");
        AddSwitchAction(actions,c.UsbPower,"Останавливать USB при выключенном экране","Не останавливать USB при выключенном экране");
        AddSwitchAction(actions,c.BatteryPercentage,"Показывать процент заряда батареи на панели задач","Скрыть процент заряда батареи на панели задач");
        AddSwitchAction(actions,c.ClockSeconds,"Показывать секунды в часах панели задач","Скрыть секунды в часах панели задач");
        if(c.TaskbarRepair!=0)actions.Add("Исправление панели задач - Все панели задач, затем "+(c.TaskbarRepair==1?"Главная панель задач и панель задач, в которой открыто окно":"Панель задач, в которой открыто окно"));
        preview.Text=actions.Count==0?"Действия не выбраны.":String.Join(Environment.NewLine+Environment.NewLine,actions);
        UpdateSaveState();
    }
    static void AddSwitchAction(List<string> actions,int value,string on,string off){if(value==1)actions.Add(on);else if(value==2)actions.Add(off);}
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
    [STAThread] public static int Main(){try{Application app=new Application();app.ShutdownMode=ShutdownMode.OnMainWindowClose;ConstructorWindow ui=new ConstructorWindow();return app.Run(ui.Window);}catch{return 1;}}
}
