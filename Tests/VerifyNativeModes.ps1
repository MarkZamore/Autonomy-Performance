$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$art=Join-Path $PSScriptRoot 'Artifacts\NativeModes';[void][IO.Directory]::CreateDirectory($art)
$runner=Join-Path $root 'Source\Build\RunnerTemplate.exe'
$test=Join-Path $art 'NativeModeTests.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /platform:x64 ('/out:'+$test) ('/r:'+$runner) (Join-Path $PSScriptRoot 'NativeModeTests.cs')
if($LASTEXITCODE -ne 0){throw 'Native test compilation failed'}
Copy-Item -LiteralPath $runner -Destination (Join-Path $art 'RunnerTemplate.exe') -Force
& $test | Tee-Object -FilePath (Join-Path $art 'results.txt')
if($LASTEXITCODE -ne 0){throw 'Native mode tests failed'}
