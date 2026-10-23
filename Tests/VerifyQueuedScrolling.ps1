$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$art=Join-Path $PSScriptRoot 'Artifacts\QueuedScrolling'
[void][IO.Directory]::CreateDirectory($art)
$test=Join-Path $art 'QueuedScrollingTests.exe'
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf=Join-Path $framework WPF
& (Join-Path $framework csc.exe) /nologo /target:exe /platform:x64 /optimize+ /r:System.Xaml.dll ('/out:'+$test) ('/r:'+(Join-Path $wpf PresentationFramework.dll)) ('/r:'+(Join-Path $wpf PresentationCore.dll)) ('/r:'+(Join-Path $wpf WindowsBase.dll)) (Join-Path $root 'Source\SmoothScrolling.cs') (Join-Path $PSScriptRoot QueuedScrollingTests.cs)
if($LASTEXITCODE -ne 0){throw 'Queued scrolling test compilation failed'}
& $test
if($LASTEXITCODE -ne 0){throw 'Queued scrolling tests failed'}
