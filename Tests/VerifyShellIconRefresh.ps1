param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
$Builder=[IO.Path]::GetFullPath($Builder)
$artifacts=Join-Path $PSScriptRoot 'Artifacts\ShellIconRefresh'
[void][IO.Directory]::CreateDirectory($artifacts)
$test=Join-Path $artifacts 'ShellIconRefreshTests.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ ('/out:'+$test) ('/r:'+$Builder) /r:System.Drawing.dll /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'ShellIconRefreshTests.cs')
if($LASTEXITCODE -ne 0){throw 'Shell icon regression failed to compile'}
& $test $Builder $artifacts
if($LASTEXITCODE -ne 0){throw 'Shell icon regression failed'}
