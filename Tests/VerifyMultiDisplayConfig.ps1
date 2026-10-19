$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$art=Join-Path $PSScriptRoot 'Artifacts\MultiDisplay'
[void][IO.Directory]::CreateDirectory($art)
$test=Join-Path $art 'MultiDisplayConfigTests.exe'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:exe /platform:x64 /optimize+ ('/out:'+$test) /r:System.Management.dll /r:System.Web.Extensions.dll (Join-Path $root 'Source\Configuration.cs') (Join-Path $root 'Source\DisplayControl.cs') (Join-Path $root 'Source\HdrControl.cs') (Join-Path $root 'Source\DisplayBrightnessControl.cs') (Join-Path $root 'Source\StepResult.cs') (Join-Path $PSScriptRoot 'MultiDisplayConfigTests.cs')
if($LASTEXITCODE -ne 0){throw 'Multi-display configuration test compilation failed'}
& $test
if($LASTEXITCODE -ne 0){throw 'Multi-display configuration test failed'}
