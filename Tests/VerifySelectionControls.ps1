param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
$script:assertions=0
function Assert($condition,$message){$script:assertions++;if(-not $condition){throw $message}}
function Toggle($control){[void]$control.GetType().GetMethod('OnToggle',[Reflection.BindingFlags]'NonPublic,Instance').Invoke($control,@())}
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
    Toggle $brightness
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
    # An isolated popup item exercises selection without opening a native window.
    $item=New-Object Windows.Controls.ComboBoxItem;$item.Content='Выбранный пункт';$item.Style=$app.Resources[[Windows.Controls.ComboBoxItem]];$item.IsSelected=$true
    [void]$item.ApplyTemplate();$item.Measure([Windows.Size]::new(300,40));$item.Arrange([Windows.Rect]::new(0,0,300,40));$item.UpdateLayout()
    $surface=$item.Template.FindName('ItemSurface',$item);$mark=$item.Template.FindName('SelectionMark',$item)
    Assert ($surface.Background.ToString() -eq $app.Resources['Brush.SubtleHover'].ToString() -and $item.Foreground.ToString() -eq $app.Resources['Brush.Text'].ToString()) 'Selected list item does not preserve its original neutral fill and light text'
    Assert ($null -ne $mark -and $mark.Visibility -eq 'Visible' -and $mark.Background.ToString() -eq $app.Resources['Brush.CheckFill'].ToString()) 'Selected list item does not use the muted blue indicator'
    Assert-NoRing $item
    $art=Join-Path $PSScriptRoot 'Artifacts\SelectionControls';[void][IO.Directory]::CreateDirectory($art)
    [pscustomobject]@{Passed=$true;Assertions=$script:assertions;RepeatedClickClears=$true;RefreshWithoutCheckbox=$true;BrightnessBinarySwitch=$true;IconAndTaskbarTooltips=$true;NoFocusOutline=$true;ListUsesBlueIndicator=$true;NoSystemSettingsApplied=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
    'PASS: '+$script:assertions+' assertions; repeat-click mode deselection, display refresh without checkbox, binary brightness switch, icon and taskbar tooltips, accessible icon choices and original list styling with muted blue indicator. No system settings applied.'
}finally{$win.Close()}
