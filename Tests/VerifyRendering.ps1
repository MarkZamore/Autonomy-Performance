param([string]$Executable)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(-not $Executable){$Executable=Join-Path $root 'Source\Build\AutonomyPerformance.exe'}
if(-not (Test-Path -LiteralPath $Executable -PathType Leaf)){throw 'Build the constructor before running the rendering regression.'}
$Executable=(Resolve-Path -LiteralPath $Executable).Path
$art=Join-Path $PSScriptRoot 'Artifacts\Rendering'
[void][IO.Directory]::CreateDirectory($art)
$test=Join-Path $art 'RenderingStartupTests.exe'
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf=Join-Path $framework 'WPF'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /optimize+ ('/out:'+$test) ('/r:'+(Join-Path $wpf 'PresentationFramework.dll')) ('/r:'+(Join-Path $wpf 'PresentationCore.dll')) ('/r:'+(Join-Path $wpf 'WindowsBase.dll')) ('/r:'+(Join-Path $framework 'System.Xaml.dll')) (Join-Path $PSScriptRoot 'RenderingStartupTests.cs')
if($LASTEXITCODE -ne 0){throw 'Rendering startup regression compilation failed.'}
# A new process is essential: WPF caches AppContext switches. The harness invokes
# the real production Main and keeps the test window outside the visible desktop.
$output=& $test $Executable 2>&1
$code=$LASTEXITCODE
$output | ForEach-Object { Write-Output $_ }
$output | Set-Content -LiteralPath (Join-Path $art 'startup.txt') -Encoding utf8
if($code -ne 0){throw ('Rendering startup regression failed with exit code '+$code+'.')}
