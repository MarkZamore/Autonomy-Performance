param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application
$ui=New-Object ConstructorWindow
$win=$ui.Window
$script:checks=0
$script:notes=New-Object 'Collections.Generic.List[string]'
$art=Join-Path $PSScriptRoot 'Artifacts\Revision8'
[void][IO.Directory]::CreateDirectory($art)
function Assert($condition,$message){$script:checks++;if(-not $condition){throw $message}}
function Control($name){$c=$win.FindName($name);Assert ($null -ne $c) ('Missing control: '+$name);return $c}
function Label($control){if($control.Content -is [Windows.Controls.TextBlock]){return $control.Content.Text};return [string]$control.Content}
function Paragraphs {return @((Control 'ScriptPreview').Text -split '(?:\r?\n){2}')}
function Display-Actions {return @($ui.Configuration.GetDisplayActions())}
function Display-Action($id){return @(Display-Actions | Where-Object DisplayId -eq $id)|Select-Object -First 1}
function Screen-Label($d){return ($d.Label -split ' - ',2)[0]}
function Layout($width=984,$height=661){$g=Control 'RootGrid';$g.Measure([Windows.Size]::new($width,$height));$g.Arrange([Windows.Rect]::new(0,0,$width,$height));$g.UpdateLayout();return $g}
function Descendants($parent){
    $pending=New-Object 'Collections.Generic.Queue[Windows.DependencyObject]';$pending.Enqueue($parent)
    while($pending.Count){$n=$pending.Dequeue();$n;if($n -is [Windows.Controls.Control]){[void]$n.ApplyTemplate()};if($n -is [Windows.Media.Visual]){for($i=0;$i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($n);$i++){$pending.Enqueue([Windows.Media.VisualTreeHelper]::GetChild($n,$i))}}}
}
function Copy-Configuration {return [System.Web.Script.Serialization.JavaScriptSerializer]::new().Serialize($ui.Configuration)}
$readPresetCapabilities=[ConstructorWindow].GetMethod('ReadPresetCapabilities',[Reflection.BindingFlags]'Instance,NonPublic')
function Assert-PresetIntegration([bool]$performance){
    $ui.ApplyPreset($performance)
    $capabilities=$readPresetCapabilities.Invoke($ui,[object[]]@($performance))
    $expected=[PresetBuilder]::Build($performance,$capabilities)
    Assert ((Copy-Configuration) -eq [System.Web.Script.Serialization.JavaScriptSerializer]::new().Serialize($expected)) 'Preset UI lost or changed a capability-filtered action'
    Assert ($ui.Configuration.Brightness -eq -1 -and @($ui.Configuration.Displays|Where-Object {$_.Brightness -ge 0 -or $_.ContentBrightness -ne 0}).Count -eq 0) 'Preset retained an unrelated brightness action'
    Assert ($ui.Configuration.NightLight -eq 0 -and $ui.Configuration.BatteryCare -eq 0 -and $ui.Configuration.ClockSeconds -eq 0) 'Preset retained an unrelated global action'
    Assert ($ui.Configuration.DarkTheme -eq $(if($capabilities.DarkTheme){1}else{0}) -and $ui.Configuration.TaskbarRepair -eq $(if($capabilities.TaskbarRepair){2}else{0})) 'Preset missed the requested theme/taskbar choice'
}
try {
    $preview=Control 'ScriptPreview';$reset=Control 'ResetScriptButton';$display=Control 'DisplaySelector'
    $resolution=Control 'ResolutionSelector';$rate=Control 'RefreshSelector';$scale=Control 'ScaleSelector'
    $cabc=Control 'ContentBrightnessSelector';$hdr=Control 'HdrSwitch';$dynamic=Control 'DynamicRefreshSwitch'
    $brightness=Control 'BrightnessToggleButton';$slider=Control 'BrightnessSlider'
    Assert (-not $ui.Configuration.HasActions -and -not (Control 'SaveButton').IsEnabled) 'Initial script is not empty'
    $categoryNames=@((Control 'SettingsScroll').Content.Children|Where-Object {$_ -is [Windows.Controls.Expander]}|ForEach-Object Name)
    Assert (($categoryNames -join ',') -eq 'PowerSection,DisplaySection,AdditionalSection,ArmourySection,PerformanceSection,WallpaperSection') 'Category ordering differs from requested order'
    Assert ((Control 'PowerSection').Content.Children[0].Name -eq 'EnergySaverSwitch') 'Energy saver is not the first power setting'
    Assert ($null -eq $win.FindName('AutonomySection')) 'Removed autonomy category remains'
    foreach($name in $categoryNames){Assert (-not (Control $name).IsExpanded) ('Category starts expanded: '+$name)}
    $displayChildren=@((Control 'DisplaySection').Content.Children)
    $childNames=@($displayChildren|ForEach-Object Name)
    Assert ([Array]::IndexOf($childNames,'ScaleSelector') -lt [Array]::IndexOf($childNames,'ResolutionSelector')) 'Scale does not precede resolution'
    Assert ([Array]::IndexOf($childNames,'HdrSwitch') -eq [Array]::IndexOf($childNames,'RefreshSelector')+1) 'HDR is not immediately below refresh rate'
    Assert ([Array]::IndexOf($childNames,'AdaptiveBrightnessSwitch') -eq [Array]::IndexOf($childNames,'NightLightSwitch')+1) 'Ambient brightness is not below night light'
    Assert (@($displayChildren|Where-Object {$_ -is [Windows.Controls.TextBlock] -and $_.Text -in @('Экран','Частота обновления','Яркость','Изменять яркость с учетом содержимого','Изменять яркость с учётом содержимого')}).Count -eq 0) 'Removed display label remains'
    Assert ($cabc.Items[0].ToString() -eq 'Не менять яркость с учётом содержимого') 'Neutral content-brightness caption is missing'
    Assert ((Label (Control 'DarkThemeSwitch')) -eq 'Тёмная тема Windows') 'Dark theme caption changed'
    Assert ((Label (Control 'UsbPowerSwitch')) -eq 'Останавливать USB-устройства, когда экран выключен, для экономии заряда батареи') 'USB caption does not match the requested wording'
    Assert ((Label (Control 'ClockSecondsSwitch')) -eq 'Секунды в часах на панели задач') 'Clock-seconds caption changed'
    $presence=@('PresenceSleepSwitch','PresenceWakeSwitch','PresenceDimSwitch','PresenceOnlookerSwitch')
    $presenceText=@('Отключать экран, когда я ухожу','Пробуждать мое устройство, когда я приближаюсь','Автоматически уменьшать яркость, когда я отвожу взгляд','Обнаружить, когда другие люди смотрят на мой экран')
    $additional=@((Control 'AdditionalSection').Content.Children|ForEach-Object Name)
    $batteryIndex=[Array]::IndexOf($additional,'BatteryPercentageSwitch')
    for($i=0;$i -lt $presence.Count;$i++){
        Assert ($additional[$batteryIndex+$i+1] -eq $presence[$i]) 'Presence actions are not immediately after battery percentage in the requested order'
        Assert ((Label (Control $presence[$i])) -eq $presenceText[$i]) ('Presence caption changed: '+$presence[$i])
    }
    $tip=[string](Control 'TaskbarRepairSwitch').ToolTip
    Assert ($tip -eq "Режим 1: Главная панель задач и панель задач, в которой открыто окно`nРежим 2: Панель задач, в которой открыто окно") 'Taskbar repair does not have the exact two-line explanation'
    $windowStream=$assembly.GetManifestResourceStream('Window');$reader=[IO.StreamReader]::new($windowStream)
    try{$windowXaml=$reader.ReadToEnd()}finally{$reader.Dispose()}
    Assert ($windowXaml -notmatch 'вентиляторов|без Steam') 'Outdated fan/Steam text remains in the interface'
    Assert ($brightness -is [Windows.Controls.CheckBox] -and -not $brightness.IsThreeState) 'Brightness does not use a binary switch'
    [void]$brightness.ApplyTemplate()
    $state=$brightness.Template.FindName('SwitchState',$brightness)
    Assert ($null -ne $state) 'Brightness switch has no state label'
    Assert ($state.Text -eq 'Не менять') 'Initial brightness label is not unchanged'
    $slider.Value=37;$brightness.IsChecked=$true
    Assert ($slider.IsEnabled -eq $brightness.IsEnabled) 'Brightness slider does not follow the switch and capability'
    Assert ($state.Text -eq 'Вкл.') 'Enabled brightness label is not On'
    $brightness.IsChecked=$false
    Assert (-not $slider.IsEnabled -and $slider.Value -eq 37) 'Brightness disabling loses its editor value or leaves the slider active'
    Assert ($ui.Configuration.Brightness -eq -1) 'UI emitted an unintended global brightness action'
    Assert (@(Display-Actions|Where-Object {$_.Brightness -ge 0}).Count -eq 0) 'Disabled brightness still exports an action'
    $ui.ResetScript()
    # Native recommendations are queried from the selected target, not inferred from current settings.
    for($i=0;$i -lt $display.Items.Count;$i++){
        $display.SelectedIndex=$i;$d=$display.SelectedItem
        Assert ($rate.Items[0].ToString() -eq 'Не менять частоту обновления') 'Neutral refresh caption changed'
        $recommended=[HdrControl]::InspectRecommendedScale($d.MonitorId)
        foreach($s in $scale.Items){Assert ($s.ToString().Contains('(рекомендуется)') -eq ($s.Percent -ne 0 -and $s.Percent -eq $recommended)) 'Scale recommendation does not match the native target recommendation'}
        [uint32]$rw=0;[uint32]$rh=0;$known=[HdrControl]::TryInspectPreferredResolution($d.MonitorId,[ref]$rw,[ref]$rh)
        foreach($r in $resolution.Items){Assert ($r.ToString().Contains('(рекомендуется)') -eq ($known -and $r.Width -eq $rw -and $r.Height -eq $rh)) 'Resolution recommendation does not match the native preferred mode'}
        $enabled=$false;$available=[HdrControl]::InspectDynamic($d.MonitorId,[ref]$enabled)
        if(-not $available){
            $recommended=@($d.Resolutions|Where-Object {$_.Width -eq $d.RecommendedWidth -and $_.Height -eq $d.RecommendedHeight})|Select-Object -First 1
            [uint32]$width=0;[uint32]$height=0;[uint32]$refresh=0
            if($recommended){$width=$recommended.Width;$height=$recommended.Height;$refresh=($recommended.Rates|Measure-Object -Maximum).Maximum}else{if($d.Rates.Count){$refresh=($d.Rates|Measure-Object -Maximum).Maximum}}
            $available=[HdrControl]::InspectDynamic($d.MonitorId,$width,$height,$refresh,[ref]$enabled)
        }
        Assert ($dynamic.IsEnabled -eq $available) 'DRR is gated differently from native per-display capability'
        $supported=$false;$enabled=$false;$known=[HdrControl]::TryInspect($d.MonitorId,[ref]$supported,[ref]$enabled)
        Assert ($hdr.IsEnabled -eq ($known -and $supported)) 'HDR is gated differently from native per-display capability'
    }
    # Edit higher-numbered screens first to distinguish insertion order from screen-number order.
    $expected=New-Object 'Collections.Generic.Dictionary[string,object]'
    for($i=$display.Items.Count-1;$i -ge 0;$i--){
        $display.SelectedIndex=$i;$d=$display.SelectedItem
        $r=@($resolution.Items|Where-Object {$_.Width -ne 0})|Select-Object -First 1
        Assert ($null -ne $r) 'Connected display exposes no usable resolution'
        $resolution.SelectedItem=$r
        $h=@($rate.Items|Where-Object {$_.Rate -ne 0})|Select-Object -Last 1
        Assert ($null -ne $h) 'Selected resolution exposes no usable refresh rate'
        $rate.SelectedItem=$h
        $s=@($scale.Items|Where-Object {$_.Percent -ne 0})|Select-Object -Last 1
        if($s){$scale.SelectedItem=$s}
        if($hdr.IsEnabled){$hdr.IsChecked=$true}
        if($dynamic.IsEnabled){$dynamic.IsChecked=$false}
        if($brightness.IsEnabled){$brightness.IsChecked=$true;$slider.Value=27+$i}
        $expected[$d.MonitorId]=[pscustomobject]@{Id=$d.MonitorId;Label=$d.Label;Width=$r.Width;Height=$r.Height;Rate=$h.Rate;Scale=$(if($s){$s.Percent}else{0});Hdr=$(if($hdr.IsEnabled){1}else{0});Dynamic=$(if($dynamic.IsEnabled){2}else{0});Brightness=$(if($brightness.IsEnabled){27+$i}else{-1})}
        Assert (@(Display-Actions).Count -eq $expected.Count) 'Changing the selected screen replaced a previous screen action'
    }
    $actions=@(Display-Actions)
    Assert ($ui.Configuration.DisplayWidth -eq 0 -and $ui.Configuration.RefreshRate -eq 0 -and $ui.Configuration.DisplayScale -eq 0 -and $ui.Configuration.Hdr -eq 0 -and $ui.Configuration.DynamicRefresh -eq 0 -and $ui.Configuration.ContentBrightness -eq 0) 'New UI exports both legacy and array display actions'
    for($i=0;$i -lt $display.Items.Count;$i++){
        $display.SelectedIndex=$i;$d=$display.SelectedItem;$e=$expected[$d.MonitorId];$actual=Display-Action $d.MonitorId
        Assert ($actions[$i].DisplayId -eq $d.MonitorId) 'Display array is not ordered by screen number'
        foreach($pair in @(@('DisplayWidth','Width'),@('DisplayHeight','Height'),@('RefreshRate','Rate'),@('DisplayScale','Scale'),@('Hdr','Hdr'),@('DynamicRefresh','Dynamic'),@('Brightness','Brightness'))){Assert ($actual.($pair[0]) -eq $e.($pair[1])) ('Screen action changed while switching displays: '+$pair[0])}
        Assert ($resolution.SelectedItem.Width -eq $e.Width -and $resolution.SelectedItem.Height -eq $e.Height -and $rate.SelectedItem.Rate -eq $e.Rate) 'Display editor does not restore a screen selection'
        Assert ($scale.SelectedItem.Percent -eq $e.Scale) 'Display editor does not restore per-screen scale'
        Assert (($hdr.IsChecked -eq $true) -eq ($e.Hdr -eq 1)) 'Display editor does not restore per-screen HDR'
        Assert ($brightness.IsChecked -eq ($e.Brightness -ge 0)) 'Display editor does not restore per-screen brightness'
    }
    if($display.Items.Count -lt 2){$script:notes.Add('Only one physical screen available; multi-target export is additionally covered by a synthetic configuration.')}else{
        $text=$preview.Text;$last=-1
        foreach($d in $display.Items){$first=$text.IndexOf((Screen-Label $d),[StringComparison]::Ordinal);Assert ($first -gt $last) 'Preview order follows edit order instead of screen numbering';$last=$first}
    }
    $export=Join-Path $art 'MultiDisplay.exe'
    if($ui.Configuration.HasActions){[ExecutableExporter]::Save($export,$ui.Configuration,[EmbeddedAssets]::Read('Icon0'));$read=[PeConfiguration]::Read($export);Assert ([System.Web.Script.Serialization.JavaScriptSerializer]::new().Serialize($read) -eq (Copy-Configuration)) 'Multi-display export lost configuration values'}
    # Removing a single display action must keep every other field and every other screen.
    if($display.Items.Count){
        $target=$display.Items[0];$before=@(Display-Actions);$beforeTarget=Display-Action $target.MonitorId
        $paragraphs=Paragraphs;$index=-1
        for($j=0;$j -lt $paragraphs.Count;$j++){if($paragraphs[$j].Contains((Screen-Label $target)) -and $paragraphs[$j] -match 'Гц'){$index=$j;break}}
        Assert ($index -ge 0) 'No refresh action paragraph found for removal'
        $ui.RemovePreviewAction($index);$after=Display-Action $target.MonitorId
        Assert ($after.RefreshRate -eq 0 -and $after.DisplayWidth -eq $beforeTarget.DisplayWidth -and $after.DisplayHeight -eq $beforeTarget.DisplayHeight -and $after.DisplayScale -eq $beforeTarget.DisplayScale -and $after.Hdr -eq $beforeTarget.Hdr -and $after.Brightness -eq $beforeTarget.Brightness) 'Removing refresh also removed another target action'
        foreach($other in $before|Where-Object DisplayId -ne $target.MonitorId){$a=Display-Action $other.DisplayId;Assert ([System.Web.Script.Serialization.JavaScriptSerializer]::new().Serialize($a) -eq [System.Web.Script.Serialization.JavaScriptSerializer]::new().Serialize($other)) 'Removing one preview paragraph changed another display'}
        $display.SelectedItem=$target;Assert ($rate.SelectedItem.Rate -eq 0) 'Removing preview refresh does not update its editor'
    }
    (Control 'EnergySaverSwitch').IsChecked=$true;(Control 'DarkThemeSwitch').IsChecked=$true
    $paragraphs=Paragraphs;$darkIndex=-1
    for($j=0;$j -lt $paragraphs.Count;$j++){if($paragraphs[$j] -match 'тёмн|темн'){$darkIndex=$j;break}}
    Assert ($darkIndex -ge 0) 'Dark-theme paragraph cannot be found'
    $ui.RemovePreviewAction($darkIndex)
    Assert ($ui.Configuration.DarkTheme -eq 0 -and $ui.Configuration.EnergySaver -eq 1) 'Removing one global preview action changed another action'
    $before=Copy-Configuration;$ui.RemovePreviewAction(-1);$ui.RemovePreviewAction(2147483647)
    Assert ((Copy-Configuration) -eq $before) 'Out-of-range preview removal changed the script'
    # Text remains a normal selectable/copyable TextBox without a focus outline.
    Assert ($preview -is [Windows.Controls.TextBox] -and $preview.IsReadOnly) 'Preview lost text selection/copy support'
    $preview.SelectAll();Assert ($preview.SelectedText -eq $preview.Text) 'Select All no longer selects the complete script'
    Assert ([Windows.Input.ApplicationCommands]::Copy.CanExecute($null,$preview)) 'Copy command is disabled for selected preview text'
    Assert ($null -eq $preview.FocusVisualStyle) 'Preview uses a default focus outline'
    [void]$preview.ApplyTemplate();$ring=$preview.Template.FindName('FocusRing',$preview)
    Assert ($null -eq $ring -or $ring.Visibility -eq 'Collapsed') 'Preview template exposes a focus outline'
    $focusedKey=[Windows.UIElement].GetField('IsKeyboardFocusedPropertyKey',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
    $preview.SetValue($focusedKey,$true);[void](Layout 984 661)
    Assert ($null -eq $ring -or $ring.Visibility -eq 'Collapsed') 'Focusing selectable preview text reveals an outline'
    $preview.SetValue($focusedKey,$false)
    foreach($name in $categoryNames){(Control $name).IsExpanded=$true}
    foreach($name in @('WallpaperPlaybackSwitch','WallpaperAudioSwitch','WallpaperApplicationSwitch','ScreenSaverSwitch','UsbPowerSwitch','ClockSecondsSwitch')){(Control $name).IsChecked=$true}
    (Control 'TaskbarRepairSwitch').IsChecked=$true
    $grid=Layout 984 661
    $performance=Control 'PresetPerformanceButton';$battery=Control 'BatteryWear'
    $right=$performance.TranslatePoint([Windows.Point]::new($performance.ActualWidth,0),$grid).X
    $batteryPoint=$battery.TranslatePoint([Windows.Point]::new(0,0),$grid)
    Assert ($batteryPoint.X -ge $right) 'Battery-wear readout is not to the right of Performance'
    $resetPoint=$reset.TranslatePoint([Windows.Point]::new(0,0),$grid)
    Assert ($resetPoint.X -lt $preview.TranslatePoint([Windows.Point]::new($preview.ActualWidth,0),$grid).X) 'Reset action is outside the preview header'
    $previewHost=$preview.Template.FindName('PART_ContentHost',$preview)
    Assert ($previewHost -is [Windows.Controls.ScrollViewer]) 'Preview does not use a scrolling text host'
    [void]$previewHost.ApplyTemplate();$scrollbar=$previewHost.Template.FindName('PART_VerticalScrollBar',$previewHost);$presenter=$previewHost.Template.FindName('PART_ScrollContentPresenter',$previewHost)
    Assert ($null -ne $scrollbar -and $null -ne $presenter) 'Preview scrolling template is incomplete'
    $previewHost.ScrollToBottom();$grid.UpdateLayout()
    if($previewHost.ScrollableHeight -gt 0){
        $contentRight=$presenter.TranslatePoint([Windows.Point]::new($presenter.ActualWidth,0),$previewHost).X
        $barLeft=$scrollbar.TranslatePoint([Windows.Point]::new(0,0),$previewHost).X
        Assert ($contentRight -le $barLeft) 'Preview scrollbar overlaps the text area'
    }else{
        $script:notes.Add('The selected script fits at normal size; forced long text verifies the same preview template.')
        $preview.Text=((1..60|ForEach-Object {'Проверка длинного предпросмотра настроек - '+$_}) -join "`r`n`r`n")
        $grid=Layout 804 521;$previewHost.ScrollToBottom();$grid.UpdateLayout()
        Assert ($previewHost.ScrollableHeight -gt 0) 'A long preview has no scrollable text host'
        $contentRight=$presenter.TranslatePoint([Windows.Point]::new($presenter.ActualWidth,0),$previewHost).X
        $barLeft=$scrollbar.TranslatePoint([Windows.Point]::new(0,0),$previewHost).X
        Assert ($contentRight -le $barLeft) 'Preview scrollbar overlaps the text area at minimum size'
    }
    foreach($size in @(@(984,661),@(804,521))){$grid=Layout $size[0] $size[1];foreach($name in @('PresetAutonomyButton','PresetPerformanceButton','IconButton','SaveButton','BatteryWear')){$c=Control $name;$p=$c.TranslatePoint([Windows.Point]::new(0,0),$grid);Assert ($p.X -ge 0 -and $p.Y -ge 0 -and $p.X+$c.ActualWidth -le $grid.ActualWidth+1 -and $p.Y+$c.ActualHeight -le $grid.ActualHeight+1) ('Footer content leaves window at '+$size[0]+': '+$name)}}
    # Reset and presets replace every target's actions while preserving the icon choice.
    (Control 'IconGrid').Children[3].RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
    $chosen=$ui.IconChoice.BuiltInIndex;$ui.ResetScript()
    Assert (-not $ui.Configuration.HasActions -and @(Display-Actions).Count -eq 0 -and -not (Control 'SaveButton').IsEnabled) 'Reset retained an action or enabled Save'
    Assert ($ui.IconChoice.BuiltInIndex -eq $chosen) 'Reset changed the icon choice'
    Assert ($preview.Text -eq 'Действия не выбраны.') 'Reset did not clear preview text'
    Assert-PresetIntegration $true
    Assert ($ui.IconChoice.BuiltInIndex -eq $chosen) 'Preset changed the icon choice'
    Assert-PresetIntegration $false
    Assert ($ui.IconChoice.BuiltInIndex -eq $chosen) 'Autonomy preset changed the icon choice'
    $reset.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
    Assert (-not $ui.Configuration.HasActions) 'Reset button is not connected to the reset operation'
    # Even without two physical screens, resource export must retain independent monitor identities.
    $synthetic=New-Object ModeConfiguration
    $a=New-Object DisplayModeConfiguration;$a.DisplayId='TEST\\DISPLAY1';$a.DisplayWidth=1920;$a.DisplayHeight=1080;$a.RefreshRate=60;$a.DisplayScale=125;$a.Hdr=2;$a.Brightness=31
    $b=New-Object DisplayModeConfiguration;$b.DisplayId='TEST\\DISPLAY2';$b.DisplayWidth=2560;$b.DisplayHeight=1440;$b.RefreshRate=144;$b.DisplayScale=150;$b.DynamicRefresh=1;$b.ContentBrightness=3;$b.Brightness=73
    $synthetic.Displays=[DisplayModeConfiguration[]]@($a,$b)
    $synthetic.Validate();Assert ($synthetic.HasActions) 'Display-only multi-target script is considered empty'
    $path=Join-Path $art 'SyntheticMultiDisplay.exe';[ExecutableExporter]::Save($path,$synthetic,[EmbeddedAssets]::Read('Icon0'));$read=[PeConfiguration]::Read($path)
    Assert ($read.GetDisplayActions().Count -eq 2 -and $read.GetDisplayActions()[0].DisplayId -eq $a.DisplayId -and $read.GetDisplayActions()[1].DisplayId -eq $b.DisplayId) 'Export merged/reordered monitor identities'
    Assert ($read.Displays[0].Brightness -eq 31 -and $read.Displays[1].Brightness -eq 73 -and $read.Displays[1].DynamicRefresh -eq 1 -and $read.Displays[1].ContentBrightness -eq 3) 'Export lost an individual monitor feature'
    [pscustomobject]@{Passed=$true;Checks=$script:checks;PhysicalDisplays=$display.Items.Count;NoSystemSettingsApplied=$true;NoClipboardChanged=$true;Notes=@($script:notes)}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
    'PASS: '+$script:checks+' checks; revised layout, labels, native recommendations/capability gates, independent monitor editing/export, ordered/removable preview, selection/copy, gutter, reset and presets. No system settings applied or clipboard changed.'
}finally{$win.Close()}
