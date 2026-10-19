param([string]$Builder=(Join-Path (Split-Path $PSScriptRoot -Parent) 'Source\Build\AutonomyPerformance.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))
$app=New-Object Windows.Application;$stream=$assembly.GetManifestResourceStream('Theme');try{$app.Resources=[Windows.Markup.XamlReader]::Load($stream)}finally{$stream.Dispose()}
$script:checks=0
$art=Join-Path $PSScriptRoot 'Artifacts\DropdownLayout';[void][IO.Directory]::CreateDirectory($art)
function Assert($condition,$message){$script:checks++;if(-not $condition){throw $message}}
function Descendants($parent){$queue=New-Object 'Collections.Generic.Queue[Windows.DependencyObject]';$queue.Enqueue($parent);while($queue.Count){$element=$queue.Dequeue();$element;if($element -is [Windows.Controls.Control]){[void]$element.ApplyTemplate()};if($element -is [Windows.Media.Visual]){for($i=0;$i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($element);$i++){$queue.Enqueue([Windows.Media.VisualTreeHelper]::GetChild($element,$i))}}}}
$results=@()
foreach($width in @(220,360)){
 $combo=[Windows.Controls.ComboBox]::new();$combo.Style=$app.Resources[[Windows.Controls.ComboBox]];$combo.Width=$width
 foreach($number in 1..50){[void]$combo.Items.Add('Длинная настройка дисплея - '+$number)};$combo.SelectedIndex=0
 $combo.Measure([Windows.Size]::new($width,40));$combo.Arrange([Windows.Rect]::new(0,0,$width,40));$combo.UpdateLayout();[void]$combo.ApplyTemplate()
 $popup=$combo.Template.FindName('PART_Popup',$combo);Assert ($null -ne $popup -and -not $popup.IsOpen) 'The combo popup is missing or unexpectedly open'
 $child=$popup.Child;$child.Measure([Windows.Size]::new($width,288));$child.Arrange([Windows.Rect]::new(0,0,$width,288));$child.UpdateLayout()
 $scroll=@((Descendants $child)|Where-Object {$_ -is [Windows.Controls.ScrollViewer]})[0]
 Assert ($null -ne $scroll -and $scroll.Padding.Right -ge 20) 'The popup has no separate scrollbar gutter'
 [void]$scroll.ApplyTemplate();$presenter=$scroll.Template.FindName('PART_ScrollContentPresenter',$scroll);$bar=$scroll.Template.FindName('PART_VerticalScrollBar',$scroll)
 Assert ($scroll.ScrollableHeight -gt 0 -and $bar.Visibility -eq 'Visible') 'The synthetic long popup does not scroll'
 $selected=$combo.ItemContainerGenerator.ContainerFromIndex(0)
 Assert ($selected -is [Windows.Controls.ComboBoxItem] -and $selected.IsSelected) 'The selected popup item was not realized'
 [void]$selected.ApplyTemplate();$surface=$selected.Template.FindName('ItemSurface',$selected)
 $edge=$surface.TranslatePoint([Windows.Point]::new($surface.ActualWidth,0),$scroll).X;$barLeft=$bar.TranslatePoint([Windows.Point]::new(0,0),$scroll).X
 Assert ($surface.ActualWidth -gt 0 -and $edge -le $barLeft-3.9) 'The selected-item highlight extends underneath the popup scrollbar'
 Assert ($presenter.ActualWidth -gt 0 -and $bar.ActualWidth -le 16.1) 'The popup has an invalid text area or scrollbar width'
 Assert (-not $popup.IsOpen -and -not $combo.IsDropDownOpen) 'The layout check opened a native popup'
 $results+=[pscustomobject]@{Width=$width;HighlightRight=$edge;ScrollbarLeft=$barLeft;ClearGap=$barLeft-$edge;ScrollbarWidth=$bar.ActualWidth}
}
[pscustomobject]@{Passed=$true;Checks=$script:checks;PopupNeverOpened=$true;SelectedItemClearOfScrollbar=$true;Scenarios=$results}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
'PASS: '+$script:checks+' closed-popup layout checks; selected-item highlighting ends before the scrollbar with a separate gutter. No native popup or system setting was changed.'
