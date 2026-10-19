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
    static void UpdateSwitch(CheckBox control,bool requested)
    {
        control.ApplyTemplate();
        Border track=control.Template.FindName("SwitchTrack",control) as Border;
        Ellipse thumb=control.Template.FindName("SwitchThumb",control) as Ellipse;
        if(track==null||thumb==null)return;
        TranslateTransform move=thumb.RenderTransform as TranslateTransform;
        if(move==null||move.IsFrozen){move=move==null?new TranslateTransform(23,0):move.Clone();thumb.RenderTransform=move;}
        bool animate=Animate(control,requested);bool? value=control.IsChecked;
        bool taskbar=control.Name=="TaskbarRepairSwitch";
        TextBlock label=control.Template.FindName("SwitchState",control) as TextBlock;
        if(label!=null){
            string on="Вкл.",off="Выкл.";
            if(control.Name=="WallpaperPlaybackSwitch"){on="Возобновить";off="Пауза";}
            if(control.Name=="WallpaperAudioSwitch"){on="Вкл. звук";off="Выкл. звук";}
            if(control.Name=="WallpaperApplicationSwitch"){on="Запустить";off="Завершить";}
            label.Text=taskbar?(value==false?"Не менять":value==true?"Режим 2":"Режим 1"):value==true?on:value==false?off:"Не менять";
        }
        Color blue=((SolidColorBrush)control.FindResource("Brush.CheckFill")).Color;
        Color dark=((SolidColorBrush)control.FindResource("Brush.CheckMark")).Color;
        Color background=taskbar?(value==false?Color.FromArgb(77,97,113,128):blue):value==true?blue:value==false?dark:Color.FromArgb(77,97,113,128);
        Color foreground=taskbar?(value==false?blue:dark):value==true?dark:blue;
        Move(move,TranslateTransform.XProperty,value==true?46:value==false?0:23,animate);
        Paint(track,Border.BackgroundProperty,background,animate);
        Paint(thumb,Shape.FillProperty,foreground,animate);
    }
    public static void AttachBrightness(ToggleButton control)
    {
        control.Checked+=delegate { UpdateBrightness(control,true); };
        control.Unchecked+=delegate { UpdateBrightness(control,true); };
        control.Loaded+=delegate { UpdateBrightness(control,false); };
        UpdateBrightness(control,false);
    }
    static void UpdateBrightness(ToggleButton control,bool requested)
    {
        control.ApplyTemplate();
        Path slash=control.Template.FindName("SunSlash",control) as Path;
        FrameworkElement sun=control.Template.FindName("BrightnessSun",control) as FrameworkElement;
        if(slash==null||sun==null)return;
        bool enabled=control.IsChecked==true,animate=Animate(control,requested);
        Move(slash,UIElement.OpacityProperty,enabled?0:1,animate);
        ScaleTransform scale=slash.RenderTransform as ScaleTransform;
        if(scale!=null&&scale.IsFrozen){scale=scale.Clone();slash.RenderTransform=scale;}
        if(scale!=null){Move(scale,ScaleTransform.ScaleXProperty,enabled?0.1:1,animate);Move(scale,ScaleTransform.ScaleYProperty,enabled?0.1:1,animate);}
        RotateTransform turn=sun.RenderTransform as RotateTransform;
        if(turn!=null&&turn.IsFrozen){turn=turn.Clone();sun.RenderTransform=turn;}
        if(turn!=null)Move(turn,RotateTransform.AngleProperty,enabled?0:-12,animate);
    }
    static void Move(Animatable target,DependencyProperty property,double value,bool animate)
    {
        double from=(double)target.GetValue(property);
        target.BeginAnimation(property,null);target.SetValue(property,value);
        if(animate&&Math.Abs(from-value)>0.001)target.BeginAnimation(property,new DoubleAnimation(from,value,TimeSpan.FromMilliseconds(DurationMilliseconds)){
            EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
    }
    static void Move(UIElement target,DependencyProperty property,double value,bool animate)
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
