$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$art=Join-Path $PSScriptRoot 'Artifacts\DisplayFeatures'
[void][IO.Directory]::CreateDirectory($art)
$test=Join-Path $art 'DisplayFeatureTests.exe'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:exe /platform:x64 /optimize+ ('/out:'+$test) /r:System.Management.dll /r:System.Web.Extensions.dll (Join-Path $root 'Source\DisplayControl.cs') (Join-Path $root 'Source\HdrControl.cs') (Join-Path $root 'Source\StepResult.cs') (Join-Path $root 'Source\SystemActions.cs') (Join-Path $root 'Source\BrokerWindowsControl.cs') (Join-Path $root 'Source\NativeWindowsPreferences.cs') (Join-Path $root 'Source\WindowsPreferenceCatalog.cs') (Join-Path $PSScriptRoot 'DisplayFeatureTests.cs')
if($LASTEXITCODE -ne 0){throw 'Display feature test compilation failed'}
# This test deliberately changes physical display settings, then restores them.
& $test (Join-Path $art 'results.json')
if($LASTEXITCODE -ne 0){throw 'Display feature test failed; inspect restore results'}
Get-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8 -Raw
