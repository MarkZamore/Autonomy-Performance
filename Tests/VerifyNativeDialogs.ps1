param([string]$AssemblyPath)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(-not $AssemblyPath){$AssemblyPath=Join-Path $root 'Source\Build\AutonomyPerformance.exe'}
$assembly=[Reflection.Assembly]::LoadFrom($AssemblyPath)
$type=$assembly.GetType('NativeFileDialogs',$true)
$create=$type.GetMethod('CreateDialog',[Reflection.BindingFlags]'NonPublic,Static')
$dialogType=$type.GetNestedType('IFileDialog',[Reflection.BindingFlags]::NonPublic)
$shellType=$type.GetNestedType('IShellItem',[Reflection.BindingFlags]::NonPublic)
$workingDirectory=[Environment]::CurrentDirectory
$observed=@()
function Assert($condition,$message){if(-not $condition){throw $message}}
function Read-NativeValue($interface,$instance,[string]$method,$value){
    $arguments=[object[]]@($value)
    [void]$interface.GetMethod($method).Invoke($instance,$arguments)
    return $arguments[0]
}
foreach($saving in @($true,$false)){
    $dialog=$null;$folder=$null;$namePointer=[IntPtr]::Zero
    try{
        # Configure real native COM objects without displaying or automating a
        # dialog. This checks the flags accepted by this installed Windows.
        $dialog=$create.Invoke($null,[object[]]@($saving))
        $options=[uint32](Read-NativeValue $dialogType $dialog 'GetOptions' ([uint32]0))
        Assert (($options -band 0x02000000) -ne 0) 'Recent-document suppression is missing'
        Assert (($options -band 0x8) -ne 0) 'Current-directory protection is missing'
        Assert (($options -band 0x40) -ne 0) 'File-system selection is not required'
        Assert (($options -band 0x800) -ne 0) 'Parent-folder validation is missing'
        Assert (($options -band 0x100) -eq 0) 'Normal validation was disabled'
        Assert ((Read-NativeValue $dialogType $dialog 'GetFileTypeIndex' ([uint32]0)) -eq 1) 'Wrong initial file filter'
        if($saving){
            Assert (($options -band 0x10000) -ne 0) 'The save dialog may create a write-permission probe'
            Assert (($options -band 0x2) -ne 0) 'Overwrite confirmation is missing'
            Assert (($options -band 0x4) -ne 0) 'EXE file-type restriction is missing'
            Assert ((Read-NativeValue $dialogType $dialog 'GetFileName' $null) -ceq 'Режим.exe') 'Wrong initial save filename'
            $folder=Read-NativeValue $dialogType $dialog 'GetFolder' $null
            $arguments=[object[]]@([Convert]::ToUInt32('80058000',16),[IntPtr]::Zero)
            [void]$shellType.GetMethod('GetDisplayName').Invoke($folder,$arguments)
            $namePointer=[IntPtr]$arguments[1]
            $folderPath=[Runtime.InteropServices.Marshal]::PtrToStringUni($namePointer)
            Assert ($folderPath -eq [Environment]::GetFolderPath('DesktopDirectory')) 'The save dialog does not start on the desktop'
        }else{
            Assert (($options -band 0x1000) -ne 0) 'Icon selection does not require an existing file'
        }
        $observed+=[pscustomobject]@{Dialog=if($saving){'Save'}else{'Open'};NativeOptions=('0x{0:X8}' -f $options);Displayed=$false}
    }finally{
        if($namePointer -ne [IntPtr]::Zero){[Runtime.InteropServices.Marshal]::FreeCoTaskMem($namePointer)}
        if($null -ne $folder){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($folder)}
        if($null -ne $dialog){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($dialog)}
    }
}
Assert ([Environment]::CurrentDirectory -ceq $workingDirectory) 'Dialog configuration changed the current directory'
$observed | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Artifacts\native-dialogs.json') -Encoding UTF8
'PASS: native Save/Open configuration, no recent-document additions, no save probe, preserved validation, desktop location, Russian filename, unchanged current directory. No dialog was displayed; this is not a Process Monitor trace.'
