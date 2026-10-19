param([switch]$Source)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
if($Source){Add-Type -Path (Join-Path $root 'Source\SmoothScrolling.cs') -ReferencedAssemblies @('PresentationFramework','PresentationCore','WindowsBase','System.Xaml')}
else{[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))}
function Assert($condition,$message){if(-not $condition){throw $message}}
$viewer=New-Object Windows.Controls.ScrollViewer
$viewer.Width=400;$viewer.Height=300;$viewer.VerticalScrollBarVisibility='Auto'
$content=New-Object Windows.Controls.StackPanel
for($i=0;$i -lt 80;$i++){$row=New-Object Windows.Controls.Border;$row.Height=40;$content.Children.Add($row)|Out-Null}
$viewer.Content=$content
$viewer.Measure([Windows.Size]::new(400,300));$viewer.Arrange([Windows.Rect]::new(0,0,400,300));$viewer.UpdateLayout()
$controller=New-Object SmoothScrollingController($viewer)
$advance=$controller.GetType().GetMethod('AdvanceFrame',[Reflection.BindingFlags]'NonPublic,Instance')
function Frames([int]$count){for($i=0;$i -lt $count;$i++){[void]$advance.Invoke($controller,@([double](1000.0/60)));$viewer.UpdateLayout()}}
try{
    Assert ($viewer.ScrollableHeight -gt 1000) 'Viewport has no overflow'
    [void]$controller.ScrollByWheelDelta(-120)
    Assert ($controller.TargetOffset -eq 36) 'One notch must be 36 DIP'
    Assert ($viewer.VerticalOffset -eq 0) 'Wheel input jumped immediately'
    Frames 1;$first=$viewer.VerticalOffset
    if([Windows.SystemParameters]::ClientAreaAnimation){Assert ($first -gt 0 -and $first -lt 12) 'First frame jumps too far'}
    $positions=@($first)
    for($i=0;$i -lt 8;$i++){Frames 1;$positions+=$viewer.VerticalOffset;Assert ($positions[-1] -ge $positions[-2]) 'Forward scroll reversed direction'}
    $beforeRetarget=$viewer.VerticalOffset
    [void]$controller.ScrollByWheelDelta(-120);[void]$controller.ScrollByWheelDelta(-120);[void]$controller.ScrollByWheelDelta(-60)
    Assert ($controller.TargetOffset -eq 126) 'Fast/partial input was not accumulated'
    Assert ($viewer.VerticalOffset -eq $beforeRetarget) 'Retargeting changed current position abruptly'
    Frames 60;Assert ([math]::Abs($viewer.VerticalOffset-126) -lt 0.01 -and -not $controller.IsAnimating) 'Accumulated motion did not settle'
    [void]$controller.ScrollByWheelDelta(120);Frames 1
    Assert ($viewer.VerticalOffset -lt 126 -and $viewer.VerticalOffset -gt 110) 'Reverse direction jumped'
    Frames 60;Assert ([math]::Abs($viewer.VerticalOffset-90) -lt 0.01) 'Reverse motion did not settle'
    [void]$controller.ScrollByWheelDelta(-120000);Assert ($controller.TargetOffset -eq $viewer.ScrollableHeight) 'Bottom target not clamped';Frames 80
    [void]$controller.ScrollByWheelDelta(120000);Assert ($controller.TargetOffset -eq 0) 'Top target not clamped';Frames 80
    [void]$controller.ScrollByWheelDelta(-120);$viewer.ScrollToVerticalOffset(200);$viewer.UpdateLayout()
    Assert (-not $controller.IsAnimating) 'External scrolling did not cancel motion';Frames 30
    Assert ($viewer.VerticalOffset -eq 200) 'Motion fought manual scrolling'
    [void]$controller.ScrollByWheelDelta(-120);Frames 1;$controller.Cancel();$stopped=$viewer.VerticalOffset;Frames 30
    Assert (-not $controller.IsAnimating -and $viewer.VerticalOffset -eq $stopped) 'Cancel left motion running'
    $viewer.ScrollToTop();$viewer.UpdateLayout();$controller.Cancel()
    [void]$controller.ScrollByWheelDelta(120)
    if([Windows.SystemParameters]::ClientAreaAnimation){Assert ($controller.BounceOffset -gt 0) 'Top spring response waits for a later render tick'}
    Frames 1;$topBounce=$controller.BounceOffset
    if([Windows.SystemParameters]::ClientAreaAnimation){Assert ($topBounce -gt 0 -and $topBounce -le 18) 'Top edge did not spring inward'}
    Assert ($viewer.VerticalOffset -eq 0) 'Spring moved actual scroll offset outside top bound'
    Frames 90;Assert ([math]::Abs($controller.BounceOffset) -lt 0.01 -and -not $controller.IsAnimating) 'Top spring did not settle'
    $viewer.ScrollToBottom();$viewer.UpdateLayout();$controller.Cancel()
    [void]$controller.ScrollByWheelDelta(-120);Frames 1;$bottomBounce=$controller.BounceOffset
    if([Windows.SystemParameters]::ClientAreaAnimation){Assert ($bottomBounce -lt 0 -and $bottomBounce -ge -18) 'Bottom edge did not spring inward'}
    Frames 90;Assert ([math]::Abs($controller.BounceOffset) -lt 0.01 -and -not $controller.IsAnimating) 'Bottom spring did not settle'
    $result=[pscustomobject]@{Passed=$true;StepDIP=36;TimeConstantMS=75;FrameDriven=$true;FirstFrame=$first;MonotonicOffsets=$positions;RetargetDoesNotJump=$true;ReverseSmooth=$true;Bounds=$true;ExternalScrollCancelled=$true;CancelStopped=$true;TopBounce=$topBounce;BottomBounce=$bottomBounce;SpringReturnsToRest=$true}
    $result|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Artifacts\smooth-scroll.json') -Encoding UTF8
    $result
}finally{$controller.Dispose()}
