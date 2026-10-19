$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent;$art=Join-Path $PSScriptRoot 'Artifacts\Functional';[void](New-Item -ItemType Directory -Path $art -Force)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path (Join-Path $PSScriptRoot 'WindowObserver.cs')
[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))
function Assert($condition,$message){if(-not $condition){throw $message}}
$results=New-Object Collections.Generic.List[object]
function Invoke-Mode([string]$path,[string]$label,[string[]]$extra=@()){
$report=Join-Path $art ($label+'.json');$start=New-Object Diagnostics.ProcessStartInfo;$start.FileName=$path;$start.UseShellExecute=$false
$arguments=@($extra)+@('--report',$report);$start.Arguments=($arguments|ForEach-Object {'"'+$_+'"'})-join ' '
$observer=New-Object WindowObserver
try{
$p=[Diagnostics.Process]::Start($start);$clock=[Diagnostics.Stopwatch]::StartNew()
while(-not $p.HasExited -and $clock.ElapsedMilliseconds -lt 35000){[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 30}
Assert ($p.HasExited) ('Timed out: '+$label)
for($i=0;$i -lt 8;$i++){[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 30}
$events=@($observer.Events);$relevant=@($events|Where-Object {(Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue).ProcessName -ne 'java'});$state=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
$result=[pscustomobject]@{Label=$label;ExitCode=$p.ExitCode;VisibleWindows=$relevant;UnrelatedWindows=@($events|Where-Object {$_ -notin $relevant});Report=$state};$results.Add($result)
Assert ($p.ExitCode -eq 0) ('Failed '+$label+': '+($state.Steps|ConvertTo-Json -Compress))
Assert ($relevant.Count -eq 0) ('Visible window during '+$label+': '+($events|ConvertTo-Json -Compress))
return $state
}finally{$observer.Dispose();if($p){$p.Dispose()}}
}
$perf=Join-Path $art 'Performance.exe';$auto=Join-Path $art 'Autonomy.exe'
$performance=New-Object ModeConfiguration;$performance.EnergySaver=2;$performance.FanProfile=2;$performance.PowerMode=3;$performance.Brightness=100
$autonomy=New-Object ModeConfiguration;$autonomy.EnergySaver=1;$autonomy.FanProfile=1
[ExecutableExporter]::Save($perf,$performance,[EmbeddedAssets]::Read('Icon3'))
[ExecutableExporter]::Save($auto,$autonomy,[EmbeddedAssets]::Read('Icon1'))
$original=Invoke-Mode $perf 'original' @('--status');$saved=Join-Path $art 'original.json'
try{
$lower=$original|ConvertTo-Json -Depth 12|ConvertFrom-Json;$lower.State.Windows.Brightness|ForEach-Object {$_.CurrentBrightness=55}
$lowerPath=Join-Path $art 'low-brightness.json';$lower|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $lowerPath -Encoding UTF8
$baseline=Invoke-Mode $perf 'low-baseline' @('--restore',$lowerPath)
Assert ($baseline.State.Windows.Brightness[0].CurrentBrightness -eq 55) 'Brightness baseline unavailable'
Assert ([math]::Abs($baseline.State.Windows.Volume-$original.State.Windows.Volume) -lt 0.001 -and $baseline.State.Windows.Muted -eq $original.State.Windows.Muted) 'Audio baseline changed'
for($i=0;$i -lt 2;$i++){
$a=Invoke-Mode $auto ('autonomy-'+$i)
Assert ($a.State.Windows.EnergySaverStatus -eq 1 -and $a.State.Fan.ActiveProfile -eq 1) 'Autonomy state mismatch'
Assert ($a.State.Windows.Brightness[0].CurrentBrightness -eq $(if($i -eq 0){55}else{100})) 'Autonomy changed brightness'
Assert ([math]::Abs($a.State.Windows.Volume-$original.State.Windows.Volume) -lt 0.001 -and $a.State.Windows.Muted -eq $original.State.Windows.Muted) 'Autonomy changed audio'
$p=Invoke-Mode $perf ('performance-'+$i)
Assert ($p.State.Windows.EnergySaverStatus -eq 0 -and $p.State.Fan.ActiveProfile -eq 2) 'Performance state mismatch'
Assert ($p.State.Windows.ACPowerMode -eq 'ded574b5-45a0-4f42-8737-46345c09c238' -and $p.State.Windows.DCPowerMode -eq 'ded574b5-45a0-4f42-8737-46345c09c238') 'Performance power mismatch'
Assert ($p.State.Windows.Brightness[0].CurrentBrightness -eq 100) 'Performance brightness mismatch'
Assert ([math]::Abs($p.State.Windows.Volume-$original.State.Windows.Volume) -lt 0.001 -and $p.State.Windows.Muted -eq $original.State.Windows.Muted) 'Performance changed audio'
}
foreach($mode in @('balanced','efficiency','independent')){
$c=New-Object ModeConfiguration
if($mode -eq 'balanced'){$c.EnergySaver=2;$c.FanProfile=1;$c.PowerMode=2}
elseif($mode -eq 'efficiency'){$c.EnergySaver=1;$c.PowerMode=1}
else{$c.EnergySaver=1;$c.PowerMode=3}
$path=Join-Path $art ($mode+'.exe');[ExecutableExporter]::Save($path,$c,[EmbeddedAssets]::Read('Icon0'))
$r=Invoke-Mode $path $mode
Assert ($r.State.Windows.ACPowerMode -eq [ModeConfiguration]::PowerGuid($c.PowerMode).ToString() -and $r.State.Windows.DCPowerMode -eq [ModeConfiguration]::PowerGuid($c.PowerMode).ToString()) ('Power mismatch: '+$mode)
Assert ($r.State.Windows.EnergySaverStatus -eq $(if($c.EnergySaver -eq 1){1}else{0})) ('Saver mismatch: '+$mode)
}
$before=Invoke-Mode $perf 'before-skip' @('--status')
$a=Invoke-Mode $auto 'autonomy-skip' @('--skip-fan')
Assert ($a.State.Fan.ActiveProfile -eq $before.State.Fan.ActiveProfile -and $a.State.Windows.EnergySaverStatus -eq 1) 'Missing fan scenario failed'
$p=Invoke-Mode $perf 'performance-skip' @('--skip-fan')
Assert ($p.State.Fan.ActiveProfile -eq $before.State.Fan.ActiveProfile -and $p.State.Windows.EnergySaverStatus -eq 0) 'Missing fan performance failed'
# Fast launches all serialize against the same mutex; final profile deterministic.
$burst=New-Object Collections.Generic.List[Diagnostics.Process]
for($i=0;$i -lt 4;$i++){$s=New-Object Diagnostics.ProcessStartInfo;$s.FileName=$(if($i%2 -eq 0){$auto}else{$perf});$s.UseShellExecute=$false;$burst.Add([Diagnostics.Process]::Start($s))}
foreach($p in $burst){Assert ($p.WaitForExit(35000)) 'Burst timed out';Assert ($p.ExitCode -eq 0) 'Burst failed';$p.Dispose()}
$last=Invoke-Mode $perf 'after-burst'
$portable=Join-Path $art 'Portable';[void](New-Item -ItemType Directory -Path $portable -Force);$portableExe=Join-Path $portable 'Performance.exe';Copy-Item -LiteralPath $perf -Destination $portableExe -Force
[void](Invoke-Mode $portableExe 'portable')
Assert (@(Get-ChildItem -LiteralPath $portable -Force).Count -eq 1) 'Portable execution created files'
}finally{
$restored=Invoke-Mode $perf 'restored' @('--restore',$saved)
$results|ConvertTo-Json -Depth 18|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
}
foreach($key in @('EnergySaverOverride','ACPowerMode','DCPowerMode','Volume','Muted')){Assert ($original.State.Windows.$key -eq $restored.State.Windows.$key) ('Restore mismatch: '+$key)}
Assert ($original.State.Fan.ACProfile -eq $restored.State.Fan.ACProfile -and $original.State.Fan.DCProfile -eq $restored.State.Fan.DCProfile) 'Restore fan mismatch'
Assert ($original.State.Windows.Brightness[0].CurrentBrightness -eq $restored.State.Windows.Brightness[0].CurrentBrightness) 'Restore brightness mismatch'
$results|ForEach-Object {[pscustomobject]@{Label=$_.Label;ExitCode=$_.ExitCode;VisibleWindows=@($_.VisibleWindows).Count;Saver=$_.Report.State.Windows.EnergySaverStatus;Fan=$_.Report.State.Fan.ActiveProfile}}
