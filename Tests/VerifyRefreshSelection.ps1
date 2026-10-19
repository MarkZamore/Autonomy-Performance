param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
[void][Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
$script:checks=0
$art=Join-Path $PSScriptRoot 'Artifacts\RefreshSelection';[void][IO.Directory]::CreateDirectory($art)
function Assert($condition,$message){$script:checks++;if(-not $condition){throw $message}}
function Fake-Display([int]$number,[uint32]$maximum){
 $d=New-Object DisplayInfo;$d.MonitorId='DISPLAY\REFRESH_TEST\'+$number;$d.DeviceName='\\.\DISPLAY'+$number;$d.Label='Экран '+$number;$d.Width=1920;$d.Height=1080;$d.CurrentRate=60
 $d.Rates.Add(60);$d.Rates.Add($maximum)
 foreach($spec in @(@(1920,1080),@(2560,1440),@(3840,2160))){$r=New-Object DisplayResolution;$r.Width=$spec[0];$r.Height=$spec[1];$r.Rates.Add(60);if($r.Width -lt 3840){$r.Rates.Add($maximum)};$d.Resolutions.Add($r)}
 return $d
}
function Set-Resolution([uint32]$width){$choice=@($resolution.Items|Where-Object Width -eq $width);Assert ($choice.Count -eq 1) 'Synthetic resolution is unavailable';$resolution.SelectedItem=$choice[0]}
function Set-Rate([uint32]$value){$choice=@($rate.Items|Where-Object Rate -eq $value);Assert ($choice.Count -eq 1) 'Synthetic refresh choice is unavailable';$rate.SelectedItem=$choice[0]}
function Assert-DescendingChoices {
 Assert ($resolution.Items[0].Width -eq 0 -and $rate.Items[0].Rate -eq 0) 'Neutral resolution and refresh choices are not first'
 $modes=@($resolution.Items|Where-Object Width -gt 0);$sorted=@($modes|Sort-Object @{Expression='Width';Descending=$true},@{Expression='Height';Descending=$true})
 Assert ((($modes|ForEach-Object {$_.Width.ToString()+'x'+$_.Height}) -join ',') -eq (($sorted|ForEach-Object {$_.Width.ToString()+'x'+$_.Height}) -join ',')) 'UI resolutions are not ordered largest first'
 $frequencies=@($rate.Items|Where-Object Rate -gt 0|ForEach-Object Rate)
 Assert (($frequencies -join ',') -eq (($frequencies|Sort-Object -Descending) -join ',')) 'UI refresh rates are not ordered highest first'
}
function Display-Action([string]$id){return @($ui.Configuration.Displays|Where-Object DisplayId -eq $id)[0]}
function Native-Modes {return @([DisplayControl]::Enumerate()|Sort-Object MonitorId|ForEach-Object {[pscustomobject]@{Id=$_.MonitorId;Width=$_.Width;Height=$_.Height;Rate=$_.CurrentRate}})|ConvertTo-Json -Compress}
try{
 $before=Native-Modes
 $first=Fake-Display 1 144;$second=Fake-Display 2 120
 $displays=New-Object 'Collections.Generic.List[DisplayInfo]';$displays.Add($first);$displays.Add($second)
 $arguments=New-Object object[] 1;$arguments[0]=$displays.PSObject.BaseObject
 [void][ConstructorWindow].GetMethod('ApplyDisplayList',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($ui,$arguments)
 $selector=$win.FindName('DisplaySelector');$resolution=$win.FindName('ResolutionSelector');$rate=$win.FindName('RefreshSelector')
 Assert-DescendingChoices
 Assert ((($first.Resolutions|ForEach-Object Width) -join ',') -eq '1920,2560,3840' -and ($first.Rates -join ',') -eq '60,144') 'Ordering the UI mutated the native display mode collections'
 Set-Resolution 1920;Set-Rate 144
 Assert-DescendingChoices
 Set-Resolution 2560
 Assert ($rate.SelectedItem.Rate -eq 144 -and (Display-Action $first.MonitorId).RefreshRate -eq 144) 'A refresh rate offered by the new resolution was unnecessarily cleared'
 Assert ((Display-Action $first.MonitorId).DisplayWidth -eq 2560) 'Preserving refresh prevented the resolution update'
 Set-Resolution 3840
 Assert-DescendingChoices
 Assert ($rate.SelectedItem.Rate -eq 0 -and (Display-Action $first.MonitorId).RefreshRate -eq 0) 'A refresh rate unsupported by the new resolution was retained'
 Assert ($rate.Items.Count -eq 2 -and (Display-Action $first.MonitorId).DisplayWidth -eq 3840) 'An unsupported frequency was offered or the resolution action was lost'
 Set-Resolution 1920
 Assert ($rate.SelectedItem.Rate -eq 0) 'Changing resolution invented a previously cleared frequency action'
 Set-Rate 60;Set-Resolution 2560
 Assert ($rate.SelectedItem.Rate -eq 60) 'The common 60 Hz choice was not preserved'
 Set-Resolution 0
 Assert ($rate.SelectedItem.Rate -eq 60) 'Returning to the current resolution cleared a supported frequency'
 Set-Rate 144
 $selector.SelectedItem=$second
 Assert-DescendingChoices
 Assert ($rate.SelectedItem.Rate -eq 0) 'A new display inherited another display frequency'
 Set-Resolution 1920;Set-Rate 120
 $selector.SelectedItem=$first
 Assert ($rate.SelectedItem.Rate -eq 144 -and $resolution.SelectedItem.Width -eq 0) 'Returning to the first display lost its independent frequency'
 $selector.SelectedItem=$second
 Assert ($rate.SelectedItem.Rate -eq 120) 'Returning to the second display inherited the first frequency'
 Set-Resolution 2560
 Assert ($rate.SelectedItem.Rate -eq 120 -and (Display-Action $first.MonitorId).RefreshRate -eq 144) 'Preserving a second display mode changed the first display'
 Set-Resolution 3840
 Assert ($rate.SelectedItem.Rate -eq 0 -and (Display-Action $second.MonitorId).RefreshRate -eq 0 -and (Display-Action $first.MonitorId).RefreshRate -eq 144) 'Clearing an unsupported second-display frequency affected another target'
 $after=Native-Modes;Assert ($before -eq $after) 'Editing simulated display modes changed actual Windows display modes'
 [pscustomobject]@{Passed=$true;Checks=$script:checks;SupportedFrequencyPreserved=$true;UnsupportedFrequencyCleared=$true;NeutralNotAutoSelected=$true;DisplayTargetsIndependent=$true;ActualDisplayModesUnchanged=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
 'PASS: '+$script:checks+' synthetic resolution/refresh checks; supported Hz preserved, unavailable Hz cleared, display targets independent. No actual display mode was changed.'
}finally{$win.Close()}
