param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
[void][Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))
$art=Join-Path $PSScriptRoot 'Artifacts\Revision8Native'
[void][IO.Directory]::CreateDirectory($art)
$script:checks=0
$script:runs=New-Object 'Collections.Generic.List[object]'
function Assert($condition,$message){$script:checks++;if(-not $condition){throw $message}}
function Snapshot {
    return @([DisplayControl]::Enumerate()|ForEach-Object {
        [pscustomobject]@{Id=$_.MonitorId;Device=$_.DeviceName;Width=$_.Width;Height=$_.Height;Rate=$_.CurrentRate;Scale=[HdrControl]::InspectCurrentScale($_.MonitorId)}
    })
}
function Assert-Unchanged($expected,[string]$stage){
    $observed=Snapshot
    Assert ($observed.Count -eq $expected.Count) ('Connected monitor count changed: '+$stage)
    foreach($before in $expected){
        $after=@($observed|Where-Object Id -eq $before.Id)
        Assert ($after.Count -eq 1) ('Monitor identity changed: '+$stage+' '+$before.Id)
        foreach($field in @('Device','Width','Height','Rate','Scale')){Assert ($after[0].$field -eq $before.$field) ('Display setting changed: '+$stage+' '+$field+' '+$before.Id)}
    }
}
function Run-Hidden([string]$path,[string]$arguments){
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=$path;$start.Arguments=$arguments;$start.WorkingDirectory=[IO.Path]::GetDirectoryName($path)
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $watch=[Diagnostics.Stopwatch]::StartNew();$process=[Diagnostics.Process]::Start($start)
    try{
        if(-not $process.WaitForExit(25000)){try{$process.Kill()}catch{};throw 'Multi-display runner exceeded its bounded execution time'}
        Assert ($process.ExitCode -eq 0) ('Multi-display runner failed with exit code '+$process.ExitCode)
        $watch.Stop();$script:runs.Add([pscustomobject]@{Arguments=$arguments;ExitCode=$process.ExitCode;Milliseconds=$watch.ElapsedMilliseconds})
    }finally{$process.Dispose();$watch.Stop()}
}
$before=Snapshot
Assert ($before.Count -gt 0) 'No connected monitor is available for native runner verification'
$missing='AP_TEST_DISCONNECTED_'+[Guid]::NewGuid().ToString('N')
$config=New-Object ModeConfiguration
$targets=New-Object 'Collections.Generic.List[DisplayModeConfiguration]'
$absent=New-Object DisplayModeConfiguration
$absent.DisplayId=$missing;$absent.DisplayWidth=$before[0].Width;$absent.DisplayHeight=$before[0].Height;$absent.RefreshRate=$before[0].Rate
$targets.Add($absent)
foreach($screen in $before){
    $target=New-Object DisplayModeConfiguration
    $target.DisplayId=$screen.Id;$target.DisplayWidth=$screen.Width;$target.DisplayHeight=$screen.Height;$target.RefreshRate=$screen.Rate
    if($screen.Scale -gt 0){$target.DisplayScale=$screen.Scale}
    $targets.Add($target)
}
$config.Displays=$targets.ToArray();$config.Validate()
# Only current resolution, Hz and DPI are selected: the runner should take its
# already-applied paths, without introducing a physical display transition.
Assert ($config.Brightness -eq -1 -and $config.EnergySaver -eq 0 -and $config.PowerMode -eq 0 -and $config.FanProfile -eq 0 -and $config.NightLight -eq 0) 'Native test contains an unrelated system action'
foreach($target in $config.Displays){Assert ($target.Brightness -eq -1 -and $target.Hdr -eq 0 -and $target.DynamicRefresh -eq 0 -and $target.ContentBrightness -eq 0) 'Native test contains a display transition beyond current size/Hz/DPI'}
$portable=Join-Path $art ('Standalone-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($portable)
$executable=Join-Path $portable 'Mode.exe'
[ExecutableExporter]::Save($executable,$config,[EmbeddedAssets]::Read('Icon0'))
$read=[PeConfiguration]::Read($executable)
Assert ($read.GetDisplayActions().Count -eq $before.Count+1) 'Export lost one of the monitor targets'
Assert ($read.GetDisplayActions()[0].DisplayId -eq $missing) 'Disconnected target is not first in the generated executable'
$reportPath=Join-Path $art 'native-report.json'
Run-Hidden $executable ('--report "'+$reportPath+'"')
Assert ([IO.File]::Exists($reportPath)) 'Explicit diagnostic report was not written'
$report=Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8|ConvertFrom-Json
Assert ($report.Mode -eq 'configured') 'The generated executable did not execute its embedded configuration'
$steps=@($report.Steps)
$expectedSteps=1+$before.Count+@($before|Where-Object {$_.Scale -gt 0}).Count
Assert ($steps.Count -eq $expectedSteps) 'The runner skipped a target or executed an unselected action'
Assert ($steps[0].Name -eq 'DisplayRefreshRate' -and $steps[0].Status -eq 'skipped' -and $steps[0].Detail.Contains('['+$missing+']')) 'The unavailable first monitor was not independently skipped'
foreach($screen in $before){
    $own=@($steps|Where-Object {$_.Detail.Contains('['+$screen.Id+']')})
    $mode=@($own|Where-Object Name -eq 'DisplayRefreshRate')
    Assert ($mode.Count -eq 1 -and $mode[0].Status -eq 'ok') ('A connected monitor was cancelled after the missing target: '+$screen.Id)
    Assert ($mode[0].Detail -match 'already selected') ('Current display mode did not use its nonchanging path: '+$screen.Id)
    if($screen.Scale -gt 0){$dpi=@($own|Where-Object Name -eq 'DisplayScale');Assert ($dpi.Count -eq 1 -and $dpi[0].Status -eq 'ok') ('Current scale was not preserved/applied: '+$screen.Id)}
    Assert (@($own|Where-Object Status -ne 'ok').Count -eq 0) ('A selected current monitor setting failed: '+$screen.Id)
}
Assert-Unchanged $before 'after diagnostic execution'
$filesBefore=@([IO.Directory]::GetFiles($portable)|Sort-Object)
Assert ($filesBefore.Count -eq 1 -and [IO.Path]::GetFileName($filesBefore[0]) -eq 'Mode.exe') 'Standalone folder includes a dependency/sidecar before ordinary execution'
for($repeat=1;$repeat -le 2;$repeat++){
    Run-Hidden $executable ''
    Assert-Unchanged $before ('after ordinary execution '+$repeat)
    $filesAfter=@([IO.Directory]::GetFiles($portable)|Sort-Object)
    Assert (@(Compare-Object $filesBefore $filesAfter).Count -eq 0) 'Ordinary execution created a sibling file'
    Assert ([IO.Directory]::GetDirectories($portable).Count -eq 0) 'Ordinary execution created a sibling directory'
}
[pscustomobject]@{Passed=$true;Checks=$script:checks;PhysicalDisplays=$before.Count;MissingTarget=$missing;Steps=$steps;NativeSettingsBefore=$before;NativeSettingsAfter=(Snapshot);Runs=$script:runs.ToArray();StandaloneExecutable=$executable;NoPhysicalModeTransitionsRequested=$true;NoBrightnessOrGlobalActions=$true;NoOrdinarySiblingArtifacts=$true}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
'PASS: '+$script:checks+' checks; exported EXE independently skips a disconnected first target, executes current resolution/Hz/scale for '+$before.Count+' real monitors, preserves native display states and completes repeated ordinary standalone runs without sibling artifacts.'
