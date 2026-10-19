param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Web.Extensions
[void][Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
$script:checks=0;$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$art=Join-Path $PSScriptRoot 'Artifacts\BrightnessEditor';[void][IO.Directory]::CreateDirectory($art)
function Assert($condition,$message){$script:checks++;if(-not $condition){throw $message}}
function Read-Brightness {return @(Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorBrightness -Filter 'Active=TRUE' -ErrorAction SilentlyContinue|Sort-Object InstanceName|ForEach-Object {[pscustomobject]@{InstanceName=$_.InstanceName;Value=[int]$_.CurrentBrightness}})}
function Actions {return @($ui.Configuration.Displays|Where-Object {$_.Brightness -ge 0})}
try{
 $before=Read-Brightness
 $selector=$win.FindName('DisplaySelector');$toggle=$win.FindName('BrightnessToggleButton');$slider=$win.FindName('BrightnessSlider')
 $supported=@($selector.Items|Where-Object {[DisplayBrightnessControl]::Available($_.MonitorId)})
 $targetField=[ConstructorWindow].GetField('brightnessDisplayId',$flags)
 Assert ($null -ne $targetField) 'The common brightness editor has no stable target identity'
 foreach($display in @($selector.Items)){
  $selector.SelectedItem=$display
  Assert ($toggle.IsEnabled -eq ($supported.Count -gt 0)) 'Brightness availability depends on the selected display instead of panel support'
 }
 if($supported.Count -eq 0){
  Assert (-not $slider.IsEnabled -and @(Actions).Count -eq 0) 'Unsupported brightness is offered as an action'
 }else{
  $toggle.IsChecked=$true;$slider.Value=37
  $actions=@(Actions);$target=$targetField.GetValue($ui)
  Assert ($actions.Count -eq 1 -and $actions[0].DisplayId -eq $target -and $actions[0].Brightness -eq 37) 'Common brightness does not produce exactly one stable panel action'
  Assert (@($supported|Where-Object MonitorId -eq $target).Count -eq 1) 'The brightness target is not an independently supported panel'
  Assert ($ui.PreviewText -eq 'Яркость 37%') 'Common brightness preview exposes a display number or duplicate action'
  foreach($display in @($selector.Items)){
   $selector.SelectedItem=$display
   Assert ($toggle.IsEnabled -and $toggle.IsChecked -eq $true -and $slider.IsEnabled -and $slider.Value -eq 37) 'Changing the selected screen resets or disables common brightness'
   Assert ($targetField.GetValue($ui) -eq $target -and @(Actions).Count -eq 1 -and (Actions)[0].DisplayId -eq $target) 'Changing the selected screen retargets or duplicates brightness'
  }
  $slider.Value=62;$actions=@(Actions)
  Assert ($actions.Count -eq 1 -and $actions[0].DisplayId -eq $target -and $actions[0].Brightness -eq 62 -and $ui.PreviewText -eq 'Яркость 62%') 'Editing brightness from another display changes the wrong target'
  $path=Join-Path $art 'CommonBrightness.exe';[ExecutableExporter]::Save($path,$ui.Configuration,[EmbeddedAssets]::Read('Icon0'));$loaded=[PeConfiguration]::Read($path)
  Assert ($loaded.Brightness -eq -1 -and $loaded.Displays.Count -eq 1 -and $loaded.Displays[0].DisplayId -eq $target -and $loaded.Displays[0].Brightness -eq 62) 'Export loses or converts the independently targeted brightness action'
  # Resource inspection only; the exported brightness executable is never launched.
  $ui.RemovePreviewAction(0)
  Assert (@(Actions).Count -eq 0 -and -not $toggle.IsChecked -and -not $slider.IsEnabled -and $ui.PreviewText -eq '') 'Removing common brightness does not update its editor'
  $toggle.IsChecked=$true;$slider.Value=41;$ui.ResetScript()
  Assert (@(Actions).Count -eq 0 -and -not $toggle.IsChecked -and -not $slider.IsEnabled) 'Reset retains common brightness'
 }
 $after=Read-Brightness
 Assert (($before|ConvertTo-Json -Compress) -eq ($after|ConvertTo-Json -Compress)) 'Editing future-script brightness applied an actual hardware value'
 foreach($mode in @(1,2)){$win.FindName('TaskbarRepairSwitch').IsChecked=if($mode -eq 1){$null}else{$true};Assert ($ui.PreviewText -eq ('Исправление панели задач - Режим '+$mode)) 'Taskbar repair preview is not the concise selected mode'}
 [pscustomobject]@{Passed=$true;Checks=$script:checks;Displays=$selector.Items.Count;SupportedPanels=$supported.Count;Target=$(if($target){$target}else{$null});HardwareValuesUnchanged=$true;ExportedExecutableRun=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
 'PASS: '+$script:checks+' common-brightness checks; stable target independent of display selection, one preview/export action, reset and concise taskbar modes. Hardware brightness was not changed.'
}finally{$win.Close()}
