$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
Add-Type -Path (Join-Path $PSScriptRoot 'DialogCancellation.cs')
Add-Type -Path (Join-Path $PSScriptRoot 'DialogSave.cs') -ReferencedAssemblies 'System.dll','System.Core.dll','C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\UIAutomationClient.dll','C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\UIAutomationTypes.dll'
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$win=$ui.Window
function Assert($condition,$message){if(-not $condition){throw $message}}
function Click($button){$button.RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Primitives.ButtonBase]::ClickEvent)))}
try{
$expectedTitle='Autonomy&Performance';Assert ($win.Title -eq $expectedTitle) 'Window title is not localized'
$win.Show();$win.UpdateLayout();$win.FindName('DarkThemeSwitch').IsChecked=$true
$cancel=New-Object CancelNativeDialog('Сохранить режим');Click $win.FindName('SaveButton')
Assert ($cancel.Wait()) 'Save dialog cancellation failed'
Assert ($ui.Configuration.DarkTheme -eq 1 -and $ui.IconChoice.BuiltInIndex -eq 0) 'Save cancellation changed selection'
Click $win.FindName('IconButton');$popup=$win.FindName('IconPopup');Assert ($popup.IsOpen) 'Icon popup failed to open'
$panel=$win.FindName('IconGrid');Assert ($panel.Children.Count -eq 6) 'Popup does not contain six choices'
foreach($choice in $panel.Children){Assert ($null -eq $choice.ToolTip) 'Icon option tooltip remains';Assert (-not [string]::IsNullOrEmpty([Windows.Automation.AutomationProperties]::GetName($choice))) 'Icon option has no accessible name'}
$size=$panel.RenderSize
$bitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap([int]$size.Width,[int]$size.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($panel)
$encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder;$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $PSScriptRoot 'Artifacts\ui-icons.png'));try{$encoder.Save($stream)}finally{$stream.Dispose()}
$cancel=New-Object CancelNativeDialog('Выберите иконку');Click $panel.Children[5]
Assert ($cancel.Wait()) 'Custom icon dialog cancellation failed'
Assert ($ui.IconChoice.CustomPath -eq $null -and $ui.IconChoice.BuiltInIndex -eq 0) 'Icon cancellation changed selection'
Click $win.FindName('IconButton');Click $win.FindName('IconGrid').Children[3]
Assert ($ui.IconChoice.BuiltInIndex -eq 3 -and -not $popup.IsOpen) 'Built-in popup choice failed'
Click $win.FindName('IconButton');$panel=$win.FindName('IconGrid');[void]$panel.Children[0].Focus()
function PressPopupKey([Windows.Input.Key]$key){$source=[Windows.PresentationSource]::FromVisual($panel);$event=[Windows.Input.KeyEventArgs]::new([Windows.Input.Keyboard]::PrimaryDevice,$source,0,$key);$event.RoutedEvent=[Windows.Input.Keyboard]::PreviewKeyDownEvent;$panel.RaiseEvent($event)}
PressPopupKey Right;Assert ([object]::ReferenceEquals([Windows.Input.Keyboard]::FocusedElement,$panel.Children[1])) 'Popup Right failed'
PressPopupKey Down;Assert ([object]::ReferenceEquals([Windows.Input.Keyboard]::FocusedElement,$panel.Children[4])) 'Popup Down failed'
PressPopupKey Home;Assert ([object]::ReferenceEquals([Windows.Input.Keyboard]::FocusedElement,$panel.Children[0])) 'Popup Home failed'
PressPopupKey End;Assert ([object]::ReferenceEquals([Windows.Input.Keyboard]::FocusedElement,$panel.Children[5])) 'Popup End failed'
PressPopupKey Escape;Assert (-not $popup.IsOpen) 'Popup Escape failed'
Click $win.FindName('IconButton');$panel=$win.FindName('IconGrid');[void]$panel.Children[3].Focus();PressPopupKey Enter
Assert ($ui.IconChoice.BuiltInIndex -eq 3 -and -not $popup.IsOpen) 'Popup Enter failed'
$ui.IconChoice.SelectCustom((Join-Path $root 'Source\Assets\Icon2.ico'));$ui.RefreshIcons()
$exportPath=Join-Path $PSScriptRoot ('Artifacts\dialog-export-'+[Guid]::NewGuid().ToString('N')+'.exe')
$nativeSave=New-Object SaveNativeDialog($exportPath);Click $win.FindName('SaveButton')
Assert ($nativeSave.Wait()) ('Native save failed: '+$nativeSave.Error)
Assert (Test-Path -LiteralPath $exportPath) 'Save button did not generate EXE'
Assert ([PeConfiguration]::Read($exportPath).DarkTheme -eq 1) 'Save button lost configuration'
Assert ($ui.IconChoice.CustomPath -eq $null -and $ui.IconChoice.BuiltInIndex -eq 0) 'Custom ICO not reset after real save'
'PASS: keyboard popup navigation, real EXE save, custom icon reset, native Save and ICO dialogs open and cancel, popup six choices and built-in selection.'|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Artifacts\dialog-results.txt') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Artifacts\dialog-results.txt')
}finally{$win.Close()}
