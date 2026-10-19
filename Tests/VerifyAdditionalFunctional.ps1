param([string]$Root=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -eq 'Core'){throw 'Run with Windows PowerShell 5.1.'}
$folder=Join-Path $Root 'Tests\Artifacts\AdditionalFunctional'
[void](New-Item -ItemType Directory -Path $folder -Force)
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $Root 'Source\Build\RunnerTemplate.exe'))
$control=$assembly.GetType('AdditionalControl',$true)
$configurationType=$assembly.GetType('ModeConfiguration',$true)
$windows=$assembly.GetType('WindowsControl',$true)
$apply=$control.GetMethod('Apply')
$rows=New-Object 'System.Collections.Generic.List[object]'
$restorationErrors=New-Object 'System.Collections.Generic.List[string]'

function Native([string]$Name,[object[]]$Values){$control.GetMethod($Name,[Reflection.BindingFlags]'NonPublic,Static').Invoke($null,$Values)}
function Require([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function With-Key([Microsoft.Win32.RegistryKey]$Hive,[string]$Path,[bool]$Writable,[scriptblock]$Action){
 $key=$Hive.OpenSubKey($Path,$Writable)
 try{& $Action $key}finally{if($null -ne $key){$key.Dispose()}}
}
function Selected([string]$Field,[int]$Value){
 $config=[Activator]::CreateInstance($configurationType);$configurationType.GetField($Field).SetValue($config,$Value)
 foreach($step in $apply.Invoke($null,@($config,$true))){$step}
}
function PowerMode([bool]$AC){$windows.GetMethod('ReadPowerMode').Invoke($null,@($AC))}
function Restore-Overlays{
 foreach($source in @('AC','DC')){
  $isAC=$source -eq 'AC';$original=$before.Overlays[$source]
  if((PowerMode $isAC) -ne $original){
   $result=$windows.GetMethod('SetPowerMode').Invoke($null,@($isAC,[Guid]$original))
   Require ($result.Status -eq 'ok') ('Cannot restore '+$source+' power mode.')
  }
  Require ((PowerMode $isAC) -eq $original) ($source+' power mode restore verification failed.')
 }
}
function Read-Power([string]$Name,[string]$Source){
 $entry=$powerEntries[$Name]
 $arguments=@([IntPtr]::Zero,$before.Scheme,$entry.Subgroup,$entry.Setting,[uint32]0)
 $result=Native ('PowerRead'+$Source+'ValueIndex') $arguments
 Require ($result -eq 0) ('Cannot read '+$Name+$Source+': '+$result)
 [uint32]$arguments[4]
}
function Restore-Power([string]$Name){
 $entry=$powerEntries[$Name]
 foreach($source in @('AC','DC')){
  $original=[uint32]$before.Power[$Name][$source]
  if((Read-Power $Name $source) -ne $original){
   $arguments=@([IntPtr]::Zero,$before.Scheme,$entry.Subgroup,$entry.Setting,$original)
   $result=Native ('PowerWrite'+$source+'ValueIndex') $arguments
   Require ($result -eq 0) ('Cannot restore '+$Name+$source+': '+$result)
  }
 }
 $arguments=@([IntPtr]::Zero,$before.Scheme)
 $result=Native 'PowerSetActiveScheme' $arguments
 Require ($result -eq 0) ('Cannot reactivate original scheme: '+$result)
 foreach($source in @('AC','DC')){Require ((Read-Power $Name $source) -eq [uint32]$before.Power[$Name][$source]) ($Name+$source+' restore verification failed.')}
 Restore-Overlays
}
function Read-Presence{
 $settings=[Windows.Devices.Sensors.HumanPresenceSettings,Windows.Devices.Sensors,ContentType=WindowsRuntime]::GetCurrentSettings()
 [ordered]@{PresenceSleep=[bool]$settings.IsLockOnLeaveEnabled;PresenceDim=[bool]$settings.IsAdaptiveDimmingEnabled}
}
function Restore-Presence([string]$Field){
 if((Read-Presence)[$Field] -ne $before.Presence[$Field]){
  $value=if($before.Presence[$Field]){1}else{2}
  $result=Selected $Field $value
  Require (@($result|Where-Object {$_.Status -ne 'ok'}).Count -eq 0) ('Cannot restore '+$Field)
 }
 Require ((Read-Presence)[$Field] -eq $before.Presence[$Field]) ($Field+' restore verification failed.')
}
function Read-ScreenSaver{
 $arguments=@([uint32]0x0010,[uint32]0,[int]0,[uint32]0)
 Require ([bool](Native 'GetSystemParametersInfo' $arguments)) 'Cannot read screensaver state.'
 [bool]$arguments[2]
}
function Restore-ScreenSaver{
 if((Read-ScreenSaver) -ne $before.ScreenSaver.Active){
  $arguments=@([uint32]0x0011,[uint32]$(if($before.ScreenSaver.Active){1}else{0}),[IntPtr]::Zero,[uint32]3)
  Require ([bool](Native 'SystemParametersInfo' $arguments)) 'Cannot restore screensaver state.'
 }
 With-Key ([Microsoft.Win32.Registry]::CurrentUser) 'Control Panel\Desktop' $true {param($desktop)
  if($null -eq $before.ScreenSaver.RegistryValue){$desktop.DeleteValue('ScreenSaveActive',$false)}
  elseif($desktop.GetValue('ScreenSaveActive',$null) -ne $before.ScreenSaver.RegistryValue){$desktop.SetValue('ScreenSaveActive',$before.ScreenSaver.RegistryValue,$before.ScreenSaver.RegistryKind)}
 }
 Require ((Read-ScreenSaver) -eq $before.ScreenSaver.Active) 'Screensaver restore verification failed.'
}
function Read-Usb{With-Key ([Microsoft.Win32.Registry]::LocalMachine) $usbPath $false {param($key) $key.GetValue('AttemptRecoveryFromUsbPowerDrain',$null)}}
function Restore-Usb{
 if((Read-Usb) -ne $before.Usb.Value){
  With-Key ([Microsoft.Win32.Registry]::LocalMachine) $usbPath $true {param($key) $key.SetValue('AttemptRecoveryFromUsbPowerDrain',$before.Usb.Value,$before.Usb.Kind)}
 }
 Require ((Read-Usb) -eq $before.Usb.Value) 'USB preference restore verification failed.'
}
function Brightness-Snapshot{
 $list=@()
 try{foreach($item in Get-WmiObject -Namespace root\wmi -Class WmiMonitorBrightness -Filter 'Active=TRUE'){$list+=([ordered]@{InstanceName=$item.InstanceName;Brightness=[int]$item.CurrentBrightness})}}catch{}
 @($list)
}
function Restore-BrightnessIfChanged{
 $current=Brightness-Snapshot
 $brightnessType=$assembly.GetType('DisplayBrightnessState',$true)
 $listType=[Type]::GetType('System.Collections.Generic.List`1').MakeGenericType($brightnessType)
 $changed=[Activator]::CreateInstance($listType)
 foreach($original in $before.Brightness){
  $actual=@($current|Where-Object {$_.InstanceName -eq $original.InstanceName})
  if($actual.Count -eq 1 -and $actual[0].Brightness -ne $original.Brightness){
   $state=[Activator]::CreateInstance($brightnessType);$state.InstanceName=$original.InstanceName;$state.CurrentBrightness=[byte]$original.Brightness
   $changed.Add($state)
  }
 }
 if($changed.Count -gt 0){
  $results=foreach($step in $windows.GetMethod('SetBrightness',[Reflection.BindingFlags]'NonPublic,Static').Invoke($null,@($changed,[byte]100))){$step}
  Require (@($results|Where-Object {$_.Status -ne 'ok'}).Count -eq 0) 'Collateral brightness change could not be restored.'
 }
}

# A process-local, in-memory P/Invoke prevents inherited idle time from causing
# an immediate sleep or display-off when the three-minute policies are tested.
$name=New-Object Reflection.AssemblyName 'AdditionalFunctionalNative'
$dynamic=[AppDomain]::CurrentDomain.DefineDynamicAssembly($name,[Reflection.Emit.AssemblyBuilderAccess]::Run)
$module=$dynamic.DefineDynamicModule('Native')
$type=$module.DefineType('FunctionalNative',[Reflection.TypeAttributes]'Public,Abstract,Sealed')
$method=$type.DefinePInvokeMethod('SetThreadExecutionState','kernel32.dll','SetThreadExecutionState',[Reflection.MethodAttributes]'Public,Static,PinvokeImpl',[Reflection.CallingConventions]::Standard,[uint32],[Type[]]@([uint32]),[Runtime.InteropServices.CallingConvention]::Winapi,[Runtime.InteropServices.CharSet]::Auto)
$method.SetImplementationFlags([Reflection.MethodImplAttributes]::PreserveSig)
$keepAwake=$type.CreateType().GetMethod('SetThreadExecutionState')
$previousExecutionState=[uint32]$keepAwake.Invoke($null,@([uint32]2147483651))
Require ($previousExecutionState -ne 0) 'Cannot prevent sleep safely; no settings were changed.'
$powerEntries=[ordered]@{}
foreach($entry in @(@('SleepTimeout','SleepSubgroup','SleepIdle'),@('ScreenTimeout','DisplaySubgroup','DisplayIdle'),@('AdaptiveBrightness','DisplaySubgroup','AdaptiveBrightness'))){
 $powerEntries[$entry[0]]=[ordered]@{Subgroup=$control.GetField($entry[1],[Reflection.BindingFlags]'NonPublic,Static').GetValue($null);Setting=$control.GetField($entry[2],[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)}
}
$usbPath='SYSTEM\CurrentControlSet\Control\USB\AutomaticSurpriseRemoval'
$before=$null;$snapshotted=$false;$failure=$null
try{
 $activeArgs=@([IntPtr]::Zero,[IntPtr]::Zero);$active=Native 'PowerGetActiveScheme' $activeArgs
 Require ($active -eq 0) 'Cannot snapshot active power scheme.'
 try{$scheme=[Runtime.InteropServices.Marshal]::PtrToStructure($activeArgs[1],[type][Guid])}finally{[void](Native 'LocalFree' @($activeArgs[1]))}
 $before=[ordered]@{Scheme=$scheme;Overlays=[ordered]@{AC=(PowerMode $true);DC=(PowerMode $false)};Power=[ordered]@{};Presence=(Read-Presence);ScreenSaver=[ordered]@{Active=(Read-ScreenSaver)};Usb=[ordered]@{};Brightness=(Brightness-Snapshot)}
 foreach($field in $powerEntries.Keys){$before.Power[$field]=[ordered]@{AC=(Read-Power $field 'AC');DC=(Read-Power $field 'DC')}}
 $scr=With-Key ([Microsoft.Win32.Registry]::CurrentUser) 'Control Panel\Desktop' $false {param($desktop)
  $before.ScreenSaver.RegistryValue=$desktop.GetValue('ScreenSaveActive',$null)
  $before.ScreenSaver.RegistryKind=if($null -ne $before.ScreenSaver.RegistryValue){$desktop.GetValueKind('ScreenSaveActive')}else{[Microsoft.Win32.RegistryValueKind]::String}
  $desktop.GetValue('SCRNSAVE.EXE',$null)
 }
 $validScreenSaver=$false
 if($scr -is [string] -and -not [string]::IsNullOrWhiteSpace($scr)){
  $scr=[Environment]::ExpandEnvironmentVariables($scr).Trim('"')
  if(-not [IO.Path]::IsPathRooted($scr)){$scr=Join-Path ([Environment]::SystemDirectory) $scr}
  $validScreenSaver=[IO.File]::Exists($scr) -and [IO.Path]::GetExtension($scr) -ieq '.scr'
 }
 $before.ScreenSaver.ValidConfiguredFile=$validScreenSaver
 With-Key ([Microsoft.Win32.Registry]::LocalMachine) $usbPath $false {param($key)
  $before.Usb.Value=$key.GetValue('AttemptRecoveryFromUsbPowerDrain',$null)
  $before.Usb.Kind=$key.GetValueKind('AttemptRecoveryFromUsbPowerDrain')
 }
 Require ($before.Usb.Value -is [int] -and $before.Usb.Value -ge 0 -and $before.Usb.Value -le 1) 'Cannot safely snapshot USB preference.'
 $snapshotted=$true
 [IO.File]::WriteAllText((Join-Path $folder 'before.json'),($before|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($true))
 foreach($field in $powerEntries.Keys){
  try{
   foreach($mode in @(1,1,2,2)){
    $expected=if($mode -eq 1){if($field -eq 'AdaptiveBrightness'){1}else{180}}else{0}
    $result=Selected $field $mode
    $actual=[ordered]@{AC=(Read-Power $field 'AC');DC=(Read-Power $field 'DC')}
    $errors=@($result|Where-Object {$_.Status -eq 'error'})
    Require ($errors.Count -eq 0) ($field+' returned an error: '+($result|ConvertTo-Json -Compress))
    $skipped=@($result|Where-Object {$_.Status -eq 'skipped'}).Count -gt 0
    if(-not $skipped){Require ($actual.AC -eq $expected -and $actual.DC -eq $expected) ($field+' did not reach selected AC/DC values.')}
    $rows.Add([ordered]@{Action=$field;Mode=$mode;Expected=$expected;Actual=$actual;Status=$(if($skipped){'Skipped'}else{'PASS'});Steps=$result})
    Restore-Overlays
   }
  }finally{Restore-Power $field}
 }
 foreach($field in @('PresenceSleep','PresenceDim')){
  try{
   $desired=-not $before.Presence[$field];$mode=if($desired){1}else{2}
   $result=Selected $field $mode
   $actual=(Read-Presence)[$field]
   $skipped=@($result|Where-Object {$_.Status -eq 'skipped'}).Count -gt 0
   Require (@($result|Where-Object {$_.Status -eq 'error'}).Count -eq 0) ($field+' returned an unexpected error.')
   if($skipped){Require ($actual -eq $before.Presence[$field]) ($field+' changed despite reporting skipped.')}
   else{Require ($actual -eq $desired) ($field+' did not reach its selected state.')}
   $rows.Add([ordered]@{Action=$field;Requested=$desired;Actual=$actual;Status=$(if($skipped){'Skipped'}else{'PASS'});Steps=$result})
  }finally{Restore-Presence $field}
 }
 try{
  $canWrite=$false
  try{$canWrite=With-Key ([Microsoft.Win32.Registry]::LocalMachine) $usbPath $true {param($key) $null -ne $key}}catch [UnauthorizedAccessException]{}
  if($canWrite){
   $desired=1-[int]$before.Usb.Value;$mode=if($desired -eq 1){1}else{2}
   $result=Selected 'UsbPower' $mode
   Require (@($result|Where-Object {$_.Status -ne 'ok'}).Count -eq 0) 'USB preference could not be applied.'
   $actual=Read-Usb;Require ($actual -eq $desired) 'USB preference did not verify.'
   $rows.Add([ordered]@{Action='UsbPower';Requested=$desired;Actual=$actual;Status='PASS';Steps=$result})
  }else{$rows.Add([ordered]@{Action='UsbPower';Status='Skipped';Reason='Existing key does not grant write access.'})}
 }finally{Restore-Usb}
 try{
  $result=Selected 'ScreenSaver' 2
  Require (@($result|Where-Object {$_.Status -ne 'ok'}).Count -eq 0) 'Screensaver off failed.'
  Require (-not (Read-ScreenSaver)) 'Screensaver did not turn off.'
  $rows.Add([ordered]@{Action='ScreenSaver';Requested=$false;Actual=(Read-ScreenSaver);Status='PASS';Steps=$result})
  if($validScreenSaver){
   $result=Selected 'ScreenSaver' 1
   Require (@($result|Where-Object {$_.Status -ne 'ok'}).Count -eq 0) 'Configured screensaver on failed.'
   Require (Read-ScreenSaver) 'Configured screensaver did not turn on.'
   $rows.Add([ordered]@{Action='ScreenSaver';Requested=$true;Actual=(Read-ScreenSaver);Status='PASS';Steps=$result})
  }else{$rows.Add([ordered]@{Action='ScreenSaver';Requested=$true;Status='Skipped';Reason='No existing valid configured .scr file.'})}
 }finally{Restore-ScreenSaver}
}catch{$failure=$_.Exception.Message}
finally{
 if($snapshotted){
  foreach($field in $powerEntries.Keys){try{Restore-Power $field}catch{$restorationErrors.Add($_.Exception.Message)}}
  foreach($field in @('PresenceSleep','PresenceDim')){try{Restore-Presence $field}catch{$restorationErrors.Add($_.Exception.Message)}}
  try{Restore-Usb}catch{$restorationErrors.Add($_.Exception.Message)}
  try{Restore-ScreenSaver}catch{$restorationErrors.Add($_.Exception.Message)}
  try{Restore-Overlays}catch{$restorationErrors.Add($_.Exception.Message)}
  try{Restore-BrightnessIfChanged}catch{$restorationErrors.Add($_.Exception.Message)}
 }
 [void]$keepAwake.Invoke($null,@([uint32]$previousExecutionState))
}
$after=[ordered]@{Power=[ordered]@{};Overlays=[ordered]@{};Presence=$null;ScreenSaver=$null;Usb=$null;Brightness=(Brightness-Snapshot)}
if($snapshotted){
 foreach($field in $powerEntries.Keys){$after.Power[$field]=[ordered]@{AC=(Read-Power $field 'AC');DC=(Read-Power $field 'DC')}}
 $after.Overlays.AC=(PowerMode $true);$after.Overlays.DC=(PowerMode $false);$after.Presence=(Read-Presence);$after.ScreenSaver=(Read-ScreenSaver);$after.Usb=(Read-Usb)
}
$brightnessUnchanged=($before.Brightness|ConvertTo-Json -Compress) -eq ($after.Brightness|ConvertTo-Json -Compress)
$report=[ordered]@{Result=$(if($failure -or $restorationErrors.Count -gt 0){'FAIL'}else{'PASS'});Failure=$failure;RestorationErrors=$restorationErrors.ToArray();Before=$before;After=$after;BrightnessUnchanged=$brightnessUnchanged;Tests=$rows.ToArray()}
[IO.File]::WriteAllText((Join-Path $folder 'result.json'),($report|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($true))
$rows|ForEach-Object {[pscustomobject]$_}|Select-Object Action,Mode,Requested,Expected,Actual,Status|Format-Table -AutoSize
[pscustomobject]@{Result=$report.Result;Failure=$failure;RestorationErrors=$restorationErrors.Count;BrightnessUnchanged=$brightnessUnchanged;Report=(Join-Path $folder 'result.json')}|Format-List
if($report.Result -eq 'FAIL'){throw 'Additional functional verification failed; see result.json.'}
