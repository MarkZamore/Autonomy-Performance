using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

// Each transition starts at its current rendered value, including rapid reversals.
// Clocks are finite; no timer or frame handler remains after the transition.
public static class ControlMotion
{
    public const double DurationMilliseconds = 120;
    static bool Animate(Control control,bool requested) { return requested && control.IsLoaded && SystemParameters.ClientAreaAnimation; }
    public static void AttachSwitch(CheckBox control)
    {
        RoutedEventHandler changed=delegate { UpdateSwitch(control,true); };
        control.Checked+=changed;control.Unchecked+=changed;control.Indeterminate+=changed;
        control.Loaded+=delegate { UpdateSwitch(control,false); };
        UpdateSwitch(control,false);
    }
    public static void AttachComboBox(ComboBox control)
    {
        EventHandler changed=delegate { UpdateComboBox(control,control.IsDropDownOpen,true); };
        control.DropDownOpened+=changed;control.DropDownClosed+=changed;
        control.Loaded+=delegate { UpdateComboBox(control,control.IsDropDownOpen,false); };
        UpdateComboBox(control,control.IsDropDownOpen,false);
    }
    static void UpdateComboBox(ComboBox control,bool open,bool requested)
    {
        TurnArrow(control,"ComboToggle","ComboChevron",open?0:-90,requested);
    }
    public static void AttachCategory(Expander control)
    {
        RoutedEventHandler changed=delegate(object sender,RoutedEventArgs e){if(e.OriginalSource==control)UpdateCategoryArrow(control,control.IsExpanded,true);};
        control.Expanded+=changed;control.Collapsed+=changed;
        control.Loaded+=delegate{UpdateCategoryArrow(control,control.IsExpanded,false);};
        UpdateCategoryArrow(control,control.IsExpanded,false);
    }
    static void UpdateCategoryArrow(Expander control,bool expanded,bool requested)
    {
        TurnArrow(control,"HeaderToggle","Chevron",expanded?0:-90,requested);
    }
    static void TurnArrow(Control control,string toggleName,string chevronName,double angle,bool requested)
    {
        control.ApplyTemplate();
        ToggleButton toggle=control.Template.FindName(toggleName,control) as ToggleButton;if(toggle==null)return;
        toggle.ApplyTemplate();Path chevron=toggle.Template.FindName(chevronName,toggle) as Path;if(chevron==null)return;
        RotateTransform turn=chevron.RenderTransform as RotateTransform;
        if(turn==null||turn.IsFrozen){turn=turn==null?new RotateTransform():turn.Clone();chevron.RenderTransform=turn;}
        Move(turn,RotateTransform.AngleProperty,angle,Animate(control,requested));
    }
    static void UpdateSwitch(CheckBox control,bool requested)
    {
        control.ApplyTemplate();
        Border track=control.Template.FindName("SwitchTrack",control) as Border;
        Ellipse thumb=control.Template.FindName("SwitchThumb",control) as Ellipse;
        if(track==null||thumb==null)return;
        TranslateTransform move=thumb.RenderTransform as TranslateTransform;
        if(move==null||move.IsFrozen){move=move==null?new TranslateTransform(23,0):move.Clone();thumb.RenderTransform=move;}
        bool animate=Animate(control,requested);bool? value=control.IsChecked;
        bool binary=control.Name=="BrightnessToggleButton";
        TextBlock label=control.Template.FindName("SwitchState",control) as TextBlock;
        if(label!=null){
            string on="Вкл.",off="Выкл.";
            if(binary)off="Не менять";
            if(control.Name=="WallpaperPlaybackSwitch"){on="Возобновить";off="Пауза";}
            if(control.Name=="WallpaperAudioSwitch"){on="Вкл. звук";off="Выкл. звук";}
            if(control.Name=="WallpaperApplicationSwitch"){on="Запустить";off="Завершить";}
            label.Text=value==true?on:value==false?off:"Не менять";
        }
        Color blue=((SolidColorBrush)control.FindResource("Brush.CheckFill")).Color;
        Color dark=((SolidColorBrush)control.FindResource("Brush.ControlDark")).Color;
        Color background=binary?(value==true?blue:Color.FromArgb(77,97,113,128)):value==true?blue:value==false?dark:Color.FromArgb(77,97,113,128);
        Color foreground=binary?(value==true?dark:blue):value==true?dark:blue;
        Move(move,TranslateTransform.XProperty,value==true?46:value==false?0:23,animate);
        Paint(track,Border.BackgroundProperty,background,animate);
        Paint(thumb,Shape.FillProperty,foreground,animate);
    }
    static void Move(Animatable target,DependencyProperty property,double value,bool animate)
    {
        double from=(double)target.GetValue(property);
        target.BeginAnimation(property,null);target.SetValue(property,value);
        if(animate&&Math.Abs(from-value)>0.001)target.BeginAnimation(property,new DoubleAnimation(from,value,TimeSpan.FromMilliseconds(DurationMilliseconds)){
            EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
    }
    static void Paint(DependencyObject target,DependencyProperty property,Color value,bool animate)
    {
        SolidColorBrush old=target.GetValue(property) as SolidColorBrush;
        Color from=old==null?value:old.Color;
        SolidColorBrush brush=new SolidColorBrush(value);target.SetValue(property,brush);
        if(animate&&from!=value)brush.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(from,value,TimeSpan.FromMilliseconds(DurationMilliseconds)){
            EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
    }
}
