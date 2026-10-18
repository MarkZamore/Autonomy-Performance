param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$art=Join-Path $PSScriptRoot 'Artifacts\Presets'
[void](New-Item -ItemType Directory -Path $art -Force)
[void][Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
$checks=@('SaverOn','SaverOff','Whisper','FanPerformance','Brightness','Volume','Efficiency','Balanced','BestPerformance','DisplayEnabled')
$results=@();$assertions=0
function Assert($condition,$message){$script:assertions++;if(-not $condition){throw $message}}
function Click($button){$button.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent))}
function Get-SelectionFiles {
    @([IO.Directory]::GetFiles($root)+[IO.Directory]::GetFiles([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)))|Sort-Object
}
function Assert-Preset([bool]$performance,[string]$label) {
    $wanted=if($performance){@('SaverOff','FanPerformance','Brightness','Volume','BestPerformance')}else{@('SaverOn','Whisper')}
    foreach($name in $checks){Assert (($win.FindName($name).IsChecked -eq $true) -eq ($wanted -contains $name)) ('Wrong check state: '+$label+' '+$name)}
    $c=$ui.Configuration
    Assert ($c.EnergySaver -eq $(if($performance){2}else{1})) ('Wrong saver: '+$label)
    Assert ($c.FanProfile -eq $(if($performance){2}else{1})) ('Wrong fan: '+$label)
    Assert ($c.PowerMode -eq $(if($performance){3}else{0})) ('Wrong power: '+$label)
    Assert ($c.Brightness100 -eq $performance -and $c.Volume100 -eq $performance) ('Wrong brightness/volume: '+$label)
    Assert ($c.RefreshRate -eq 0 -and [string]::IsNullOrEmpty($c.DisplayId)) ('Display configuration not removed: '+$label)
    $expected=if($performance){@('Выключить энергосбережение','Производительный режим вентиляторов','Яркость 100%','Громкость 100%','Оптимальная производительность')}else{@('Включить энергосбережение','Тихий режим вентиляторов')}
    $lines=@($win.FindName('ScriptPreview').Text -split '\r?\n')
    Assert ($lines.Count -eq $expected.Count) ('Unexpected preview count: '+$label)
    for($i=0;$i -lt $lines.Count;$i++){Assert ($lines[$i] -eq (($i+1).ToString()+'. '+$expected[$i])) ('Wrong preview line: '+$label+' '+$i)}
    Assert ($win.FindName('SaveButton').IsEnabled) ('Save disabled after preset: '+$label)
}
try {
    Assert (-not $ui.Configuration.HasActions) 'Initial state has actions'
    $monitorsBefore=@([DisplayControl]::Enumerate())
    Assert ($win.FindName('DisplaySelector').Items.Count -gt 0) 'Test needs a supported monitor for a mixed display configuration'
    foreach($custom in @($false,$true)) {
        foreach($performance in @($false,$true)) {
            $label=$(if($performance){'performance'}else{'autonomy'})+'-'+$(if($custom){'custom'}else{'builtin'})
            # Begin with a mixed profile containing an actual enumerated display target.
            foreach($name in $checks){$win.FindName($name).IsChecked=$false}
            $win.FindName('SaverOn').IsChecked=$true;$win.FindName('FanPerformance').IsChecked=$true
            $win.FindName('Balanced').IsChecked=$true;$win.FindName('Volume').IsChecked=$true
            $win.FindName('DisplayEnabled').IsChecked=$true
            Assert ($ui.Configuration.RefreshRate -gt 0 -and -not [string]::IsNullOrEmpty($ui.Configuration.DisplayId)) 'Mixed profile did not include the display action'
            if($custom){$ui.IconChoice.SelectCustom((Join-Path $root 'Source\Assets\Icon2.ico'))}else{$ui.IconChoice.SelectBuiltIn(4)}
            $ui.RefreshIcons();$iconPath=$ui.IconChoice.CustomPath;$iconIndex=$ui.IconChoice.BuiltInIndex
            $iconHash=[Convert]::ToBase64String($ui.IconChoice.GetBytes())
            $filesBefore=Get-SelectionFiles
            $button=$win.FindName($(if($performance){'PresetPerformanceButton'}else{'PresetAutonomyButton'}))
            Click $button
            Assert-Preset $performance $label
            Assert ($ui.IconChoice.CustomPath -eq $iconPath -and $ui.IconChoice.BuiltInIndex -eq $iconIndex -and [Convert]::ToBase64String($ui.IconChoice.GetBytes()) -eq $iconHash) ('Preset changed icon choice: '+$label)
            Assert (@(Compare-Object $filesBefore (Get-SelectionFiles)).Count -eq 0) ('Preset created/deleted a desktop or root file: '+$label)
            # The public method is also idempotent and produces the same preview.
            $ui.ApplyPreset($performance);Assert-Preset $performance ($label+'-repeat')
            $path=Join-Path $art ($label+'.exe')
            [ExecutableExporter]::Save($path,$ui.Configuration,$ui.IconChoice.GetBytes())
            $loaded=[PeConfiguration]::Read($path);$selected=$ui.Configuration
            foreach($field in @('EnergySaver','FanProfile','PowerMode','Brightness100','Volume100','RefreshRate','DisplayId')){Assert ($loaded.$field -eq $selected.$field) ('Preset export mismatch: '+$label+' '+$field)}
            # Do not execute the output: presets themselves must never change the system.
            $win.FindName('Balanced').IsChecked=$true;$win.FindName('Whisper').IsChecked=$true;$win.FindName('Brightness').IsChecked=$false
            Assert ($ui.Configuration.PowerMode -eq 2 -and $ui.Configuration.FanProfile -eq 1 -and -not $ui.Configuration.Brightness100) ('Preset could not be edited: '+$label)
            Assert (-not $win.FindName('BestPerformance').IsChecked -and -not $win.FindName('FanPerformance').IsChecked) ('Manual conflicts failed after preset: '+$label)
            $results+=[pscustomobject]@{Scenario=$label;PresetReplacedAllActions=$true;DisplayCleared=$true;IconPreserved=$true;ManualEditing=$true;ExportRoundtrip=$true;ExportPath=$path}
        }
    }
    $monitorsAfter=@([DisplayControl]::Enumerate())
    foreach($monitor in $monitorsBefore){$after=@($monitorsAfter|Where-Object {$_.MonitorId -eq $monitor.MonitorId});Assert ($after.Count -eq 1 -and $after[0].CurrentRate -eq $monitor.CurrentRate -and $after[0].Width -eq $monitor.Width -and $after[0].Height -eq $monitor.Height) 'Preset interaction changed display mode'}
    [pscustomobject]@{Passed=$true;Assertions=$assertions;Scenarios=$results;ExportedExecutablesRun=$false;DisplayModesUnchanged=$true}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
    'PASS: '+$assertions+' assertions; both preset buttons, replacement of mixed/display configuration, repeat use, retained builtin/custom icons, editing after preset, no automatic save, and EXE configuration roundtrip. Exported programs were not executed.'
} finally {$win.Close()}
