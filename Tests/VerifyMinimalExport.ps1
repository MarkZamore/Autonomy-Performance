param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
$Builder=[IO.Path]::GetFullPath($Builder)
$artifacts=Join-Path $PSScriptRoot 'Artifacts\MinimalExport'
[void][IO.Directory]::CreateDirectory($artifacts)
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$test=Join-Path $artifacts 'MinimalExportTests.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ ('/out:'+$test) ('/r:'+$Builder) /r:System.Core.dll /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'MinimalExportTests.cs')
if($LASTEXITCODE -ne 0){throw 'Minimal export test compilation failed'}
& $test $Builder $artifacts
if($LASTEXITCODE -ne 0){throw 'Minimal export test failed'}
