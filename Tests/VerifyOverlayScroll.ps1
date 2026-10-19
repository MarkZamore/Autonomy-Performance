param([switch]$Source)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application
if($Source){
    $app.Resources=[Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText((Join-Path $root 'Source\Theme.xaml')))
    $app.Resources.MergedDictionaries.Add([Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText((Join-Path $root 'Source\CategoryIcons.xaml'))))
    $win=[Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText((Join-Path $root 'Source\Window.xaml')))
}else{
    [void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))
    $ui=New-Object ConstructorWindow;$win=$ui.Window
}
$grid=$win.FindName('RootGrid');$scroll=$win.FindName('SettingsScroll')
function Assert($condition,$message){if(-not $condition){throw $message}}
function Layout($width,$height){$size=[Windows.Size]::new($width,$height);$grid.Measure($size);$grid.Arrange([Windows.Rect]::new($size));$grid.UpdateLayout()}
$results=@()
try{
foreach($size in @(@{Width=984;Height=661},@{Width=804;Height=521})){
foreach($name in @('AutonomySection','PerformanceSection','WallpaperSection','ArmourySection','PowerSection','DisplaySection','AdditionalSection')){$win.FindName($name).IsExpanded=$false}
Layout $size.Width $size.Height
$columns=$scroll.Parent.ColumnDefinitions;Assert ([math]::Abs($columns[0].ActualWidth-2*$columns[2].ActualWidth) -lt 1) 'Settings and preview ratio is not 2:1';$before=$win.FindName('AutonomySection').ActualWidth;$pointBefore=$win.FindName('AutonomySection').TranslatePoint([Windows.Point]::new(0,0),$grid)
if($size.Height -ge 661){Assert ($scroll.ComputedVerticalScrollBarVisibility -eq [Windows.Visibility]::Collapsed) 'Scrollbar visible when collapsed categories fit'}else{Assert ($scroll.ComputedVerticalScrollBarVisibility -eq [Windows.Visibility]::Visible) 'Seven categories must remain scrollable at minimum height'}
foreach($name in @('AutonomySection','PerformanceSection','PowerSection','DisplaySection')){$win.FindName($name).IsExpanded=$true}
Layout $size.Width $size.Height
$after=$win.FindName('AutonomySection').ActualWidth;$pointAfter=$win.FindName('AutonomySection').TranslatePoint([Windows.Point]::new(0,0),$grid)
Assert ($scroll.ComputedVerticalScrollBarVisibility -eq [Windows.Visibility]::Visible) 'Scrollbar missing for expanded categories'
Assert ([math]::Abs($before-$after) -lt 0.01 -and [math]::Abs($pointBefore.X-$pointAfter.X) -lt 0.01) 'Scrollbar shifted or resized settings'
$bar=$scroll.Template.FindName('PART_VerticalScrollBar',$scroll)
$barPoint=$bar.TranslatePoint([Windows.Point]::new(0,0),$grid)
$cardRight=$pointAfter.X+$after
Assert ($barPoint.X-$cardRight -ge 3.9) 'Scrollbar overlaps the settings cards'
$track=$bar.Template.FindName('PART_Track',$bar);$thumb=$track.Thumb;$surface=$thumb.Template.FindName('ThumbSurface',$thumb)
Assert ([math]::Abs($bar.ActualWidth-16) -lt 0.01) 'Scrollbar hit target is not 16 DIP'
Assert ([math]::Abs($surface.ActualWidth-2) -lt 0.01) 'Idle scrollbar indicator is not 2 DIP'
# Offscreen visual-state simulation does not move the desktop pointer.
$hoverKey=[Windows.UIElement].GetField('IsMouseOverPropertyKey',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
$dragKey=[Windows.Controls.Primitives.Thumb].GetField('IsDraggingPropertyKey',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
$bar.SetValue($hoverKey,$true);$grid.UpdateLayout()
Assert ([math]::Abs($surface.ActualWidth-6) -lt 0.01) 'Hovering the scrollbar track does not expand its indicator to 6 DIP'
$bar.SetValue($hoverKey,$false);$thumb.SetValue($dragKey,$true);$grid.UpdateLayout()
Assert ([math]::Abs($surface.ActualWidth-6) -lt 0.01) 'Dragging outside the scrollbar does not keep its indicator at 6 DIP'
$thumb.SetValue($dragKey,$false);$grid.UpdateLayout()
Assert ([math]::Abs($surface.ActualWidth-2) -lt 0.01) 'Scrollbar indicator did not return to 2 DIP'
Assert ([math]::Abs($after-$win.FindName('AutonomySection').ActualWidth) -lt 0.01) 'Hovering scrollbar changed settings layout'
$scroll.ScrollToBottom();$grid.UpdateLayout();Assert ($scroll.VerticalOffset -gt 0) 'Overlay scrollbar does not scroll'
$scroll.ScrollToTop();$grid.UpdateLayout()
$results+=[pscustomobject]@{ClientWidth=$size.Width;Before=$before;After=$after;XBefore=$pointBefore.X;XAfter=$pointAfter.X;CardRight=$cardRight;ScrollbarLeft=$barPoint.X;GapDIP=($barPoint.X-$cardRight);HitTargetDIP=$bar.ActualWidth;IdleDIP=2;HoverDIP=6;DragDIP=6;Scrolled=$true}
}
$results|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Artifacts\overlay-scrollbar.json') -Encoding UTF8
$results
}finally{$win.Close()}
