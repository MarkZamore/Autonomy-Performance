$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application
$ui=New-Object ConstructorWindow
$window=$ui.Window
$script:checks=0
function Assert($condition,$message){$script:checks++;if(-not $condition){throw $message}}
$apply=$ui.GetType().GetMethod('ApplyProgramAvailability',[Reflection.BindingFlags]'Instance,NonPublic')
$readPresetCapabilities=$ui.GetType().GetMethod('ReadPresetCapabilities',[Reflection.BindingFlags]'Instance,NonPublic')
function SetPrograms([bool]$screen,[bool]$wallpaper,[bool]$armoury){[void]$apply.Invoke($ui,[object[]]@($screen,$wallpaper,$armoury))}
function Set-FanMode([int]$profile){$fan=$window.FindName('FanModeSelector');$fan.SelectedItem=@($fan.Items|Where-Object Profile -eq $profile)[0]}
function Set-ArmouryMode([int]$mode){$selector=$window.FindName('ArmouryModeSelector');$selector.SelectedItem=@($selector.Items|Where-Object Mode -eq $mode)[0]}
try{
    $timer=[Diagnostics.Stopwatch]::StartNew();$actual=[ProgramAvailability]::Read();$timer.Stop()
    Assert ($window.FindName('PerformanceSection').IsEnabled -eq $actual.ScreenXpert) 'ScreenXpert category differs from installation probe'
    Assert ($window.FindName('WallpaperSection').IsEnabled -eq $actual.WallpaperEngine) 'Wallpaper category differs from installation probe'
    Assert ($window.FindName('ArmourySection').IsEnabled -eq $actual.ArmouryCrate) 'Armoury category differs from installation probe'
    SetPrograms $true $true $true
    foreach($name in @('PerformanceSection','WallpaperSection','ArmourySection')){
        $section=$window.FindName($name);Assert $section.IsEnabled ('Installed category disabled: '+$name)
        $section.IsExpanded=$true;Assert $section.IsExpanded ('Installed category cannot expand: '+$name)
    }
    $window.FindName('DarkThemeSwitch').IsChecked=$true
    Set-FanMode 3
    Set-ArmouryMode 4
    foreach($name in @('BatteryCareSwitch','WallpaperPlaybackSwitch','WallpaperAudioSwitch','WallpaperApplicationSwitch')){$window.FindName($name).IsChecked=$true}
    Assert ($ui.Configuration.FanProfile -eq 3 -and $ui.Configuration.ArmouryMode -eq 4 -and $ui.Configuration.WallpaperPlayback -eq 1 -and $ui.Configuration.BatteryCare -eq 1) 'Installed program actions unavailable'
    SetPrograms $false $false $false
    foreach($name in @('PerformanceSection','WallpaperSection','ArmourySection')){
        $section=$window.FindName($name)
        Assert (-not $section.IsEnabled -and -not $section.IsExpanded) ('Missing category remains interactive/open: '+$name)
        [void]$section.ApplyTemplate();$header=$section.Template.FindName('HeaderToggle',$section);[void]$header.ApplyTemplate()
        Assert (-not $header.IsEnabled -and $header.Opacity -eq 0.4) ('Missing category header does not use disabled appearance: '+$name)
        Assert (-not $header.Focus()) ('Missing category accepts keyboard focus: '+$name)
        $section.IsExpanded=$true
        Assert (-not $section.IsExpanded) ('Missing category can be programmatically expanded: '+$name)
    }
    Assert ($window.FindName('FanModeSelector').SelectedItem.Profile -eq 0 -and -not $window.FindName('FanModeSelector').IsEnabled) 'Unavailable fan choice is retained or enabled'
    Assert ($window.FindName('ArmouryModeSelector').SelectedItem.Mode -eq 0 -and -not $window.FindName('ArmouryModeSelector').IsEnabled) 'Unavailable Armoury choice is retained or enabled'
    foreach($name in @('BatteryCareSwitch','WallpaperPlaybackSwitch','WallpaperAudioSwitch','WallpaperApplicationSwitch')){Assert ($null -eq $window.FindName($name).IsChecked -and -not $window.FindName($name).IsEnabled) ('Unavailable program action retained/enabled: '+$name)}
    $configuration=$ui.Configuration
    Assert ($configuration.FanProfile -eq 0 -and $configuration.ArmouryMode -eq 0 -and $configuration.BatteryCare -eq 0 -and $configuration.WallpaperPlayback -eq 0 -and $configuration.WallpaperAudio -eq 0 -and $configuration.WallpaperApplication -eq 0) 'Missing program action reaches generated configuration'
    Assert (-not $ui.PreviewText.Contains('Armoury Crate:')) 'Missing Armoury application retains a preview action'
    Assert ($configuration.DarkTheme -eq 1 -and $configuration.HasActions) 'Unavailable category cleared independent Windows action'
    foreach($profile in @(1,2,3)){
        Set-FanMode $profile
        Assert ($ui.Configuration.FanProfile -eq 0) ('Disabled fan action bypassed configuration guard: '+$profile)
        Set-FanMode 0
    }
    foreach($mode in @(1,2,3,4,5)){
        Set-ArmouryMode $mode
        Assert ($ui.Configuration.ArmouryMode -eq 0 -and -not $ui.PreviewText.Contains('Armoury Crate:')) ('Disabled Armoury action bypassed configuration guard: '+$mode)
        Set-ArmouryMode 0
    }
    foreach($name in @('BatteryCareSwitch','WallpaperPlaybackSwitch','WallpaperAudioSwitch','WallpaperApplicationSwitch')){
        $window.FindName($name).IsChecked=$true
        $configuration=$ui.Configuration
        Assert ($configuration.BatteryCare -eq 0 -and $configuration.WallpaperPlayback -eq 0 -and $configuration.WallpaperAudio -eq 0 -and $configuration.WallpaperApplication -eq 0) ('Disabled switch bypassed configuration guard: '+$name)
        $window.FindName($name).IsChecked=$null
    }
    foreach($performance in @($false,$true)){
        # Build from the injected absence snapshot. ApplyPreset deliberately refreshes
        # real installations, which would replace this simulated missing-app state.
        $capabilities=$readPresetCapabilities.Invoke($ui,[object[]]@($performance))
        $preset=[PresetBuilder]::Build($performance,$capabilities)
        Assert ($preset.FanProfile -eq 0) 'Preset selected an action for absent ScreenXpert'
        Assert (-not $capabilities.ArmouryMode -and $preset.ArmouryMode -eq 0) 'Preset selected an action for absent Armoury Crate'
        Assert ($window.FindName('FanModeSelector').SelectedItem.Profile -eq 0) 'Preset left a selected disabled fan choice'
        Assert ($preset.WallpaperPlayback -eq 0 -and $preset.WallpaperAudio -eq 0 -and $preset.WallpaperApplication -eq 0) 'Preset selected missing Wallpaper Engine action'
        Assert ($preset.DarkTheme -eq $(if($capabilities.DarkTheme){1}else{0})) 'Missing apps changed independent Windows-action availability in the preset'
    }
    foreach($values in @(@($true,$false,$false),@($false,$true,$false),@($false,$false,$true))){
        SetPrograms $values[0] $values[1] $values[2]
        Assert ($window.FindName('PerformanceSection').IsEnabled -eq $values[0] -and $window.FindName('WallpaperSection').IsEnabled -eq $values[1] -and $window.FindName('ArmourySection').IsEnabled -eq $values[2]) 'Availability categories are coupled'
        Assert ($window.FindName('ArmouryModeSelector').IsEnabled -eq $values[2]) 'Armoury selector availability depends on another program'
    }
    SetPrograms $true $true $true
    $ui.ResetScript();Set-FanMode 1;Set-ArmouryMode 2;$window.FindName('WallpaperAudioSwitch').IsChecked=$true
    Assert ($ui.Configuration.FanProfile -eq 1 -and $ui.Configuration.ArmouryMode -eq 2 -and $ui.Configuration.WallpaperAudio -eq 1) 'Newly installed program did not become editable'
    SetPrograms $actual.ScreenXpert $actual.WallpaperEngine $actual.ArmouryCrate
    'PASS: '+$script:checks+' checks; independent installation gates, disabled appearance/focus/expansion, action cleanup, presets and configuration guards. Native probe: ScreenXpert='+$actual.ScreenXpert+', WallpaperEngine='+$actual.WallpaperEngine+', ArmouryCrate='+$actual.ArmouryCrate+'; '+$timer.ElapsedMilliseconds+' ms.'
}finally{$window.Close()}
