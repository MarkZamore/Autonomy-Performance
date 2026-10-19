param([switch]$SkipPresets,[int]$CommitNumber=-1)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$build=Join-Path $PSScriptRoot 'Build';[void](New-Item -ItemType Directory -Path $build -Force)
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf=Join-Path $framework 'WPF'
& (Join-Path $PSScriptRoot 'GenerateCategoryIcons.ps1')
if($CommitNumber -lt 0){
    $CommitNumber=1
    if(Get-Command git -ErrorAction SilentlyContinue){
        $count=& git -C $root rev-list --count HEAD 2>$null
        if($LASTEXITCODE -eq 0 -and $count){$CommitNumber=[Math]::Max(1,[int]$count)}
    }
}
if($CommitNumber -lt 1 -or $CommitNumber -gt 65535){throw 'Номер коммита должен быть от 1 до 65535.'}
$version=$CommitNumber.ToString([Globalization.CultureInfo]::InvariantCulture)
$versionSource=Join-Path $build 'ConstructorVersion.cs'
[IO.File]::WriteAllText($versionSource,('using System.Reflection;'+[Environment]::NewLine+'[assembly: AssemblyTitle("Autonomy&Performance")]'+[Environment]::NewLine+'[assembly: AssemblyProduct("Autonomy&Performance")]'+[Environment]::NewLine+'[assembly: AssemblyFileVersion("'+$version+'")]'+[Environment]::NewLine+'[assembly: AssemblyInformationalVersion("'+$version+'")]'+[Environment]::NewLine),[Text.UTF8Encoding]::new($true))
function Compile([string[]]$arguments){& $csc /nologo @arguments;if($LASTEXITCODE -ne 0){throw 'Compilation failed'}}
$generator=Join-Path $build 'GenerateIcons.exe'
Compile @('/target:exe','/optimize+',('/out:'+$generator),'/r:System.Drawing.dll',(Join-Path $PSScriptRoot 'IconArtwork.cs'),(Join-Path $PSScriptRoot 'GenerateIcons.cs'))
& $generator (Join-Path $PSScriptRoot 'Assets');if($LASTEXITCODE -ne 0){throw 'Icon generation failed'}
$runner=Join-Path $build 'RunnerTemplate.exe'
Compile @('/target:winexe','/platform:x64','/optimize+',('/out:'+$runner),('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')),'/r:System.Management.dll','/r:System.Web.Extensions.dll',(Join-Path $PSScriptRoot 'StepResult.cs'),(Join-Path $PSScriptRoot 'SensorCapabilities.cs'),(Join-Path $PSScriptRoot 'Configuration.cs'),(Join-Path $PSScriptRoot 'DisplayControl.cs'),(Join-Path $PSScriptRoot 'DisplayBrightnessControl.cs'),(Join-Path $PSScriptRoot 'HdrControl.cs'),(Join-Path $PSScriptRoot 'AdditionalActions.cs'),(Join-Path $PSScriptRoot 'SystemActions.cs'),(Join-Path $PSScriptRoot 'WallpaperControl.cs'),(Join-Path $PSScriptRoot 'BatteryCareControl.cs'),(Join-Path $PSScriptRoot 'TaskbarControl.cs'),(Join-Path $PSScriptRoot 'NightLightControl.cs'),(Join-Path $PSScriptRoot 'Runner.cs'))
$builder=Join-Path $build 'AutonomyPerformance.exe'
$arguments=@('/target:winexe','/platform:x64','/optimize+',('/out:'+$builder),('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')),('/win32icon:'+(Join-Path $PSScriptRoot 'Assets\Icon0.ico')),'/r:System.Web.Extensions.dll','/r:System.Management.dll','/r:System.Drawing.dll',('/r:'+(Join-Path $wpf 'PresentationFramework.dll')),('/r:'+(Join-Path $wpf 'PresentationCore.dll')),('/r:'+(Join-Path $wpf 'WindowsBase.dll')),('/r:'+(Join-Path $framework 'System.Xaml.dll')),('/resource:'+$runner+',RunnerTemplate'),('/resource:'+(Join-Path $PSScriptRoot 'Theme.xaml')+',Theme'),('/resource:'+(Join-Path $PSScriptRoot 'Window.xaml')+',Window'),(Join-Path $PSScriptRoot 'StepResult.cs'),(Join-Path $PSScriptRoot 'SensorCapabilities.cs'),(Join-Path $PSScriptRoot 'Configuration.cs'),(Join-Path $PSScriptRoot 'DisplayControl.cs'),(Join-Path $PSScriptRoot 'DisplayBrightnessControl.cs'),(Join-Path $PSScriptRoot 'HdrControl.cs'),(Join-Path $PSScriptRoot 'NightLightControl.cs'),(Join-Path $PSScriptRoot 'Export.cs'),(Join-Path $PSScriptRoot 'Constructor.cs'),(Join-Path $PSScriptRoot 'WindowsTheme.cs'))
$arguments+=$versionSource
foreach($sourceFile in @('SmoothScrolling.cs','ControlMotion.cs','NativeFileDialogs.cs','PortableResources.cs','CategoryIconAttribution.cs','BatteryHealth.cs','ProgramAvailability.cs','WallpaperControl.cs')){$arguments+=(Join-Path $PSScriptRoot $sourceFile)}
for($i=0;$i -lt 5;$i++){$arguments+=('/resource:'+(Join-Path $PSScriptRoot ('Assets\Icon'+$i+'.ico'))+',Icon'+$i)}
$arguments+=('/resource:'+(Join-Path $PSScriptRoot 'CategoryIcons.xaml')+',CategoryIcons')
$arguments+=('/resource:'+(Join-Path $PSScriptRoot 'Assets\Category\NOTICE.txt')+',CategoryIconNotices')
$arguments+=('/resource:'+(Join-Path $PSScriptRoot 'Assets\Category\CC-BY-SA-4.0.txt')+',CategoryIconLicense')
Compile $arguments
Copy-Item -LiteralPath $builder -Destination (Join-Path $root 'Autonomy&Performance.exe') -Force
if(-not $SkipPresets){
$assembly=[Reflection.Assembly]::LoadFrom($builder)
foreach($mode in @('Autonomy','Performance')){
$c=New-Object ModeConfiguration
if($mode -eq 'Autonomy'){$c.EnergySaver=1;$c.FanProfile=1;$icon=1}else{$c.EnergySaver=2;$c.FanProfile=2;$c.PowerMode=3;$c.Brightness=100;$icon=3}
[ExecutableExporter]::Save((Join-Path $root ($mode+'.exe')),$c,[EmbeddedAssets]::Read('Icon'+$icon))
}}
@('Autonomy&Performance.exe','Autonomy.exe','Performance.exe') | ForEach-Object { $path=Join-Path $root $_; if(Test-Path -LiteralPath $path){Get-Item -LiteralPath $path} } | Select-Object Name,Length
