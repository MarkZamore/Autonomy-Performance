using System;using System.Collections.Generic;using System.IO;using System.Reflection;using System.Runtime.InteropServices;using System.Windows;using System.Windows.Controls;using System.Windows.Controls.Primitives;using System.Windows.Input;using System.Windows.Markup;using System.Windows.Media;using System.Windows.Media.Imaging;using Microsoft.Win32;

public sealed class ConstructorWindow
{
    public Window Window {get;private set;}
    public readonly IconSelection IconChoice=new IconSelection();
    readonly CheckBox[] powerChecks,fanChecks,switches;readonly Button save,iconButton;readonly Image currentIcon;readonly Popup popup;readonly UniformGrid icons;readonly TextBlock status;readonly TextBox preview;
    readonly ToggleButton brightnessToggle;readonly Slider brightnessSlider;readonly ComboBox displaySelector,refreshSelector;readonly TextBlock displayUnavailable;
    static readonly string[] switchNames={"EnergySaverSwitch","DarkThemeSwitch","SleepTimeoutSwitch","ScreenTimeoutSwitch","HdrSwitch","PresenceSleepSwitch","PresenceDimSwitch","ContentBrightnessSwitch","AdaptiveBrightnessSwitch","ScreenSaverSwitch","UsbPowerSwitch","WallpaperPlaybackSwitch"};
    bool updating,updatingDisplays;
    string savedScriptState;
    readonly Dictionary<Expander,int> categoryAnimationVersions=new Dictionary<Expander,int>();
    public sealed class RefreshChoice {public uint Rate;public override string ToString(){return Rate==0?"Не менять":Rate+" Гц";}}
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
            System.Windows.Automation.AutomationProperties.SetHelpText(switches[i],"Слева — выключить, в центре — не менять, справа — включить. Стрелки влево и вправо выбирают состояние; стрелка вниз — не менять.");
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
        brightnessToggle=(ToggleButton)Window.FindName("BrightnessToggleButton");brightnessSlider=(Slider)Window.FindName("BrightnessSlider");
        brightnessToggle.Checked+=SelectionChanged;brightnessToggle.Unchecked+=SelectionChanged;brightnessSlider.ValueChanged+=delegate{SelectionChanged(null,null);};
        displaySelector.SelectionChanged+=delegate{if(!updatingDisplays){RefreshRates();DisplaySelectionChanged(null,null);}};
        refreshSelector.SelectionChanged+=delegate{DisplaySelectionChanged(null,null);};
        ((Expander)Window.FindName("DisplaySection")).Expanded+=delegate{RefreshDisplays();};
        ((Button)Window.FindName("PresetPerformanceButton")).Click+=delegate{ApplyPreset(true);};
        ((Button)Window.FindName("PresetAutonomyButton")).Click+=delegate{ApplyPreset(false);};
        foreach(string categoryName in new[]{"AutonomySection","PerformanceSection","WallpaperSection","ArmourySection","PowerSection","DisplaySection","AdditionalSection"}){
            Expander category=(Expander)Window.FindName(categoryName);
            category.Expanded+=CategoryStateChanged;category.Collapsed+=CategoryStateChanged;
        }
        RefreshDisplays();
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
            bool change=display!=null&&rate!=null&&rate.Rate!=0;
            int hdr=display==null?0:SwitchValue(switches[4]);
            int contentBrightness=display==null?0:SwitchValue(switches[7]);
            return new ModeConfiguration{DisplayId=change||hdr!=0||contentBrightness!=0?display.MonitorId:null,RefreshRate=change?rate.Rate:0,
                EnergySaver=SwitchValue(switches[0]),FanProfile=SelectedMode(fanChecks),
                Brightness=brightnessToggle.IsChecked==true?(int)Math.Round(brightnessSlider.Value):-1,
                PowerMode=SelectedMode(powerChecks),
                DarkTheme=SwitchValue(switches[1]),SleepTimeout=SwitchValue(switches[2]),ScreenTimeout=SwitchValue(switches[3]),Hdr=hdr,
                PresenceSleep=SwitchValue(switches[5]),PresenceDim=SwitchValue(switches[6]),ContentBrightness=contentBrightness,
                AdaptiveBrightness=SwitchValue(switches[8]),ScreenSaver=SwitchValue(switches[9]),UsbPower=SwitchValue(switches[10]),WallpaperPlayback=SwitchValue(switches[11])};
        }
    }
    static int SwitchValue(CheckBox control){return control.IsChecked.HasValue?(control.IsChecked.Value?1:2):0;}
    CheckBox[] WireExclusiveChoices(string[] names){CheckBox[] result=new CheckBox[names.Length];for(int i=0;i<names.Length;i++){result[i]=(CheckBox)Window.FindName(names[i]);result[i].Checked+=ExclusiveSelectionChanged;result[i].Unchecked+=ExclusiveSelectionChanged;}return result;}
    static int SelectedMode(CheckBox[] choices){for(int i=0;i<choices.Length;i++)if(choices[i].IsChecked==true)return i+1;return 0;}
    void SwitchPointerDown(object sender,MouseButtonEventArgs e)
    {
        CheckBox control=(CheckBox)sender;FrameworkElement track=control.Template.FindName("SwitchTrack",control) as FrameworkElement;
        if(track==null)return;Point p=e.GetPosition(track);
        if(p.X<0||p.X>track.ActualWidth||p.Y<0||p.Y>track.ActualHeight)return;
        control.Focus();control.IsChecked=p.X<track.ActualWidth/3?false:p.X>track.ActualWidth*2/3?(bool?)true:null;e.Handled=true;
    }
    void SwitchKeyDown(object sender,KeyEventArgs e)
    {
        CheckBox control=(CheckBox)sender;
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
            foreach(CheckBox check in switches)check.IsChecked=null;
            for(int i=0;i<powerChecks.Length;i++)powerChecks[i].IsChecked=performance&&i==2;
            for(int i=0;i<fanChecks.Length;i++)fanChecks[i].IsChecked=i==(performance?1:0);
            switches[0].IsChecked=!performance;
            brightnessToggle.IsChecked=performance;brightnessSlider.Value=100;
            if(refreshSelector.Items.Count>0)refreshSelector.SelectedIndex=0;
        }finally{updating=previousUpdating;updatingDisplays=previousDisplays;}
        SelectionChanged(null,null);DisplaySelectionChanged(null,null);
    }
    void SelectionChanged(object sender,RoutedEventArgs e)
    {
        if(updating)return;
        brightnessSlider.IsEnabled=brightnessToggle.IsChecked==true;
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
        DisplayInfo old=displaySelector.SelectedItem as DisplayInfo;RefreshChoice oldRate=refreshSelector.SelectedItem as RefreshChoice;
        updatingDisplays=true;
        try{
            List<DisplayInfo> displays;
            try{displays=DisplayControl.Enumerate();}catch{displays=new List<DisplayInfo>();}
            displaySelector.ItemsSource=displays;DisplayInfo selected=old==null?null:displays.Find(delegate(DisplayInfo d){return String.Equals(d.MonitorId,old.MonitorId,StringComparison.OrdinalIgnoreCase);});
            if(old!=null&&selected==null){switches[4].IsChecked=null;switches[7].IsChecked=null;}
            displaySelector.SelectedItem=selected??(displays.Count==0?null:displays[0]);RefreshRates();
            if(selected!=null&&oldRate!=null)foreach(RefreshChoice rate in refreshSelector.Items)if(rate.Rate==oldRate.Rate)refreshSelector.SelectedItem=rate;
            switches[4].IsEnabled=switches[7].IsEnabled=displays.Count>0;displayUnavailable.Visibility=displays.Count==0?Visibility.Visible:Visibility.Collapsed;
        }finally{updatingDisplays=false;}
        DisplaySelectionChanged(null,null);
    }
    void RefreshRates()
    {
        bool previous=updatingDisplays;updatingDisplays=true;
        try{
            DisplayInfo display=displaySelector.SelectedItem as DisplayInfo;List<RefreshChoice> rates=new List<RefreshChoice>{new RefreshChoice{Rate=0}};
            if(display!=null)foreach(uint rate in display.Rates)rates.Add(new RefreshChoice{Rate=rate});
            refreshSelector.ItemsSource=rates;refreshSelector.SelectedIndex=0;
        }finally{updatingDisplays=previous;}
    }
    void DisplaySelectionChanged(object sender,RoutedEventArgs e)
    {
        if(updating||updatingDisplays)return;
        displaySelector.IsEnabled=displaySelector.Items.Count>0;refreshSelector.IsEnabled=displaySelector.Items.Count>0&&refreshSelector.Items.Count>1;
        save.IsEnabled=Configuration.HasActions;status.Text="";UpdatePreview();
    }
    void UpdatePreview()
    {
        ModeConfiguration c=Configuration;List<string> actions=new List<string>();
        if(c.EnergySaver==1)actions.Add("Включить энергосбережение");else if(c.EnergySaver==2)actions.Add("Выключить энергосбережение");
        if(c.FanProfile==1)actions.Add("Тихий режим вентиляторов");else if(c.FanProfile==2)actions.Add("Производительный режим вентиляторов");
        else if(c.FanProfile==3)actions.Add("Стандартный режим вентиляторов");
        AddSwitchAction(actions,c.WallpaperPlayback,"Продолжить воспроизведение обоев Wallpaper Engine","Поставить обои Wallpaper Engine на паузу");
        if(c.Brightness>=0)actions.Add("Яркость "+c.Brightness+"%");
        if(c.PowerMode==1)actions.Add("Максимальная эффективность энергосбережения");else if(c.PowerMode==2)actions.Add("Сбалансированный");else if(c.PowerMode==3)actions.Add("Оптимальная производительность");
        if(c.RefreshRate!=0){DisplayInfo d=(DisplayInfo)displaySelector.SelectedItem;actions.Add(d.Label+" — "+c.RefreshRate+" Гц");}
        AddSwitchAction(actions,c.DarkTheme,"Включить тёмную тему Windows и приложений","Выключить тёмную тему Windows и приложений");
        AddSwitchAction(actions,c.SleepTimeout,"Переходить в спящий режим через 3 минуты бездействия","Не переходить в спящий режим по таймеру");
        AddSwitchAction(actions,c.ScreenTimeout,"Выключать экран через 3 минуты бездействия","Не выключать экран по таймеру");
        if(c.Hdr!=0){DisplayInfo d=(DisplayInfo)displaySelector.SelectedItem;actions.Add((c.Hdr==1?"Включить HDR — ":"Выключить HDR — ")+d.Label);}
        AddSwitchAction(actions,c.PresenceSleep,"Выключать экран при уходе от устройства","Выключить отключение экрана при уходе");
        AddSwitchAction(actions,c.PresenceDim,"Затемнять экран при отведении взгляда","Не затемнять экран при отведении взгляда");
        if(c.ContentBrightness!=0){DisplayInfo d=(DisplayInfo)displaySelector.SelectedItem;actions.Add((c.ContentBrightness==1?"Включить оптимизацию яркости по содержимому — ":"Выключить оптимизацию яркости по содержимому — ")+d.Label);}
        AddSwitchAction(actions,c.AdaptiveBrightness,"Включить автояркость по освещению","Выключить автояркость по освещению");
        AddSwitchAction(actions,c.ScreenSaver,"Включить заставку","Выключить заставку");
        AddSwitchAction(actions,c.UsbPower,"Останавливать USB при выключенном экране","Не останавливать USB при выключенном экране");
        List<string> lines=new List<string>();for(int i=0;i<actions.Count;i++)lines.Add((i+1)+". "+actions[i]);
        preview.Text=lines.Count==0?"Действия не выбраны.":String.Join(Environment.NewLine,lines);
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
