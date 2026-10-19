param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
$script:checks=0
function Assert($condition,$message){$script:checks++;if(-not $condition){throw $message}}
$art=Join-Path $PSScriptRoot 'Artifacts\Latest';[void][IO.Directory]::CreateDirectory($art)
try{
 Assert ($null -eq $win.FindName('AutonomySection')) 'Removed autonomy category remains'
 Assert ($win.FindName('PowerSection').Content.Children[0].Name -eq 'EnergySaverSwitch') 'Saver is not first in Power'
 foreach($name in @('SleepTimeoutSwitch','ScreenTimeoutSwitch','ContentBrightnessSwitch')){Assert ($null -eq $win.FindName($name)) ('Removed/duplicate control remains: '+$name)}
 Assert ($win.FindName('PerformanceSection').Content.Children[0].Name -eq 'BatteryCareSwitch') 'Battery care is not first in ScreenXpert'
 Assert (-not $ui.Configuration.HasActions) 'Initial state has an action'
 Assert ([BatteryHealth]::Format(90500,100000) -eq 'Износ батареи: 90.500 Wh / 100.000 Wh | 9.5%') 'Battery wear formula or formatting is incorrect'
 Assert ([BatteryHealth]::Format(0,100000) -eq 'Износ батареи: нет данных') 'Missing battery report is fabricated'
 Assert ([BatteryHealth]::Format(101000,100000).EndsWith('0.0%')) 'Wear becomes negative above rated capacity'
 Assert ($win.FindName('BatteryWear').Text.StartsWith('Износ батареи: ')) 'Battery footer missing'
 $names=[ConstructorWindow].GetField('switchNames',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
 foreach($name in $names){
  $control=$win.FindName($name);$field=$name.Replace('Switch','');if($field -eq 'TaskbarRepair'){continue}
  foreach($pair in @(@($true,1),@($false,2),@($null,0))){
   $control.IsChecked=$pair[0];$expected=if($control.IsEnabled){$pair[1]}else{0}
   Assert ($ui.Configuration.$field -eq $expected) ('Switch mapping/capability failed: '+$field)
   if($expected -ne 0){$path=Join-Path $art ($field+'-'+$expected+'.exe');[ExecutableExporter]::Save($path,$ui.Configuration,[EmbeddedAssets]::Read("Icon0"));Assert ([PeConfiguration]::Read($path).$field -eq $expected) ('Export lost '+$field)}
  }
 }
 $task=$win.FindName('TaskbarRepairSwitch')
 foreach($pair in @(@($false,0),@($null,1),@($true,2))){$task.IsChecked=$pair[0];Assert ($ui.Configuration.TaskbarRepair -eq $pair[1]) 'Taskbar positions mismatch';$task.ApplyTemplate()|Out-Null;$text=$task.Template.FindName('SwitchState',$task).Text;Assert ($text -eq @('Не менять','Режим 1','Режим 2')[$pair[1]]) 'Taskbar labels mismatch'}
 $task.IsChecked=$false
 $night=$win.FindName('NightLightSwitch');$slider=$win.FindName('NightLightSlider')
 foreach($value in @($false,$null,$true)){$night.IsChecked=$value;Assert ($slider.IsEnabled -eq ($value -eq $true)) 'Night slider enablement mismatch'}
 $slider.Value=73;Assert ($ui.Configuration.NightLightStrength -eq 73 -and $win.FindName('ScriptPreview').Text.Contains('73%')) 'Night strength missing in preview'
 $night.IsChecked=$null
 $display=$win.FindName('DisplaySelector');$resolution=$win.FindName('ResolutionSelector');$rate=$win.FindName('RefreshSelector');$scale=$win.FindName('ScaleSelector');$cabc=$win.FindName('ContentBrightnessSelector')
 Assert ($rate.Items[0].ToString() -eq 'Не менять частоту обновления') 'Wrong neutral refresh caption'
 for($i=0;$i -lt $display.Items.Count;$i++){
  $display.SelectedIndex=$i;$d=$display.SelectedItem
  foreach($r in $resolution.Items){$resolution.SelectedItem=$r;Assert ($ui.Configuration.DisplayWidth -eq $r.Width -and $ui.Configuration.DisplayHeight -eq $r.Height) 'Resolution mapping failed';if($r.Width -ne 0){Assert ($ui.Configuration.DisplayId -eq $d.MonitorId) 'Resolution lost display identity';Assert (@($rate.Items|Where-Object {$_.Rate -ne 0 -and $_.Rate -notin $r.Rates}).Count -eq 0) 'Invalid refresh/resolution pair offered'}}
  $resolution.SelectedIndex=0
  foreach($s in $scale.Items){$scale.SelectedItem=$s;Assert ($ui.Configuration.DisplayScale -eq $s.Percent) 'Scale mapping failed';if($s.Percent){Assert ($ui.Configuration.DisplayId -eq $d.MonitorId) 'Scale-only action lost display identity'}}
  $scale.SelectedIndex=0
  $supported=$false;$enabled=$false;$known=[HdrControl]::TryInspect($d.MonitorId,[ref]$supported,[ref]$enabled)
  Assert ($win.FindName('HdrSwitch').IsEnabled -eq ($known -and $supported)) 'HDR capability mismatch'
  $drr=[HdrControl]::InspectDynamic($d.MonitorId,[ref]$enabled);Assert ($win.FindName('DynamicRefreshSwitch').IsEnabled -eq $drr) 'DRR capability mismatch'
  if($cabc.IsEnabled){foreach($choice in $cabc.Items){$cabc.SelectedItem=$choice;Assert ($ui.Configuration.ContentBrightness -eq $choice.Value) 'CABC selection mismatch'}}
  $cabc.SelectedIndex=0
 }
 foreach($preset in @($true,$false)){
  foreach($name in $names){$win.FindName($name).IsChecked=$true};$slider.Value=31;if($resolution.Items.Count -gt 1){$resolution.SelectedIndex=1};if($scale.Items.Count -gt 1){$scale.SelectedIndex=1};$ui.ApplyPreset($preset)
  $c=$ui.Configuration
  foreach($field in @('BatteryCare','WallpaperAudio','WallpaperPlayback','WallpaperApplication','TaskbarRepair','DisplayWidth','DisplayHeight','DisplayScale','DynamicRefresh','NightLight','ContentBrightness','PresenceWake','PresenceOnlooker','BatteryPercentage','ClockSeconds')){Assert ($c.$field -eq 0) ('Preset retained '+$field)}
  Assert ($c.EnergySaver -eq $(if($preset){2}else{1}) -and $c.FanProfile -eq $(if($preset){2}else{1})) 'Preset core changed'
 }
 foreach($tile in $win.FindName('IconGrid').Children){if([int]$tile.Tag -lt 5){Assert (-not [string]::IsNullOrEmpty($tile.ToolTip)) 'Preset icon tooltip missing'}}
 foreach($name in @('WallpaperPlaybackSwitch','WallpaperAudioSwitch')){foreach($v in @($true,$false)){$t=$win.FindName($name);$t.IsChecked=$v;$t.ApplyTemplate()|Out-Null;Assert ($t.Template.FindName('SwitchState',$t).Text -notin @('Вкл.','Выкл.')) 'Generic Wallpaper caption remains'}}
 $mixed=New-Object ModeConfiguration;$mixed.DisplayId='TEST\DISPLAY';$mixed.DisplayWidth=1920;$mixed.DisplayHeight=1080;$mixed.RefreshRate=60;$mixed.DisplayScale=150;$mixed.DynamicRefresh=1;$mixed.NightLight=1;$mixed.NightLightStrength=73;$mixed.ContentBrightness=3;$mixed.PresenceWake=1;$mixed.PresenceOnlooker=2;$mixed.TaskbarRepair=2;$mixed.WallpaperApplication=1;$mixed.BatteryPercentage=1;$mixed.ClockSeconds=2
 $path=Join-Path $art 'MixedDisplay.exe';[ExecutableExporter]::Save($path,$mixed,[EmbeddedAssets]::Read('Icon0'));$read=[PeConfiguration]::Read($path)
 foreach($field in @('DisplayId','DisplayWidth','DisplayHeight','RefreshRate','DisplayScale','DynamicRefresh','NightLight','NightLightStrength','ContentBrightness','PresenceWake','PresenceOnlooker','TaskbarRepair','WallpaperApplication','BatteryPercentage','ClockSeconds')){Assert ($mixed.$field -eq $read.$field) ('Mixed export loses '+$field)}
 foreach($f in Get-ChildItem (Join-Path $root 'Source') -File|Where-Object Extension -in '.cs','.xaml'){Assert ([IO.File]::ReadAllText($f.FullName) -notmatch '[\u2013\u2014]') ('Long dash remains: '+$f.Name)}
 'PASS: '+$script:checks+' checks; configuration, exports, capabilities, display mode pairs, presets, labels and relocated controls. No system settings applied.'
}finally{$win.Close()}


