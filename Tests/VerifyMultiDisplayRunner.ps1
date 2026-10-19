$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$art=Join-Path $PSScriptRoot 'Artifacts\MultiDisplay'
[void][IO.Directory]::CreateDirectory($art)
$test=Join-Path $art 'MultiDisplayRunnerTests.exe'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:exe /platform:x64 /optimize+ /main:MultiDisplayRunnerTests ('/out:'+$test) /r:System.Web.Extensions.dll (Join-Path $root 'Source\Configuration.cs') (Join-Path $root 'Source\Runner.cs') (Join-Path $root 'Source\StepResult.cs') (Join-Path $PSScriptRoot 'MultiDisplayRunnerTests.cs')
if($LASTEXITCODE -ne 0){throw 'Multi-display runner test compilation failed'}
& $test
if($LASTEXITCODE -ne 0){throw 'Multi-display runner test failed'}
