param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$art=Join-Path $PSScriptRoot 'Artifacts\Presets'
[void][IO.Directory]::CreateDirectory($art)
[void][Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Web.Extensions
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
$additional=@('DarkTheme','Hdr','PresenceSleep','PresenceDim','AdaptiveBrightness','ScreenSaver','UsbPower','WallpaperPlayback','WallpaperAudio','BatteryCare','WallpaperApplication','NightLight','DynamicRefresh','PresenceWake','PresenceOnlooker','BatteryPercentage','ClockSeconds')
$globalFields=@([ModeConfiguration].GetFields()|Where-Object {$_.Name -ne 'Displays'}|ForEach-Object {$_.Name})
$displayFields=@([DisplayModeConfiguration].GetFields()|ForEach-Object {$_.Name})
$snapshot=[ConstructorWindow].GetMethod('ReadPresetCapabilities',[Reflection.BindingFlags]'Instance,NonPublic')
$results=@();$assertions=0
function Assert($condition,$message){$script:assertions++;if(-not $condition){throw $message}}
function Set-FanMode([int]$profile){$fan=$win.FindName('FanModeSelector');$fan.SelectedItem=@($fan.Items|Where-Object Profile -eq $profile)[0]}
function Click($button){$button.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent))}
function Get-SelectionFiles {
    @([IO.Directory]::GetFiles($root)+[IO.Directory]::GetFiles([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)))|Sort-Object
}
function Get-DisplayState {
    @([DisplayControl]::Enumerate()|ForEach-Object {
        $supported=$false;$enabled=$false;$hdr=$null
        if([HdrControl]::TryInspect($_.MonitorId,[ref]$supported,[ref]$enabled)-and $supported){$hdr=$enabled}
        $dynamic=$false;$drr=$null;if([HdrControl]::InspectDynamic($_.MonitorId,[ref]$dynamic)){$drr=$dynamic}
        [pscustomobject]@{Id=$_.MonitorId;Width=$_.Width;Height=$_.Height;Rate=$_.CurrentRate;Scale=[HdrControl]::InspectCurrentScale($_.MonitorId);Hdr=$hdr;Drr=$drr}
    })
}
function Assert-Configuration($actual,$expected,[string]$label) {
    $actual.Validate();$expected.Validate()
    foreach($field in $globalFields){Assert ($actual.$field -eq $expected.$field) ('Configuration mismatch: '+$label+' '+$field)}
    Assert ($actual.Displays.Count -eq $expected.Displays.Count) ('Wrong display-action count: '+$label)
    for($i=0;$i -lt $expected.Displays.Count;$i++){
        foreach($field in $displayFields){Assert ($actual.Displays[$i].$field -eq $expected.Displays[$i].$field) ('Display mismatch: '+$label+' target '+$i+' '+$field)}
    }
}
function Get-ExpectedPreview($configuration) {
    $text=New-Object 'Collections.Generic.List[string]'
    if($configuration.EnergySaver -ne 0){$text.Add($(if($configuration.EnergySaver -eq 1){'Включить энергосбережение'}else{'Выключить энергосбережение'}))}
    if($configuration.PowerMode -ne 0){$text.Add($(if($configuration.PowerMode -eq 1){'Максимальная эффективность энергосбережения'}elseif($configuration.PowerMode -eq 3){'Оптимальная производительность'}else{'Сбалансированный'}))}
    foreach($target in $configuration.Displays){
        $display=@($win.FindName('DisplaySelector').Items|Where-Object {$_.MonitorId -eq $target.DisplayId})[0]
        $prefix='Экран '+[regex]::Match($display.DeviceName,'DISPLAY(\d+)$').Groups[1].Value+' - '
        if($target.DisplayScale -ne 0){$text.Add($prefix+'масштаб '+$target.DisplayScale+'%')}
        if($target.DisplayWidth -ne 0){$text.Add($prefix+'разрешение '+$target.DisplayWidth+' × '+$target.DisplayHeight)}
        if($target.RefreshRate -ne 0){$text.Add($prefix+'частота обновления '+$target.RefreshRate+' Гц')}
        if($target.Hdr -ne 0){$text.Add($prefix+$(if($target.Hdr -eq 1){'включить HDR'}else{'выключить HDR'}))}
        if($target.DynamicRefresh -ne 0){$text.Add($prefix+$(if($target.DynamicRefresh -eq 1){'включить динамическую частоту обновления'}else{'выключить динамическую частоту обновления'}))}
    }
    if($configuration.DarkTheme -eq 1){$text.Add('Включить тёмную тему Windows')}
    if($configuration.FanProfile -ne 0){$text.Add($(if($configuration.FanProfile -eq 2){'Производительный режим вентилятора'}else{'Тихий режим вентилятора'}))}
    if($configuration.WallpaperApplication -eq 1){$text.Add('Запустить Wallpaper Engine')}
    if($configuration.WallpaperPlayback -ne 0){$text.Add($(if($configuration.WallpaperPlayback -eq 1){'Продолжить воспроизведение обоев Wallpaper Engine'}else{'Поставить обои Wallpaper Engine на паузу'}))}
    if($configuration.WallpaperAudio -ne 0){$text.Add($(if($configuration.WallpaperAudio -eq 1){'Включить звук обоев Wallpaper Engine'}else{'Выключить звук обоев Wallpaper Engine'}))}
    return ,$text.ToArray()
}
function Assert-Preset([bool]$performance,[string]$label) {
    $capabilities=$snapshot.Invoke($ui,[object[]]@($performance));$expected=[PresetBuilder]::Build($performance,$capabilities)
    $c=$ui.Configuration;Assert-Configuration $c $expected $label
    Assert ($c.EnergySaver -eq $(if(-not $capabilities.EnergySaver){0}elseif($performance){2}else{1})) ('Wrong energy saver: '+$label)
    Assert ($c.PowerMode -eq $(if(-not $capabilities.PowerMode){0}elseif($performance){3}else{1})) ('Wrong Windows power mode: '+$label)
    Assert ($c.DarkTheme -eq $(if($capabilities.DarkTheme){1}else{0})) ('Missing common actions: '+$label)
    Assert ($c.FanProfile -eq $(if(-not $capabilities.FanProfile){0}elseif($performance){2}else{1})) ('Wrong available fan mode: '+$label)
    Assert ($c.Brightness -eq -1 -and -not $win.FindName('BrightnessToggleButton').IsChecked) ('Preset selected brightness: '+$label)
    foreach($field in @('PresenceSleep','PresenceDim','AdaptiveBrightness','ScreenSaver','UsbPower','BatteryCare','NightLight','PresenceWake','PresenceOnlooker','BatteryPercentage','ClockSeconds')){
        Assert ($c.$field -eq 0 -and $null -eq $win.FindName($field+'Switch').IsChecked) ('Unrelated action selected: '+$label+' '+$field)
    }
    $numbers=@();foreach($target in $c.Displays){
        $display=@($win.FindName('DisplaySelector').Items|Where-Object {$_.MonitorId -eq $target.DisplayId})[0]
        $numbers+=[int][regex]::Match($display.DeviceName,'DISPLAY(\d+)$').Groups[1].Value
        Assert ($target.Brightness -eq -1 -and $target.ContentBrightness -eq 0) ('Preset retained brightness/CABC: '+$label)
        $recommendedScale=[HdrControl]::InspectRecommendedScale($display.MonitorId);$offeredScales=[HdrControl]::InspectScales($display.MonitorId)
        $expectedScale=0
        if($recommendedScale -gt 0 -and $offeredScales.Contains($recommendedScale)){
            if($performance){$expectedScale=$recommendedScale}else{
                $width=if($target.DisplayWidth -gt 0){$target.DisplayWidth}else{$display.Width}
                $height=if($target.DisplayHeight -gt 0){$target.DisplayHeight}else{$display.Height}
                foreach($scale in $offeredScales){if($scale -ge 100 -and $scale -le $recommendedScale -and $scale -gt $expectedScale -and [Math]::Min($width,$height)*100 -ge 720*$scale){$expectedScale=$scale}}
            }
        }
        Assert ($target.DisplayScale -eq $expectedScale) ('Preset scale differs from the recommendation adapted to its resolution: '+$label)
        if($target.DisplayWidth -ne 0){$mode=@($display.Resolutions|Where-Object {$_.Width -eq $target.DisplayWidth -and $_.Height -eq $target.DisplayHeight});Assert ($mode.Count -eq 1) ('Unavailable resolution: '+$label);if($target.RefreshRate -gt 0){Assert ($mode[0].Rates.Contains($target.RefreshRate)) ('Unavailable resolution/Hz pair: '+$label)}}
    }
    Assert ((($numbers|Sort-Object)-join ',') -eq ($numbers-join ',')) ('Display order is not numeric: '+$label)
    $expectedLines=Get-ExpectedPreview $expected;$lines=@($ui.PreviewText -split '\r?\n\r?\n')
    Assert ($lines.Count -eq $expectedLines.Count) ('Unexpected preview count: '+$label)
    for($i=0;$i -lt $lines.Count;$i++){Assert ($lines[$i] -eq $expectedLines[$i]) ('Wrong preview paragraph: '+$label+' '+$i)}
    Assert ($win.FindName('SaveButton').IsEnabled -eq $c.HasActions) ('Wrong Save availability: '+$label)
    return $c
}
try {
    Assert ($null -ne $snapshot) 'Read-only preset capability snapshot is unavailable'
    Assert (-not $ui.Configuration.HasActions) 'Initial state has actions'
    $monitorsBefore=Get-DisplayState
    foreach($custom in @($false,$true)) {
        foreach($performance in @($false,$true)) {
            $label=$(if($performance){'performance'}else{'autonomy'})+'-'+$(if($custom){'custom'}else{'builtin'})
            $win.FindName('EnergySaverSwitch').IsChecked=$false;Set-FanMode 3
            $win.FindName('Balanced').IsChecked=$true;$win.FindName('BrightnessToggleButton').IsChecked=$true;$win.FindName('BrightnessSlider').Value=22
            foreach($field in $additional){$win.FindName($field+'Switch').IsChecked=$true}
            foreach($display in @($win.FindName('DisplaySelector').Items)){
                $win.FindName('DisplaySelector').SelectedItem=$display
                $rates=$win.FindName('RefreshSelector');$choice=@($rates.Items|Where-Object {$_.Rate -gt 0})|Select-Object -First 1
                if($choice){$rates.SelectedItem=$choice}
                $content=$win.FindName('ContentBrightnessSelector');if($content.IsEnabled -and $content.Items.Count -gt 1){$content.SelectedIndex=1}
            }
            if($custom){$ui.IconChoice.SelectCustom((Join-Path $root 'Source\Assets\Icon2.ico'))}else{$ui.IconChoice.SelectBuiltIn(4)}
            $ui.RefreshIcons();$iconPath=$ui.IconChoice.CustomPath;$iconIndex=$ui.IconChoice.BuiltInIndex
            $iconHash=[Convert]::ToBase64String($ui.IconChoice.GetBytes());$filesBefore=Get-SelectionFiles
            Click ($win.FindName($(if($performance){'PresetPerformanceButton'}else{'PresetAutonomyButton'})))
            $selected=Assert-Preset $performance $label
            Assert ($ui.IconChoice.CustomPath -eq $iconPath -and $ui.IconChoice.BuiltInIndex -eq $iconIndex -and [Convert]::ToBase64String($ui.IconChoice.GetBytes()) -eq $iconHash) ('Preset changed icon: '+$label)
            Assert (@(Compare-Object $filesBefore (Get-SelectionFiles)).Count -eq 0) ('Preset created/deleted a desktop or root file: '+$label)
            $ui.ApplyPreset($performance);$repeat=Assert-Preset $performance ($label+'-repeat');Assert-Configuration $repeat $selected ($label+' repeat')
            $path=Join-Path $art ($label+'.exe');[ExecutableExporter]::Save($path,$ui.Configuration,$ui.IconChoice.GetBytes())
            $loaded=[PeConfiguration]::Read($path);Assert-Configuration $loaded $ui.Configuration ($label+' exported resource')
            # Exported real presets are inspected as resources, never executed.
            if($win.FindName('Balanced').IsEnabled){$win.FindName('Balanced').IsChecked=$true}
            if($win.FindName('FanModeSelector').IsEnabled){Set-FanMode 1}
            $win.FindName('BrightnessToggleButton').IsChecked=$false;$edited=$ui.Configuration
            Assert ($edited.PowerMode -eq $(if($win.FindName('Balanced').IsEnabled){2}else{0})) ('Power mode cannot be edited: '+$label)
            Assert ($edited.FanProfile -eq $(if($win.FindName('FanModeSelector').IsEnabled){1}else{0})) ('Fan mode cannot be edited: '+$label)
            Assert (-not $win.FindName('BestPerformance').IsChecked -and -not $win.FindName('Efficiency').IsChecked) ('Manual power conflict failed: '+$label)
            if($edited.Displays.Count -gt 0){
                $paragraphs=@($ui.PreviewText -split '\r?\n\r?\n');$index=-1
                for($i=0;$i -lt $paragraphs.Count;$i++){if($paragraphs[$i] -match '^Экран \d+ - '){$index=$i;break}}
                Assert ($index -ge 0) ('Display action absent from preview: '+$label)
                $beforePreview=$paragraphs.Count;$ui.RemovePreviewAction($index)
                Assert (@($ui.PreviewText -split '\r?\n\r?\n').Count -eq $beforePreview-1) ('Removing a preview action changed more than one action: '+$label)
            }
            $ui.ResetScript();Assert (-not $ui.Configuration.HasActions -and -not $win.FindName('SaveButton').IsEnabled) ('Reset did not clear preset: '+$label)
            Assert ([Convert]::ToBase64String($ui.IconChoice.GetBytes()) -eq $iconHash) ('Reset changed icon: '+$label)
            $results+=[pscustomobject]@{Scenario=$label;PresetReplacedAllActions=$true;AdaptiveDisplayTargets=$selected.Displays.Count;IconPreserved=$true;ManualEditing=$true;PreviewRemoval=$true;Reset=$true;ExportRoundtrip=$true;ExportPath=$path}
        }
    }
    $monitorsAfter=Get-DisplayState;Assert ($monitorsBefore.Count -eq $monitorsAfter.Count) 'Connected display count changed'
    foreach($monitor in $monitorsBefore){
        $after=@($monitorsAfter|Where-Object {$_.Id -eq $monitor.Id});Assert ($after.Count -eq 1) 'Display disappeared'
        foreach($field in @('Width','Height','Rate','Scale','Hdr','Drr')){Assert ($after[0].$field -eq $monitor.$field) ('Preset interaction changed display state: '+$field)}
    }
    [pscustomobject]@{Passed=$true;Assertions=$assertions;Scenarios=$results;ExportedExecutablesRun=$false;DisplayModesUnchanged=$true;Before=$monitorsBefore;After=$monitorsAfter}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
    'PASS: '+$assertions+' assertions; adaptive presets replace mixed actions, preserve icons, order preview, support editing/reset, roundtrip PE resources, and leave display settings unchanged.'
} finally {$win.Close()}
