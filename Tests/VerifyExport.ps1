param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
$Builder=[IO.Path]::GetFullPath($Builder)
$artifacts=Join-Path $PSScriptRoot 'Artifacts\Export'
[void](New-Item -ItemType Directory -Path $artifacts -Force)
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$test=Join-Path $artifacts 'ExportTests.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /optimize+ ('/out:'+$test) ('/r:'+$Builder) /r:System.Core.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'ExportTests.cs')
if($LASTEXITCODE -ne 0){throw 'Export test compilation failed'}
& $test $Builder $artifacts 2>&1 | Tee-Object -FilePath (Join-Path $artifacts 'Result.txt')
if($LASTEXITCODE -ne 0){throw 'Export tests failed'}
