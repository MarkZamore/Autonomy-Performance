param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
[void][Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DisplayHotplugMessages {
 [DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
}
'@
$app=New-Object Windows.Application
$ui=New-Object ConstructorWindow
$window=$ui.Window
$selector=$window.FindName('DisplaySelector')
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$apply=$ui.GetType().GetMethod('ApplyDisplayList',$flags)
$refresh=$ui.GetType().GetMethod('RefreshDisplays',$flags)
$timer=$ui.GetType().GetField('displayRefreshTimer',$flags).GetValue($ui)
$script:checks=0
function Assert($condition,$message){$script:checks++;if(-not $condition){throw $message}}
function Apply-Displays([Collections.Generic.List[DisplayInfo]]$displays){$invokeArguments=New-Object object[] 1;$invokeArguments[0]=$displays;[void]$apply.Invoke($ui,$invokeArguments)}
function Display-List($values){$list=New-Object 'Collections.Generic.List[DisplayInfo]';foreach($value in $values){$list.Add($value)};return ,$list}
function Fake-Display([int]$number,[uint32]$width=1920){
    $d=New-Object DisplayInfo;$d.MonitorId='DISPLAY\AUTONOMY_TEST\'+$number;$d.DeviceName='\\.\DISPLAY'+$number;$d.Label='Экран '+$number+' - '+$width+' × 1080';$d.Width=$width;$d.Height=1080;$d.CurrentRate=60
    $d.Rates.Add(60);$d.Rates.Add(120)
    $r=New-Object DisplayResolution;$r.Width=$width;$r.Height=1080;$r.Rates.Add(60);$r.Rates.Add(120);$d.Resolutions.Add($r)
    return $d
}
function Set-Rate($rate){$control=$window.FindName('RefreshSelector');foreach($choice in $control.Items){if($choice.Rate -eq $rate){$control.SelectedItem=$choice;return}};throw 'Requested test rate is absent'}
function Display-Action($id){return @($ui.Configuration.Displays|Where-Object DisplayId -eq $id)|Select-Object -First 1}
function Pump([int]$milliseconds){$frame=New-Object Windows.Threading.DispatcherFrame;$stop=New-Object Windows.Threading.DispatcherTimer;$stop.Interval=[TimeSpan]::FromMilliseconds($milliseconds);$stop.Add_Tick({$stop.Stop();$frame.Continue=$false}.GetNewClosure());$stop.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
try{
    $native=@($selector.Items)
    $first=Fake-Display 1;$second=Fake-Display 2
    Apply-Displays (Display-List @($first,$second))
    Assert ($selector.Items.Count -eq 2 -and $selector.SelectedItem.MonitorId -eq $first.MonitorId) 'Initial active displays were not installed'
    Set-Rate 120
    $selector.SelectedItem=$second;Set-Rate 60
    Assert ((Display-Action $first.MonitorId).RefreshRate -eq 120 -and (Display-Action $second.MonitorId).RefreshRate -eq 60) 'Independent display actions were not captured'
    $selector.SelectedItem=$first
    Apply-Displays (Display-List @($second))
    Assert ($selector.Items.Count -eq 1 -and $selector.Items[0].MonitorId -eq $second.MonitorId) 'Disconnected display remains selectable'
    Assert ($selector.SelectedItem.MonitorId -eq $second.MonitorId) 'Remaining display was not selected'
    Assert ($window.FindName('RefreshSelector').SelectedItem.Rate -eq 60) 'Editor retained disconnected screen values'
    Assert ((Display-Action $first.MonitorId).RefreshRate -eq 120) 'Disconnect discarded the saved action for that screen'
    Assert ($ui.PreviewText -match 'Экран 1 - частота обновления 120 Гц') 'Disconnected display action disappeared from the preview'
    Apply-Displays (Display-List @($first,$second))
    Assert ($selector.Items.Count -eq 2 -and $selector.SelectedItem.MonitorId -eq $second.MonitorId) 'Reconnection reset an existing selection'
    $selector.SelectedItem=$first
    Assert ($window.FindName('RefreshSelector').SelectedItem.Rate -eq 120) 'Reconnected screen lost its saved editor state'
    $originalItems=$selector.ItemsSource;$originalEditor=$window.FindName('RefreshSelector').ItemsSource
    Apply-Displays (Display-List @((Fake-Display 1),(Fake-Display 2)))
    Assert ([object]::ReferenceEquals($originalItems,$selector.ItemsSource) -and [object]::ReferenceEquals($originalEditor,$window.FindName('RefreshSelector').ItemsSource)) 'Unchanged topology unnecessarily rebuilt the controls'
    Apply-Displays (Display-List @((Fake-Display 1),(Fake-Display 2 2560)))
    Assert (-not [object]::ReferenceEquals($originalItems,$selector.ItemsSource)) 'Changed display modes were not refreshed'
    Assert ($selector.SelectedItem.MonitorId -eq $first.MonitorId -and $window.FindName('RefreshSelector').SelectedItem.Rate -eq 120) 'Refreshing display modes changed another editor selection'
    Apply-Displays (Display-List @())
    Assert ($selector.Items.Count -eq 0 -and $null -eq $selector.SelectedItem -and -not $selector.IsEnabled) 'Empty display list remains selectable'
    Assert ($window.FindName('DisplayUnavailable').Visibility -eq [Windows.Visibility]::Visible) 'Empty display explanation is hidden'
    foreach($name in @('ResolutionSelector','RefreshSelector','ScaleSelector','ContentBrightnessSelector','HdrSwitch','DynamicRefreshSwitch','BrightnessToggleButton','BrightnessSlider')){Assert (-not $window.FindName($name).IsEnabled) ('Empty topology left a display editor enabled: '+$name)}
    Assert (@($ui.Configuration.Displays).Count -eq 2) 'Empty topology cleared saved display actions'
    Apply-Displays (Display-List @($first,$second))
    Assert ($selector.SelectedItem.MonitorId -eq $first.MonitorId -and $window.FindName('RefreshSelector').SelectedItem.Rate -eq 120) 'Restoring the topology did not restore the first available screen'
    # EnsureHandle creates the native window and message hook without showing a UI.
    $handle=[Windows.Interop.WindowInteropHelper]::new($window).EnsureHandle()
    Assert (-not $window.IsVisible) 'The native message test unexpectedly showed the constructor'
    Assert ($null -ne $ui.GetType().GetField('displayMessageSource',$flags).GetValue($ui)) 'Native display observer was not attached'
    foreach($message in @(@(0x7e,0),@(0x219,7))){
        $timer.Stop();[void][DisplayHotplugMessages]::SendMessage($handle,$message[0],[IntPtr]$message[1],[IntPtr]::Zero)
        Assert $timer.IsEnabled ('A topology/device event did not schedule a refresh: '+$message[0]+'/'+$message[1])
    }
    # Windows validates synthetic arrival/removal broadcasts; exercise those hook branches directly.
    foreach($deviceEvent in @(0x8000,0x8004)){
        $timer.Stop();$messageArguments=[object[]]@($handle,[int]0x219,[IntPtr]$deviceEvent,[IntPtr]::Zero,$false)
        [void]$ui.GetType().GetMethod('DisplayMessage',$flags).Invoke($ui,$messageArguments)
        Assert ($timer.IsEnabled -and -not $messageArguments[4]) 'Monitor arrival/removal notification was ignored or swallowed'
    }
    $timer.Stop();[void][DisplayHotplugMessages]::SendMessage($handle,0x219,[IntPtr]0x1234,[IntPtr]::Zero)
    Assert (-not $timer.IsEnabled) 'Unrelated device message scheduled a display query'
    [void][DisplayHotplugMessages]::SendMessage($handle,0x7e,[IntPtr]::Zero,[IntPtr]::Zero)
    Pump 60
    Assert ($selector.Items[0].MonitorId -eq $first.MonitorId) 'Debounced refresh ran before its delay'
    [void][DisplayHotplugMessages]::SendMessage($handle,0x7e,[IntPtr]::Zero,[IntPtr]::Zero)
    Pump 300
    Assert (-not $timer.IsEnabled) 'Debounced display refresh became a repeating poll'
    Assert ($selector.Items.Count -eq $native.Count) 'Native display event did not restore the actual active topology'
    for($i=0;$i -lt $native.Count;$i++){Assert ($selector.Items[$i].MonitorId -eq $native[$i].MonitorId) 'Native refresh list contains a stale display'}
    Assert (@($ui.Configuration.Displays|Where-Object {$_.DisplayId -in @($first.MonitorId,$second.MonitorId)}).Count -eq 2) 'Native refresh discarded actions for absent screens'
    $running=$ui.GetType().GetField('displayRefreshRunning',$flags);$pending=$ui.GetType().GetField('displayRefreshPending',$flags)
    $running.SetValue($ui,$true)
    try{
        [void][DisplayHotplugMessages]::SendMessage($handle,0x7e,[IntPtr]::Zero,[IntPtr]::Zero)
        Assert ($pending.GetValue($ui) -and -not $timer.IsEnabled) 'Notification during a refresh was lost or started a nested query'
        $pending.SetValue($ui,$false);[void]$refresh.Invoke($ui,$null)
        Assert ($pending.GetValue($ui) -and -not $timer.IsEnabled) 'Reentrant refresh did not retain a deferred request'
    }finally{$running.SetValue($ui,$false)}
    [void]$refresh.Invoke($ui,$null)
    Assert (-not $pending.GetValue($ui)) 'Successful refresh retained a stale deferred request'
    $window.Close()
    Assert (-not $timer.IsEnabled -and $null -eq $ui.GetType().GetField('displayMessageSource',$flags).GetValue($ui)) 'Closing the constructor left its display observer attached'
    [void]$ui.GetType().GetMethod('QueueDisplayRefresh',$flags).Invoke($ui,$null)
    Assert (-not $timer.IsEnabled) 'Closed constructor restarted its refresh timer'
    'PASS: '+$script:checks+' checks; active-only selector, disconnect/reconnect, independent saved actions, empty topology, stable selection, native hidden-window notifications, debounce and cleanup. No display settings were changed.'
}finally{if(-not $ui.GetType().GetField('displayObserverClosed',$flags).GetValue($ui)){$window.Close()}}
