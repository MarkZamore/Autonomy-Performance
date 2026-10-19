$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path (Join-Path $PSScriptRoot 'WindowObserver.cs')
$art=Join-Path $PSScriptRoot 'Artifacts\AppActions';[void][IO.Directory]::CreateDirectory($art)
function Assert($condition,$message){if(-not $condition){throw $message}}
function Export-Action($name,$config){$path=Join-Path $art ($name+'.exe');[ExecutableExporter]::Save($path,$config,[EmbeddedAssets]::Read('Icon0'));return $path}
function Run-Action($file,$label,$extra=''){
    $report=Join-Path $art ($label+'.json');$start=[Diagnostics.ProcessStartInfo]::new($file,($extra+' --report "'+$report+'"'));$start.UseShellExecute=$false
    $observer=New-Object WindowObserver;$process=$null
    try{
        $process=[Diagnostics.Process]::Start($start);$watch=[Diagnostics.Stopwatch]::StartNew()
        while(-not $process.HasExited -and $watch.ElapsedMilliseconds -lt 30000){[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 25}
        Assert ($process.HasExited -and $process.ExitCode -eq 0) ('Action did not terminate successfully: '+$label)
        [Windows.Forms.Application]::DoEvents()
        $own=@($observer.Events|Where-Object {$_.ProcessId -eq $process.Id})
        Assert ($own.Count -eq 0) ('Exported EXE showed a window: '+$label)
        $wallpaper=@($observer.Events|Where-Object {$_.ClassName -match 'Wallpaper|Chrome' -and $_.Title -match 'Wallpaper'})
        Assert ($wallpaper.Count -eq 0) ('Wallpaper command showed a window: '+$label)
        return Get-Content -LiteralPath $report -Encoding UTF8 -Raw|ConvertFrom-Json
    }finally{if($null -ne $process){$process.Dispose()};$observer.Dispose()}
}
$standard=New-Object ModeConfiguration;$standard.FanProfile=3;$standardExe=Export-Action 'Standard' $standard
$original=Run-Action $standardExe 'original' '--status'
try{
    if($original.State.Fan.Available){
        foreach($attempt in @(1,2)){
            $result=Run-Action $standardExe ('standard-'+$attempt)
            Assert ($result.State.Fan.ActiveProfile -eq 0) 'Standard config did not map to ASUS native profile 0'
            foreach($field in @('ACPowerMode','DCPowerMode')){Assert ($result.State.Windows.$field -eq $original.State.Windows.$field) ('Standard fan changed unselected power choice: '+$field)}
        }
    }
}finally{
    $restored=Run-Action $standardExe 'restored' ('--restore "'+(Join-Path $art 'original.json')+'"')
    Assert ($restored.State.Fan.ACProfile -eq $original.State.Fan.ACProfile -and $restored.State.Fan.DCProfile -eq $original.State.Fan.DCProfile) 'Original fan profiles did not restore'
}
$pause=New-Object ModeConfiguration;$pause.WallpaperPlayback=2;$pauseExe=Export-Action 'Pause' $pause
$play=New-Object ModeConfiguration;$play.WallpaperPlayback=1;$playExe=Export-Action 'Play' $play
$checks=@()
# The user explicitly confirmed wallpapers were not manually paused before testing.
try{
    foreach($action in @(@('pause',$pauseExe),@('pause-repeat',$pauseExe),@('play',$playExe),@('play-repeat',$playExe))){
        $result=Run-Action $action[1] $action[0]
        $step=@($result.Steps|Where-Object {$_.Name -eq 'WallpaperPlayback'})
        Assert ($step.Count -eq 1 -and $step[0].Status -eq 'ok') ('Wallpaper Engine did not accept the command: '+$action[0])
        $checks+=@{Action=$action[0];Status=$step[0].Status;Detail=$step[0].Detail}
    }
}finally{[void](Run-Action $playExe 'play-restored')}
@{Passed=$true;StandardAsusProfile=0;FanAndWindowsRestored=$true;WallpaperOriginalPlaybackRestored=$true;WallpaperChecks=$checks;IndependentPlaybackStateQueryAvailable=$false}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
'PASS: standalone Standard mode and repeat, original fan/power restoration; pause/play CLI accepted twice, no observed command windows, playback resumed.'
