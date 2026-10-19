param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$art=Join-Path $PSScriptRoot 'Artifacts'
[void](New-Item -ItemType Directory -Path $art -Force)
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application
$ui=New-Object ConstructorWindow
function Assert($condition,$message){if(-not $condition){throw $message}}
function Set-FanMode([int]$profile){$fan=$ui.Window.FindName('FanModeSelector');$fan.SelectedItem=@($fan.Items|Where-Object Profile -eq $profile)[0]}
function Clear-Actions {
    $ui.ResetScript()
}
function Render-Ui($scenario) {
    $size=[Windows.Size]::new($scenario.Width,$scenario.Height)
    $grid.Measure($size);$grid.Arrange([Windows.Rect]::new($size));$grid.UpdateLayout()
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]($scenario.Width*$scenario.Scale),[int]($scenario.Height*$scenario.Scale),96*$scenario.Scale,96*$scenario.Scale,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($grid)
    $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $path=Join-Path $art ('ui-'+$scenario.Name+'.png')
    $stream=[IO.File]::Create($path);try{$encoder.Save($stream)}finally{$stream.Dispose()}
    foreach($name in @('SaveButton','IconButton','PresetPerformanceButton','PresetAutonomyButton')){
        $button=$win.FindName($name);$point=$button.TranslatePoint([Windows.Point]::new(0,0),$grid)
        Assert ($button.ActualWidth -gt 0 -and $button.ActualHeight -gt 0) ('Invisible footer control: '+$name+' '+$scenario.Name)
        Assert ($point.X -ge 0 -and $point.Y -ge 0 -and $point.X+$button.ActualWidth -le $grid.ActualWidth+1 -and $point.Y+$button.ActualHeight -le $grid.ActualHeight+1) ('Footer control outside window: '+$name+' '+$scenario.Name)
    }
    $performance=$win.FindName('PresetPerformanceButton');$autonomy=$win.FindName('PresetAutonomyButton');$icon=$win.FindName('IconButton')
    $presetRight=$performance.TranslatePoint([Windows.Point]::new($performance.ActualWidth,0),$grid).X
    Assert ($presetRight -le $icon.TranslatePoint([Windows.Point]::new(0,0),$grid).X) ('Preset and export buttons overlap: '+$scenario.Name)
    $performanceContentWidth=$performance.DesiredSize.Width-$performance.Margin.Left-$performance.Margin.Right
    $autonomyContentWidth=$autonomy.DesiredSize.Width-$autonomy.Margin.Left-$autonomy.Margin.Right
    Assert ($performance.ActualWidth -ge $performanceContentWidth-1 -and $autonomy.ActualWidth -ge $autonomyContentWidth-1) ('Preset text clipped: '+$scenario.Name)
    $battery=$win.FindName('BatteryWear');$batteryPoint=$battery.TranslatePoint([Windows.Point]::new(0,0),$grid)
    Assert ($batteryPoint.X+$battery.ActualWidth -le $icon.TranslatePoint([Windows.Point]::new(0,0),$grid).X) ('Battery information overlaps export controls: '+$scenario.Name)
    Assert ($batteryPoint.Y+$battery.ActualHeight -le $grid.ActualHeight+1) ('Battery information is outside the footer: '+$scenario.Name)
    [pscustomobject]@{Scenario=$scenario.Name;ClientWidth=$scenario.Width;ClientHeight=$scenario.Height;Scale=$scenario.Scale;ContentWidth=$grid.ActualWidth;ContentHeight=$grid.ActualHeight;FooterVisible=$true;Image=$path}
}
$win=$ui.Window
try {
    $preview=$win.FindName('ScriptPreview')
    Assert ($win.Title -eq 'Autonomy&Performance') 'Window title does not match executable name'
    Assert ($win.Width -eq 1000 -and $win.Height -eq 700 -and $win.MinWidth -eq 820 -and $win.MinHeight -eq 560) 'Window geometry differs from approved layout'
    Assert ($preview -is [Windows.Controls.ScrollViewer] -and $ui.PreviewText -eq '') 'Preview initial state failed'
    foreach($section in @('PerformanceSection','WallpaperSection','ArmourySection','PowerSection','DisplaySection','AdditionalSection')){Assert (-not $win.FindName($section).IsExpanded) ('Section not collapsed: '+$section)}
    Assert (-not $ui.Configuration.HasActions -and -not $win.FindName('SaveButton').IsEnabled) 'Initial selection is not empty'
    $win.FindName('PowerSection').IsExpanded=$true
    $win.FindName('EnergySaverSwitch').IsChecked=$true;Set-FanMode 1
    Assert ($ui.PreviewText -eq "Включить энергосбережение`r`n`r`nТихий режим вентилятора") 'Autonomy preview is not localized or separated into unnumbered paragraphs'
    Set-FanMode 2
    Assert ($ui.PreviewText -eq "Включить энергосбережение`r`n`r`nПроизводительный режим вентилятора") 'Fan conflict not reflected in preview'
    $win.FindName('EnergySaverSwitch').IsChecked=$null
    Assert ($ui.PreviewText -eq 'Производительный режим вентилятора') 'Preview paragraph did not update'
    Set-FanMode 0
    Assert ($ui.PreviewText -eq '') 'Preview did not clear'
    $win.FindName('EnergySaverSwitch').IsChecked=$false
    Assert ($ui.Configuration.EnergySaver -eq 2) 'Saver off state failed'
    Set-FanMode 1;Set-FanMode 2
    Assert ($ui.Configuration.FanProfile -eq 2) 'Fan replacement failed'
    $win.FindName('Efficiency').IsChecked=$true;$win.FindName('Balanced').IsChecked=$true;$win.FindName('BestPerformance').IsChecked=$true
    Assert ((-not $win.FindName('Efficiency').IsChecked)-and(-not $win.FindName('Balanced').IsChecked)) 'Power conflict failed'
    $win.FindName('EnergySaverSwitch').IsChecked=$true
    Assert ($win.FindName('BestPerformance').IsChecked -and $win.FindName('EnergySaverSwitch').IsChecked) 'Power and energy saver are not independent'
    $win.FindName('BestPerformance').IsChecked=$false
    Assert ($ui.Configuration.PowerMode -eq 0 -and -not $win.FindName('Efficiency').IsChecked -and -not $win.FindName('Balanced').IsChecked) 'Power does not return to unchanged'
    Clear-Actions
    Assert (-not $win.FindName('SaveButton').IsEnabled) 'Save stays enabled after clearing'
    Set-FanMode 1;$win.FindName('EnergySaverSwitch').IsChecked=$false;$win.FindName('Balanced').IsChecked=$true
    Assert ($win.FindName('SaveButton').IsEnabled) 'Save disabled for mixed profile'
    $win.FindName('PowerSection').IsExpanded=$false
    Assert ($ui.Configuration.PowerMode -eq 2) 'Collapsing a category clears selection'
    Clear-Actions
    $win.FindName('EnergySaverSwitch').IsChecked=$true;Set-FanMode 1;$win.FindName('Balanced').IsChecked=$true
    Assert ($ui.PreviewText.Contains('Сбалансированный')) 'Power missing from preview'
    $win.FindName('Efficiency').IsChecked=$true
    Assert ($ui.PreviewText.Contains('Максимальная эффективность энергосбережения') -and -not $ui.PreviewText.Contains('Сбалансированный')) 'Power replacement not reflected'
    $font=[Windows.Media.FontFamily]$app.Resources['Font.Family']
    Assert ($font.Source -match 'Segoe UI Variable Text' -and $font.Source -notmatch 'Montserrat|pack:') 'System font family not used'
    Assert ($font.GetTypefaces().Count -gt 0) 'System font unavailable'
    Assert (@($assembly.GetManifestResourceNames()|Where-Object {$_ -match 'FontLicense|Montserrat|AutonomyPerformance\.g\.resources'}).Count -eq 0) 'Old embedded font resource remains'
    Assert ($app.Resources['Font.Body'] -eq 14) 'Body type size differs from Windows typography'
    foreach($type in @([Windows.Controls.Button],[Windows.Controls.CheckBox],[Windows.Controls.ComboBox],[Windows.Controls.Expander],[Windows.Controls.Primitives.ScrollBar],[Windows.Controls.ToolTip])){Assert ($app.Resources[$type] -ne $null) ('Missing themed style: '+$type.Name)}
    $themeStream=$assembly.GetManifestResourceStream('Theme');$reader=[IO.StreamReader]::new($themeStream)
    try{$theme=$reader.ReadToEnd()}finally{$reader.Dispose()}
    foreach($state in @('IsMouseOver','IsPressed','IsEnabled','IsKeyboardFocused')){Assert ($theme.Contains($state)) ('Missing interactive theme state: '+$state)}
    Assert ($theme -notmatch 'Montserrat|Obsidian|Обсидиан') 'Old design remains in theme'
    $grid=$win.FindName('RootGrid');$grid.Background=$app.Resources['Brush.WindowBackdrop']
    foreach($section in @('PerformanceSection','PowerSection','DisplaySection','AdditionalSection')){$win.FindName($section).IsExpanded=$true}
    $results=@()
    foreach($scenario in @(@{Name='normal';Width=984;Height=661;Scale=1},@{Name='minimum';Width=804;Height=521;Scale=1},@{Name='dpi150';Width=984;Height=661;Scale=1.5},@{Name='dpi200';Width=984;Height=661;Scale=2})){$results+=Render-Ui $scenario}
    $scroll=$win.FindName('SettingsScroll')
    Assert ($scroll.ScrollableHeight -gt 0) 'Expanded settings do not offer vertical scrolling'
    $scroll.ScrollToBottom();$grid.UpdateLayout()
    $results+=Render-Ui @{Name='expanded-bottom';Width=984;Height=661;Scale=1}
    $actions=$win.FindName('PreviewActions');$original=@($actions.Items);$actions.Items.Clear()
    foreach($i in 1..50){$row=[Windows.Controls.Button]::new();$row.Style=$app.Resources['ScriptPreviewAction'];$text=[Windows.Controls.TextBlock]::new();$text.Text='Длинная настройка выбранного экрана: 2880 × 1800, частота обновления 144 Гц';$text.TextWrapping='Wrap';$row.Content=$text;[void]$actions.Items.Add($row)}
    $results+=Render-Ui @{Name='long-preview';Width=804;Height=521;Scale=1}
    $previewHost=$preview;[void]$previewHost.ApplyTemplate()
    $presenter=$previewHost.Template.FindName('PART_ScrollContentPresenter',$previewHost);$bar=$previewHost.Template.FindName('PART_VerticalScrollBar',$previewHost)
    Assert ($previewHost.ScrollableHeight -gt 0 -and $bar.Visibility -eq 'Visible') 'Long preview has no visible scrollbar'
    Assert ($previewHost.ActualWidth-$presenter.ActualWidth -ge $bar.ActualWidth) 'Preview scrollbar overlaps its text'
    $focusedKey=[Windows.UIElement].GetField('IsKeyboardFocusedPropertyKey',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
    $preview.SetValue($focusedKey,$true);$grid.UpdateLayout()
    Assert ($null -eq $preview.Template.FindName('FocusRing',$preview) -and $null -eq $preview.FocusVisualStyle) 'Preview focus outline remains'
    Assert (-not ($preview -is [Windows.Controls.Primitives.TextBoxBase])) 'Preview still supports selectable text'
    Assert ([Windows.Input.ApplicationCommands]::Copy.CanExecute($null,$preview)) 'Copying the full script is unavailable'
    $preview.SetValue($focusedKey,$false);$actions.Items.Clear();foreach($item in $original){[void]$actions.Items.Add($item)}
    Assert ($win.FindName('ResetScriptButton').IsEnabled) 'Reset is disabled for a configured script'
    $win.FindName('ResetScriptButton').RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
    Assert (-not $ui.Configuration.HasActions -and -not $win.FindName('ResetScriptButton').IsEnabled -and $ui.PreviewText -eq '') 'Reset button does not clear the script'
    $results|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $art 'ui-results.json') -Encoding UTF8
    $results
    'PASS: Russian UI, empty/collapsed start, exclusive power selection, independent saver/power, live preview, system fonts, themed states, all six sections and footer geometry. DPI images use offscreen rendering; live native DPI/theme validation is separate.'
} finally {$win.Close()}
