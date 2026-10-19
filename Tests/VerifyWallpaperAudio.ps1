param([switch]$OriginalMuted)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))
$art=Join-Path $PSScriptRoot 'Artifacts\WallpaperAudio';[void][IO.Directory]::CreateDirectory($art)
function Run-Hidden([string]$path,[string]$arguments){
    $start=New-Object Diagnostics.ProcessStartInfo;$start.FileName=$path;$start.Arguments=$arguments
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $process=[Diagnostics.Process]::Start($start)
    try{if(-not $process.WaitForExit(25000)){try{$process.Kill()}catch{};throw 'Wallpaper audio runner timed out'};if($process.ExitCode -ne 0){throw ('Wallpaper audio runner failed: '+$process.ExitCode)}}finally{$process.Dispose()}
}
function Read-Run([string]$path,[string]$label){
    $report=Join-Path $art ($label+'.json');Run-Hidden $path ('--report "'+$report+'"')
    $data=Get-Content -LiteralPath $report -Raw -Encoding UTF8|ConvertFrom-Json
    $steps=@($data.Steps);if($steps.Count -ne 1 -or $steps[0].Name -ne 'WallpaperAudio' -or $steps[0].Status -ne 'ok'){
        throw ('Wallpaper audio command was not accepted: '+($steps|ConvertTo-Json -Compress))
    }
    return $data
}
$mute=Join-Path $art 'Mute.exe';$unmute=Join-Path $art 'Unmute.exe'
foreach($entry in @(@($mute,2),@($unmute,1))){$config=New-Object ModeConfiguration;$config.WallpaperAudio=$entry[1];[ExecutableExporter]::Save($entry[0],$config,[EmbeddedAssets]::Read('Icon0'))}
$beforePath=Join-Path $art 'before.json';Run-Hidden (Join-Path $root 'Source\Build\RunnerTemplate.exe') ('--status --report "'+$beforePath+'"')
$before=Get-Content -LiteralPath $beforePath -Raw -Encoding UTF8|ConvertFrom-Json
$restore=if($OriginalMuted){$mute}else{$unmute}
try{
    foreach($entry in @(@($mute,'mute-1'),@($mute,'mute-2'),@($unmute,'unmute-1'),@($unmute,'unmute-2'))){
        $run=Read-Run $entry[0] $entry[1]
        foreach($field in @('Volume','Muted','AudioEndpointId')){if($run.State.Windows.$field -ne $before.State.Windows.$field){throw ('System audio changed: '+$field)}}
    }
    $portable=Join-Path $art 'Portable';[void][IO.Directory]::CreateDirectory($portable)
    $standalone=Join-Path $portable 'Mode.exe';Copy-Item -LiteralPath $restore -Destination $standalone -Force
    $filesBefore=@([IO.Directory]::GetFiles($portable)|Sort-Object)
    Run-Hidden $standalone '';Run-Hidden $standalone ''
    $filesAfter=@([IO.Directory]::GetFiles($portable)|Sort-Object)
    if(@(Compare-Object $filesBefore $filesAfter).Count -ne 0){throw 'Ordinary wallpaper audio EXE wrote additional sibling files'}
    'PASS: standalone mute twice/unmute twice accepted; system volume/mute/endpoint unchanged; ordinary repeated EXE completed without sibling artifacts. Original wallpaper audio state restored. Internal Wallpaper Engine mute flag is not independently exposed by its CLI.'
}finally{[void](Read-Run $restore 'restored')}
