$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
[Windows.Media.RenderOptions]::ProcessRenderMode=[Windows.Interop.RenderMode]::SoftwareOnly
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
$script:assertions=0
function Assert($condition,$message){$script:assertions++;if(-not $condition){throw $message}}
function Frame([int]$milliseconds){
    $frame=New-Object Windows.Threading.DispatcherFrame
    $timer=New-Object Windows.Threading.DispatcherTimer
    $timer.Interval=[TimeSpan]::FromMilliseconds($milliseconds)
    $timer.Add_Tick({$frame.Continue=$false})
    $timer.Start();try{[Windows.Threading.Dispatcher]::PushFrame($frame)}finally{$timer.Stop()}
}
try{
    # A real WPF Loaded lifecycle and animation clock, hosted entirely offscreen.
    # This is not a validation of on-screen GPU/DWM resizing.
    $win.WindowStartupLocation=[Windows.WindowStartupLocation]::Manual
    $win.Left=-32000;$win.Top=-32000;$win.ShowActivated=$false;$win.ShowInTaskbar=$false
    $win.Show();Frame 40
    $switch=$win.FindName('WallpaperAudioSwitch');[void]$switch.ApplyTemplate()
    $track=$switch.Template.FindName('SwitchTrack',$switch);$thumb=$switch.Template.FindName('SwitchThumb',$switch)
    Assert ($switch.IsLoaded) 'Switch did not receive a real Loaded lifecycle'
    Assert ([Math]::Abs($thumb.RenderTransform.X-23) -lt 0.01) 'Neutral thumb is not centered'
    $switch.IsChecked=$true
    if([Windows.SystemParameters]::ClientAreaAnimation){
        Frame 35;Assert ($thumb.RenderTransform.X -gt 23 -and $thumb.RenderTransform.X -lt 46) 'Switch jumps instead of animating'
        Assert ($thumb.RenderTransform.HasAnimatedProperties -and $track.Background.HasAnimatedProperties) 'Movement or color transition is missing'
    }
    Frame 180
    $blue=$app.Resources['Brush.CheckFill'].Color;$dark=$app.Resources['Brush.CheckMark'].Color
    Assert ([Math]::Abs($thumb.RenderTransform.X-46) -lt 0.01 -and $track.Background.Color -eq $blue -and $thumb.Fill.Color -eq $dark) 'Enabled colors/position incorrect'
    $switch.IsChecked=$false
    Frame 35
    $intermediate=$thumb.RenderTransform.X;$switch.IsChecked=$true
    # Sample a rendered clock tick, not the base value before WPF starts a clock.
    if([Windows.SystemParameters]::ClientAreaAnimation){Assert ($thumb.RenderTransform.HasAnimatedProperties) 'Rapid reversal lost its animation clock';Frame 30}
    Frame 180;$switch.IsChecked=$false;Frame 180
    Assert ([Math]::Abs($thumb.RenderTransform.X) -lt 0.01 -and $track.Background.Color -eq $dark -and $thumb.Fill.Color -eq $blue) 'Disabled colors are not the enabled colors swapped'
    $switch.IsChecked=$null;Frame 180
    Assert ([Math]::Abs($thumb.RenderTransform.X-23) -lt 0.01) 'Neutral thumb did not return to center'
    $sun=$win.FindName('BrightnessToggleButton');[void]$sun.ApplyTemplate();$slash=$sun.Template.FindName('SunSlash',$sun)
    $surface=$sun.Template.FindName('BrightnessButtonSurface',$sun)
    Assert ($slash.Opacity -eq 1) 'Disabled brightness has no crossed-out sun'
    $sun.IsChecked=$true
    if([Windows.SystemParameters]::ClientAreaAnimation){Assert ($slash.HasAnimatedProperties) 'Sun slash has no animation clock';Frame 35}
    Frame 180
    Assert ($slash.Opacity -eq 0 -and $win.FindName('BrightnessSlider').IsEnabled) 'Enabled brightness retains slash or disables slider'
    Assert ($surface.Background.ToString() -eq '#00FFFFFF') 'Brightness still uses selected background fill'
    $sun.IsChecked=$false;Frame 180
    Assert ($slash.Opacity -eq 1 -and -not $win.FindName('BrightnessSlider').IsEnabled) 'Disabling brightness did not restore slash'
    $win.FindName('EnergySaverSwitch').IsChecked=$true;$switch.IsChecked=$false;Frame 180
    $preview=$win.FindName('ScriptPreview').Text
    Assert ($preview -notmatch '(?m)^\d+\. ' -and $preview.Contains([Environment]::NewLine+[Environment]::NewLine)) 'Preview is numbered or has no spacing between actions'
    Assert ([double]::IsNaN([Windows.Controls.TextBlock]::GetLineHeight($win.FindName('ScriptPreview')))) 'Preview has forced line spacing'
    'PASS: '+$script:assertions+' assertions; loaded WPF transitions, intermediate frames, rapid reversal, swapped colors, crossed sun, unnumbered spaced preview. No system settings applied.'
}finally{$win.Close()}
