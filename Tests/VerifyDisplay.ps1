$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent;$art=Join-Path $PSScriptRoot 'Artifacts\Display';[void](New-Item -ItemType Directory -Path $art -Force)
[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Windows.Forms
Add-Type -Path (Join-Path $PSScriptRoot 'WindowObserver.cs')
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
function Assert($condition,$message){if(-not $condition){throw $message}}
$selector=$win.FindName('DisplaySelector');$rates=$win.FindName('RefreshSelector');$preview=$win.FindName('ScriptPreview');$section=$win.FindName('DisplaySection')
Assert ($null -eq $win.FindName('DisplayEnabled') -and $ui.Configuration.RefreshRate -eq 0 -and $rates.SelectedItem.Rate -eq 0) 'Display action initially enabled'
Assert ($selector.Items.Count -gt 0) 'No monitors enumerated on test system'
$section.IsExpanded=$true
if($rates.Items.Count -gt 1){$rates.SelectedIndex=1}
Assert ($selector.IsEnabled -and $rates.IsEnabled -and $win.FindName('SaveButton').IsEnabled) 'Display controls unavailable'
$selected=$selector.SelectedItem;$rate=$rates.SelectedItem.Rate
Assert ($ui.PreviewText.Contains($selected.Label) -and $ui.PreviewText.Contains($rate.ToString()+' Гц')) 'Display not in preview'
if($selector.Items.Count -gt 1){$selector.SelectedIndex=1;if($rates.Items.Count -gt 1){$rates.SelectedIndex=1};Assert ($ui.Configuration.DisplayId -eq $selector.SelectedItem.MonitorId) 'Wrong selected display id';Assert ($ui.PreviewText.Contains($selector.SelectedItem.Label)) 'Monitor change not reflected'}
$rates.SelectedIndex=0;Assert ($ui.Configuration.RefreshRate -eq $rates.SelectedItem.Rate) 'Rate selection not reflected'
Assert (-not $ui.Configuration.HasActions -and $ui.PreviewText -eq '') 'Unchanged refresh rate remains in config'
$selector.SelectedIndex=0
# Prefer 60 Hz for a real rate transition, supported by this monitor.
foreach($choice in $rates.Items){if($choice.Rate -eq 60){$rates.SelectedItem=$choice;break}}
$original=@([DisplayControl]::Enumerate())
$config=$ui.Configuration;$exe=Join-Path $art 'display-mode.exe'
[ExecutableExporter]::Save($exe,$config,[EmbeddedAssets]::Read('Icon0'))
$loaded=[PeConfiguration]::Read($exe);Assert ($loaded.DisplayId -eq $config.DisplayId -and $loaded.RefreshRate -eq $config.RefreshRate -and $loaded.HasActions) 'Export lost display config'
$grid=$win.FindName('RootGrid');$grid.Background=$app.Resources['Brush.Canvas'];$size=[Windows.Size]::new(984,661);$grid.Measure($size);$grid.Arrange([Windows.Rect]::new($size));$grid.UpdateLayout()
$bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(984,661,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($grid);$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $art 'ui-display.png'));try{$encoder.Save($stream)}finally{$stream.Dispose()}
function RunExport($file,$label){
$observer=New-Object WindowObserver;$p=$null
try{$s=New-Object Diagnostics.ProcessStartInfo;$s.FileName=$file;$s.UseShellExecute=$false;$report=Join-Path $art ($label+'.json');$s.Arguments='--report "'+$report+'"';$p=[Diagnostics.Process]::Start($s);$clock=[Diagnostics.Stopwatch]::StartNew()
while(-not $p.HasExited -and $clock.ElapsedMilliseconds -lt 35000){[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 30}
Assert ($p.HasExited -and $p.ExitCode -eq 0) ('Display EXE failed: '+$label)
[Windows.Forms.Application]::DoEvents();$observed=@($observer.Events);$observed|ConvertTo-Json -Depth 4|Set-Content (Join-Path $art ($label+'-windows.json')) -Encoding UTF8
$own=@($observed|Where-Object {$_.ProcessId -eq $p.Id});Assert ($own.Count -eq 0) 'Display EXE showed a window'
return (Get-Content -LiteralPath $report -Raw|ConvertFrom-Json)
}finally{$observer.Dispose();if($p){$p.Dispose()}}
}
try{
$r=RunExport $exe 'apply'
Assert (@($r.Steps|Where-Object {$_.Name -eq 'DisplayRefreshRate' -and $_.Status -eq 'ok'}).Count -eq 1) 'Display step did not apply'
$changed=@([DisplayControl]::Enumerate())
$selectedAfter=@($changed|Where-Object {$_.MonitorId -eq $config.DisplayId})[0]
Assert ($selectedAfter.CurrentRate -eq $config.RefreshRate) 'Rate mismatch'
foreach($before in $original){$after=@($changed|Where-Object {$_.MonitorId -eq $before.MonitorId})[0];Assert ($after.Width -eq $before.Width -and $after.Height -eq $before.Height) 'Resolution changed';if($before.MonitorId -ne $config.DisplayId){Assert ($after.CurrentRate -eq $before.CurrentRate) 'Other monitor changed'}}
[void](RunExport $exe 'repeat')
# Missing screen and unsupported rate should leave displays untouched and exit silently.
foreach($scenario in @('missing','unsupported')){
$c=New-Object ModeConfiguration;$c.DisplayId=$(if($scenario -eq 'missing'){'missing-monitor'}else{$config.DisplayId});$c.RefreshRate=$(if($scenario -eq 'missing'){60}else{999})
$path=Join-Path $art ($scenario+'.exe');[ExecutableExporter]::Save($path,$c,[EmbeddedAssets]::Read('Icon0'));$skip=RunExport $path $scenario
Assert (@($skip.Steps|Where-Object {$_.Name -eq 'DisplayRefreshRate' -and $_.Status -eq 'skipped'}).Count -eq 1) 'Missing/unsupported mode not skipped'
Assert (@([DisplayControl]::Enumerate()|Where-Object {$_.MonitorId -eq $config.DisplayId})[0].CurrentRate -eq $config.RefreshRate) 'Skip changed frequency'
}
}finally{
foreach($before in $original){$restore=[DisplayControl]::Apply($before.MonitorId,$before.CurrentRate);Assert ($restore.Status -eq 'ok') ('Restore failed: '+$restore.Detail)}
$win.Close()
}
$after=@([DisplayControl]::Enumerate());foreach($before in $original){Assert (@($after|Where-Object {$_.MonitorId -eq $before.MonitorId})[0].CurrentRate -eq $before.CurrentRate) 'Original rate not restored'}
[pscustomobject]@{Passed=$true;Original=$original;Restored=$after;SelectedId=$config.DisplayId;AppliedHz=$config.RefreshRate}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $art 'results.json') -Encoding UTF8
'PASS: monitor selection, preview, EXE export, actual rate switch, repeat, unavailable monitor/rate, other-screen preservation and restore.'
