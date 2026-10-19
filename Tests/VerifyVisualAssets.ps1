$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[void][IO.Directory]::CreateDirectory((Join-Path $PSScriptRoot 'Artifacts\Display'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
function Render($visual,$width,$height,$path){
$size=[Windows.Size]::new($width,$height);$visual.Measure($size);$visual.Arrange([Windows.Rect]::new($size));$visual.UpdateLayout()
$drawing=[Windows.Media.DrawingVisual]::new();$context=$drawing.RenderOpen();try{$context.DrawRectangle($app.Resources['Brush.Canvas'],$null,[Windows.Rect]::new($size));$context.DrawRectangle($app.Resources['Brush.WindowBackdrop'],$null,[Windows.Rect]::new($size))}finally{$context.Close()}
$bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($drawing);$bitmap.Render($visual);$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create($path);try{$encoder.Save($stream)}finally{$stream.Dispose()}
}
try{
$icons=$win.FindName('IconGrid');$icons.Background=$app.Resources['Brush.Popup'];Render $icons 216 144 (Join-Path $PSScriptRoot 'Artifacts\ui-icons.png')
$list=New-Object Windows.Controls.StackPanel
foreach($text in @('Не менять','60 Гц','144 Гц')){$item=New-Object Windows.Controls.ComboBoxItem;$item.Content=$text;$item.Style=$app.Resources[[Windows.Controls.ComboBoxItem]];$item.IsSelected=$text -eq '60 Гц';[void]$list.Children.Add($item)}
$listSurface=New-Object Windows.Controls.Border;$listSurface.Background=$app.Resources['Brush.Popup'];$listSurface.Padding=[Windows.Thickness]::new(8);$listSurface.Child=$list;Render $listSurface 300 140 (Join-Path $PSScriptRoot 'Artifacts\ui-list.png')
$win.FindName('DisplaySection').IsExpanded=$true
foreach($display in @($win.FindName('DisplaySelector').Items)){
    $win.FindName('DisplaySelector').SelectedItem=$display
    $recommendedScale=@($win.FindName('ScaleSelector').Items|Where-Object {$_.Recommended})|Select-Object -First 1
    if($recommendedScale){$win.FindName('ScaleSelector').SelectedItem=$recommendedScale}
    $recommendedResolution=@($win.FindName('ResolutionSelector').Items|Where-Object {$_.IsRecommended})|Select-Object -First 1
    if($recommendedResolution){$win.FindName('ResolutionSelector').SelectedItem=$recommendedResolution}
    foreach($rate in $win.FindName('RefreshSelector').Items){if($rate.Rate -eq 60){$win.FindName('RefreshSelector').SelectedItem=$rate;break}}
}
if($win.FindName('DisplaySelector').Items.Count -gt 0){$win.FindName('DisplaySelector').SelectedIndex=0}
$win.FindName('BrightnessToggleButton').IsChecked=$true;$win.FindName('BrightnessSlider').Value=63
$grid=$win.FindName('RootGrid');$grid.Background=[Windows.Media.Brushes]::Transparent;Render $grid 984 661 (Join-Path $PSScriptRoot 'Artifacts\Display\ui-display.png')
$win.FindName('SettingsScroll').ScrollToVerticalOffset(380);$grid.UpdateLayout();Render $grid 984 661 (Join-Path $PSScriptRoot 'Artifacts\Display\ui-display-controls.png')
$ui.ResetScript();$win.FindName('DisplaySection').IsExpanded=$false
$ui.ApplyPreset($true);$win.FindName('PerformanceSection').IsExpanded=$true
$win.FindName('SettingsScroll').ScrollToTop();$grid.UpdateLayout()
Render $grid 984 661 (Join-Path $PSScriptRoot 'Artifacts\ui-performance-preset.png')
$win.FindName('PerformanceSection').IsExpanded=$false;$win.FindName('WallpaperSection').IsExpanded=$true
$win.FindName('WallpaperPlaybackSwitch').IsChecked=$true;$win.FindName('WallpaperAudioSwitch').IsChecked=$false
Render $grid 984 661 (Join-Path $PSScriptRoot 'Artifacts\ui-wallpaper.png')
$states=New-Object Windows.Controls.StackPanel
foreach($state in @($false,$null,$true)){
    $check=New-Object Windows.Controls.CheckBox;$check.Style=$app.Resources['TriStateSwitch'];$check.Content='Состояние переключателя';$check.Foreground=$app.Resources['Brush.Text'];$check.IsChecked=$state
    [ControlMotion]::AttachSwitch($check);[void]$states.Children.Add($check)
}
Render $states 500 168 (Join-Path $PSScriptRoot 'Artifacts\ui-switch-states.png')
$brightnessStates=New-Object Windows.Controls.StackPanel
foreach($enabled in @($false,$true)){$button=New-Object Windows.Controls.CheckBox;$button.Name='BrightnessToggleButton';$button.Style=$app.Resources['BinaryActionSwitch'];$button.Content='Яркость';$button.IsChecked=$enabled;[ControlMotion]::AttachSwitch($button);[void]$brightnessStates.Children.Add($button)}
Render $brightnessStates 500 112 (Join-Path $PSScriptRoot 'Artifacts\ui-brightness-states.png')
$win.FindName('WallpaperSection').IsExpanded=$false;$win.FindName('AdditionalSection').IsExpanded=$true;$win.FindName('TaskbarRepairSwitch').IsChecked=$true
$win.FindName('SettingsScroll').ScrollToVerticalOffset(160);$grid.UpdateLayout();Render $grid 984 661 (Join-Path $PSScriptRoot 'Artifacts\ui-additional.png')
}finally{$win.Close()}
