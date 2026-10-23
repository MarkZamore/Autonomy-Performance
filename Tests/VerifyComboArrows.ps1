$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
$references=@([Windows.Controls.Control].Assembly.Location,[Windows.Media.Animation.Animatable].Assembly.Location,[Windows.DependencyObject].Assembly.Location,[Windows.Markup.IQueryAmbient].Assembly.Location)
Add-Type -Path (Join-Path $root 'Source\ControlMotion.cs') -ReferencedAssemblies $references
$app=New-Object Windows.Application
function Load-Xaml([string]$relative){$stream=[IO.File]::OpenRead((Join-Path $root $relative));try{return [Windows.Markup.XamlReader]::Load($stream)}finally{$stream.Dispose()}}
$app.Resources=Load-Xaml 'Source\Theme.xaml';$app.Resources.MergedDictionaries.Add((Load-Xaml 'Source\CategoryIcons.xaml'))
$win=Load-Xaml 'Source\Window.xaml'
$script:checks=0;$flags=[Reflection.BindingFlags]'Static,NonPublic'
$art=Join-Path $PSScriptRoot 'Artifacts\ComboArrows';[void][IO.Directory]::CreateDirectory($art)
function Assert($condition,$message){$script:checks++;if(-not $condition){throw $message}}
function Frame([int]$milliseconds){$frame=New-Object Windows.Threading.DispatcherFrame;$timer=New-Object Windows.Threading.DispatcherTimer;$timer.Interval=[TimeSpan]::FromMilliseconds($milliseconds);$timer.Add_Tick({$timer.Stop();$frame.Continue=$false}.GetNewClosure());$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)}
function Arrow($control,[string]$toggleName,[string]$pathName){[void]$control.ApplyTemplate();$toggle=$control.Template.FindName($toggleName,$control);Assert ($null -ne $toggle) ('Missing arrow toggle: '+$control.Name);[void]$toggle.ApplyTemplate();$arrow=$toggle.Template.FindName($pathName,$toggle);Assert ($arrow -is [Windows.Shapes.Path] -and $arrow.RenderTransform -is [Windows.Media.RotateTransform]) ('Missing rotating arrow: '+$control.Name);return $arrow}
function Invoke-Motion([string]$method,[object[]]$arguments){return [ControlMotion].GetMethod($method,$flags).Invoke($null,$arguments)}
try{
 Assert ([ControlMotion]::DurationMilliseconds -eq 120) 'Arrow transitions do not use the quick control duration'
 $names=@('FanModeSelector','ArmouryModeSelector','DisplaySelector','ScaleSelector','ResolutionSelector','RefreshSelector','ContentBrightnessSelector')
 $transforms=New-Object 'Collections.Generic.List[Windows.Media.RotateTransform]'
 foreach($name in $names){
  $choice=$win.FindName($name);Assert ($choice -is [Windows.Controls.ComboBox]) ('Missing expected selection list: '+$name)
  [ControlMotion]::AttachComboBox($choice);$arrow=Arrow $choice 'ComboToggle' 'ComboChevron';$turn=$arrow.RenderTransform
  Assert (-not $choice.IsLoaded -and -not $choice.IsDropDownOpen -and $turn.Angle -eq -90 -and -not $turn.HasAnimatedProperties) ('Initial combo arrow is not pointing right: '+$name)
  Assert (-not $turn.IsFrozen) ('Combo arrow transform cannot animate: '+$name)
  foreach($existing in $transforms){Assert (-not [object]::ReferenceEquals($turn,$existing)) 'Two lists share a mutable arrow transform'};$transforms.Add($turn)
  [void](Invoke-Motion 'UpdateComboBox' @($choice,$true,$true))
  Assert ($turn.Angle -eq 0 -and -not $turn.HasAnimatedProperties -and -not $choice.IsDropDownOpen) ('Unloaded combo open state animates or opens a native popup: '+$name)
  [void](Invoke-Motion 'UpdateComboBox' @($choice,$false,$true))
  Assert ($turn.Angle -eq -90 -and -not $turn.HasAnimatedProperties) ('Unloaded combo close state is incorrect: '+$name)
  Assert (-not (Invoke-Motion 'Animate' @($choice,$true)) -and -not (Invoke-Motion 'Animate' @($choice,$false))) 'Unloaded controls or initialization can start animations'
 }
 foreach($name in @('PowerSection','DisplaySection','AdditionalSection','ArmourySection','PerformanceSection','WallpaperSection')){
  $category=$win.FindName($name);[ControlMotion]::AttachCategory($category);$arrow=Arrow $category 'HeaderToggle' 'Chevron';$turn=$arrow.RenderTransform
  Assert (-not $category.IsLoaded -and $turn.Angle -eq -90 -and -not $turn.IsFrozen) ('Collapsed category arrow is not pointing right: '+$name)
  $category.IsExpanded=$true
  Assert ($turn.Angle -eq 0 -and -not $turn.HasAnimatedProperties) ('Category Expanded event does not point down immediately when unloaded: '+$name)
  $category.IsExpanded=$false
  Assert ($turn.Angle -eq -90 -and -not $turn.HasAnimatedProperties) ('Category Collapsed event does not point right: '+$name)
 }
 # A dedicated host receives a real Loaded lifecycle entirely outside the desktop.
 # No ComboBox popup is opened; private state updates drive the same production arrow path.
 [Windows.Media.RenderOptions]::ProcessRenderMode=[Windows.Interop.RenderMode]::SoftwareOnly
 $motionHostWindow=[Windows.Window]::new();$motionHostWindow.Title='Проверка анимации стрелок';$motionHostWindow.Width=240;$motionHostWindow.Height=180;$motionHostWindow.WindowStartupLocation='Manual';$motionHostWindow.Left=-32000;$motionHostWindow.Top=-32000;$motionHostWindow.ShowActivated=$false;$motionHostWindow.ShowInTaskbar=$false
 $panel=[Windows.Controls.StackPanel]::new();$motionChoice=[Windows.Controls.ComboBox]::new();$motionChoice.Style=$app.Resources[[Windows.Controls.ComboBox]];$motionChoice.Items.Add('Первый')|Out-Null;$motionChoice.SelectedIndex=0
 $motionCategory=[Windows.Controls.Expander]::new();$motionCategory.Style=$app.Resources[[Windows.Controls.Expander]];$motionCategory.Header='Категория';$motionCategory.Content='Содержимое'
 $panel.Children.Add($motionChoice)|Out-Null;$panel.Children.Add($motionCategory)|Out-Null;$motionHostWindow.Content=$panel
 [ControlMotion]::AttachComboBox($motionChoice);[ControlMotion]::AttachCategory($motionCategory)
 $motionHostWindow.Show();Frame 40
 Assert ($motionChoice.IsLoaded -and $motionCategory.IsLoaded -and $motionHostWindow.Left+$motionHostWindow.Width -lt -10000 -and -not $motionHostWindow.ShowInTaskbar -and -not $motionHostWindow.ShowActivated) ('Animation host did not load outside the desktop: combo='+$motionChoice.IsLoaded+'; category='+$motionCategory.IsLoaded+'; left='+$motionHostWindow.Left+'; taskbar='+$motionHostWindow.ShowInTaskbar+'; activated='+$motionHostWindow.ShowActivated)
 $turn=(Arrow $motionChoice 'ComboToggle' 'ComboChevron').RenderTransform
 Assert ((Invoke-Motion 'Animate' @($motionChoice,$true)) -eq [Windows.SystemParameters]::ClientAreaAnimation) 'Loaded animation policy ignores the system animation preference'
 Assert (-not (Invoke-Motion 'Animate' @($motionChoice,$false))) 'Initialization starts a loaded animation'
 $clock=[Diagnostics.Stopwatch]::StartNew();[void](Invoke-Motion 'UpdateComboBox' @($motionChoice,$true,$true));Frame 35
 $forward=$turn.Angle;$sampleMilliseconds=$clock.Elapsed.TotalMilliseconds
 if([Windows.SystemParameters]::ClientAreaAnimation){
  Assert ($turn.HasAnimatedProperties -and $forward -gt -90 -and $forward -lt 0) ('Loaded combo arrow has no intermediate open angle: '+$forward+'; elapsed='+$sampleMilliseconds)
  $base=$turn.GetAnimationBaseValue([Windows.Media.RotateTransform]::AngleProperty)
  Assert ($base -eq 0) 'The final open angle is not retained as the animation base'
  if($sampleMilliseconds -lt 100){Assert (($forward+90)/90 -gt $sampleMilliseconds/120) 'The arrow transition does not decelerate toward its final position'}
  $beforeReverse=$turn.Angle;[void](Invoke-Motion 'UpdateComboBox' @($motionChoice,$false,$true))
  Assert ($turn.HasAnimatedProperties -and $turn.GetAnimationBaseValue([Windows.Media.RotateTransform]::AngleProperty) -eq -90) 'Rapid reversal loses the transition clock or final base angle'
  Frame 30
  Assert ($turn.Angle -gt -90 -and $turn.Angle -lt $beforeReverse -and $turn.HasAnimatedProperties) 'The arrow does not reverse through intermediate rendered angles'
 }else{
  Assert ($turn.Angle -eq 0 -and -not $turn.HasAnimatedProperties) 'System-disabled animations still animate the combo arrow'
  [void](Invoke-Motion 'UpdateComboBox' @($motionChoice,$false,$true))
 }
 Frame 180;Assert ([Math]::Abs($turn.Angle+90) -lt 0.01) 'The closing arrow does not settle at its base angle'
 [void](Invoke-Motion 'UpdateComboBox' @($motionChoice,$true,$false))
 Assert ($turn.Angle -eq 0 -and -not $turn.HasAnimatedProperties -and -not $motionChoice.IsDropDownOpen) 'Initialization leaves a clock or opens a combo popup'
 $categoryTurn=(Arrow $motionCategory 'HeaderToggle' 'Chevron').RenderTransform
 $motionCategory.IsExpanded=$true;Frame 35
 if([Windows.SystemParameters]::ClientAreaAnimation){Assert ($categoryTurn.HasAnimatedProperties -and $categoryTurn.Angle -gt -90 -and $categoryTurn.Angle -lt 0) 'Loaded category arrow jumps instead of rotating right to down'}else{Assert ($categoryTurn.Angle -eq 0 -and -not $categoryTurn.HasAnimatedProperties) 'System-disabled animations still animate the category arrow'}
 $categoryBefore=$categoryTurn.Angle;$motionCategory.IsExpanded=$false;Frame 30
 if([Windows.SystemParameters]::ClientAreaAnimation){Assert ($categoryTurn.Angle -lt $categoryBefore -and $categoryTurn.Angle -gt -90) 'Category reversal does not start from its rendered angle'}
 Frame 180;Assert ([Math]::Abs($categoryTurn.Angle+90) -lt 0.01) 'The category arrow does not finish pointing right'
 Assert (-not $win.IsVisible) 'Arrow verification unexpectedly opened the constructor'
 [pscustomobject]@{Passed=$true;Checks=$script:checks;Lists=$names;CategoryCount=6;DurationMilliseconds=120;OpenAngle=0;ClosedAngle=-90;CollapsedCategoryAngle=-90;ForwardSampleAngle=$forward;ForwardSampleMilliseconds=$sampleMilliseconds;NativePopupOpened=$false;ConstructorWindowShown=$false;OffscreenLoadedHost=$true;ClientAreaAnimation=[Windows.SystemParameters]::ClientAreaAnimation;SourceCompiledInIsolation=$true}|ConvertTo-Json -Depth 3|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
 'PASS: '+$script:checks+' isolated arrow checks; seven list arrows, six category arrows, actual animation frames, rapid reversal, initialization and no-animation fallback. An isolated offscreen host verified Loaded animation clocks; no popup or system setting was changed.'
}finally{if($motionHostWindow){$motionHostWindow.Close()};$win.Close()}
