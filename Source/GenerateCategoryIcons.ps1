$ErrorActionPreference='Stop'
$assetRoot=Join-Path $PSScriptRoot 'Assets\Category'
$output=New-Object Text.StringBuilder
[void]$output.AppendLine(@'
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Generated from the attributed SVGs in Assets/Category; see NOTICE.txt.
         Geometry is preserved; scale and pen weight match 20 DIP category glyphs. -->
    <SolidColorBrush x:Key="CategoryIcon.Stroke" Color="#FFFFFF"/>
    <Pen x:Key="CategoryIcon.Pen" Brush="{StaticResource CategoryIcon.Stroke}" Thickness="1.3" StartLineCap="Round" EndLineCap="Round" LineJoin="Round"/>
    <Pen x:Key="CategoryIcon.ArcticonsPen" Brush="{StaticResource CategoryIcon.Stroke}" Thickness="3.12" StartLineCap="Round" EndLineCap="Round" LineJoin="Round"/>
    <!-- ScreenXpert: manually traced from the official Microsoft Store PNG.
         Two flat-ended ribbons with rounded opposing ends; no tile background. -->
    <DrawingImage x:Key="Icon.ScreenXpert"><DrawingImage.Drawing><DrawingGroup>
        <GeometryDrawing Brush="Transparent" Geometry="M0,0 H20 V20 H0 Z"/>
        <GeometryDrawing Pen="{StaticResource CategoryIcon.Pen}" Geometry="M9.873,1 H17.606 Q17.986,1 17.733,1.38 L7.21,11.9 C6.05,13.06 4.16,13.06 3,11.9 C1.83,10.73 1.83,8.85 3,7.68 L9.49,1.25 Q9.62,1 9.873,1 Z"/>
        <DrawingGroup><DrawingGroup.Transform><RotateTransform Angle="180" CenterX="10" CenterY="10"/></DrawingGroup.Transform>
            <GeometryDrawing Pen="{StaticResource CategoryIcon.Pen}" Geometry="M9.873,1 H17.606 Q17.986,1 17.733,1.38 L7.21,11.9 C6.05,13.06 4.16,13.06 3,11.9 C1.83,10.73 1.83,8.85 3,7.68 L9.49,1.25 Q9.62,1 9.873,1 Z"/>
        </DrawingGroup>
    </DrawingGroup></DrawingImage.Drawing></DrawingImage>
'@)
foreach($entry in @(@('WallpaperEngine','wallpaperengine.svg'),@('ArmouryCrate','armoury_crate.svg'))){
    [xml]$svg=[IO.File]::ReadAllText((Join-Path $assetRoot $entry[1]))
    [void]$output.AppendLine(('    <DrawingImage x:Key="Icon.'+$entry[0]+'"><DrawingImage.Drawing><DrawingGroup>'))
    [void]$output.AppendLine('        <GeometryDrawing Brush="Transparent" Geometry="M0,0 H20 V20 H0 Z"/>')
    [void]$output.AppendLine('        <DrawingGroup Transform="0.4166666666666667,0,0,0.4166666666666667,0,0">')
    foreach($node in $svg.SelectNodes('//*[local-name()="path" or local-name()="circle"]')){
        if($node.LocalName -eq 'path'){
            $geometry=[Security.SecurityElement]::Escape($node.GetAttribute('d'))
            if($entry[0] -eq 'ArmouryCrate'){
                # The supplied SVG defines the contours as filled compound paths.
                # SVG's default nonzero rule is F1 in WPF; adding a pen changes its shape.
                [void]$output.AppendLine(('            <GeometryDrawing Brush="{StaticResource CategoryIcon.Stroke}" Geometry="F1 '+$geometry+'"/>'))
            }else{
                [void]$output.AppendLine(('            <GeometryDrawing Pen="{StaticResource CategoryIcon.ArcticonsPen}" Geometry="'+$geometry+'"/>'))
            }
        }elseif($node.LocalName -eq 'circle'){
            [void]$output.AppendLine(('            <GeometryDrawing Pen="{StaticResource CategoryIcon.ArcticonsPen}"><GeometryDrawing.Geometry><EllipseGeometry Center="'+$node.GetAttribute('cx')+','+$node.GetAttribute('cy')+'" RadiusX="'+$node.GetAttribute('r')+'" RadiusY="'+$node.GetAttribute('r')+'"/></GeometryDrawing.Geometry></GeometryDrawing>'))
        }
    }
    [void]$output.AppendLine('        </DrawingGroup></DrawingGroup></DrawingImage.Drawing></DrawingImage>')
}
[void]$output.AppendLine('</ResourceDictionary>')
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'CategoryIcons.xaml'),$output.ToString(),[Text.UTF8Encoding]::new($true))
