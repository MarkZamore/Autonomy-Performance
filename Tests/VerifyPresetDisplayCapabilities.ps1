$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$art=Join-Path $PSScriptRoot 'Artifacts\PresetNative'
[void][IO.Directory]::CreateDirectory($art)
$test=Join-Path $art 'PresetDisplayCapabilitiesTests.exe'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:exe /platform:x64 /optimize+ ('/out:'+$test) ('/win32manifest:'+(Join-Path $root 'Source\app.manifest')) (Join-Path $root 'Source\DisplayControl.cs') (Join-Path $root 'Source\HdrControl.cs') (Join-Path $PSScriptRoot 'PresetDisplayCapabilitiesTests.cs')
if($LASTEXITCODE -ne 0){throw 'Preset display capability test compilation failed'}
& $test
if($LASTEXITCODE -ne 0){throw 'Preset display capability test failed'}
