param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$art=Join-Path $PSScriptRoot 'Artifacts\NewSettings'
[void][IO.Directory]::CreateDirectory($art)
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application
$ui=New-Object ConstructorWindow
$win=$ui.Window
$script:assertions=0
$additional=@('DarkTheme','SleepTimeout','ScreenTimeout','Hdr','PresenceSleep','PresenceDim','ContentBrightness','AdaptiveBrightness','ScreenSaver','UsbPower')
$power=@('Efficiency','Balanced','BestPerformance')
$fanNames=@('FanWhisper','FanPerformance','FanStandard')
$fields=@('Version','EnergySaver','FanProfile','PowerMode','Brightness','DisplayId','RefreshRate')+$additional
$results=@()
function Assert($condition,$message){$script:assertions++;if(-not $condition){throw $message}}
function Click($button){$button.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent))}
function Reset-Actions {
    $ui.ApplyPreset($false)
    $win.FindName('EnergySaverSwitch').IsChecked=$null
    foreach($name in $fanNames+$power){$win.FindName($name).IsChecked=$false}
    $win.FindName('BrightnessToggleButton').IsChecked=$false
    $win.FindName('RefreshSelector').SelectedIndex=0
    foreach($field in $additional){$win.FindName($field+'Switch').IsChecked=$null}
}
function Assert-NumberedPreview {
    $lines=@($win.FindName('ScriptPreview').Text -split '\r?\n')
    for($i=0;$i -lt $lines.Count;$i++){Assert ($lines[$i] -match ('^'+($i+1)+'\. ')) ('Preview numbering failed: '+$lines[$i])}
}
function Render-Ui($name,$width,$height,$scale) {
    $grid=$win.FindName('RootGrid')
    $grid.Background=$app.Resources['Brush.WindowBackdrop']
    $size=[Windows.Size]::new($width,$height)
    $grid.Measure($size);$grid.Arrange([Windows.Rect]::new($size));$grid.UpdateLayout()
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]($width*$scale),[int]($height*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($grid)
    $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $path=Join-Path $art ($name+'.png');$stream=[IO.File]::Create($path)
    try{$encoder.Save($stream)}finally{$stream.Dispose()}
    foreach($buttonName in @('PresetAutonomyButton','PresetPerformanceButton','IconButton','SaveButton')){
        $button=$win.FindName($buttonName);$point=$button.TranslatePoint([Windows.Point]::new(0,0),$grid)
        Assert ($button.ActualWidth -gt 0 -and $point.X -ge 0 -and $point.Y -ge 0 -and $point.X+$button.ActualWidth -le $grid.ActualWidth+1 -and $point.Y+$button.ActualHeight -le $grid.ActualHeight+1) ('Footer outside client area: '+$name+' '+$buttonName)
    }
    foreach($field in @('EnergySaver')+$additional){
        $toggle=$win.FindName($field+'Switch');[void]$toggle.ApplyTemplate()
        $track=$toggle.Template.FindName('SwitchTrack',$toggle)
        Assert ($track -ne $null -and $track.ActualWidth -gt 0) ('Switch has no visible track: '+$name+' '+$field)
        $point=$track.TranslatePoint([Windows.Point]::new(0,0),$toggle)
        $right=$point.X+$track.ActualWidth
        Assert ($point.X -gt $toggle.ActualWidth/2 -and $right -le $toggle.ActualWidth+1 -and $toggle.ActualWidth-$right -le 24) ('Switch is not aligned to the setting right edge: '+$name+' '+$field)
    }
    [pscustomobject]@{Scenario=$name;Width=$width;Height=$height;Scale=$scale;Image=$path}
}
try {
    Assert (-not $ui.Configuration.HasActions -and -not $win.FindName('SaveButton').IsEnabled) 'Initial configuration has actions'
    Assert ($ui.Configuration.Version -eq 2 -and $ui.Configuration.Brightness -eq -1) 'Wrong new configuration defaults'
    Assert ($win.FindName('Volume') -eq $null) 'Removed volume setting remains in the UI'
    Assert ($ui.Configuration.GetType().GetField('Volume100') -eq $null) 'Volume remains an executable configuration field'
    Assert ($null -eq $win.FindName('PowerUnchanged') -and $ui.Configuration.PowerMode -eq 0) 'Removed unchanged power option remains'
    foreach($field in @('EnergySaver')+$additional){
        $toggle=$win.FindName($field+'Switch')
        Assert ($toggle -ne $null -and $toggle.IsThreeState -and $null -eq $toggle.IsChecked) ('Switch is not initially neutral and three-state: '+$field)
        $toggle.IsChecked=$true;Assert ($ui.Configuration.$field -eq 1) ('On state mapping failed: '+$field)
        $toggle.IsChecked=$false;Assert ($ui.Configuration.$field -eq 2) ('Off state mapping failed: '+$field)
        $toggle.IsChecked=$null;Assert ($ui.Configuration.$field -eq 0) ('Neutral state mapping failed: '+$field)
    }
    Assert (-not $ui.Configuration.HasActions) 'Returning every switch to neutral leaves an action'
    for($i=0;$i -lt 3;$i++){
        $win.FindName($fanNames[$i]).IsChecked=$true;Assert ($ui.Configuration.FanProfile -eq ($i+1)) ('Fan mode mapping failed: '+$fanNames[$i])
        foreach($other in $fanNames){Assert (($win.FindName($other).IsChecked -eq $true) -eq ($other -eq $fanNames[$i])) ('Fan choices not exclusive: '+$other)}
    }
    $win.FindName('FanStandard').IsChecked=$false
    for($i=0;$i -lt 3;$i++){
        $win.FindName($power[$i]).IsChecked=$true
        Assert ($ui.Configuration.PowerMode -eq ($i+1)) ('Power mode mismatch: '+$power[$i])
        foreach($other in $power){Assert (($win.FindName($other).IsChecked -eq $true) -eq ($other -eq $power[$i])) ('Power choices not exclusive: '+$other)}
        $win.FindName($power[$i]).IsChecked=$false
        Assert ($ui.Configuration.PowerMode -eq 0) 'Unchecking a power mode does not restore unchanged'
    }
    $win.FindName('Balanced').IsChecked=$true;$win.FindName('EnergySaverSwitch').IsChecked=$true
    Assert ($ui.Configuration.PowerMode -eq 2 -and $ui.Configuration.EnergySaver -eq 1) 'Energy Saver and power mode are no longer independent'
    Reset-Actions
    $slider=$win.FindName('BrightnessSlider');$brightness=$win.FindName('BrightnessToggleButton')
    Assert ($slider.Minimum -eq 0 -and $slider.Maximum -eq 100 -and $slider.Value -eq 100) 'Brightness slider range/default is incorrect'
    $brightness.IsChecked=$true;$slider.Value=37
    Assert ($ui.Configuration.Brightness -eq 37 -and $win.FindName('ScriptPreview').Text.Contains('37%')) 'Brightness percentage missing from config/preview'
    Assert ($slider.IsEnabled) 'Brightness icon does not enable its slider'
    $win.FindName('DisplaySection').IsExpanded=$true
    $layout=$win.FindName('RootGrid');$layout.Measure([Windows.Size]::new(936,613));$layout.Arrange([Windows.Rect]::new(0,0,936,613));$layout.UpdateLayout()
    [void]$slider.ApplyTemplate();$thumb=$slider.Template.FindName('BrightnessThumb',$slider)
    Assert ($thumb -ne $null -and $null -eq $thumb.ToolTip) 'Brightness thumb tooltip remains'
    $slider.Value=0;Assert ($ui.Configuration.Brightness -eq 0 -and $ui.Configuration.HasActions) 'Zero brightness is confused with unchanged'
    $brightness.IsChecked=$false;Assert ($ui.Configuration.Brightness -eq -1 -and -not $ui.Configuration.HasActions) 'Disabled brightness remains an action'
    Assert (-not $slider.IsEnabled) 'Brightness slider remains editable after unchecking'
    $selector=$win.FindName('DisplaySelector');$rates=$win.FindName('RefreshSelector')
    Assert ($selector.IsEnabled) 'Display selector must remain usable as the HDR target'
    if($selector.Items.Count -gt 0){
        $choice=@($rates.Items|Where-Object {$_.Rate -gt 0})|Select-Object -First 1
        if($choice){
            $rates.SelectedItem=$choice
            Assert ($ui.Configuration.DisplayId -eq $selector.SelectedItem.MonitorId -and $ui.Configuration.RefreshRate -eq $choice.Rate) 'Display selection not stored'
            Assert ($win.FindName('ScriptPreview').Text.Contains($selector.SelectedItem.Label) -and $win.FindName('ScriptPreview').Text.Contains(([string]$choice.Rate)+' Гц')) 'Selected display/rate missing from preview'
            $win.FindName('HdrSwitch').IsChecked=$true
            Assert ($win.FindName('ScriptPreview').Text.Contains($selector.SelectedItem.Label)) 'HDR preview has no target display'
            Assert-NumberedPreview
        }
        $rates.SelectedIndex=0;$win.FindName('HdrSwitch').IsChecked=$null
        $win.FindName('ContentBrightnessSwitch').IsChecked=$true
        Assert ($ui.Configuration.ContentBrightness -eq 1 -and $ui.Configuration.RefreshRate -eq 0 -and $ui.Configuration.Hdr -eq 0 -and $ui.Configuration.DisplayId -eq $selector.SelectedItem.MonitorId) 'Content brightness did not independently retain its display target'
        Assert ($win.FindName('ScriptPreview').Text.Contains('оптимизацию яркости по содержимому') -and $win.FindName('ScriptPreview').Text.Contains($selector.SelectedItem.Label)) 'Content brightness preview has no action or target display'
        Assert-NumberedPreview
    }
    foreach($performance in @($false,$true)){
        foreach($field in $additional){$win.FindName($field+'Switch').IsChecked=$true}
        $win.FindName('EnergySaverSwitch').IsChecked=$false;$win.FindName('FanPerformance').IsChecked=$true
        $win.FindName('Balanced').IsChecked=$true;$brightness.IsChecked=$true;$slider.Value=22
        $ui.IconChoice.SelectBuiltIn(4);$ui.RefreshIcons();$icon=[Convert]::ToBase64String($ui.IconChoice.GetBytes())
        Click ($win.FindName($(if($performance){'PresetPerformanceButton'}else{'PresetAutonomyButton'})))
        $config=$ui.Configuration
        Assert ($config.EnergySaver -eq $(if($performance){2}else{1}) -and $config.FanProfile -eq $(if($performance){2}else{1})) 'Preset saver/fan selection failed'
        Assert ($config.PowerMode -eq $(if($performance){3}else{0}) -and $config.Brightness -eq $(if($performance){100}else{-1})) 'Preset power/brightness selection failed'
        foreach($field in $additional){Assert ($config.$field -eq 0 -and $null -eq $win.FindName($field+'Switch').IsChecked) ('Preset does not reset additional action: '+$field)}
        Assert ($config.RefreshRate -eq 0) 'Preset retained a refresh-rate action'
        Assert ([Convert]::ToBase64String($ui.IconChoice.GetBytes()) -eq $icon) 'Preset changed the selected icon'
        $preview=$win.FindName('ScriptPreview').Text
        Assert ($preview -notmatch 'Громкость|Energy Saver|Whisper|Performance Mode') 'Removed or untranslated action appears in preview'
        Assert-NumberedPreview
        $path=Join-Path $art ($(if($performance){'Performance'}else{'Autonomy'})+'.exe')
        [ExecutableExporter]::Save($path,$config,$ui.IconChoice.GetBytes())
        $loaded=[PeConfiguration]::Read($path)
        foreach($field in $fields){Assert ($loaded.$field -eq $config.$field) ('Preset export lost field '+$field)}
        $version=[Diagnostics.FileVersionInfo]::GetVersionInfo($path)
        Assert ([string]::IsNullOrEmpty($version.FileVersion) -and [string]::IsNullOrEmpty($version.ProductVersion)) 'Exported EXE has version metadata'
    }
    # The exporter must preserve every independent action, including false and zero values.
    $mixed=New-Object ModeConfiguration;$mixed.EnergySaver=2;$mixed.FanProfile=1;$mixed.PowerMode=2;$mixed.Brightness=0
    $mixed.DisplayId='disconnected-test-monitor';$mixed.RefreshRate=60
    for($i=0;$i -lt $additional.Count;$i++){$mixed.($additional[$i])=1+($i%2)}
    $roundtrip=Join-Path $art 'Mixed.exe';[ExecutableExporter]::Save($roundtrip,$mixed,[EmbeddedAssets]::Read('Icon0'))
    $loaded=[PeConfiguration]::Read($roundtrip)
    foreach($field in $fields){Assert ($loaded.$field -eq $mixed.$field) ('Mixed EXE export lost field '+$field)}
    Assert (@(Get-ChildItem -LiteralPath $art -Filter '*.tmp' -Force).Count -eq 0) 'Export left a temporary file'
    $legacyPath=Join-Path $art 'LegacyConfiguration.exe'
    $legacyJson=[Text.Encoding]::UTF8.GetBytes('{"Version":1,"Brightness100":true,"Volume100":true}')
    [IO.File]::WriteAllBytes($legacyPath,[PortableResources]::CreateModeExecutable([EmbeddedAssets]::Read('RunnerTemplate'),$legacyJson,[EmbeddedAssets]::Read('Icon0')))
    $legacy=[PeConfiguration]::Read($legacyPath)
    Assert ($legacy.Version -eq 1 -and $legacy.Brightness -eq 100 -and $legacy.HasActions) 'Legacy fixed brightness was not mapped to a percentage'
    $legacyJson=[Text.Encoding]::UTF8.GetBytes('{"Version":1,"Brightness100":false,"Volume100":true}')
    [IO.File]::WriteAllBytes($legacyPath,[PortableResources]::CreateModeExecutable([EmbeddedAssets]::Read('RunnerTemplate'),$legacyJson,[EmbeddedAssets]::Read('Icon0')))
    $legacy=[PeConfiguration]::Read($legacyPath)
    Assert ($legacy.Brightness -eq -1 -and -not $legacy.HasActions) 'Removed legacy volume action is still accepted'
    foreach($value in @(-2,101)){
        $invalid=New-Object ModeConfiguration;$invalid.Brightness=$value;$rejected=$false
        try{$invalid.Validate()}catch{$rejected=$true};Assert $rejected ('Out-of-range brightness accepted: '+$value)
    }
    foreach($field in $additional){
        $invalid=New-Object ModeConfiguration;$invalid.$field=3;$rejected=$false
        try{$invalid.Validate()}catch{$rejected=$true};Assert $rejected ('Invalid switch value accepted: '+$field)
    }
    foreach($field in @('Hdr','ContentBrightness')){
        $invalid=New-Object ModeConfiguration;$invalid.$field=1;$rejected=$false
        try{$invalid.Validate()}catch{$rejected=$true};Assert $rejected ('Display-bound action accepted without a target: '+$field)
    }
    # Run only deliberately missing-display actions. No physical monitor is modified.
    $safe=New-Object ModeConfiguration;$safe.DisplayId='AP-TEST-DISCONNECTED-46f5f8bf';$safe.RefreshRate=60;$safe.Hdr=2;$safe.ContentBrightness=1
    $safePath=Join-Path $art 'Disconnected.exe';[ExecutableExporter]::Save($safePath,$safe,[EmbeddedAssets]::Read('Icon0'))
    $displayBefore=@([DisplayControl]::Enumerate())
    $processInfo=New-Object Diagnostics.ProcessStartInfo;$processInfo.FileName=$safePath;$processInfo.UseShellExecute=$false
    $reportPath=Join-Path $art 'disconnected-report.json'
    $folderBefore=@([IO.Directory]::GetFiles($art)|Where-Object {$_ -ne $reportPath}|Sort-Object)
    $processInfo.Arguments='--report "'+$reportPath+'"'
    $process=[Diagnostics.Process]::Start($processInfo)
    try{Assert ($process.WaitForExit(30000) -and $process.ExitCode -eq 0) 'Missing-display EXE failed or did not terminate'}finally{$process.Dispose()}
    $report=Get-Content -LiteralPath $reportPath -Raw|ConvertFrom-Json
    Assert (@($report.Steps).Count -eq 3) 'Missing-display EXE executed an unrelated action'
    foreach($step in @('DisplayRefreshRate','DisplayHdr','ContentBrightness')){Assert (@($report.Steps|Where-Object {$_.Name -eq $step -and $_.Status -eq 'skipped'}).Count -eq 1) ('Missing display was not safely skipped: '+$step)}
    $folderAfter=@([IO.Directory]::GetFiles($art)|Where-Object {$_ -ne $reportPath}|Sort-Object)
    Assert (@(Compare-Object $folderBefore $folderAfter).Count -eq 0) 'Diagnostic missing-display run created an unexpected artifact'
    $folderBefore=@([IO.Directory]::GetFiles($art)|Sort-Object)
    $processInfo.Arguments='';$process=[Diagnostics.Process]::Start($processInfo)
    try{Assert ($process.WaitForExit(30000) -and $process.ExitCode -eq 0) 'Repeated missing-display EXE failed or did not terminate'}finally{$process.Dispose()}
    $folderAfter=@([IO.Directory]::GetFiles($art)|Sort-Object)
    Assert (@(Compare-Object $folderBefore $folderAfter).Count -eq 0) 'Ordinary missing-display run wrote an artifact in its folder'
    $displayAfter=@([DisplayControl]::Enumerate())
    foreach($before in $displayBefore){
        $after=@($displayAfter|Where-Object {$_.MonitorId -eq $before.MonitorId})
        Assert ($after.Count -eq 1 -and $after[0].CurrentRate -eq $before.CurrentRate -and $after[0].Width -eq $before.Width -and $after[0].Height -eq $before.Height) 'Missing-display run changed an active display'
    }
    Reset-Actions
    foreach($name in @('AutonomySection','PerformanceSection','PowerSection','DisplaySection','AdditionalSection')){
        $section=$win.FindName($name)
        if($section){$section.IsExpanded=$true}
    }
    foreach($scenario in @(@{Name='normal';Width=984;Height=661;Scale=1},@{Name='minimum';Width=804;Height=521;Scale=1},@{Name='dpi150';Width=984;Height=661;Scale=1.5},@{Name='dpi200';Width=984;Height=661;Scale=2})){
        $results+=Render-Ui $scenario.Name $scenario.Width $scenario.Height $scenario.Scale
    }
    $win.FindName('SettingsScroll').ScrollToBottom();$win.FindName('RootGrid').UpdateLayout()
    $results+=Render-Ui 'additional-bottom' 984 661 1
    [pscustomobject]@{Passed=$true;Assertions=$script:assertions;ExportRoundtrip=$true;PresetsReplaceAll=$true;DisconnectedDisplaySkipped=$true;NoPhysicalSettingsApplied=$true;Screenshots=$results}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
    'PASS: '+$script:assertions+' assertions; three-state switches, deselectable power/fan choices, brightness icon and slider, display/HDR/content-brightness preview, presets, standalone export and responsive layout. Missing-display execution safely skipped all three display actions; no physical settings were applied.'
}finally{$win.Close()}
