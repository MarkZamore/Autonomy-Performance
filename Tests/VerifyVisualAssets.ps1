$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
function Render($visual,$width,$height,$path){
$size=[Windows.Size]::new($width,$height);$visual.Measure($size);$visual.Arrange([Windows.Rect]::new($size));$visual.UpdateLayout()
$drawing=[Windows.Media.DrawingVisual]::new();$context=$drawing.RenderOpen();try{$context.DrawRectangle($app.Resources['Brush.Canvas'],$null,[Windows.Rect]::new($size))}finally{$context.Close()}
$bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($drawing);$bitmap.Render($visual);$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create($path);try{$encoder.Save($stream)}finally{$stream.Dispose()}
}
try{
$icons=$win.FindName('IconGrid');$icons.Background=$app.Resources['Brush.Popup'];Render $icons 216 144 (Join-Path $PSScriptRoot 'Artifacts\ui-icons.png')
$win.FindName('DisplaySection').IsExpanded=$true;$win.FindName('DisplayEnabled').IsChecked=$true
foreach($rate in $win.FindName('RefreshSelector').Items){if($rate.Rate -eq 60){$win.FindName('RefreshSelector').SelectedItem=$rate;break}}
$grid=$win.FindName('RootGrid');$grid.Background=$app.Resources['Brush.Canvas'];Render $grid 984 661 (Join-Path $PSScriptRoot 'Artifacts\Display\ui-display.png')
$win.FindName('DisplayEnabled').IsChecked=$false;$win.FindName('DisplaySection').IsExpanded=$false
$ui.ApplyPreset($true);$win.FindName('PerformanceSection').IsExpanded=$true
Render $grid 984 661 (Join-Path $PSScriptRoot 'Artifacts\ui-performance-preset.png')
}finally{$win.Close()}
