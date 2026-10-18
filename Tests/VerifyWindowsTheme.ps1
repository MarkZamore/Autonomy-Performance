$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'Source\Build\AutonomyPerformance.exe'))
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
Add-Type 'using System;using System.Runtime.InteropServices;public class ThemeAttributes{[DllImport("dwmapi.dll")]public static extern int DwmGetWindowAttribute(IntPtr h,int attribute,out int value,int size);}'
$app=New-Object Windows.Application;$ui=New-Object ConstructorWindow;$window=$ui.Window
function Assert($condition,$message){if(-not $condition){throw $message}}
try {
$projection=[Windows.UI.ViewManagement.UISettings,Windows.UI.ViewManagement,ContentType=WindowsRuntime]
$settings=[Activator]::CreateInstance($projection)
$fill=$settings.GetColorValue([Windows.UI.ViewManagement.UIColorType]::AccentLight2);$text=$settings.GetColorValue([Windows.UI.ViewManagement.UIColorType]::AccentLight3)
Assert ([WindowsTheme]::AccentSource -eq 'Windows.UI.ViewManagement.UISettings') 'Native accent API unavailable'
Assert ([WindowsTheme]::AccentFill.R -eq $fill.R -and [WindowsTheme]::AccentFill.G -eq $fill.G -and [WindowsTheme]::AccentFill.B -eq $fill.B) 'AccentLight2 mismatch'
Assert ([WindowsTheme]::AccentText.R -eq $text.R -and [WindowsTheme]::AccentText.G -eq $text.G -and [WindowsTheme]::AccentText.B -eq $text.B) 'AccentLight3 mismatch'
$originalBrush=$app.Resources['Brush.Accent'];[WindowsTheme]::RefreshAccent($app)
Assert (-not [object]::ReferenceEquals($originalBrush,$app.Resources['Brush.Accent'])) 'Accent resource not refreshed'
$interop=New-Object Windows.Interop.WindowInteropHelper($window);$handle=$interop.EnsureHandle()
$value=0;$get=[ThemeAttributes]::DwmGetWindowAttribute($handle,38,[ref]$value,4)
Assert ($get -eq 0 -and $value -eq 2 -and [WindowsTheme]::MicaEnabled) 'Native Mica not enabled on supported test system'
Assert ($window.Background -is [Windows.Media.LinearGradientBrush] -and $window.Background.Opacity -lt 1) 'Gradient must allow the native backdrop to show through'
$enabled=$true
[void][WindowsTheme]::ApplyBackdrop($window,$false)
Assert (-not [WindowsTheme]::MicaEnabled -and $window.Background -is [Windows.Media.LinearGradientBrush]) 'Gradient fallback failed'
[void][WindowsTheme]::ApplyBackdrop($window,$true)
Assert ([WindowsTheme]::MicaEnabled) 'Mica reactivation failed'
[pscustomobject]@{AccentSource=[WindowsTheme]::AccentSource;AccentFill=[WindowsTheme]::AccentFill.ToString();AccentText=[WindowsTheme]::AccentText.ToString();Mica=$enabled;Fallback=$true;HiddenWindow=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Artifacts\windows-theme.json') -Encoding UTF8
'PASS: native/managed accent equality, refresh, DWM Mica with translucent gradient and gradient fallback (hidden test window).'
}finally{$window.Close()}
