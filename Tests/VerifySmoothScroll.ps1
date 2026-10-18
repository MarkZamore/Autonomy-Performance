param([switch]$Source)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
if($Source){
    Add-Type -Path (Join-Path $root 'Source\SmoothScrolling.cs') -ReferencedAssemblies @('PresentationFramework','PresentationCore','WindowsBase','System.Xaml')
}else{
    [void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))
}
function Assert($condition,$message){if(-not $condition){throw $message}}
function Advance([int]$milliseconds){
    $frame=New-Object Windows.Threading.DispatcherFrame
    $timer=New-Object Windows.Threading.DispatcherTimer
    $timer.Interval=[TimeSpan]::FromMilliseconds($milliseconds)
    $handler=[EventHandler]{param($s,$e) $s.Stop();$frame.Continue=$false}
    $timer.add_Tick($handler);$timer.Start()
    [Windows.Threading.Dispatcher]::PushFrame($frame)
    $timer.remove_Tick($handler)
}
$viewer=New-Object Windows.Controls.ScrollViewer
$viewer.Width=400;$viewer.Height=300
$viewer.VerticalScrollBarVisibility='Auto'
$content=New-Object Windows.Controls.StackPanel
for($i=0;$i -lt 80;$i++){$row=New-Object Windows.Controls.Border;$row.Height=40;$content.Children.Add($row)|Out-Null}
$viewer.Content=$content
$viewer.Measure([Windows.Size]::new(400,300));$viewer.Arrange([Windows.Rect]::new(0,0,400,300));$viewer.UpdateLayout()
$controller=New-Object SmoothScrollingController($viewer)
$samples=@()
try{
    Assert ($viewer.ScrollableHeight -gt 1000) 'Offscreen settings viewport has no overflow'
    Assert ($controller.ScrollByWheelDelta(-120)) 'Wheel was not accepted'
    Assert ($controller.TargetOffset -eq 44) 'One notch is not 44 DIP'
    $initial=$viewer.VerticalOffset
    Advance 55;$viewer.UpdateLayout();$middle=$viewer.VerticalOffset
    if([Windows.SystemParameters]::ClientAreaAnimation){Assert ($middle -gt $initial -and $middle -lt 44) 'Wheel skipped intermediate animation offsets'}
    Advance 220;$viewer.UpdateLayout();Assert ([math]::Abs($viewer.VerticalOffset-44) -lt 1) 'Animation did not finish at requested offset'
    [void]$controller.ScrollByWheelDelta(-120);[void]$controller.ScrollByWheelDelta(-120);[void]$controller.ScrollByWheelDelta(-60)
    Assert ([math]::Abs($controller.TargetOffset-154) -lt 1) 'Rapid/partial wheel input was not accumulated proportionally'
    Advance 220;$viewer.UpdateLayout();Assert ([math]::Abs($viewer.VerticalOffset-154) -lt 1) 'Accumulated animation did not finish'
    [void]$controller.ScrollByWheelDelta(-120000)
    Assert ($controller.TargetOffset -eq $viewer.ScrollableHeight) 'Bottom bound was not clamped'
    Advance 220;$viewer.UpdateLayout()
    [void]$controller.ScrollByWheelDelta(120000)
    Assert ($controller.TargetOffset -eq 0) 'Top bound was not clamped'
    Advance 220;$viewer.UpdateLayout()
    [void]$controller.ScrollByWheelDelta(-120)
    $viewer.ScrollToVerticalOffset(200);$viewer.UpdateLayout()
    Assert (-not $controller.IsAnimating) 'External scroll did not cancel animation'
    Advance 220;$viewer.UpdateLayout();Assert ([math]::Abs($viewer.VerticalOffset-200) -lt 1) 'Animation fought an external scroll'
    [void]$controller.ScrollByWheelDelta(-120);$controller.Cancel();$stopped=$viewer.VerticalOffset
    Advance 220;$viewer.UpdateLayout();Assert (-not $controller.IsAnimating -and [math]::Abs($viewer.VerticalOffset-$stopped) -lt 1) 'Cancel left animation running'
    $result=[pscustomobject]@{Passed=$true;StepDIP=44;DurationMS=160;AnimationsEnabled=[Windows.SystemParameters]::ClientAreaAnimation;Initial=$initial;Intermediate=$middle;Final=44;Accumulated=154;Bounds=$true;ExternalScrollCancelled=$true;CancelStopped=$true}
    $result|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Artifacts\smooth-scroll.json') -Encoding UTF8
    $result
}finally{$controller.Dispose()}
