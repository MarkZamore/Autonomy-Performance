$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
$script:checks=0
function Assert($condition,$message){$script:checks++;if(-not $condition){throw $message}}
function Call-Position([byte[]]$il,[int]$opcode,[int]$token){$bytes=[BitConverter]::GetBytes($token);for($i=0;$i -le $il.Length-5;$i++){if($il[$i] -eq $opcode -and $il[$i+1] -eq $bytes[0] -and $il[$i+2] -eq $bytes[1] -and $il[$i+3] -eq $bytes[2] -and $il[$i+4] -eq $bytes[3]){return $i}};return -1}
$folder=Join-Path ([Environment]::GetFolderPath('Programs')) 'Autonomy&Performance'
$folderExisted=[IO.Directory]::Exists($folder)
$art=Join-Path $PSScriptRoot 'Artifacts\StartMenuOption';[void][IO.Directory]::CreateDirectory($art)
$name='Проверка ярлыка '+[Guid]::NewGuid().ToString('N')
$target=Join-Path $art ($name+'.exe');$shortcut=Join-Path $folder ($name+'.lnk')
$save=[ConstructorWindow].GetMethod('SaveToPath',[Reflection.BindingFlags]'Instance,NonPublic')
try{
 $option=$win.FindName('AddToStartMenu');$preview=$win.FindName('ScriptPreview');$grid=$win.FindName('RootGrid')
 Assert ($option -is [Windows.Controls.CheckBox] -and $option.IsChecked -eq $true) 'Shortcut checkbox is not enabled by default'
 Assert ($option.Content -eq 'Добавить в меню Пуск' -and $option.ToolTip -eq 'Создать ярлык .lnk в папке меню «Пуск» текущего пользователя') 'Shortcut caption or tooltip is incorrect'
 Assert ([object]::ReferenceEquals($option.Parent,$preview.Parent) -and [Windows.Controls.Grid]::GetRow($option) -gt [Windows.Controls.Grid]::GetRow($preview)) 'Shortcut option is outside the preview panel'
 foreach($size in @(@(804,521),@(984,661))){
  $grid.Measure([Windows.Size]::new($size[0],$size[1]));$grid.Arrange([Windows.Rect]::new(0,0,$size[0],$size[1]));$grid.UpdateLayout()
  $point=$option.TranslatePoint([Windows.Point]::new(0,0),$grid);$end=$preview.TranslatePoint([Windows.Point]::new(0,$preview.ActualHeight),$grid)
  Assert ($option.ActualHeight -ge 32 -and $point.Y -ge $end.Y -and $point.Y+$option.ActualHeight -le $grid.ActualHeight) 'Pinned preview option is clipped or overlaps scrolling actions'
 }
 $cleanup=[StartMenuShortcuts].GetMethod('Cleanup');$run=[ConstructorApp].GetMethod('Run',[Reflection.BindingFlags]'Static,NonPublic')
 $il=$run.GetMethodBody().GetILAsByteArray();$ctor=[ConstructorWindow].GetConstructor([Type[]]@())
 $cleanupAt=Call-Position $il 0x28 $cleanup.MetadataToken;$constructorAt=Call-Position $il 0x73 $ctor.MetadataToken
 Assert ($cleanupAt -ge 0 -and $constructorAt -gt $cleanupAt) 'Startup cleanup does not run before the constructor window'
 $ui.ApplyPreset($false);Assert ($option.IsChecked) 'A preset changes the shortcut preference'
 $ui.ResetScript();Assert ($option.IsChecked -and -not $win.FindName('SaveButton').IsEnabled) 'Reset changes the preference or permits saving an empty script'
 $win.FindName('DarkThemeSwitch').IsChecked=$true;$option.IsChecked=$false
 [void]$save.Invoke($ui,[object[]]@([string]$target));Assert ([IO.File]::Exists($target) -and -not [IO.File]::Exists($shortcut) -and -not $win.FindName('SaveButton').IsEnabled) 'Unchecked save creates a menu link or remains dirty'
 $option.IsChecked=$true;Assert ($win.FindName('SaveButton').IsEnabled) 'Shortcut preference cannot be applied to the saved script'
 [void]$save.Invoke($ui,[object[]]@([string]$target));Assert ([IO.File]::Exists($shortcut) -and $win.FindName('Status').Text -eq '' -and -not $win.FindName('SaveButton').IsEnabled) 'Checked save does not create a shortcut successfully'
 $arguments=[object[]]@([string]$shortcut,$null)
 $owned=[StartMenuShortcuts].GetMethod('ReadOwnedTarget',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,$arguments)
 Assert ($owned -and [string]::Equals($arguments[1],$target,[StringComparison]::OrdinalIgnoreCase)) 'Saved menu link does not point at the selected EXE'
 Assert ([IO.Directory]::GetFiles($folder,$name+'*.lnk').Length -eq 1) 'Export creates duplicate links'
 [void]$save.Invoke($ui,[object[]]@([string]$target));Assert ([IO.Directory]::GetFiles($folder,$name+'*.lnk').Length -eq 1) 'Repeated export duplicates its menu link'
 $readFingerprint=[StartMenuShortcuts].GetMethod('ReadFingerprint',[Reflection.BindingFlags]'Static,NonPublic')
 $oldFingerprint=$readFingerprint.Invoke($null,[object[]]@([string]$shortcut,[string]$target))
 Assert ($oldFingerprint -match '^\d+:[A-F0-9]{64}$') 'Created shortcut has no portable fingerprint'
 $config=New-Object ModeConfiguration;$config.DarkTheme=2
 [ExecutableExporter]::Save($target,$config,[EmbeddedAssets]::Read('Icon0'))
 $newFingerprint=$readFingerprint.Invoke($null,[object[]]@([string]$shortcut,[string]$target))
 $digest=[Security.Cryptography.SHA256]::Create()
 try{$expected=([IO.FileInfo]$target).Length.ToString()+':'+[BitConverter]::ToString($digest.ComputeHash([IO.File]::ReadAllBytes($target))).Replace('-','')}finally{$digest.Dispose()}
 Assert ($newFingerprint -eq $expected -and $newFingerprint -ne $oldFingerprint) 'Configuration-only overwrite does not refresh existing shortcut fingerprint'
 @{Passed=$true;Checks=$script:checks;DefaultChecked=$true;InsidePreview=$true;RealNativeLinkCreatedAndRemoved=$true;StartupCleanupWired=$true;ExportExecuted=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
 'PASS: '+$script:checks+' Start menu option checks; one real test shortcut created and removed, exported EXE not executed.'
}finally{
 $win.Close()
 if([IO.File]::Exists($shortcut)){[IO.File]::Delete($shortcut)}
 if([IO.File]::Exists($target)){[IO.File]::Delete($target)}
 if(-not $folderExisted -and [IO.Directory]::Exists($folder)){try{[IO.Directory]::Delete($folder,$false)}catch{}}
}
