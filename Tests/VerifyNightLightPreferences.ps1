$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$art=Join-Path $PSScriptRoot 'Artifacts\NightLightPreferences'
[void][IO.Directory]::CreateDirectory($art)
$test=Join-Path $art 'NightLightPreferenceTests.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /main:NightLightPreferenceTests ('/out:'+$test) (Join-Path $root 'Source\StepResult.cs') (Join-Path $root 'Source\NightLightControl.cs') (Join-Path $root 'Source\NativeTrayPreferences.cs') (Join-Path $root 'Source\NativeWindowsPreferences.cs') (Join-Path $root 'Source\WindowsPreferenceCatalog.cs') (Join-Path $PSScriptRoot 'NightLightPreferenceTests.cs')
if($LASTEXITCODE -ne 0){throw 'Night Light preference test compilation failed'}
& $test
if($LASTEXITCODE -ne 0){throw 'Night Light preference tests failed'}
