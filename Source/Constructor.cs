using System;using System.Collections.Generic;using System.IO;using System.Reflection;using System.Runtime.InteropServices;using System.Windows;using System.Windows.Controls;using System.Windows.Controls.Primitives;using System.Windows.Input;using System.Windows.Markup;using System.Windows.Media;using System.Windows.Media.Imaging;using Microsoft.Win32;

public sealed class ConstructorWindow
{
    public Window Window {get;private set;}
    public readonly IconSelection IconChoice=new IconSelection();
    readonly CheckBox[] checks;readonly Button save,iconButton;readonly Image currentIcon;readonly Popup popup;readonly UniformGrid icons;readonly TextBlock status;readonly TextBox preview;
    readonly CheckBox displayEnabled;readonly ComboBox displaySelector,refreshSelector;readonly TextBlock displayUnavailable;
    bool updating,updatingDisplays;
    public sealed class RefreshChoice {public uint Rate;public override string ToString(){return Rate+" Гц";}}
    static readonly string[] iconNames={"Автономность и производительность","Батарея","Экономия энергии","Производительность","Скорость","Своя иконка ICO"};
    public ConstructorWindow()
    {
        using(Stream theme=Assembly.GetExecutingAssembly().GetManifestResourceStream("Theme"))Application.Current.Resources=(ResourceDictionary)XamlReader.Load(theme);
        WindowsTheme.Initialize(Application.Current);
        using(Stream content=Assembly.GetExecutingAssembly().GetManifestResourceStream("Window"))Window=(Window)XamlReader.Load(content);
        Window.Icon=ImageSource(EmbeddedAssets.Read("Icon0"));
        string[] names={"SaverOn","SaverOff","Whisper","FanPerformance","Brightness","Volume","Efficiency","Balanced","BestPerformance"};
        checks=new CheckBox[names.Length];for(int i=0;i<names.Length;i++){checks[i]=(CheckBox)Window.FindName(names[i]);checks[i].Checked+=SelectionChanged;checks[i].Unchecked+=SelectionChanged;}
        save=(Button)Window.FindName("SaveButton");iconButton=(Button)Window.FindName("IconButton");currentIcon=(Image)Window.FindName("CurrentIcon");popup=(Popup)Window.FindName("IconPopup");icons=(UniformGrid)Window.FindName("IconGrid");status=(TextBlock)Window.FindName("Status");preview=(TextBox)Window.FindName("ScriptPreview");
        save.Click+=SaveClicked;iconButton.Click+=delegate{RefreshIcons();popup.IsOpen=!popup.IsOpen;};
        popup.Opened+=delegate{int selected=IconChoice.CustomPath==null?IconChoice.BuiltInIndex:5;((Button)icons.Children[selected]).Focus();};
        icons.PreviewKeyDown+=PopupKeyDown;
        Window.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape&&popup.IsOpen){popup.IsOpen=false;iconButton.Focus();e.Handled=true;}};
        WindowsTheme.Attach(Window);
        SmoothScrollingController settingsScrolling=new SmoothScrollingController((ScrollViewer)Window.FindName("SettingsScroll"));
        Window.Closed+=delegate{settingsScrolling.Dispose();};
        displayEnabled=(CheckBox)Window.FindName("DisplayEnabled");displaySelector=(ComboBox)Window.FindName("DisplaySelector");refreshSelector=(ComboBox)Window.FindName("RefreshSelector");displayUnavailable=(TextBlock)Window.FindName("DisplayUnavailable");
        displayEnabled.Checked+=DisplaySelectionChanged;displayEnabled.Unchecked+=DisplaySelectionChanged;
        displaySelector.SelectionChanged+=delegate{if(!updatingDisplays){RefreshRates();DisplaySelectionChanged(null,null);}};
        refreshSelector.SelectionChanged+=delegate{DisplaySelectionChanged(null,null);};
        ((Expander)Window.FindName("DisplaySection")).Expanded+=delegate{RefreshDisplays();};
        ((Button)Window.FindName("PresetPerformanceButton")).Click+=delegate{ApplyPreset(true);};
        ((Button)Window.FindName("PresetAutonomyButton")).Click+=delegate{ApplyPreset(false);};
        RefreshDisplays();
        RefreshIcons();UpdatePreview();
    }
    public ModeConfiguration Configuration {
        get{DisplayInfo display=displaySelector.SelectedItem as DisplayInfo;RefreshChoice rate=refreshSelector.SelectedItem as RefreshChoice;bool change=displayEnabled.IsChecked==true&&display!=null&&rate!=null;return new ModeConfiguration{DisplayId=change?display.MonitorId:null,RefreshRate=change?rate.Rate:0,EnergySaver=checks[0].IsChecked==true?1:checks[1].IsChecked==true?2:0,FanProfile=checks[2].IsChecked==true?1:checks[3].IsChecked==true?2:0,Brightness100=checks[4].IsChecked==true,Volume100=checks[5].IsChecked==true,PowerMode=checks[6].IsChecked==true?1:checks[7].IsChecked==true?2:checks[8].IsChecked==true?3:0};}
    }
    public void ApplyPreset(bool performance)
    {
        bool previousUpdating=updating,previousDisplays=updatingDisplays;
        updating=true;updatingDisplays=true;
        try{
            foreach(CheckBox check in checks)check.IsChecked=false;
            displayEnabled.IsChecked=false;
            checks[performance?1:0].IsChecked=true;
            checks[performance?3:2].IsChecked=true;
            if(performance){checks[4].IsChecked=true;checks[5].IsChecked=true;checks[8].IsChecked=true;}
        }finally{updating=previousUpdating;updatingDisplays=previousDisplays;}
        displaySelector.IsEnabled=false;refreshSelector.IsEnabled=false;
        save.IsEnabled=Configuration.HasActions;status.Text="";UpdatePreview();
    }
    void SelectionChanged(object sender,RoutedEventArgs e)
    {
        if(updating)return;updating=true;
        try{
            CheckBox selected=(CheckBox)sender;
            if(selected.IsChecked==true){
                if(selected==checks[0])checks[1].IsChecked=false;if(selected==checks[1])checks[0].IsChecked=false;
                if(selected==checks[2])checks[3].IsChecked=false;if(selected==checks[3])checks[2].IsChecked=false;
                for(int i=6;i<9;i++)if(selected==checks[i])for(int j=6;j<9;j++)if(j!=i)checks[j].IsChecked=false;
            }
            save.IsEnabled=Configuration.HasActions;status.Text="";UpdatePreview();
        }finally{updating=false;}
    }
    void RefreshDisplays()
    {
        DisplayInfo old=displaySelector.SelectedItem as DisplayInfo;RefreshChoice oldRate=refreshSelector.SelectedItem as RefreshChoice;
        updatingDisplays=true;
        try{
            List<DisplayInfo> displays;
            try{displays=DisplayControl.Enumerate();}catch{displays=new List<DisplayInfo>();}
            displaySelector.ItemsSource=displays;DisplayInfo selected=old==null?null:displays.Find(delegate(DisplayInfo d){return String.Equals(d.MonitorId,old.MonitorId,StringComparison.OrdinalIgnoreCase);});
            if(old!=null&&selected==null)displayEnabled.IsChecked=false;
            displaySelector.SelectedItem=selected??(displays.Count==0?null:displays[0]);RefreshRates();
            if(selected!=null&&oldRate!=null)foreach(RefreshChoice rate in refreshSelector.Items)if(rate.Rate==oldRate.Rate)refreshSelector.SelectedItem=rate;
            displayEnabled.IsEnabled=displays.Count>0;displayUnavailable.Visibility=displays.Count==0?Visibility.Visible:Visibility.Collapsed;
        }finally{updatingDisplays=false;}
        DisplaySelectionChanged(null,null);
    }
    void RefreshRates()
    {
        bool previous=updatingDisplays;updatingDisplays=true;
        try{
            DisplayInfo display=displaySelector.SelectedItem as DisplayInfo;List<RefreshChoice> rates=new List<RefreshChoice>();
            if(display!=null)foreach(uint rate in display.Rates)rates.Add(new RefreshChoice{Rate=rate});
            refreshSelector.ItemsSource=rates;RefreshChoice current=display==null?null:rates.Find(delegate(RefreshChoice r){return r.Rate==display.CurrentRate;});
            refreshSelector.SelectedItem=current??(rates.Count==0?null:rates[0]);
        }finally{updatingDisplays=previous;}
    }
    void DisplaySelectionChanged(object sender,RoutedEventArgs e)
    {
        if(updating||updatingDisplays)return;
        bool enabled=displayEnabled.IsChecked==true&&displaySelector.Items.Count>0;
        displaySelector.IsEnabled=enabled;refreshSelector.IsEnabled=enabled&&refreshSelector.Items.Count>0;
        save.IsEnabled=Configuration.HasActions;status.Text="";UpdatePreview();
    }
    void UpdatePreview()
    {
        ModeConfiguration c=Configuration;List<string> actions=new List<string>();
        if(c.EnergySaver==1)actions.Add("Включить энергосбережение");else if(c.EnergySaver==2)actions.Add("Выключить энергосбережение");
        if(c.FanProfile==1)actions.Add("Тихий режим вентиляторов");else if(c.FanProfile==2)actions.Add("Производительный режим вентиляторов");
        if(c.Brightness100)actions.Add("Яркость 100%");if(c.Volume100)actions.Add("Громкость 100%");
        if(c.PowerMode==1)actions.Add("Максимальная эффективность энергосбережения");else if(c.PowerMode==2)actions.Add("Сбалансированный");else if(c.PowerMode==3)actions.Add("Оптимальная производительность");
        if(c.RefreshRate!=0){DisplayInfo d=(DisplayInfo)displaySelector.SelectedItem;actions.Add(d.Label+" — "+c.RefreshRate+" Гц");}
        List<string> lines=new List<string>();for(int i=0;i<actions.Count;i++)lines.Add((i+1)+". "+actions[i]);
        preview.Text=lines.Count==0?"Действия не выбраны.":String.Join(Environment.NewLine,lines);
    }
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
            int choice=i;Button button=new Button{ToolTip=iconNames[i],Margin=(Thickness)Window.FindResource("Gap.Tile"),Height=(double)Window.FindResource("Size.Tile"),Tag=i};
            System.Windows.Automation.AutomationProperties.SetName(button,iconNames[i]);
            bool selected=i<5&&IconChoice.CustomPath==null&&IconChoice.BuiltInIndex==i||i==5&&IconChoice.CustomPath!=null;
            if(selected)button.BorderBrush=(Brush)Window.FindResource("Brush.Accent");
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
    }
    void SaveClicked(object sender,RoutedEventArgs e)
    {
        if(!Configuration.HasActions)return;
        try{
            string path=NativeFileDialogs.SaveExecutable(new System.Windows.Interop.WindowInteropHelper(Window).Handle);
            if(path==null)return;
            ExecutableExporter.Save(path,Configuration,IconChoice.GetBytes());IconChoice.Saved();RefreshIcons();status.Text="Сохранено: "+Path.GetFileName(path);status.Foreground=(Brush)Window.FindResource("Brush.Accent");
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
