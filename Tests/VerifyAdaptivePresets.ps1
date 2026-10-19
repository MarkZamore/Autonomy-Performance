param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
$art=Join-Path $PSScriptRoot 'Artifacts\AdaptivePresets'
[void][IO.Directory]::CreateDirectory($art)
$Builder=[IO.Path]::GetFullPath($Builder)
$test=Join-Path $art 'AdaptivePresetTests.exe'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:exe /platform:x64 /optimize+ ('/out:'+$test) ('/r:'+$Builder) /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'AdaptivePresetTests.cs')
if($LASTEXITCODE -ne 0){throw 'Adaptive preset test compilation failed'}
& $test $Builder
if($LASTEXITCODE -ne 0){throw 'Adaptive preset test failed'}
