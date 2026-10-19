param([string]$ThemePath,[switch]$Compiled,[string]$Executable)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(-not $ThemePath){$ThemePath=Join-Path $root 'Source\Theme.xaml'}
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
[Windows.Media.RenderOptions]::ProcessRenderMode=[Windows.Interop.RenderMode]::SoftwareOnly
if($Compiled){
    if(-not $Executable){$Executable=Join-Path $root 'Source\Build\AutonomyPerformance.exe'}
    $assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Executable))
    $stream=$assembly.GetManifestResourceStream('Theme')
    if(-not $stream){throw 'Compiled constructor lacks its Theme resource.'}
    $reader=New-Object IO.StreamReader($stream)
    try{$themeText=$reader.ReadToEnd()}finally{$reader.Dispose()}
}else{$themeText=[IO.File]::ReadAllText($ThemePath)}
$hoverKey=[Windows.UIElement].GetField('IsMouseOverPropertyKey',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
$setDpi=[Windows.Media.VisualTreeHelper].GetMethod('SetRootDpi',[Reflection.BindingFlags]'Static,Public,NonPublic')
$scales=if($setDpi){@(1.0,1.25,1.5,2.0)}else{@(1.0)}
$checks=0;$results=@()
$artifacts=Join-Path $PSScriptRoot 'Artifacts\ScrollThumbGeometry'
[void][IO.Directory]::CreateDirectory($artifacts)
foreach($scale in $scales){foreach($maximum in @(100.0,1000000.0)){foreach($fraction in @(0.0,0.5,1.0)){foreach($hover in @($false,$true)){
    $resources=[Windows.Markup.XamlReader]::Parse($themeText)
    $grid=New-Object Windows.Controls.Grid
    $grid.Width=64;$grid.Height=264;$grid.Background=[Windows.Media.Brushes]::Black
    $grid.UseLayoutRounding=$true;$grid.SnapsToDevicePixels=$true;$grid.Resources=$resources
    $bar=New-Object Windows.Controls.Primitives.ScrollBar
    $bar.Orientation=[Windows.Controls.Orientation]::Vertical;$bar.Width=16;$bar.Height=240
    $bar.Minimum=0;$bar.Maximum=$maximum;$bar.ViewportSize=240;$bar.Value=$fraction*$maximum
    $bar.HorizontalAlignment=[Windows.HorizontalAlignment]::Center;$bar.VerticalAlignment=[Windows.VerticalAlignment]::Center
    [void]$grid.Children.Add($bar)
    for($pass=0;$pass -lt 4;$pass++){
        if($setDpi){$dpi=[Windows.DpiScale]::new($scale,$scale);$arguments=New-Object object[] 2;$arguments[0]=$grid.PSObject.BaseObject;$arguments[1]=$dpi.PSObject.BaseObject;[void]$setDpi.Invoke($null,$arguments)}
        $grid.Measure([Windows.Size]::new(64,264));$grid.Arrange([Windows.Rect]::new(0,0,64,264));$grid.UpdateLayout()
    }
    $bar.SetValue($hoverKey,$hover);$grid.UpdateLayout()
    $track=$bar.Template.FindName('PART_Track',$bar)
    $thumb=$track.Thumb;[void]$thumb.ApplyTemplate();$grid.UpdateLayout()
    $grip=$thumb.Template.FindName('ThumbSurface',$thumb)
    $actualDpi=[Windows.Media.VisualTreeHelper]::GetDpi($thumb).DpiScaleX
    $checks++
    if([Math]::Abs($actualDpi-$scale) -gt 0.001){throw 'The actual Thumb DPI does not match the requested test scale.'}
    $slot=[Windows.Controls.Primitives.LayoutInformation]::GetLayoutSlot($thumb)
    $clip=[Windows.Media.VisualTreeHelper]::GetClip($thumb)
    $checks++
    if($thumb.ActualHeight -gt $slot.Height+0.01){throw ('Thumb exceeds its Track slot: '+$thumb.ActualHeight+' > '+$slot.Height+'; scale='+$scale+'; max='+$maximum+'; hover='+$hover)}
    $checks++
    if($grip.ActualHeight -gt $slot.Height+0.01){throw 'The rounded indicator exceeds the Track slot.'}
    $checks++
    if($clip -and $clip.Bounds.Height+0.01 -lt $thumb.ActualHeight){throw 'WPF clips the rounded thumb.'}
    $checks++
    $expected=if($hover){6.0}else{2.0}
    if([Math]::Abs($grip.ActualWidth-$expected) -gt 1.0/$scale+0.01){throw 'Hover width differs from Fluent indicator width.'}
    $results+=@{Scale=$scale;ActualDpi=$actualDpi;Maximum=$maximum;Fraction=$fraction;Hover=$hover;SlotHeight=$slot.Height;ThumbHeight=$thumb.ActualHeight;IndicatorHeight=$grip.ActualHeight;IndicatorWidth=$grip.ActualWidth}
    if($maximum -eq 1000000 -and $fraction -eq 1 -and $hover){
        $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](64*$scale),[int](264*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32)
        $bitmap.Render($grid);$encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $path=Join-Path $artifacts ('bottom-hover-'+$scale.ToString([Globalization.CultureInfo]::InvariantCulture)+'.png')
        $stream=[IO.File]::Create($path);try{$encoder.Save($stream)}finally{$stream.Dispose()}
    }
}}}}
@{Passed=$true;Checks=$checks;Cases=$results;Runtime=[Environment]::Version.ToString()}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $artifacts 'results.json') -Encoding utf8
'PASS: '+$checks+' assertions, '+$results.Count+' real WPF scrollbar cases; proportional and minimum thumbs, top/middle/bottom, hover, no window or native input.'
