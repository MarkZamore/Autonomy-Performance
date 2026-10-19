$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
$script:checks=0
function Assert($condition,$message){$script:checks++;if(-not $condition){throw $message}}
$readPresetCapabilities=[ConstructorWindow].GetMethod('ReadPresetCapabilities',[Reflection.BindingFlags]'Instance,NonPublic')
try{
    foreach($item in @(@('PerformanceSection','ScreenXpert','ScreenXpertIcon'),@('WallpaperSection','Wallpaper Engine','WallpaperEngineIcon'),@('ArmourySection','Armoury Crate','ArmouryCrateIcon'))){
        $section=$win.FindName($item[0]);Assert ($null -ne $section -and -not $section.IsExpanded) ('Missing/collapsed category: '+$item[1])
        Assert ([Windows.Automation.AutomationProperties]::GetName($section) -eq $item[1]) ('Wrong category name: '+$item[1])
        $image=$win.FindName($item[2]);Assert ($image.Source -is [Windows.Media.DrawingImage]) ('Category icon is not an embedded vector: '+$item[1])
        Assert ($image.Width -eq 20 -and $image.Height -eq 20 -and $image.Source.Width -eq 20 -and $image.Source.Height -eq 20) ('Category icon dimensions differ: '+$item[1])
    }
    Assert ($win.FindName('ArmourySection').Content -is [Windows.Controls.Grid] -and $win.FindName('ArmourySection').Content.Children.Count -eq 0) 'Armoury Crate is not empty'
    $fan=$win.FindName('FanModeSelector');Assert ($fan -is [Windows.Controls.ComboBox] -and $fan.Items.Count -eq 4 -and $fan.Items[0].ToString() -eq 'Не менять режим вентилятора') 'Fan mode list is incomplete'
    $fan.SelectedIndex=3;Assert ($ui.Configuration.FanProfile -eq 3 -and $ui.PreviewText.Contains('Стандартный режим вентилятора') -and -not $ui.PreviewText.Contains('вентиляторов')) 'Standard fan mode missing from config/preview'
    $fan.SelectedIndex=0
    $toggle=$win.FindName('WallpaperPlaybackSwitch');Assert ($toggle.IsThreeState -and $null -eq $toggle.IsChecked) 'Wallpaper switch is not neutral'
    $toggle.IsChecked=$false;Assert ($ui.Configuration.WallpaperPlayback -eq 2 -and $ui.Configuration.HasActions -and $ui.PreviewText.Contains('паузу')) 'Wallpaper pause is not selected'
    $toggle.IsChecked=$true;Assert ($ui.Configuration.WallpaperPlayback -eq 1 -and $ui.PreviewText.Contains('Продолжить')) 'Wallpaper play is not selected'
    $toggle.IsChecked=$null;Assert (-not $ui.Configuration.HasActions) 'Neutral wallpaper switch remains an action'
    $audio=$win.FindName('WallpaperAudioSwitch');Assert ($audio.IsThreeState -and $null -eq $audio.IsChecked) 'Wallpaper audio switch is not neutral'
    $audio.IsChecked=$false;Assert ($ui.Configuration.WallpaperAudio -eq 2 -and $ui.Configuration.WallpaperPlayback -eq 0 -and $ui.PreviewText.Contains('Выключить звук')) 'Wallpaper audio mute is missing or changes playback'
    $audio.IsChecked=$true;Assert ($ui.Configuration.WallpaperAudio -eq 1 -and $ui.PreviewText.Contains('Включить звук')) 'Wallpaper audio unmute is missing'
    $audio.IsChecked=$null;Assert (-not $ui.Configuration.HasActions) 'Neutral wallpaper audio switch remains an action'
    $application=$win.FindName('WallpaperApplicationSwitch');Assert ($application.IsThreeState -and $null -eq $application.IsChecked -and $application.Content.Text -eq 'Wallpaper Engine') 'Wallpaper application switch is not neutral or retains the removed caption'
    $application.IsChecked=$true;Assert ($ui.Configuration.WallpaperApplication -eq 1 -and $ui.PreviewText.Contains('Запустить Wallpaper Engine') -and -not $ui.PreviewText.Contains('без Steam')) 'Wallpaper application start is missing or retains the removed text'
    $application.IsChecked=$false;Assert ($ui.Configuration.WallpaperApplication -eq 2 -and $ui.PreviewText.Contains('Завершить Wallpaper Engine')) 'Wallpaper application exit is missing'
    $application.IsChecked=$null;Assert (-not $ui.Configuration.HasActions) 'Neutral wallpaper application switch remains an action'
    $art=Join-Path $PSScriptRoot 'Artifacts\AppCategories';[void][IO.Directory]::CreateDirectory($art)
    foreach($mode in @(1,2)){$c=New-Object ModeConfiguration;$c.WallpaperPlayback=$mode;$path=Join-Path $art ('Wallpaper-'+$mode+'.exe');[ExecutableExporter]::Save($path,$c,[EmbeddedAssets]::Read('Icon0'));Assert ([PeConfiguration]::Read($path).WallpaperPlayback -eq $mode) 'Wallpaper export lost configuration'}
    foreach($mode in @(1,2)){$c=New-Object ModeConfiguration;$c.WallpaperAudio=$mode;$path=Join-Path $art ('WallpaperAudio-'+$mode+'.exe');[ExecutableExporter]::Save($path,$c,[EmbeddedAssets]::Read('Icon0'));Assert ([PeConfiguration]::Read($path).WallpaperAudio -eq $mode) 'Wallpaper audio export lost configuration'}
    foreach($mode in @(1,2)){$c=New-Object ModeConfiguration;$c.WallpaperApplication=$mode;$path=Join-Path $art ('WallpaperApplication-'+$mode+'.exe');[ExecutableExporter]::Save($path,$c,[EmbeddedAssets]::Read('Icon0'));Assert ([PeConfiguration]::Read($path).WallpaperApplication -eq $mode) 'Wallpaper application export lost configuration'}
    $c=New-Object ModeConfiguration;$c.FanProfile=3;$path=Join-Path $art 'FanStandard.exe';[ExecutableExporter]::Save($path,$c,[EmbeddedAssets]::Read('Icon0'));Assert ([PeConfiguration]::Read($path).FanProfile -eq 3) 'Standard fan export lost configuration'
    $invalid=New-Object ModeConfiguration;$invalid.WallpaperPlayback=3;$rejected=$false;try{$invalid.Validate()}catch{$rejected=$true};Assert $rejected 'Invalid wallpaper action accepted'
    foreach($preset in @($false,$true)){
        $toggle.IsChecked=$false;$application.IsChecked=$false;$audio.IsChecked=$false;$ui.ApplyPreset($preset)
        $c=$ui.Configuration;$capabilities=$readPresetCapabilities.Invoke($ui,[object[]]@($preset))
        foreach($pair in @(@('WallpaperPlayback',$toggle),@('WallpaperAudio',$audio))){
            $expected=if($capabilities.($pair[0])){if($preset){1}else{2}}else{0}
            Assert ($c.($pair[0]) -eq $expected) ('Preset wallpaper action ignores mode or installation: '+$pair[0])
            Assert ($(if($expected -eq 0){$null -eq $pair[1].IsChecked}else{$pair[1].IsChecked -eq ($expected -eq 1)})) ('Preset wallpaper control is not synchronized: '+$pair[0])
        }
        $expected=if($preset -and $capabilities.WallpaperApplication){1}else{0}
        Assert ($c.WallpaperApplication -eq $expected -and $(if($expected -eq 0){$null -eq $application.IsChecked}else{$application.IsChecked -eq $true})) 'Preset wallpaper launch is incorrect or out of sync'
    }
    $ui.ResetScript();$toggle.IsChecked=$false
    $win.FindName('PerformanceSection').IsExpanded=$true;$win.FindName('WallpaperSection').IsExpanded=$true
    $grid=$win.FindName('RootGrid');$grid.Background=$app.Resources['Brush.WindowBackdrop'];$size=[Windows.Size]::new(984,661);$grid.Measure($size);$grid.Arrange([Windows.Rect]::new($size));$grid.UpdateLayout()
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(984,661,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($grid);$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $art 'categories.png'));try{$encoder.Save($stream)}finally{$stream.Dispose()}
    'PASS: '+$script:checks+' checks; branded embedded categories, three fan modes, neutral/pause/play/audio/start/exit wallpaper actions, presets and standalone EXE configuration.'
}finally{$win.Close()}
