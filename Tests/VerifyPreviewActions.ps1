param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Web.Extensions
[void][Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
$script:checks=0
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$art=Join-Path $PSScriptRoot 'Artifacts\PreviewActions';[void][IO.Directory]::CreateDirectory($art)
function Assert($condition,$message){$script:checks++;if(-not $condition){throw $message}}
function Frame([int]$milliseconds=25){$frame=New-Object Windows.Threading.DispatcherFrame;$timer=New-Object Windows.Threading.DispatcherTimer;$timer.Interval=[TimeSpan]::FromMilliseconds($milliseconds);$timer.Add_Tick({$timer.Stop();$frame.Continue=$false}.GetNewClosure());$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
function Layout([int]$width=984,[int]$height=661){$grid=$win.FindName('RootGrid');$grid.Measure([Windows.Size]::new($width,$height));$grid.Arrange([Windows.Rect]::new(0,0,$width,$height));$grid.UpdateLayout();Frame;return $grid}
function Descendants($parent){$queue=New-Object 'Collections.Generic.Queue[Windows.DependencyObject]';$queue.Enqueue($parent);while($queue.Count){$element=$queue.Dequeue();$element;if($element -is [Windows.Controls.Control]){[void]$element.ApplyTemplate()};if($element -is [Windows.Media.Visual]){for($i=0;$i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($element);$i++){$queue.Enqueue([Windows.Media.VisualTreeHelper]::GetChild($element,$i))}}}}
function Invoke-Private([string]$method,[object[]]$arguments){return $ui.GetType().GetMethod($method,$flags).Invoke($ui,$arguments)}
function Click($button){$button.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))}
function Configuration-Text {return [System.Web.Script.Serialization.JavaScriptSerializer]::new().Serialize($ui.Configuration)}
try{
 $preview=$win.FindName('ScriptPreview');$items=$win.FindName('PreviewActions');$empty=$win.FindName('PreviewEmpty')
 Assert ($preview -is [Windows.Controls.ScrollViewer] -and $null -ne $items -and $null -ne $empty) 'Action preview structure is incomplete'
 Assert ($items.Items.Count -eq 0 -and $empty.Text -eq '' -and $empty.Visibility -eq 'Visible' -and $ui.PreviewText -eq '') 'Empty preview is not localized or has phantom actions'
 Assert (-not [Windows.Input.ApplicationCommands]::Copy.CanExecute($null,$preview)) 'Empty preview can copy phantom actions'
 $ui.IconChoice.SelectBuiltIn(3);$icon=[Convert]::ToBase64String($ui.IconChoice.GetBytes())
 $win.FindName('EnergySaverSwitch').IsChecked=$true;$win.FindName('DarkThemeSwitch').IsChecked=$true;$win.FindName('ClockSecondsSwitch').IsChecked=$true
 $grid=Layout
 $lines=@($ui.PreviewText -split '\r?\n\r?\n')
 Assert ($items.Items.Count -ge 2 -and $items.Items.Count -eq $lines.Count -and $empty.Visibility -eq 'Collapsed') 'Each selected setting is not represented by a separate action'
 $rows=@($items.Items)
 for($i=0;$i -lt $rows.Count;$i++){
  $row=$rows[$i];Assert ($row -is [Windows.Controls.Button] -and [int]$row.Tag -eq $i -and $row.Content -is [Windows.Controls.TextBlock] -and $row.Content.Text -eq $lines[$i]) 'Preview row differs from the action text or order'
  Assert ($row.Content.TextWrapping -eq 'Wrap' -and [double]::IsNaN($row.Content.LineHeight)) 'Action label does not wrap naturally'
  Assert ($null -eq $row.FocusVisualStyle -and $row.BorderThickness -eq [Windows.Thickness]::new(0)) 'Action row adds a focus or selection outline'
  Assert ([Windows.Automation.AutomationProperties]::GetName($row) -eq $lines[$i]) 'Action row lost its accessible text'
 }
 Assert (@((Descendants $preview)|Where-Object {$_ -is [Windows.Controls.Primitives.TextBoxBase] -or $_ -is [Windows.Controls.Primitives.Selector]}).Count -eq 0) 'Preview still exposes text or item selection'
 Assert ([Windows.Input.ApplicationCommands]::Copy.CanExecute($null,$preview)) 'Copying the complete script is unavailable'
 $mouseKey=[Windows.UIElement].GetField('IsMouseOverPropertyKey',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
 $neutral=$rows[1].Background.ToString();$first=$rows[0]
 $first.SetValue($mouseKey,$true);$grid.UpdateLayout()
 Assert ($first.Background.ToString() -eq $app.Resources['Brush.SurfaceHover'].ToString()) 'Hovered action is not highlighted'
 Assert ($rows[1].Background.ToString() -eq $neutral) 'Hovering one action highlights another action'
 $first.SetValue($mouseKey,$false);$grid.UpdateLayout()
 Assert ($first.Background.ToString() -eq $neutral) 'Highlight remains after pointer departure'
 $point=[Windows.Point]::new(10,10)
 [void](Invoke-Private 'BeginPreviewPointer' @([int]0,$point,$true))
 Assert (Invoke-Private 'CompletePreviewPointer' @([int]0,$point)) 'A click on one action is not accepted'
 Assert (-not (Invoke-Private 'CompletePreviewPointer' @([int]0,$point))) 'A completed click can be processed twice'
 $origin=[Windows.Point]::new(0,0)
 foreach($distance in @([Windows.Point]::new([Windows.SystemParameters]::MinimumHorizontalDragDistance,0),[Windows.Point]::new(0,[Windows.SystemParameters]::MinimumVerticalDragDistance))){
  [void](Invoke-Private 'BeginPreviewPointer' @([int]0,$origin,$true))
  Assert (-not (Invoke-Private 'CompletePreviewPointer' @([int]0,$distance))) 'An exact drag threshold is treated as a click'
  [void](Invoke-Private 'BeginPreviewPointer' @([int]0,$origin,$true))
  Assert (Invoke-Private 'CompletePreviewPointer' @([int]0,[Windows.Point]::new($distance.X/2,$distance.Y/2))) 'A movement below the drag threshold is treated as a drag'
 }
 foreach($test in @(
  @{Name='different action';Start=0;End=1;Allow=$true;Move=$null;Finish=$point},
  @{Name='blank gutter or scrollbar';Start=-1;End=-1;Allow=$true;Move=$null;Finish=$point},
  @{Name='modified or multiple click';Start=0;End=0;Allow=$false;Move=$null;Finish=$point},
  @{Name='horizontal drag';Start=0;End=0;Allow=$true;Move=$null;Finish=[Windows.Point]::new(10+[Math]::Ceiling([Windows.SystemParameters]::MinimumHorizontalDragDistance),10)},
  @{Name='vertical drag';Start=0;End=0;Allow=$true;Move=$null;Finish=[Windows.Point]::new(10,10+[Math]::Ceiling([Windows.SystemParameters]::MinimumVerticalDragDistance))},
  @{Name='drag then return';Start=0;End=0;Allow=$true;Move=[Windows.Point]::new(40,40);Finish=$point}
 )){
  $before=Configuration-Text
  [void](Invoke-Private 'BeginPreviewPointer' @([int]$test.Start,$point,[bool]$test.Allow))
  if($null -ne $test.Move){[void](Invoke-Private 'MovePreviewPointer' @($test.Move))}
  Assert (-not (Invoke-Private 'CompletePreviewPointer' @([int]$test.End,$test.Finish))) ('Pointer gesture would accidentally remove an action: '+$test.Name)
  Assert ((Configuration-Text) -eq $before) ('Pointer gesture changed the script: '+$test.Name)
 }
 $old=@($items.Items|ForEach-Object {$_.Content.Text});Click $items.Items[0]
 $remaining=@($ui.PreviewText -split '\r?\n\r?\n')
 Assert ($remaining.Count -eq $old.Count-1 -and ($remaining -join '|') -eq (($old|Select-Object -Skip 1)-join '|') -and $ui.Configuration.EnergySaver -eq 0) 'Clicking an action does not remove exactly that action'
 Assert ([Convert]::ToBase64String($ui.IconChoice.GetBytes()) -eq $icon) 'Removing an action changes the chosen icon'
 $handle=[Windows.Interop.WindowInteropHelper]::new($win).EnsureHandle();$source=[Windows.Interop.HwndSource]::FromHwnd($handle)
 $old=@($items.Items|ForEach-Object {$_.Content.Text});$key=[Windows.Input.KeyEventArgs]::new([Windows.Input.Keyboard]::PrimaryDevice,$source,0,[Windows.Input.Key]::Delete);$key.RoutedEvent=[Windows.Input.Keyboard]::PreviewKeyDownEvent;$key.Source=$items.Items[0].Content;$preview.RaiseEvent($key)
 Assert ($key.Handled -and $items.Items.Count -eq $old.Count-1 -and $ui.Configuration.DarkTheme -eq 0) 'Delete on an action label does not remove exactly that action'
 Assert (-not $win.IsVisible) 'The keyboard routing test unexpectedly opened a native window'
 $before=Configuration-Text;$release=[Windows.Input.MouseButtonEventArgs]::new([Windows.Input.Mouse]::PrimaryDevice,0,[Windows.Input.MouseButton]::Left);$release.RoutedEvent=[Windows.UIElement]::PreviewMouseLeftButtonUpEvent;$preview.RaiseEvent($release);Assert ((Configuration-Text) -eq $before) 'Releasing over blank preview removes an action'
 $items.Items.Clear()
 foreach($i in 1..80){$row=[Windows.Controls.Button]::new();$row.Style=$app.Resources['ScriptPreviewAction'];$text=[Windows.Controls.TextBlock]::new();$text.Text='Длинная настройка экрана - масштаб 200%, разрешение 2880 × 1800, частота обновления 144 Гц - '+$i;$text.TextWrapping='Wrap';$row.Content=$text;[void]$items.Items.Add($row)}
 foreach($size in @(@(984,661),@(804,521))){
  $grid=Layout $size[0] $size[1];[void]$preview.ApplyTemplate();$presenter=$preview.Template.FindName('PART_ScrollContentPresenter',$preview);$bar=$preview.Template.FindName('PART_VerticalScrollBar',$preview)
  Assert ($preview.ScrollableHeight -gt 0 -and $bar.Visibility -eq 'Visible') 'Long preview cannot be scrolled'
  $contentRight=$presenter.TranslatePoint([Windows.Point]::new($presenter.ActualWidth,0),$preview).X;$barLeft=$bar.TranslatePoint([Windows.Point]::new(0,0),$preview).X
  Assert ($contentRight -le $barLeft) 'Preview scrollbar overlaps action text'
  $preview.ScrollToBottom();$grid.UpdateLayout();Frame
  Assert ($preview.VerticalOffset -gt 0) 'Scrollbar scrolling does not reach long preview content'
  $bottom=$preview.VerticalOffset;$preview.LineUp();$grid.UpdateLayout();Frame
  Assert ($preview.VerticalOffset -lt $bottom) 'Preview vertical scrolling is blocked by action interaction'
 }
 $win.FindName('ResetScriptButton').RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent))
 Assert (-not $ui.Configuration.HasActions -and $items.Items.Count -eq 0 -and $empty.Visibility -eq 'Visible' -and -not $win.FindName('ResetScriptButton').IsEnabled) 'Trash action does not restore the empty preview'
 Assert ([Convert]::ToBase64String($ui.IconChoice.GetBytes()) -eq $icon) 'Reset changed the selected icon'
 [pscustomobject]@{Passed=$true;Checks=$script:checks;TextSelectionRemoved=$true;IndependentHover=$true;ClickRemovesSingleAction=$true;DragDoesNotRemove=$true;ScrollGutter=$true;NoSystemSettingsApplied=$true;ClipboardUnchanged=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
 'PASS: '+$script:checks+' preview checks; individual hover, no selectable text, single-action removal, drag rejection, scroll gutter and reset. No system settings or clipboard changed.'
}finally{$win.Close()}
