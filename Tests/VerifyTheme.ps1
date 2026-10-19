$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$runner=[Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\RunnerTemplate.exe'))
$control=$runner.GetType('AdditionalControl');$configuration=$runner.GetType('ModeConfiguration')
$path='Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path,$true)
if($null -eq $key){throw 'Windows personalization key unavailable'}
$before=@{};$checks=@()
foreach($name in @('AppsUseLightTheme','SystemUsesLightTheme')){
    $exists=$key.GetValueNames() -contains $name
    $before[$name]=@{Exists=$exists;Value=$key.GetValue($name);Kind=$(if($exists){$key.GetValueKind($name)}else{$null})}
}
try{
    foreach($mode in @(2,1)){
        $c=[Activator]::CreateInstance($configuration);$configuration.GetField('DarkTheme').SetValue($c,$mode)
        $steps=@($control.GetMethod('Apply').Invoke($null,@($c,$true)))
        if($steps.Count -ne 1 -or $steps[0].Status -ne 'ok'){throw 'Theme action failed'}
        $expected=if($mode -eq 1){0}else{1}
        foreach($name in $before.Keys){if($key.GetValue($name) -ne $expected){throw 'Theme state did not verify'}}
        $checks+=@{Mode=$mode;Passed=$true}
    }
}finally{
    foreach($name in $before.Keys){$item=$before[$name];if($item.Exists){$key.SetValue($name,$item.Value,$item.Kind)}else{$key.DeleteValue($name,$false)}}
    $native=$control.GetMethod('SendMessageTimeout',[Reflection.BindingFlags]'NonPublic,Static')
    [void]$native.Invoke($null,@([IntPtr]0xffff,[uint32]0x1a,[UIntPtr]::Zero,'ImmersiveColorSet',[uint32]2,[uint32]200,[UIntPtr]::Zero))
    foreach($name in $before.Keys){if($key.GetValue($name) -ne $before[$name].Value){throw 'Original theme did not restore'}}
    $key.Dispose()
}
$folder=Join-Path $PSScriptRoot 'Artifacts\Theme';[void][IO.Directory]::CreateDirectory($folder)
@{Passed=$true;Restored=$true;Checks=$checks}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $folder 'results.json') -Encoding UTF8
'PASS: light/dark Windows and application color settings applied and original values restored.'
