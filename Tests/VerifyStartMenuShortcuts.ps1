$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$art=Join-Path $PSScriptRoot 'Artifacts\StartMenuShortcuts'
[void][IO.Directory]::CreateDirectory($art)
$run=Join-Path $art ([Guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($run)
$test=Join-Path $art 'StartMenuShortcutsTests.exe'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:exe /platform:x64 /optimize+ ('/out:'+$test) (Join-Path $root 'Source\StartMenuShortcuts.cs') (Join-Path $PSScriptRoot 'StartMenuShortcutsTests.cs')
if($LASTEXITCODE -ne 0){throw 'Shortcut tests failed to compile'}
& $test $run
if($LASTEXITCODE -ne 0){throw 'Shortcut tests failed'}
