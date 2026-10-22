$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$art=Join-Path $PSScriptRoot 'Artifacts\TaskbarPreferences'
[void][IO.Directory]::CreateDirectory($art)
$test=Join-Path $art 'TaskbarPreferenceTests.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /main:TaskbarPreferenceTests ('/out:'+$test) (Join-Path $root 'Source\StepResult.cs') (Join-Path $root 'Source\TaskbarControl.cs') (Join-Path $root 'Source\NativeTrayPreferences.cs') (Join-Path $PSScriptRoot 'TaskbarPreferenceTests.cs')
if($LASTEXITCODE -ne 0){throw 'Taskbar preference test compilation failed'}
& $test
if($LASTEXITCODE -ne 0){throw 'Taskbar preference tests failed'}
