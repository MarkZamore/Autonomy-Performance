$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
function Assert($condition,$message){if(-not $condition){throw $message}}
try{
    $grid=$win.FindName('RootGrid');$grid.Measure([Windows.Size]::new(984,661));$grid.Arrange([Windows.Rect]::new(0,0,984,661));$grid.UpdateLayout()
    $section=$win.FindName('AutonomySection');$header=$section.Template.FindName('HeaderToggle',$section)
    $surface=$header.Template.FindName('HeaderSurface',$header);$ring=$header.Template.FindName('FocusRing',$header)
    $hoverKey=[Windows.UIElement].GetField('IsMouseOverPropertyKey',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
    $pressedKey=[Windows.Controls.Primitives.ButtonBase].GetField('IsPressedPropertyKey',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
    $header.SetValue($hoverKey,$true);$grid.UpdateLayout()
    Assert ($surface.Background.ToString() -eq '#0CFFFFFF' -and $null -eq $ring) 'Hover is not a fill highlight without an outline'
    $header.SetValue($pressedKey,$true);$grid.UpdateLayout()
    Assert ($surface.Background.ToString() -eq '#18000000' -and $null -eq $ring) 'Press does not temporarily darken the category'
    $header.SetValue($pressedKey,$false);$grid.UpdateLayout()
    Assert ($surface.Background.ToString() -eq '#0CFFFFFF') 'Release did not restore hover highlight'
    $header.SetValue($hoverKey,$false);$grid.UpdateLayout()
    Assert ($surface.Background.ToString() -eq '#00FFFFFF') 'Mouse leave did not restore the neutral header'
    $chevron=$header.Template.FindName('Chevron',$header)
    Assert ($chevron.RenderTransform.Angle -eq -90) 'Collapsed chevron must point right'
    $section.IsExpanded=$true;$grid.UpdateLayout()
    Assert ($chevron.RenderTransform.Angle -eq 0) 'Expanded chevron must point down'
    $animate=$ui.GetType().GetMethod('AnimateCategory',[Reflection.BindingFlags]'NonPublic,Instance')
    [void]$animate.Invoke($ui,@($section,$true))
    $content=$section.Template.FindName('SectionContent',$section)
    Assert ($content.HasAnimatedProperties) 'Content does not animate on expansion'
    $frame=New-Object Windows.Threading.DispatcherFrame;$timer=New-Object Windows.Threading.DispatcherTimer
    $timer.Interval=[TimeSpan]::FromMilliseconds(80);$handler=[EventHandler]{param($s,$e)$s.Stop();$frame.Continue=$false};$timer.add_Tick($handler);$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame);$timer.remove_Tick($handler)
    Assert ($content.Height -gt 0 -and $content.Opacity -gt 0 -and $content.Opacity -lt 1) 'Content skipped the intermediate reveal state'
    $section.IsExpanded=$false;$grid.UpdateLayout()
    Assert ($content.Visibility -eq 'Collapsed' -and [double]::IsNaN($content.Height)) 'Rapid collapse left stale animation state'
    $section.IsExpanded=$true;$grid.UpdateLayout();[void]$animate.Invoke($ui,@($section,$true))
    $frame=New-Object Windows.Threading.DispatcherFrame;$timer=New-Object Windows.Threading.DispatcherTimer;$timer.Interval=[TimeSpan]::FromMilliseconds(350);$timer.add_Tick($handler);$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame);$timer.remove_Tick($handler)
    Assert ([double]::IsNaN($content.Height) -and $content.Opacity -eq 1) 'Reveal did not restore automatic content height'
    $expandedHeight=$content.ActualHeight
    $section.IsExpanded=$false;[void]$animate.Invoke($ui,@($section,$true));$grid.UpdateLayout()
    Assert ($content.Visibility -eq 'Visible' -and $content.HasAnimatedProperties) 'Collapse hides content immediately'
    $frame=New-Object Windows.Threading.DispatcherFrame;$timer=New-Object Windows.Threading.DispatcherTimer;$timer.Interval=[TimeSpan]::FromMilliseconds(70);$timer.add_Tick($handler);$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame);$timer.remove_Tick($handler)
    Assert ($content.Height -gt 0 -and $content.Height -lt $expandedHeight -and $content.Opacity -gt 0 -and $content.Opacity -lt 1) 'Collapse skips intermediate height or opacity'
    $section.IsExpanded=$true;[void]$animate.Invoke($ui,@($section,$true))
    $frame=New-Object Windows.Threading.DispatcherFrame;$timer=New-Object Windows.Threading.DispatcherTimer;$timer.Interval=[TimeSpan]::FromMilliseconds(350);$timer.add_Tick($handler);$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame);$timer.remove_Tick($handler)
    Assert ($content.Visibility -eq 'Visible' -and [double]::IsNaN($content.Height)) 'Reopening during collapse leaves content hidden or fixed-height'
    $background=$win.Background
    $check=$win.FindName('EnergySaverSwitch');$check.IsChecked=$true;$grid.UpdateLayout();$check.ApplyTemplate()|Out-Null
    $track=$check.Template.FindName('SwitchTrack',$check);$thumb=$check.Template.FindName('SwitchThumb',$check)
    Assert ($track -ne $null -and $thumb -ne $null) 'Three-state switch template is missing track or thumb'
    $on=$thumb.TranslatePoint([Windows.Point]::new($thumb.ActualWidth/2,0),$track).X
    $check.IsChecked=$false;$grid.UpdateLayout();$off=$thumb.TranslatePoint([Windows.Point]::new($thumb.ActualWidth/2,0),$track).X
    $check.IsChecked=$null;$grid.UpdateLayout();$neutral=$thumb.TranslatePoint([Windows.Point]::new($thumb.ActualWidth/2,0),$track).X
    Assert ($off -lt $neutral -and $neutral -lt $on -and [Math]::Abs($neutral-$track.ActualWidth/2) -le 1) 'Switch thumb does not represent off/unchanged/on positions'
    Assert ($background -is [Windows.Media.LinearGradientBrush] -and $background.GradientStops[0].Color.ToString() -eq '#FF101B30' -and $background.GradientStops[2].Color.ToString() -eq '#FF282828') 'Dark-blue to grey gradient missing'
    @{Passed=$true;HoverFill=$true;PressedDarkening=$true;NoCategoryOutline=$true;Gradient=$true;ChevronDirections=$true;RevealIntermediate=$true;RevealAutoHeightRestored=$true;FastCollapseSafe=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Artifacts\category-feedback.json') -Encoding utf8
    'PASS: category fill/press states, no outline, gradient, right/down arrows, intermediate reveal and safe rapid collapse/reopen.'
}finally{$win.Close()}
