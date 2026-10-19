$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
function Assert($condition,$message){if(-not $condition){throw $message}}
try{
    foreach($item in @(@('PerformanceSection','ScreenXpert','ScreenXpertIcon'),@('WallpaperSection','Wallpaper Engine','WallpaperEngineIcon'),@('ArmourySection','ARMOURY CRATE','ArmouryCrateIcon'))){
        $section=$win.FindName($item[0]);Assert ($null -ne $section -and -not $section.IsExpanded) ('Missing/collapsed category: '+$item[1])
        Assert ([Windows.Automation.AutomationProperties]::GetName($section) -eq $item[1]) ('Wrong category name: '+$item[1])
        Assert ($win.FindName($item[2]).Source.PixelWidth -gt 16) ('Missing embedded category icon: '+$item[1])
    }
    Assert ($win.FindName('ArmourySection').Content -is [Windows.Controls.Grid] -and $win.FindName('ArmourySection').Content.Children.Count -eq 0) 'ARMOURY CRATE is not empty'
    $fan=$win.FindName('FanSelector');Assert ($fan.Items.Count -eq 4) 'Missing fan mode or unchanged option'
    $fan.SelectedIndex=3;Assert ($ui.Configuration.FanProfile -eq 3 -and $win.FindName('ScriptPreview').Text.Contains('Стандартный режим')) 'Standard fan mode missing from config/preview'
    $fan.SelectedIndex=0
    $toggle=$win.FindName('WallpaperPlaybackSwitch');Assert ($toggle.IsThreeState -and $null -eq $toggle.IsChecked) 'Wallpaper switch is not neutral'
    $toggle.IsChecked=$false;Assert ($ui.Configuration.WallpaperPlayback -eq 2 -and $ui.Configuration.HasActions -and $win.FindName('ScriptPreview').Text.Contains('паузу')) 'Wallpaper pause is not selected'
    $toggle.IsChecked=$true;Assert ($ui.Configuration.WallpaperPlayback -eq 1 -and $win.FindName('ScriptPreview').Text.Contains('Продолжить')) 'Wallpaper play is not selected'
    $toggle.IsChecked=$null;Assert (-not $ui.Configuration.HasActions) 'Neutral wallpaper switch remains an action'
    $art=Join-Path $PSScriptRoot 'Artifacts\AppCategories';[void][IO.Directory]::CreateDirectory($art)
    foreach($mode in @(1,2)){$c=New-Object ModeConfiguration;$c.WallpaperPlayback=$mode;$path=Join-Path $art ('Wallpaper-'+$mode+'.exe');[ExecutableExporter]::Save($path,$c,[EmbeddedAssets]::Read('Icon0'));Assert ([PeConfiguration]::Read($path).WallpaperPlayback -eq $mode) 'Wallpaper export lost configuration'}
    $c=New-Object ModeConfiguration;$c.FanProfile=3;$path=Join-Path $art 'FanStandard.exe';[ExecutableExporter]::Save($path,$c,[EmbeddedAssets]::Read('Icon0'));Assert ([PeConfiguration]::Read($path).FanProfile -eq 3) 'Standard fan export lost configuration'
    $invalid=New-Object ModeConfiguration;$invalid.WallpaperPlayback=3;$rejected=$false;try{$invalid.Validate()}catch{$rejected=$true};Assert $rejected 'Invalid wallpaper action accepted'
    foreach($preset in @($false,$true)){$toggle.IsChecked=$false;$ui.ApplyPreset($preset);Assert ($ui.Configuration.WallpaperPlayback -eq 0 -and $null -eq $toggle.IsChecked) 'Preset retained wallpaper action'}
    $ui.ApplyPreset($false);$toggle.IsChecked=$false
    $win.FindName('PerformanceSection').IsExpanded=$true;$win.FindName('WallpaperSection').IsExpanded=$true
    $grid=$win.FindName('RootGrid');$grid.Background=$app.Resources['Brush.WindowBackdrop'];$size=[Windows.Size]::new(984,661);$grid.Measure($size);$grid.Arrange([Windows.Rect]::new($size));$grid.UpdateLayout()
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(984,661,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($grid);$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $art 'categories.png'));try{$encoder.Save($stream)}finally{$stream.Dispose()}
    'PASS: branded embedded categories, three fan modes, neutral/pause/play wallpaper action, presets and standalone EXE configuration.'
}finally{$win.Close()}
