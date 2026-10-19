param([string]$Root=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -eq 'Core'){throw 'Run with Windows PowerShell 5.1.'}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $Root 'Source\Build\RunnerTemplate.exe'))
$control=$assembly.GetType('AdditionalControl',$true)
$configuration=[Activator]::CreateInstance($assembly.GetType('ModeConfiguration',$true))
$apply=$control.GetMethod('Apply')
$empty=$apply.Invoke($null,@($configuration,$true))
if($empty.Count -ne 0){throw 'An empty configuration produced action results.'}
$configuration.SleepTimeout=1;$configuration.ScreenTimeout=2;$configuration.AdaptiveBrightness=1
$blocked=$apply.Invoke($null,@($configuration,$false))
if($blocked.Count -ne 3 -or @($blocked | Where-Object {$_.Status -ne 'skipped'}).Count -ne 0){throw 'A blocked scheme refresh was not safely skipped.'}

function Invoke-Native([string]$Name,[object[]]$Values){$control.GetMethod($Name,[Reflection.BindingFlags]'NonPublic,Static').Invoke($null,$Values)}
$activeArgs=@([IntPtr]::Zero,[IntPtr]::Zero)
$activeResult=Invoke-Native 'PowerGetActiveScheme' $activeArgs
if($activeResult -ne 0){throw ('PowerGetActiveScheme failed: '+$activeResult)}
try{$scheme=[Runtime.InteropServices.Marshal]::PtrToStructure($activeArgs[1],[type][Guid])}
finally{[void](Invoke-Native 'LocalFree' @($activeArgs[1]))}
$power=[ordered]@{}
foreach($entry in @(@('SleepIdle','SleepSubgroup','SleepIdle'),@('DisplayIdle','DisplaySubgroup','DisplayIdle'),@('AdaptiveBrightness','DisplaySubgroup','AdaptiveBrightness'))){
 $subgroup=$control.GetField($entry[1],[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
 $setting=$control.GetField($entry[2],[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
 foreach($source in @('AC','DC')){
  $readArgs=@([IntPtr]::Zero,$scheme,$subgroup,$setting,[uint32]0)
  $result=Invoke-Native ('PowerRead'+$source+'ValueIndex') $readArgs
  $power[$entry[0]+$source]=if($result -eq 0){$readArgs[4]}else{'Unavailable: '+$result}
 }
}
$screenArgs=@([uint32]0x0010,[uint32]0,[int]0,[uint32]0)
$screenRead=Invoke-Native 'GetSystemParametersInfo' $screenArgs
if(-not $screenRead){throw 'Reading screensaver state failed.'}

$presence=$control.GetNestedType('Presence',[Reflection.BindingFlags]'NonPublic')
function Invoke-VTable([IntPtr]$Pointer,[int]$Slot,[string]$Delegate,[object[]]$Values){
 $address=[Runtime.InteropServices.Marshal]::ReadIntPtr([Runtime.InteropServices.Marshal]::ReadIntPtr($Pointer),$Slot*[IntPtr]::Size)
 $delegateType=$presence.GetNestedType($Delegate,[Reflection.BindingFlags]'NonPublic')
 [Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer($address,$delegateType).DynamicInvoke($Values)
}
$factory=[IntPtr]::Zero;$settings=[IntPtr]::Zero;$features=[IntPtr]::Zero;$sensor=[IntPtr]::Zero;$className=[IntPtr]::Zero
$initialized=-1;$presenceResult='Unavailable'
try{
 $initialized=Invoke-Native 'RoInitialize' @([uint32]0)
 $text='Windows.Devices.Sensors.HumanPresenceSettings'
 $textArgs=@($text,$text.Length,[IntPtr]::Zero)
 $hr=Invoke-Native 'WindowsCreateString' $textArgs
 [Runtime.InteropServices.Marshal]::ThrowExceptionForHR($hr);$className=$textArgs[2]
 $factoryArgs=@($className,[Guid]'7f343202-e010-52c4-af0c-04a8f1e033da',[IntPtr]::Zero)
 $hr=Invoke-Native 'RoGetActivationFactory' $factoryArgs
 [Runtime.InteropServices.Marshal]::ThrowExceptionForHR($hr);$factory=$factoryArgs[2]
 $settingsArgs=@($factory,[IntPtr]::Zero)
 $hr=Invoke-VTable $factory 7 'GetObject' $settingsArgs
 [Runtime.InteropServices.Marshal]::ThrowExceptionForHR($hr);$settings=$settingsArgs[1]
 $sensorArgs=@($settings,[IntPtr]::Zero)
 $hr=Invoke-VTable $settings 6 'GetObject' $sensorArgs
 [Runtime.InteropServices.Marshal]::ThrowExceptionForHR($hr);$sensor=$sensorArgs[1]
 if($sensor -ne [IntPtr]::Zero -and (Invoke-Native 'WindowsGetStringLen' @($sensor)) -gt 0){
  $featureArgs=@($factory,$sensor,[IntPtr]::Zero)
  $hr=Invoke-VTable $factory 11 'GetFeatures' $featureArgs
  [Runtime.InteropServices.Marshal]::ThrowExceptionForHR($hr);$features=$featureArgs[2]
  $enabledArgs=@($settings,[byte]0);$hr=Invoke-VTable $settings 12 'GetBoolean' $enabledArgs
  [Runtime.InteropServices.Marshal]::ThrowExceptionForHR($hr)
  $supportArgs=@($features,[byte]0);$hr=Invoke-VTable $features 9 'GetBoolean' $supportArgs
  [Runtime.InteropServices.Marshal]::ThrowExceptionForHR($hr)
  $presenceResult=[ordered]@{LockOnLeaveEnabled=[bool]$enabledArgs[1];LockOnLeaveSupported=[bool]$supportArgs[1]}
 }else{$presenceResult='No configured Windows human-presence sensor'}
}catch{$presenceResult='Unavailable: '+$_.Exception.GetType().Name}
finally{
 foreach($pointer in @($features,$settings,$factory)){if($pointer -ne [IntPtr]::Zero){[void][Runtime.InteropServices.Marshal]::Release($pointer)}}
 if($sensor -ne [IntPtr]::Zero){[void](Invoke-Native 'WindowsDeleteString' @($sensor))}
 if($className -ne [IntPtr]::Zero){[void](Invoke-Native 'WindowsDeleteString' @($className))}
 if($initialized -ge 0){[void](Invoke-Native 'RoUninitialize' @())}
}
[ordered]@{Result='PASS';EmptyConfiguration='No actions';BlockedSchemeRefresh='Three settings safely skipped';Power=$power;ScreenSaverActive=[bool]$screenArgs[2];Presence=$presenceResult;SystemPreferencesChanged=$false}|ConvertTo-Json -Depth 5
