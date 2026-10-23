param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
[void][Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application
$ui=New-Object ConstructorWindow
$window=$ui.Window
$selector=$window.FindName('ArmouryModeSelector')
$availability=[ConstructorWindow].GetMethod('ApplyProgramAvailability',[Reflection.BindingFlags]'Instance,NonPublic')
$readCapabilities=[ConstructorWindow].GetMethod('ReadPresetCapabilities',[Reflection.BindingFlags]'Instance,NonPublic')
$artifacts=Join-Path $PSScriptRoot 'Artifacts\ArmouryMode'
[void][IO.Directory]::CreateDirectory($artifacts)
$script:checks=0
function Assert($condition,$message){$script:checks++;if(-not $condition){throw $message}}
function SetPrograms([bool]$installed){[void]$availability.Invoke($ui,[object[]]@($true,$true,$installed))}
function SelectMode([int]$mode){$selector.SelectedItem=@($selector.Items|Where-Object Mode -eq $mode)[0]}
try{
    $labels=@('Не менять режим работы','Windows','Тихий','Эффективность','Турбо','Ручная')
    Assert ($selector -is [Windows.Controls.ComboBox] -and $selector.Items.Count -eq $labels.Count) 'Armoury operating-mode list is missing or incomplete'
    Assert ([Windows.Automation.AutomationProperties]::GetName($selector) -eq 'Режим работы Armoury Crate') 'Armoury operating-mode list has no accessible name'
    Assert ([object]::ReferenceEquals($selector.Style,$window.FindName('FanModeSelector').Style)) 'Armoury selector does not use the shared combo style'
    for($mode=0;$mode -lt $labels.Count;$mode++){
        Assert ($selector.Items[$mode].Mode -eq $mode -and $selector.Items[$mode].Label -eq $labels[$mode] -and $selector.Items[$mode].ToString() -eq $labels[$mode]) ('Armoury mode order or label differs: '+$mode)
    }
    SetPrograms $true
    $ui.ResetScript()
    Assert ($selector.SelectedItem.Mode -eq 0 -and $ui.Configuration.ArmouryMode -eq 0 -and -not $ui.Configuration.HasActions -and -not $window.FindName('SaveButton').IsEnabled) 'Neutral Armoury state creates an action'
    foreach($mode in @(1,2,3,4,5)){
        SelectMode $mode
        $configuration=$ui.Configuration
        Assert ($configuration.ArmouryMode -eq $mode -and $configuration.HasActions -and $window.FindName('SaveButton').IsEnabled) ('Armoury selection does not update configuration and Save: '+$mode)
        Assert ($configuration.FanProfile -eq 0 -and $configuration.PowerMode -eq 0 -and $configuration.GetDisplayActions().Length -eq 0) ('Armoury selection adds another action: '+$mode)
        Assert ($ui.PreviewText -eq ('Armoury Crate: '+$labels[$mode])) ('Armoury isolated preview differs: '+$mode)
        $path=Join-Path $artifacts ('Armoury-'+$mode+'.exe')
        [ExecutableExporter]::Save($path,$configuration,[EmbeddedAssets]::Read('Icon0'))
        $restored=[PeConfiguration]::Read($path)
        Assert ($restored.ArmouryMode -eq $mode -and $restored.HasActions -and $restored.FanProfile -eq 0 -and $restored.PowerMode -eq 0) ('Armoury export resource lost or added actions: '+$mode)
        $ui.RemovePreviewAction(0)
        Assert ($selector.SelectedItem.Mode -eq 0 -and $ui.Configuration.ArmouryMode -eq 0 -and -not $ui.Configuration.HasActions -and -not $window.FindName('SaveButton').IsEnabled) ('Removing Armoury preview leaves an action: '+$mode)
    }
    $window.FindName('DarkThemeSwitch').IsChecked=$true
    $fan=$window.FindName('FanModeSelector');$fan.SelectedItem=@($fan.Items|Where-Object Profile -eq 3)[0]
    SelectMode 4
    $paragraphs=@($ui.PreviewText -split '\r?\n\r?\n')
    Assert ($paragraphs.Count -eq 3 -and $paragraphs[1] -eq 'Armoury Crate: Турбо' -and $paragraphs[2] -eq 'Стандартный режим вентилятора') 'Armoury preview must precede the independent fan action'
    $ui.RemovePreviewAction(1)
    Assert ($ui.Configuration.ArmouryMode -eq 0 -and $ui.Configuration.FanProfile -eq 3 -and $ui.Configuration.DarkTheme -eq 1 -and -not $ui.PreviewText.Contains('Armoury Crate:')) 'Removing Armoury preview changes independent actions'
    SelectMode 5
    $ui.ResetScript()
    Assert ($selector.SelectedItem.Mode -eq 0 -and -not $ui.Configuration.HasActions -and -not $window.FindName('SaveButton').IsEnabled) 'Reset retains an Armoury action'
    SelectMode 4
    SetPrograms $false
    Assert (-not $selector.IsEnabled -and $selector.SelectedItem.Mode -eq 0 -and -not $window.FindName('ArmourySection').IsEnabled -and -not $ui.Configuration.HasActions) 'Absent Armoury application retains an editable or selected action'
    foreach($mode in @(1,2,3,4,5)){
        SelectMode $mode
        Assert ($ui.Configuration.ArmouryMode -eq 0 -and -not $ui.Configuration.HasActions -and -not $ui.PreviewText.Contains('Armoury Crate:')) ('Disabled Armoury selector bypasses configuration guard: '+$mode)
    }
    $window.FindName('DarkThemeSwitch').IsChecked=$true
    $absentPath=Join-Path $artifacts 'Armoury-absent.exe'
    [ExecutableExporter]::Save($absentPath,$ui.Configuration,[EmbeddedAssets]::Read('Icon0'))
    $absent=[PeConfiguration]::Read($absentPath)
    Assert ($absent.ArmouryMode -eq 0 -and $absent.DarkTheme -eq 1) 'Export includes an unavailable Armoury action or drops an independent action'
    foreach($installed in @($false,$true)){
        SetPrograms $installed
        foreach($performance in @($false,$true)){
            $capabilities=$readCapabilities.Invoke($ui,[object[]]@($performance))
            $preset=[PresetBuilder]::Build($performance,$capabilities)
            $expected=if(-not $installed){0}elseif($performance){4}else{2}
            Assert ($capabilities.ArmouryMode -eq $installed -and $preset.ArmouryMode -eq $expected) 'Armoury preset does not follow installation and autonomy/performance selection'
        }
    }
    $ui.ResetScript()
    Assert (-not $window.IsVisible -and -not $selector.IsDropDownOpen) 'Armoury verification opened the constructor or a popup'
    [pscustomobject]@{Passed=$true;Checks=$script:checks;Modes=@(1,2,3,4,5);PreviewRemoval=$true;Reset=$true;MissingApplicationGuard=$true;PresetAvailability=$true;ExportRoundtrip=$true;ExportedExecutablesRun=$false;SystemModesChanged=$false;ConstructorWindowShown=$false}|ConvertTo-Json -Depth 3|Set-Content -LiteralPath (Join-Path $artifacts 'results.json') -Encoding UTF8
    'PASS: '+$script:checks+' Armoury mode UI/configuration checks; neutral and five modes, preview/removal/reset, installation gates, presets and PE-resource exports. No exported EXE ran and no system mode changed.'
}finally{$window.Close()}
