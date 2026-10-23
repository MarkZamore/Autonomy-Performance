$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$art=Join-Path $PSScriptRoot 'Artifacts\BrokerPreferences'
[void][IO.Directory]::CreateDirectory($art)
$test=Join-Path $art 'BrokerPreferenceTests.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /main:BrokerPreferenceTests ('/out:'+$test) (Join-Path $root 'Source\StepResult.cs') (Join-Path $root 'Source\BrokerWindowsControl.cs') (Join-Path $root 'Source\NativeWindowsPreferences.cs') (Join-Path $root 'Source\WindowsPreferenceCatalog.cs') (Join-Path $PSScriptRoot 'BrokerPreferenceTests.cs')
if($LASTEXITCODE -ne 0){throw 'Broker preference test compilation failed'}
& $test
if($LASTEXITCODE -ne 0){throw 'Broker preference tests failed'}
