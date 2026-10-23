$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$art=Join-Path $PSScriptRoot 'Artifacts\EnergySaverPreferences'
[void][IO.Directory]::CreateDirectory($art)
$test=Join-Path $art 'EnergySaverPreferenceTests.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /r:System.Management.dll /r:System.Web.Extensions.dll /main:EnergySaverPreferenceTests ('/out:'+$test) (Join-Path $root 'Source\StepResult.cs') (Join-Path $root 'Source\SystemActions.cs') (Join-Path $root 'Source\BrokerWindowsControl.cs') (Join-Path $root 'Source\NativeWindowsPreferences.cs') (Join-Path $root 'Source\WindowsPreferenceCatalog.cs') (Join-Path $PSScriptRoot 'EnergySaverPreferenceTests.cs')
if($LASTEXITCODE -ne 0){throw 'Broker preference test compilation failed'}
& $test
if($LASTEXITCODE -ne 0){throw 'Broker preference tests failed'}
