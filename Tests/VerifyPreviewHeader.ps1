param([string]$Builder)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$app=New-Object Windows.Application
$assembly=$null
if($Builder){$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Builder))}
function Load-Resource($name,$relative){
    if($assembly){$stream=$assembly.GetManifestResourceStream($name)}else{$stream=[IO.File]::OpenRead((Join-Path $root $relative))}
    try{return [Windows.Markup.XamlReader]::Load($stream)}finally{$stream.Dispose()}
}
$app.Resources=Load-Resource 'Theme' 'Source\Theme.xaml'
[void]$app.Resources.MergedDictionaries.Add((Load-Resource 'CategoryIcons' 'Source\CategoryIcons.xaml'))
$win=Load-Resource 'Window' 'Source\Window.xaml'
$art=Join-Path $PSScriptRoot 'Artifacts\PreviewHeader'
[void][IO.Directory]::CreateDirectory($art)
$script:checks=0
function Assert($condition,$message){$script:checks++;if(-not $condition){throw $message}}
function Bounds($element,$ancestor){return $element.TransformToAncestor($ancestor).TransformBounds([Windows.Rect]::new(0,0,$element.ActualWidth,$element.ActualHeight))}
try {
    $grid=$win.FindName('RootGrid');$grid.Background=$app.Resources['Brush.WindowBackdrop'];$header=$win.FindName('PreviewHeader')
    $title=$win.FindName('PreviewHeaderTitle');$titleHost=$win.FindName('PreviewHeaderTitleHost');$reset=$win.FindName('ResetScriptButton')
    Assert ($null -ne $title -and $null -ne $titleHost -and $null -ne $header) 'Named preview header elements are missing'
    Assert ($title.Text -eq 'Предпросмотр скрипта' -and $title.TextWrapping -eq 'NoWrap') 'The full preview heading must remain on one line'
    Assert ([Windows.Controls.Grid]::GetColumn($reset) -eq 1 -and [Windows.Controls.Grid]::GetColumn($titleHost) -eq 0) 'Reset icon must follow the heading'
    Assert ($null -eq $reset.FocusVisualStyle -and $reset.BorderThickness.Left -eq 0 -and $reset.BorderThickness.Right -eq 0) 'Reset icon has a visible button outline'
    Assert ([Windows.Automation.AutomationProperties]::GetName($reset) -eq 'Сбросить настройки скрипта') 'Reset icon lost its accessible name'
    $results=@()
    foreach($size in @(@{Name='normal';Width=984;Height=661},@{Name='minimum';Width=804;Height=521})){
        foreach($scale in @(1,1.5,2)){
            $scenario=$size.Name+'-'+[int]($scale*100)
            $available=[Windows.Size]::new($size.Width,$size.Height)
            $grid.Measure($available);$grid.Arrange([Windows.Rect]::new($available));$grid.UpdateLayout()
            [void]$reset.ApplyTemplate()
            $textBounds=Bounds $title $header;$iconBounds=Bounds $reset $header
            Assert ([Math]::Abs(($textBounds.Top+$textBounds.Height/2)-($iconBounds.Top+$iconBounds.Height/2)) -le 1) ('Title and icon are not vertically aligned: '+$scenario)
            Assert ($textBounds.Right+7 -le $iconBounds.Left) ('Title overlaps the reset icon: '+$scenario)
            Assert ($iconBounds.Left-$textBounds.Right -le 10) ('Reset icon is too far from the heading: '+$scenario)
            Assert ($textBounds.Left -ge -0.5 -and $textBounds.Right -le $header.ActualWidth+0.5 -and $textBounds.Top -ge -0.5 -and $textBounds.Bottom -le $header.ActualHeight+0.5) ('Heading is clipped: '+$scenario)
            Assert ($iconBounds.Left -ge 0 -and $iconBounds.Right -le $header.ActualWidth+0.5) ('Reset icon is outside the preview: '+$scenario)
            Assert ($reset.ActualWidth -eq 32 -and $reset.ActualHeight -eq 32) ('Reset hit area was scaled: '+$scenario)
            Assert ($textBounds.Height -le 28.5 -and $title.ActualHeight -le 28.5) ('Heading wrapped to multiple lines: '+$scenario)
            $effectiveFontSize=$title.FontSize*$textBounds.Width/$title.ActualWidth
            Assert ($effectiveFontSize -ge 13.5 -and $effectiveFontSize -le 20.1) ('Heading text is too small or scaled up: '+$scenario)
            $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]($size.Width*$scale),[int]($size.Height*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32)
            $bitmap.Render($grid)
            $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
            $path=Join-Path $art ($scenario+'.png');$stream=[IO.File]::Create($path)
            try{$encoder.Save($stream)}finally{$stream.Dispose()}
            $results+=[pscustomobject]@{Scenario=$scenario;HeaderWidth=$header.ActualWidth;TitleWidth=$textBounds.Width;EffectiveTitleFontSize=$effectiveFontSize;IconLeft=$iconBounds.Left;IconWidth=$iconBounds.Width;ResetGap=$iconBounds.Left-$textBounds.Right;Image=$path}
        }
    }
    $results|ConvertTo-Json -Depth 3|Set-Content -LiteralPath (Join-Path $art 'results.json') -Encoding UTF8
    $results|Format-Table Scenario,HeaderWidth,TitleWidth,EffectiveTitleFontSize,IconLeft,IconWidth,ResetGap -AutoSize
    'PASS: '+$script:checks+' preview-header checks at ordinary/minimum client sizes and 100/150/200% offscreen rendering. Source: '+$(if($Builder){$Builder}else{'Source XAML'})
} finally {$win.Close()}
