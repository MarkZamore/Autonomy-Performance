param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
[void][Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))
$art=Join-Path $PSScriptRoot 'Artifacts\DisplayBrightness'
[void][IO.Directory]::CreateDirectory($art)
$script:checks=0
function Assert($condition,$message){$script:checks++;if(-not $condition){throw $message}}
function Read-Brightness {
    return @(Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorBrightness -Filter 'Active=TRUE'|ForEach-Object {
        [pscustomobject]@{InstanceName=$_.InstanceName;Brightness=[int]$_.CurrentBrightness}
    })
}
function Read-Modes {
    return @([DisplayControl]::Enumerate()|ForEach-Object {
        [pscustomobject]@{Id=$_.MonitorId;Device=$_.DeviceName;Width=$_.Width;Height=$_.Height;Rate=$_.CurrentRate;Scale=[HdrControl]::InspectCurrentScale($_.MonitorId)}
    })
}
function Assert-Modes($expected,[string]$stage){
    $observed=Read-Modes
    Assert ($expected.Count -eq $observed.Count) ('Monitor count changed '+$stage)
    foreach($old in $expected){$now=@($observed|Where-Object Id -eq $old.Id);Assert ($now.Count -eq 1) ('Monitor identity changed '+$stage);foreach($field in @('Device','Width','Height','Rate','Scale')){Assert ($now[0].$field -eq $old.$field) ('Display mode changed '+$stage+' '+$field)}}
}
function Assert-Brightness($expected,[string]$stage){
    $observed=Read-Brightness
    Assert ($observed.Count -eq $expected.Count) ('Brightness endpoint count changed '+$stage)
    foreach($old in $expected){$now=@($observed|Where-Object InstanceName -eq $old.InstanceName);Assert ($now.Count -eq 1 -and $now[0].Brightness -eq $old.Brightness) ('Brightness differs '+$stage+' '+$old.InstanceName)}
}
function Run-Hidden([string]$path,[string]$label){
    $report=Join-Path $art ($label+'.json')
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=$path;$start.Arguments='--report "'+$report+'"';$start.WorkingDirectory=$art
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $process=[Diagnostics.Process]::Start($start)
    try{if(-not $process.WaitForExit(25000)){try{$process.Kill()}catch{};throw 'Brightness runner timed out'};Assert ($process.ExitCode -eq 0) ('Brightness runner failed: '+$process.ExitCode)}finally{$process.Dispose()}
    Assert ([IO.File]::Exists($report)) 'Explicit brightness diagnostic was not written'
    return Get-Content -LiteralPath $report -Raw -Encoding UTF8|ConvertFrom-Json
}
function Export-Brightness([string]$path,$targets){
    $config=New-Object ModeConfiguration;$config.Displays=[DisplayModeConfiguration[]]$targets
    $config.Validate()
    Assert ($config.Brightness -eq -1 -and $config.EnergySaver -eq 0 -and $config.PowerMode -eq 0 -and $config.FanProfile -eq 0 -and $config.NightLight -eq 0) 'Test includes a global system action'
    foreach($target in $config.Displays){Assert ($target.DisplayWidth -eq 0 -and $target.DisplayHeight -eq 0 -and $target.RefreshRate -eq 0 -and $target.DisplayScale -eq 0 -and $target.Hdr -eq 0 -and $target.DynamicRefresh -eq 0 -and $target.ContentBrightness -eq 0) 'Test includes an unrelated display action'}
    [ExecutableExporter]::Save($path,$config,[EmbeddedAssets]::Read('Icon0'))
}
$before=Read-Brightness;$modesBefore=Read-Modes
$screen=@($modesBefore|Where-Object {[DisplayBrightnessControl]::Available($_.Id)})|Select-Object -First 1
if($before.Count -eq 0 -or $null -eq $screen){
    [pscustomobject]@{Passed=$true;Skipped=$true;Reason='No readable, independently targetable brightness endpoint.';NoSystemSettingsApplied=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
    'SKIP: no independently targetable panel with readable original brightness.'
    return
}
# Map the monitor interface path to a PnP instance independently of the helper's
# private WMI query. Exactly one active endpoint must identify the chosen panel.
$pnp=$screen.Id.Substring(4).Split('#')[0..2] -join '\'
$matched=@($before|Where-Object {$_.InstanceName -match ('^'+[Regex]::Escape($pnp)+'_[0-9]+$')})
Assert ($matched.Count -eq 1) 'Selected screen does not map to exactly one original WMI brightness endpoint'
$original=$matched[0]
$value=if($original.Brightness -gt 1){$original.Brightness-1}else{[Math]::Max(1,$original.Brightness+1)}
Assert ($value -gt 0 -and $value -le 100 -and $value -ne $original.Brightness) 'Unsafe/no-op test brightness value'
$unsupported=@($modesBefore|Where-Object {-not [DisplayBrightnessControl]::Available($_.Id)})|Select-Object -First 1
$targets=New-Object 'Collections.Generic.List[DisplayModeConfiguration]'
if($null -ne $unsupported){$skip=New-Object DisplayModeConfiguration;$skip.DisplayId=$unsupported.Id;$skip.Brightness=73;$targets.Add($skip)}
$target=New-Object DisplayModeConfiguration;$target.DisplayId=$screen.Id;$target.Brightness=$value;$targets.Add($target)
$restoreTarget=New-Object DisplayModeConfiguration;$restoreTarget.DisplayId=$screen.Id;$restoreTarget.Brightness=$original.Brightness
$setExe=Join-Path $art 'SetBrightness.exe';$restoreExe=Join-Path $art 'RestoreBrightness.exe'
Export-Brightness $setExe $targets.ToArray();Export-Brightness $restoreExe @($restoreTarget)
$failure=$null;$restoreFailure=$null;$run=$null;$restoreRun=$null;$afterChange=$null;$restored=$false;$fallback=$false
try {
    $run=Run-Hidden $setExe 'applied'
    $steps=@($run.Steps)
    Assert ($steps.Count -eq $targets.Count) 'Brightness export ran an unselected action or omitted a target'
    $own=@($steps|Where-Object {$_.Detail.Contains('['+$screen.Id+']')})
    Assert ($own.Count -eq 1 -and $own[0].Name -eq 'DisplayBrightness' -and $own[0].Status -eq 'ok') 'Targeted brightness did not report success'
    if($null -ne $unsupported){$skipped=@($steps|Where-Object {$_.Detail.Contains('['+$unsupported.Id+']')});Assert ($skipped.Count -eq 1 -and $skipped[0].Name -eq 'DisplayBrightness' -and $skipped[0].Status -eq 'skipped') 'Unsupported monitor brightness was not independently skipped'}
    $afterChange=Read-Brightness
    Assert ($afterChange.Count -eq $before.Count) 'Brightness endpoint count changed during the action'
    foreach($old in $before){
        $observed=@($afterChange|Where-Object InstanceName -eq $old.InstanceName)
        $expected=if($old.InstanceName -eq $original.InstanceName){$value}else{$old.Brightness}
        Assert ($observed.Count -eq 1 -and $observed[0].Brightness -eq $expected) ('The wrong brightness endpoint changed or target did not change: '+$old.InstanceName)
    }
    Assert-Modes $modesBefore 'after brightness change'
}catch{$failure=$_}
finally {
    try{
        $restoreRun=Run-Hidden $restoreExe 'restored'
        Assert (@($restoreRun.Steps).Count -eq 1 -and $restoreRun.Steps[0].Name -eq 'DisplayBrightness' -and $restoreRun.Steps[0].Status -eq 'ok') 'Independent brightness restore EXE did not report success'
        Assert-Brightness $before 'after independent restore EXE'
        Assert-Modes $modesBefore 'after independent restore EXE'
        $restored=$true
    }catch{
        $restoreFailure=$_
        # Development-test recovery only. If a helper regression affects a second
        # WMI endpoint, recover its original value as well, then report test failure.
        try{
            $now=Read-Brightness
            foreach($old in $before){
                $current=@($now|Where-Object InstanceName -eq $old.InstanceName)
                if($current.Count -eq 1 -and $current[0].Brightness -ne $old.Brightness){
                    $method=@(Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorBrightnessMethods -Filter 'Active=TRUE'|Where-Object InstanceName -eq $old.InstanceName)
                    if($method.Count -ne 1){throw 'No unique endpoint is available for brightness recovery'}
                    [void](Invoke-CimMethod -InputObject $method[0] -MethodName WmiSetBrightness -Arguments @{Timeout=[uint32]0;Brightness=[byte]$old.Brightness})
                }
            }
            Assert-Brightness $before 'after direct recovery';$restored=$true;$fallback=$true
        }catch{$restoreFailure=$_}
    }
}
if($null -eq $failure -and $null -eq $restoreFailure){Assert ($restored) 'Original brightness restoration was not verified'}
[pscustomobject]@{Passed=($null -eq $failure -and $null -eq $restoreFailure -and $restored);Checks=$script:checks;TargetDisplay=$screen.Id;TargetEndpoint=$original.InstanceName;OriginalBrightness=$original.Brightness;TestBrightness=$value;UnsupportedTarget=$(if($unsupported){$unsupported.Id}else{$null});BrightnessBefore=$before;BrightnessAfterChange=$afterChange;BrightnessAfterRestore=(Read-Brightness);ModesBefore=$modesBefore;ModesAfterRestore=(Read-Modes);ApplicationSteps=$run.Steps;RestorationSteps=$restoreRun.Steps;OriginalBrightnessRestored=$restored;RecoveryFallbackNeeded=$fallback;Failure=$(if($failure){$failure.ToString()}else{$null});RestorationFailure=$(if($restoreFailure){$restoreFailure.ToString()}else{$null})}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
if($restoreFailure){throw $restoreFailure};if($failure){throw $failure}
'PASS: targeted brightness '+$original.Brightness+' -> '+$value+' -> '+$original.Brightness+'%; '+$script:checks+' checks; only the intended active WMI endpoint changed, unsupported target skipped, independent restore EXE verified original brightness and display modes.'
