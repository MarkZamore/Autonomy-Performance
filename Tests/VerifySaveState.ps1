$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
function Assert($condition,$message){if(-not $condition){throw $message}}
try{
    $save=$win.FindName('SaveButton');$preset=$win.FindName('PresetAutonomyButton')
    Assert (-not $save.IsEnabled) 'Empty script enables Save'
    Assert ([object]::ReferenceEquals($save.Style,$preset.Style)) 'Save differs from neutral preset style'
    $ui.ApplyPreset($false);Assert ($save.IsEnabled) 'New script cannot be saved'
    $folder=Join-Path $PSScriptRoot 'Artifacts\SaveState';[void][IO.Directory]::CreateDirectory($folder)
    $method=$ui.GetType().GetMethod('SaveToPath',[Reflection.BindingFlags]'NonPublic,Instance')
    [void]$method.Invoke($ui,@([string](Join-Path $folder 'Mode.exe')))
    Assert (-not $save.IsEnabled) 'Save stays enabled after successful export'
    Assert ($win.FindName('Status').Text -eq '') 'Successful save displays a status message'
    $ui.ApplyPreset($false);Assert (-not $save.IsEnabled) 'Identical preset falsely marks saved script changed'
    $win.FindName('DarkThemeSwitch').IsChecked=$true;Assert ($save.IsEnabled) 'Action change did not enable Save'
    $win.FindName('DarkThemeSwitch').IsChecked=$null;Assert (-not $save.IsEnabled) 'Returning to saved settings stays dirty'
    $win.FindName('IconGrid').Children[1].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
    Assert ($save.IsEnabled) 'Icon change did not enable Save'
    [void]$method.Invoke($ui,@([string](Join-Path $folder 'OtherIcon.exe')));Assert (-not $save.IsEnabled) 'Icon export did not lock Save'
    $win.FindName('BrightnessEnabled').IsChecked=$true
    $win.FindName('BrightnessSlider').Value=37
    $rejected=$false;try{[void]$method.Invoke($ui,@([string](Join-Path $folder 'Missing\No.exe')))}catch{$rejected=$true}
    Assert ($rejected -and $save.IsEnabled) 'Failed save changed clean/dirty state'
    $ui.ApplyPreset($true);Assert ($save.IsEnabled) 'Different preset did not enable Save'
    @{Passed=$true;DisabledAfterSave=$true;ConfigAndIconChanges=$true;RevertDisables=$true;NoSuccessStatus=$true;NeutralStyle=$true;FailurePreservesDirty=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $folder 'result.json') -Encoding utf8
    'PASS: save state, neutral styling, changed actions/icons, reverting, failed export, no success message.'
}finally{$win.Close()}
