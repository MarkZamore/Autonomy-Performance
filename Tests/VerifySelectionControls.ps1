param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
$script:assertions=0
function Assert($condition,$message){$script:assertions++;if(-not $condition){throw $message}}
function Toggle($control){[void]$control.GetType().GetMethod('OnToggle',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($control,@())}
function Click-SwitchSurface($control,$source){
    $event=[Windows.Input.MouseButtonEventArgs]::new([Windows.Input.Mouse]::PrimaryDevice,0,[Windows.Input.MouseButton]::Left)
    $event.RoutedEvent=[Windows.UIElement]::PreviewMouseLeftButtonDownEvent
    $event.Source=$source
    $control.RaiseEvent($event)
    return $event.Handled
}
function Assert-NoRing($control){
    [void]$control.ApplyTemplate()
    $ring=$control.Template.FindName('FocusRing',$control)
    Assert ($null -eq $ring -or $ring.Visibility -eq 'Collapsed') ('Selection shows an outline: '+$control.Name)
    Assert ($null -eq $control.FocusVisualStyle) ('Default focus outline remains: '+$control.Name)
}
function Get-UiElements {
    $pending=New-Object 'Collections.Generic.Queue[Windows.DependencyObject]'
    $seen=New-Object 'Collections.Generic.HashSet[Windows.DependencyObject]'
    $pending.Enqueue($win);$pending.Enqueue($win.FindName('IconGrid'))
    while($pending.Count -gt 0){
        $element=$pending.Dequeue();if(-not $seen.Add($element)){continue}
        $element
        if($element -is [Windows.Controls.Control]){[void]$element.ApplyTemplate()}
        foreach($child in [Windows.LogicalTreeHelper]::GetChildren($element)){if($child -is [Windows.DependencyObject]){$pending.Enqueue($child)}}
        if($element -is [Windows.Media.Visual] -or $element -is [Windows.Media.Media3D.Visual3D]){
            for($i=0;$i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($element);$i++){$pending.Enqueue([Windows.Media.VisualTreeHelper]::GetChild($element,$i))}
        }
    }
}
try{
    foreach($name in @('PowerUnchanged','FanSelector','DisplayEnabled','BrightnessEnabled')){Assert ($null -eq $win.FindName($name)) ('Obsolete control remains: '+$name)}
    Assert (-not $ui.Configuration.HasActions) 'Initial script contains actions'
    $contentChoices=@($win.FindName('ContentBrightnessSelector').Items)
    Assert ($contentChoices.Count -eq 3 -and @($contentChoices|Where-Object {$_.Value -eq 2}).Count -eq 0 -and $contentChoices[0].Value -eq 0) 'Content brightness still offers a removed off action or lost unchanged'
    $groups=@(@{Names=@('Efficiency','Balanced','BestPerformance');Field='PowerMode'},@{Names=@('FanWhisper','FanPerformance','FanStandard');Field='FanProfile'})
    foreach($group in $groups){
        for($i=0;$i -lt $group.Names.Count;$i++){
            $check=$win.FindName($group.Names[$i]);Assert ($check -is [Windows.Controls.CheckBox] -and -not $check.IsThreeState) ('Mode choice is not a normal checkbox: '+$group.Names[$i])
            Toggle $check
            Assert ($check.IsChecked -eq $true -and $ui.Configuration.($group.Field) -eq ($i+1)) ('Click did not select mode: '+$group.Names[$i])
            foreach($other in $group.Names){Assert (($win.FindName($other).IsChecked -eq $true) -eq ($other -eq $group.Names[$i])) ('Mode choices are not exclusive: '+$other)}
            Toggle $check
            Assert ($check.IsChecked -eq $false -and $ui.Configuration.($group.Field) -eq 0) ('Second click did not clear mode: '+$group.Names[$i])
        }
        foreach($name in $group.Names){Toggle ($win.FindName($name))}
        Assert ($ui.Configuration.($group.Field) -eq 3) ('Replacing selected mode failed: '+$group.Field)
        Toggle ($win.FindName($group.Names[2]))
    }
    Assert (-not $ui.Configuration.HasActions) 'Clearing both mode groups leaves an action'
    $saver=$win.FindName('EnergySaverSwitch');[void]$saver.ApplyTemplate()
    foreach($source in @($saver.Content)){
        foreach($value in @(1,2,0)){
            Assert (Click-SwitchSurface $saver $source) 'Switch pointer input was not handled'
            Assert ($ui.Configuration.EnergySaver -eq $value) 'Switch surface did not cycle unchanged -> on -> off -> unchanged'
            Assert (($value -eq 0) -eq (-not $ui.Configuration.HasActions)) 'Switch neutral state disagrees with script actions'
        }
    }
    $position=$ui.GetType().GetMethod('SwitchPosition',[Reflection.BindingFlags]'Static,NonPublic')
    foreach($case in @(@($true,0.1,$false),@($true,0.5,$null),@($true,0.9,$true),@($false,0.1,$false),@($false,0.9,$true))){
        $state=$position.Invoke($null,[object[]]@($case[0],[double]$case[1]))
        Assert ($state -eq $case[2]) 'Pointer position does not choose the corresponding switch track state'
    }
    $saver.IsEnabled=$false
    Assert (-not (Click-SwitchSurface $saver $saver.Content) -and $null -eq $saver.IsChecked) 'Disabled switch accepted pointer input'
    $saver.IsEnabled=$true
    $handle=[Windows.Interop.WindowInteropHelper]::new($win).EnsureHandle()
    $inputSource=[Windows.Interop.HwndSource]::FromHwnd($handle)
    foreach($value in @(1,2,0)){
        $key=[Windows.Input.KeyEventArgs]::new([Windows.Input.Keyboard]::PrimaryDevice,$inputSource,0,[Windows.Input.Key]::Space)
        $key.RoutedEvent=[Windows.Input.Keyboard]::PreviewKeyDownEvent;$saver.RaiseEvent($key)
        Assert ($key.Handled -and $ui.Configuration.EnergySaver -eq $value) 'Space does not use the same on-first cycle as a pointer click'
    }
    $display=$win.FindName('DisplaySelector');$rates=$win.FindName('RefreshSelector')
    Assert ($rates.Items.Count -ge 1 -and $rates.SelectedItem.Rate -eq 0 -and $ui.Configuration.Displays.Count -eq 0) 'Refresh list has no neutral initial option'
    if($display.Items.Count -gt 0){
        Assert ($rates.IsEnabled) 'Refresh list needs a removed enabling checkbox'
        $rate=@($rates.Items|Where-Object {$_.Rate -gt 0})|Select-Object -First 1
        if($rate){
            $rates.SelectedItem=$rate
            $target=@($ui.Configuration.Displays|Where-Object {$_.DisplayId -eq $display.SelectedItem.MonitorId})
            Assert ($target.Count -eq 1 -and $target[0].RefreshRate -eq $rate.Rate) 'Refresh choice alone does not create an action on its display'
            $screenNumber=$display.SelectedItem.DeviceName -replace '^.*DISPLAY',''
            Assert ($win.FindName('ScriptPreview').Text.Contains('Экран '+$screenNumber+' - ')) 'Refresh preview lost its numbered display'
            $rates.SelectedIndex=0
            Assert ($ui.Configuration.Displays.Count -eq 0 -and -not $ui.Configuration.HasActions) 'Neutral refresh choice does not remove its action'
        }
    }
    $brightness=$win.FindName('BrightnessToggleButton');$slider=$win.FindName('BrightnessSlider')
    Assert ($brightness -is [Windows.Controls.CheckBox] -and -not $brightness.IsThreeState -and -not $brightness.IsChecked -and -not $slider.IsEnabled) 'Brightness switch is not initially disabled and binary'
    Toggle $brightness;$slider.Value=37
    $target=@($ui.Configuration.Displays|Where-Object {$_.DisplayId -eq $display.SelectedItem.MonitorId})
    Assert ($slider.IsEnabled -and $target.Count -eq 1 -and $target[0].Brightness -eq 37 -and $win.FindName('ScriptPreview').Text.Contains('37%')) 'Brightness switch does not enable display slider/config/preview'
    Assert (-not (Click-SwitchSurface $brightness $slider) -and $brightness.IsChecked -eq $true -and $slider.Value -eq 37) 'Brightness slider pointer input toggles its parent switch'
    Toggle $brightness
    Assert ((Click-SwitchSurface $brightness $brightness.Content) -and $brightness.IsChecked -eq $true) 'First click on the binary brightness row does not enable it'
    Assert ((Click-SwitchSurface $brightness $brightness.Content) -and $brightness.IsChecked -eq $false -and -not $slider.IsEnabled) 'Second click on the binary brightness row does not disable it'
    $night=$win.FindName('NightLightSwitch');$nightSlider=$win.FindName('NightLightSlider')
    Assert (-not (Click-SwitchSurface $night $nightSlider) -and $null -eq $night.IsChecked) 'Night light slider pointer input toggles its parent switch'
    Assert (-not $slider.IsEnabled -and $ui.Configuration.Displays.Count -eq 0 -and $slider.Value -eq 37) 'Disabling brightness does not preserve its editor value'
    Toggle $brightness
    Assert (@($ui.Configuration.Displays|Where-Object {$_.DisplayId -eq $display.SelectedItem.MonitorId})[0].Brightness -eq 37) 'Re-enabling brightness lost its value'
    Toggle $brightness
    foreach($section in @('PerformanceSection','WallpaperSection','ArmourySection','PowerSection','DisplaySection','AdditionalSection')){$win.FindName($section).IsExpanded=$true}
    $grid=$win.FindName('RootGrid');$grid.Measure([Windows.Size]::new(984,661));$grid.Arrange([Windows.Rect]::new(0,0,984,661));$grid.UpdateLayout()
    $iconButton=$win.FindName('IconButton')
    Assert ($iconButton.ToolTip -eq 'Выбрать иконку') 'Icon button tooltip was removed or renamed'
    $elements=@(Get-UiElements);$tooltipOwners=@($elements|Where-Object {$null -ne [Windows.Controls.ToolTipService]::GetToolTip($_)})
    $taskbar=$win.FindName('TaskbarRepairSwitch')
    foreach($owner in $tooltipOwners){Assert (([object]::ReferenceEquals($owner,$iconButton)) -or ([object]::ReferenceEquals($owner,$taskbar)) -or ($owner -is [Windows.Controls.Button] -and $owner.Tag -is [int] -and [int]$owner.Tag -lt 5)) 'Unexpected tooltip outside icon choices and taskbar repair'}
    Assert ($taskbar.ToolTip -eq "Режим 1: Главная панель задач и панель задач, в которой открыто окно`nРежим 2: Панель задач, в которой открыто окно") 'Taskbar tooltip does not show the two final modes on separate lines'
    foreach($tile in $win.FindName('IconGrid').Children){Assert (-not [string]::IsNullOrEmpty([Windows.Automation.AutomationProperties]::GetName($tile))) 'Icon tile lost its accessible label'}
    $focusedKey=[Windows.UIElement].GetField('IsKeyboardFocusedPropertyKey',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
    foreach($name in @('PresetAutonomyButton','PresetPerformanceButton','IconButton','SaveButton','ResetScriptButton','DisplaySelector','RefreshSelector','ScriptPreview')){
        $control=$win.FindName($name);$control.SetValue($focusedKey,$true);$grid.UpdateLayout();Assert-NoRing $control;$control.SetValue($focusedKey,$false)
    }
    $reset=$win.FindName('ResetScriptButton');[void]$reset.ApplyTemplate();$resetRoot=[Windows.Media.VisualTreeHelper]::GetChild($reset,0)
    Assert ($reset.BorderThickness -eq [Windows.Thickness]::new(0) -and $resetRoot -is [Windows.Controls.Grid] -and $resetRoot.Background.ToString() -eq '#00FFFFFF' -and $null -eq $reset.Template.FindName('Surface',$reset)) 'Reset icon still has a button surface or outline'
    $reset.IsEnabled=$false;$grid.UpdateLayout()
    Assert ($resetRoot.Background.ToString() -eq '#00FFFFFF' -and $null -eq $reset.Template.FindName('Surface',$reset)) 'Disabled reset icon restores a button surface'
    # An isolated popup item exercises selection without opening a native window.
    $item=New-Object Windows.Controls.ComboBoxItem;$item.Content='Выбранный пункт';$item.Style=$app.Resources[[Windows.Controls.ComboBoxItem]];$item.IsSelected=$true
    [void]$item.ApplyTemplate();$item.Measure([Windows.Size]::new(300,40));$item.Arrange([Windows.Rect]::new(0,0,300,40));$item.UpdateLayout()
    $surface=$item.Template.FindName('ItemSurface',$item);$mark=$item.Template.FindName('SelectionMark',$item)
    Assert ($surface.Background.ToString() -eq $app.Resources['Brush.SubtleHover'].ToString() -and $item.Foreground.ToString() -eq $app.Resources['Brush.Text'].ToString()) 'Selected list item does not preserve its original neutral fill and light text'
    Assert ($null -ne $mark -and $mark.Visibility -eq 'Visible' -and $mark.Background.ToString() -eq $app.Resources['Brush.CheckFill'].ToString()) 'Selected list item does not use the muted blue indicator'
    Assert-NoRing $item
    $art=Join-Path $PSScriptRoot 'Artifacts\SelectionControls';[void][IO.Directory]::CreateDirectory($art)
    [pscustomobject]@{Passed=$true;Assertions=$script:assertions;RepeatedClickClears=$true;RefreshWithoutCheckbox=$true;BrightnessBinarySwitch=$true;OnFirstPointerAndSpace=$true;SliderInputPreserved=$true;ResetWithoutOutline=$true;IconAndTaskbarTooltips=$true;NoFocusOutline=$true;ListUsesBlueIndicator=$true;NoSystemSettingsApplied=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
    'PASS: '+$script:assertions+' assertions; on-first pointer/Space cycle, binary brightness and slider input, reset without outline, mode deselection, refresh choices, tooltips and original list styling. No system settings applied.'
}finally{$win.Close()}
