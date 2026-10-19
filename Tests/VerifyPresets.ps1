param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$art=Join-Path $PSScriptRoot 'Artifacts\Presets'
[void][IO.Directory]::CreateDirectory($art)
[void][Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
$additional=@('DarkTheme','Hdr','PresenceSleep','PresenceDim','AdaptiveBrightness','ScreenSaver','UsbPower','WallpaperPlayback','WallpaperAudio','BatteryCare','WallpaperApplication','NightLight','DynamicRefresh','PresenceWake','PresenceOnlooker','BatteryPercentage','ClockSeconds')
$fields=@('EnergySaver','FanProfile','PowerMode','Brightness','RefreshRate','DisplayId','DisplayWidth','DisplayHeight','DisplayScale','ContentBrightness','TaskbarRepair')+$additional
$results=@();$assertions=0
function Assert($condition,$message){$script:assertions++;if(-not $condition){throw $message}}
function Click($button){$button.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent))}
function Get-SelectionFiles {
    @([IO.Directory]::GetFiles($root)+[IO.Directory]::GetFiles([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)))|Sort-Object
}
function Assert-Preset([bool]$performance,[string]$label) {
    $c=$ui.Configuration
    Assert ($c.EnergySaver -eq $(if($performance){2}else{1})) ('Wrong saver: '+$label)
    Assert ($c.FanProfile -eq $(if($performance){2}else{1})) ('Wrong fan: '+$label)
    Assert ($c.PowerMode -eq $(if($performance){3}else{0})) ('Wrong power: '+$label)
    Assert ($c.Brightness -eq -1) ('Preset created a legacy global brightness action: '+$label)
    Assert ($win.FindName('BestPerformance').IsChecked -eq $performance -and -not $win.FindName('Efficiency').IsChecked -and -not $win.FindName('Balanced').IsChecked) ('Wrong power selection: '+$label)
    $currentDisplay=$win.FindName('DisplaySelector').SelectedItem
    $capable=[DisplayBrightnessControl]::Available($currentDisplay.MonitorId)
    Assert ($win.FindName('BrightnessToggleButton').IsChecked -eq ($performance -and $capable) -and -not $win.FindName('BrightnessToggleButton').IsThreeState) ('Wrong brightness binary-switch state: '+$label)
    foreach($field in $additional){Assert ($c.$field -eq 0 -and $null -eq $win.FindName($field+'Switch').IsChecked) ('Additional action remains selected: '+$label+' '+$field)}
    Assert ($c.RefreshRate -eq 0 -and [string]::IsNullOrEmpty($c.DisplayId) -and $win.FindName('RefreshSelector').SelectedItem.Rate -eq 0) ('Display configuration not removed: '+$label)
    $expectedDisplays=@($win.FindName('DisplaySelector').Items|Where-Object {$performance -and [DisplayBrightnessControl]::Available($_.MonitorId)})
    Assert ($c.Displays.Count -eq $expectedDisplays.Count) ('Wrong preset display-action count: '+$label)
    foreach($display in $expectedDisplays){$entry=@($c.Displays|Where-Object {$_.DisplayId -eq $display.MonitorId});Assert ($entry.Count -eq 1 -and $entry[0].Brightness -eq 100) ('Missing target brightness: '+$label);foreach($field in @('DisplayWidth','DisplayHeight','RefreshRate','DisplayScale','Hdr','DynamicRefresh','ContentBrightness')){Assert ($entry[0].$field -eq 0) ('Preset retained per-display '+$field+': '+$label)}}
    Assert ($c.TaskbarRepair -eq 0 -and $win.FindName('TaskbarRepairSwitch').IsChecked -eq $false) ('Preset retained taskbar repair: '+$label)
    $expected=if($performance){@('Выключить энергосбережение','Оптимальная производительность')+@($expectedDisplays|ForEach-Object {'Экран '+([regex]::Match($_.DeviceName,'DISPLAY(\d+)$').Groups[1].Value)+' - яркость 100%'})+@('Производительный режим вентилятора')}else{@('Включить энергосбережение','Тихий режим вентилятора')}
    $lines=@($win.FindName('ScriptPreview').Text -split '\r?\n\r?\n')
    Assert ($lines.Count -eq $expected.Count) ('Unexpected preview count: '+$label)
    for($i=0;$i -lt $lines.Count;$i++){Assert ($lines[$i] -eq $expected[$i]) ('Wrong preview paragraph: '+$label+' '+$i)}
    Assert ($win.FindName('SaveButton').IsEnabled) ('Save disabled after preset: '+$label)
}
try {
    Assert (-not $ui.Configuration.HasActions) 'Initial state has actions'
    $monitorsBefore=@([DisplayControl]::Enumerate())
    foreach($custom in @($false,$true)) {
        foreach($performance in @($false,$true)) {
            $label=$(if($performance){'performance'}else{'autonomy'})+'-'+$(if($custom){'custom'}else{'builtin'})
            $win.FindName('EnergySaverSwitch').IsChecked=$false;$win.FindName('FanPerformance').IsChecked=$true
            $win.FindName('Balanced').IsChecked=$true;$win.FindName('BrightnessToggleButton').IsChecked=$true;$win.FindName('BrightnessSlider').Value=22
            foreach($field in $additional){$win.FindName($field+'Switch').IsChecked=$true}
            $rates=$win.FindName('RefreshSelector');$choice=@($rates.Items|Where-Object {$_.Rate -gt 0})|Select-Object -First 1
            if($choice){$rates.SelectedItem=$choice;$monitor=$win.FindName('DisplaySelector').SelectedItem;Assert (@($ui.Configuration.Displays|Where-Object {$_.DisplayId -eq $monitor.MonitorId -and $_.RefreshRate -gt 0}).Count -eq 1) 'Mixed profile did not include the display action'}
            if($custom){$ui.IconChoice.SelectCustom((Join-Path $root 'Source\Assets\Icon2.ico'))}else{$ui.IconChoice.SelectBuiltIn(4)}
            $ui.RefreshIcons();$iconPath=$ui.IconChoice.CustomPath;$iconIndex=$ui.IconChoice.BuiltInIndex
            $iconHash=[Convert]::ToBase64String($ui.IconChoice.GetBytes())
            $filesBefore=Get-SelectionFiles
            Click ($win.FindName($(if($performance){'PresetPerformanceButton'}else{'PresetAutonomyButton'})))
            Assert-Preset $performance $label
            Assert ($ui.IconChoice.CustomPath -eq $iconPath -and $ui.IconChoice.BuiltInIndex -eq $iconIndex -and [Convert]::ToBase64String($ui.IconChoice.GetBytes()) -eq $iconHash) ('Preset changed icon choice: '+$label)
            Assert (@(Compare-Object $filesBefore (Get-SelectionFiles)).Count -eq 0) ('Preset created/deleted a desktop or root file: '+$label)
            $ui.ApplyPreset($performance);Assert-Preset $performance ($label+'-repeat')
            $path=Join-Path $art ($label+'.exe');[ExecutableExporter]::Save($path,$ui.Configuration,$ui.IconChoice.GetBytes())
            $loaded=[PeConfiguration]::Read($path);$selected=$ui.Configuration
            foreach($field in $fields){Assert ($loaded.$field -eq $selected.$field) ('Preset export mismatch: '+$label+' '+$field)}
            Assert ($loaded.Displays.Count -eq $selected.Displays.Count) ('Preset export loses display targets: '+$label)
            for($displayIndex=0;$displayIndex -lt $selected.Displays.Count;$displayIndex++){foreach($field in @('DisplayId','DisplayWidth','DisplayHeight','RefreshRate','DisplayScale','Hdr','DynamicRefresh','ContentBrightness','Brightness')){Assert ($loaded.Displays[$displayIndex].$field -eq $selected.Displays[$displayIndex].$field) ('Preset display export mismatch: '+$label+' '+$field)}}
            # Do not execute a real preset: editing and exporting cannot apply it to the system.
            $win.FindName('Balanced').IsChecked=$true;$win.FindName('FanWhisper').IsChecked=$true;$win.FindName('BrightnessToggleButton').IsChecked=$false
            $edited=$ui.Configuration;$selectedMonitor=$win.FindName('DisplaySelector').SelectedItem;$target=@($edited.Displays|Where-Object {$_.DisplayId -eq $selectedMonitor.MonitorId})
            Assert ($edited.PowerMode -eq 2 -and $edited.FanProfile -eq 1 -and $edited.Brightness -eq -1 -and ($target.Count -eq 0 -or $target[0].Brightness -eq -1)) ('Preset cannot be edited: '+$label)
            Assert (-not $win.FindName('BestPerformance').IsChecked) ('Manual power conflict failed: '+$label)
            $results+=[pscustomobject]@{Scenario=$label;PresetReplacedAllActions=$true;DisplayCleared=$true;IconPreserved=$true;ManualEditing=$true;ExportRoundtrip=$true;ExportPath=$path}
        }
    }
    $monitorsAfter=@([DisplayControl]::Enumerate())
    foreach($monitor in $monitorsBefore){$after=@($monitorsAfter|Where-Object {$_.MonitorId -eq $monitor.MonitorId});Assert ($after.Count -eq 1 -and $after[0].CurrentRate -eq $monitor.CurrentRate -and $after[0].Width -eq $monitor.Width -and $after[0].Height -eq $monitor.Height) 'Preset interaction changed a display mode'}
    [pscustomobject]@{Passed=$true;Assertions=$assertions;Scenarios=$results;ExportedExecutablesRun=$false;DisplayModesUnchanged=$true}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
    'PASS: '+$assertions+' assertions; preset buttons fully replace mixed/additional/display actions, preserve builtin/custom icons, support repeat and manual editing, export independently, and do not apply settings.'
} finally {$win.Close()}
