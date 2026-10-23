$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$art=Join-Path $PSScriptRoot 'Artifacts\ArmouryCrateControl'
[void][IO.Directory]::CreateDirectory($art)
$test=Join-Path $art 'ArmouryCrateControlTests.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /main:ArmouryCrateControlTests ('/out:'+$test) (Join-Path $root 'Source\StepResult.cs') (Join-Path $root 'Source\ArmouryCrateControl.cs') (Join-Path $root 'Source\ProgramAvailability.cs') (Join-Path $root 'Source\WallpaperControl.cs') (Join-Path $PSScriptRoot 'ArmouryCrateControlTests.cs')
if($LASTEXITCODE -ne 0){throw 'Armoury Crate test compilation failed'}
& $test
if($LASTEXITCODE -ne 0){throw 'Armoury Crate control tests failed'}
