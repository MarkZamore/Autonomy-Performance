[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SessionDirectory,
    [string]$CsvPath,
    [int[]]$ApplicationProcessId,
    [string]$SaveDirectory,
    [string]$Destination,
    [switch]$ExcludeDescendants
)
$ErrorActionPreference = 'Stop'
$SessionDirectory = [IO.Path]::GetFullPath($SessionDirectory)
if(-not $CsvPath) { $CsvPath = Join-Path $SessionDirectory 'capture.csv' }
$csvPath = [IO.Path]::GetFullPath($CsvPath)
$planPath = Join-Path $SessionDirectory 'plan.json'
if(-not (Test-Path -LiteralPath $csvPath)) { throw 'Не найден capture.csv.' }
if(-not (Test-Path -LiteralPath $planPath)) { throw 'Не найден plan.json.' }
$plan = [IO.File]::ReadAllText($planPath) | ConvertFrom-Json
$applicationPath = Join-Path $SessionDirectory 'application.json'
$application = if(Test-Path -LiteralPath $applicationPath) { [IO.File]::ReadAllText($applicationPath) | ConvertFrom-Json } else { $null }
if(-not $ApplicationProcessId -and $application) {
    if($application.ProcessIds) { $ApplicationProcessId = @($application.ProcessIds) }
    elseif($application.ProcessId) { $ApplicationProcessId = @($application.ProcessId) }
}
if(-not $ApplicationProcessId -or @($ApplicationProcessId | Where-Object { $_ -le 0 }).Count -gt 0) {
    throw 'Укажите PID проверенного обычного процесса приложения через -ApplicationProcessId или application.json.'
}
if(-not $SaveDirectory) { $SaveDirectory = $plan.ExpectedSaveDirectory }
if(-not $Destination) { $Destination = $plan.ExpectedDestination }
if(-not $SaveDirectory -and $Destination) { $SaveDirectory = Split-Path $Destination -Parent }
$repository = [IO.Path]::GetFullPath($plan.RepositoryPath).TrimEnd('\')
if($SaveDirectory) { $SaveDirectory = [IO.Path]::GetFullPath($SaveDirectory).TrimEnd('\') }
$owned = New-Object 'System.Collections.Generic.HashSet[int]'
foreach($processIdentifier in $ApplicationProcessId) { [void]$owned.Add($processIdentifier) }
$events = New-Object 'System.Collections.Generic.List[object]'
$mutations = New-Object 'System.Collections.Generic.List[object]'
$attempts = New-Object 'System.Collections.Generic.List[object]'
$ambiguous = New-Object 'System.Collections.Generic.List[object]'
$processTree = New-Object 'System.Collections.Generic.List[object]'
$ipcEvents = New-Object 'System.Collections.Generic.List[object]'
$registryHandleMetadata = New-Object 'System.Collections.Generic.List[object]'
$ntfsMetadataMutations = New-Object 'System.Collections.Generic.List[object]'
$firstCommonDialogModuleLoadTime = $null
$commonDialogLoaded = $false
$fileMutationOperations = @('WriteFile','SetEndOfFileInformationFile','SetAllocationInformationFile',
    'SetBasicInformationFile','SetRenameInformationFile','SetRenameInformationEx',
    'SetDispositionInformationFile','SetDispositionInformationEx','SetLinkInformationFile',
    'SetShortNameInformationFile','SetSecurityFile','SetEAFile','SetCompressionInformationFile',
    'SetValidDataLengthInformationFile','SetCaseSensitiveInformationFile')
$registryMutationOperations = @('RegSetValue','RegDeleteValue','RegDeleteKey','RegRenameKey','RegSetKeySecurity',
    'RegLoadKey','RegUnloadKey','RegRestoreKey','RegReplaceKey','RegSetInfoKey')
function Test-NamedPipePath([string]$Path) {
    return $Path -match '^(?:\\Device\\NamedPipe|\\?NamedPipe|\\\\[^\\]+\\pipe)(?:\\|$)'
}
function Test-NtfsMetadataPath([string]$Path) {
    return $Path -match '^[A-Za-z]:\\\$(?:LogFile|Mft|MftMirr|Bitmap|Volume|AttrDef|Boot|BadClus|Secure|UpCase|Extend)(?:[:\\]|$)'
}
function Get-Scope([string]$Path) {
    if(Test-NamedPipePath $Path) { return 'NamedPipeIPC' }
    if(Test-NtfsMetadataPath $Path) { return 'NtfsFilesystemMetadata' }
    if($Path -match '^HK(LM|CU|CR|U|CC)\\') { return 'Registry' }
    if($SaveDirectory -and ($Path.Equals($SaveDirectory,[StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith($SaveDirectory + '\',[StringComparison]::OrdinalIgnoreCase))) { return 'SelectedSaveDirectory' }
    if($Path.Equals($repository,[StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith($repository + '\',[StringComparison]::OrdinalIgnoreCase)) { return 'Repository' }
    return 'OutsideRepositoryAndSaveDirectory'
}
function New-Finding($Event,[string]$Kind,[string]$Reason) {
    $target = $null
    if($Event.Operation -like 'SetRenameInformation*' -and $Event.Detail -match '(?:^|, )FileName: (.+)$') { $target=$matches[1] }
    [pscustomobject]@{
        Time=$Event.'Time of Day'; ProcessName=$Event.'Process Name'; ProcessId=[int]$Event.PID
        Operation=$Event.Operation; Path=$Event.Path; Result=$Event.Result; Detail=$Event.Detail
        Kind=$Kind; Reason=$Reason; Scope=(Get-Scope $Event.Path)
        BeforeFirstCommonDialogModuleLoad=(-not $commonDialogLoaded)
        RenameTarget=$target; RenameTargetScope=$(if($target) {Get-Scope $target} else {$null})
    }
}
$totalRows = 0
$firstRow = $true
Import-Csv -LiteralPath $csvPath | ForEach-Object {
    $event = $_
    if($firstRow) {
        foreach($column in @('Time of Day','Process Name','PID','Operation','Path','Result','Detail')) {
            if(-not ($event.PSObject.Properties.Name -contains $column)) { throw ('Отсутствует столбец CSV Process Monitor: ' + $column) }
        }
        $firstRow = $false
    }
    $totalRows++
    $eventProcessId = 0
    if(-not [int]::TryParse($event.PID,[ref]$eventProcessId)) { return }
    if(-not $ExcludeDescendants) {
        if($event.Operation -eq 'Process Start' -and $event.Detail -match '(?:^|, )Parent PID: ([0-9]+)' -and
            $owned.Contains([int]$matches[1])) {
            $parentProcessId = [int]$matches[1]
            if($owned.Add($eventProcessId)) { $processTree.Add([pscustomobject]@{ParentId=$parentProcessId;ChildId=$eventProcessId;Detail=$event.Detail}) }
        }
        if($owned.Contains($eventProcessId) -and $event.Operation -eq 'Process Create' -and $event.Detail -match '(?:^|, )PID: ([0-9]+)') {
            $childProcessId = [int]$matches[1]
            if($owned.Add($childProcessId)) { $processTree.Add([pscustomobject]@{ParentId=$eventProcessId;ChildId=$childProcessId;Detail=$event.Detail}) }
        }
    }
    if(-not $owned.Contains($eventProcessId)) { return }
    $events.Add($event)
    if(-not $commonDialogLoaded -and $event.Operation -eq 'Load Image' -and $event.Path -match '(?i)\\comdlg32\.dll$') {
        $commonDialogLoaded = $true
        $firstCommonDialogModuleLoadTime = $event.'Time of Day'
    }
    $success = $event.Result -eq 'SUCCESS'
    if(Test-NamedPipePath $event.Path) {
        $ipcEvents.Add((New-Finding $event 'NamedPipeIPC' 'Именованный канал межпроцессного обмена; это не дисковый файл.'))
        return
    }
    if($event.Operation -eq 'RegSetInfoKey' -and $event.Detail -match 'KeySetHandleTagsInformation') {
        $registryHandleMetadata.Add((New-Finding $event 'RegistryHandleTags' 'Теги открытого дескриптора ключа; не запись значения или создание постоянного ключа реестра.'))
        return
    }
    $knownMutation = $fileMutationOperations -contains $event.Operation -or $registryMutationOperations -contains $event.Operation
    if($knownMutation) {
        $isNtfsMetadata = Test-NtfsMetadataPath $event.Path
        $finding = New-Finding $event $(if($event.Operation -like 'Reg*') {'RegistryMutation'} elseif($isNtfsMetadata) {'NtfsFilesystemMetadata'} else {'FileMutation'}) 'Операция изменяет данные или метаданные.'
        if($success -and $isNtfsMetadata) { $ntfsMetadataMutations.Add($finding) }
        elseif($success) { $mutations.Add($finding) }
        else { $attempts.Add($finding) }
        return
    }
    if($event.Operation -eq 'CreateFile') {
        if($success -and $event.Detail -match 'OpenResult: (Created|Overwritten|Superseded)(?:,|$)') {
            $finding=New-Finding $event 'FileCreationOrReplacement' ('Подтверждено OpenResult: ' + $matches[1])
            if(Test-NtfsMetadataPath $event.Path) { $ntfsMetadataMutations.Add($finding) }
            else { $mutations.Add($finding) }
        } elseif($event.Detail -match 'Desired Access: [^,]*(Write|Append|Delete|Generic All|All Access)' -or
                 $event.Detail -match 'Disposition: (Create|OpenIf|Overwrite|OverwriteIf|Supersede)(?:,|$)') {
            $attempts.Add((New-Finding $event 'FileWriteIntent' 'Запрошен доступ для записи/создания; само открытие не доказывает изменение.'))
            if($success -and $event.Detail -notmatch 'OpenResult: (Opened|Created|Overwritten|Superseded)(?:,|$)') {
                $ambiguous.Add((New-Finding $event 'UnknownCreateDisposition' 'В CSV не указан результат открытия; требуется проверка исходного события.'))
            }
        }
        return
    }
    if($event.Operation -eq 'RegCreateKey') {
        if($success -and $event.Detail -match 'REG_CREATED_NEW_KEY') {
            $mutations.Add((New-Finding $event 'RegistryKeyCreation' 'Disposition: REG_CREATED_NEW_KEY.'))
        } elseif($success -and $event.Detail -match 'REG_OPENED_EXISTING_KEY') {
            $attempts.Add((New-Finding $event 'ExistingRegistryKeyOpened' 'Открыт существующий ключ; RegCreateKey здесь не доказывает создание.'))
        } else {
            $finding=New-Finding $event 'RegistryCreateAttempt' 'Попытка RegCreateKey; результат создания ключа не подтверждён.'
            $attempts.Add($finding)
            if($success) { $ambiguous.Add($finding) }
        }
        return
    }
    if($event.Operation -eq 'RegOpenKey' -and $event.Detail -match 'Desired Access: [^,]*(Write|All Access|Set Value|Create Sub Key)') {
        $attempts.Add((New-Finding $event 'RegistryWriteIntent' 'Запрошено открытие с правом записи; запись не подтверждена.'))
    }
}
if($events.Count -eq 0) { throw 'Для указанных PID нет событий; такой журнал не подтверждает проверку приложения.' }
$events | Export-Csv -LiteralPath (Join-Path $SessionDirectory 'owned-events.csv') -NoTypeInformation -Encoding UTF8
$fileMutations = @($mutations | Where-Object Kind -like 'File*')
$registryMutations = @($mutations | Where-Object Kind -like 'Registry*')
$outsideMutations = @($fileMutations | Where-Object {
    $_.Scope -eq 'OutsideRepositoryAndSaveDirectory' -or $_.RenameTargetScope -eq 'OutsideRepositoryAndSaveDirectory'
})
$temporaryMutations = @($fileMutations | Where-Object { $_.Path -match '(?i)\\\.ap-[^\\]+\.tmp$' })
$resourceTemporaryMutations = @($fileMutations | Where-Object { $_.Path -match '(?i)\\RCX[^\\]+\.tmp$' })
$fileMutations | Export-Csv -LiteralPath (Join-Path $SessionDirectory 'file-mutations.csv') -NoTypeInformation -Encoding UTF8
$registryMutations | Export-Csv -LiteralPath (Join-Path $SessionDirectory 'registry-mutations.csv') -NoTypeInformation -Encoding UTF8
$attempts | Export-Csv -LiteralPath (Join-Path $SessionDirectory 'write-intents-and-attempts.csv') -NoTypeInformation -Encoding UTF8
$ambiguous | Export-Csv -LiteralPath (Join-Path $SessionDirectory 'ambiguous-events.csv') -NoTypeInformation -Encoding UTF8
$ipcEvents | Export-Csv -LiteralPath (Join-Path $SessionDirectory 'named-pipe-ipc.csv') -NoTypeInformation -Encoding UTF8
$registryHandleMetadata | Export-Csv -LiteralPath (Join-Path $SessionDirectory 'registry-handle-tags.csv') -NoTypeInformation -Encoding UTF8
$ntfsMetadataMutations | Export-Csv -LiteralPath (Join-Path $SessionDirectory 'ntfs-metadata-mutations.csv') -NoTypeInformation -Encoding UTF8
$paths = @($fileMutations | Select-Object -ExpandProperty Path -Unique | Sort-Object | ForEach-Object {
    [pscustomobject]@{Path=$_;Scope=(Get-Scope $_);ExistsAtAnalysis=(Test-Path -LiteralPath $_)
        IsExporterTemporary=($_ -match '(?i)\\\.ap-[^\\]+\.tmp$');IsResourceUpdateTemporary=($_ -match '(?i)\\RCX[^\\]+\.tmp$')}
})
$remainingTemporary = @($paths | Where-Object { $_.IsExporterTemporary -and $_.ExistsAtAnalysis })
$remainingResourceTemporary = @($paths | Where-Object { $_.IsResourceUpdateTemporary -and $_.ExistsAtAnalysis })
$summary = [ordered]@{
    AnalyzedUtc=[DateTime]::UtcNow.ToString('o'); CsvRows=$totalRows; ApplicationRows=$events.Count
    CsvPath=$csvPath
    InitialProcessIds=@($ApplicationProcessId); IncludedProcessIds=@($owned | Sort-Object); ProcessTree=$processTree.ToArray()
    ExpectedApplicationPath=$plan.ExpectedApplicationPath; RepositoryPath=$repository
    SelectedSaveDirectory=$SaveDirectory; ExpectedDestination=$Destination
    SuccessfulFileMutationEvents=$fileMutations.Count; SuccessfulRegistryMutationEvents=$registryMutations.Count
    SuccessfulFileWriteEvents=@($fileMutations | Where-Object Operation -eq 'WriteFile').Count
    SuccessfulRegistrySetValueEvents=@($registryMutations | Where-Object Operation -eq 'RegSetValue').Count
    SuccessfulRegistryDeleteValueEvents=@($registryMutations | Where-Object Operation -eq 'RegDeleteValue').Count
    SuccessfulRegistryKeyCreationEvents=@($registryMutations | Where-Object Kind -eq 'RegistryKeyCreation').Count
    ExistingRegistryKeyOpenEvents=@($attempts | Where-Object Kind -eq 'ExistingRegistryKeyOpened').Count
    NamedPipeEvents=$ipcEvents.Count
    SuccessfulNamedPipeWriteEvents=@($ipcEvents | Where-Object { $_.Operation -eq 'WriteFile' -and $_.Result -eq 'SUCCESS' }).Count
    RegistryHandleTagEvents=$registryHandleMetadata.Count
    SuccessfulNtfsMetadataMutationEvents=$ntfsMetadataMutations.Count
    NtfsMetadataPaths=@($ntfsMetadataMutations | Select-Object -ExpandProperty Path -Unique | Sort-Object)
    FirstCommonDialogModuleLoadTime=$firstCommonDialogModuleLoadTime
    SuccessfulFileMutationsBeforeFirstCommonDialogLoad=@($fileMutations | Where-Object BeforeFirstCommonDialogModuleLoad).Count
    SuccessfulRegistryMutationsBeforeFirstCommonDialogLoad=@($registryMutations | Where-Object BeforeFirstCommonDialogModuleLoad).Count
    FileMutationEventsOutsideRepositoryAndSaveDirectory=$outsideMutations.Count
    ExporterTemporaryMutationEvents=$temporaryMutations.Count; RemainingExporterTemporaryFiles=$remainingTemporary.Count
    ExporterTemporaryWriteEvents=@($temporaryMutations | Where-Object Operation -eq 'WriteFile').Count
    ResourceUpdateTemporaryMutationEvents=$resourceTemporaryMutations.Count
    ResourceUpdateTemporaryWriteEvents=@($resourceTemporaryMutations | Where-Object Operation -eq 'WriteFile').Count
    RemainingResourceUpdateTemporaryFiles=$remainingResourceTemporary.Count
    WriteIntentOrFailedAttemptEvents=$attempts.Count; AmbiguousSuccessfulEvents=$ambiguous.Count
    CaptureFinished=(Test-Path -LiteralPath (Join-Path $SessionDirectory 'finished.json'))
    FileMutationPaths=$paths; RegistryMutationPaths=@($registryMutations | Select-Object -ExpandProperty Path -Unique | Sort-Object)
    Limitations=@('Проверен только записанный сценарий и указанные PID с найденными дочерними процессами.',
        'Действия общих системных служб и других процессов не приписываются приложению без дополнительного доказательства.',
        'Успешная операция записи не равнозначна пользовательской настройке приложения: ОС и стандартный диалог тоже могут писать от его PID.',
        'ReadFile и открытие существующего файла/ключа не считаются записью. Неоднозначные события требуют ручной проверки.')
}
[IO.File]::WriteAllText((Join-Path $SessionDirectory 'summary.json'),($summary | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
function Format-Cell([string]$Value) { ($Value -replace '\|','\|') -replace '[\r\n]+',' ' }
$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('# Фактические события Process Monitor')
$report.Add('')
$report.Add(('Приложение: `{0}`. PID: {1}. Проанализировано событий: {2}.' -f $plan.ExpectedApplicationPath,(@($owned | Sort-Object) -join ', '),$events.Count))
$report.Add(('Репозиторий: `{0}`. Выбранная папка: `{1}`.' -f $repository,$SaveDirectory))
$report.Add('')
$report.Add(('- Успешные операции изменения обычных дисковых файлов/метаданных: **{0}**.' -f $fileMutations.Count))
$report.Add(('- Из них вне репозитория и выбранной папки: **{0}**.' -f $outsideMutations.Count))
$report.Add(('- Успешные изменения реестра: **{0}**.' -f $registryMutations.Count))
$report.Add(('- Операции с метаданными NTFS (отдельно от пользовательских файлов): **{0}**.' -f $ntfsMetadataMutations.Count))
$report.Add(('- Успешные WriteFile в именованные каналы IPC, не на диск: **{0}**.' -f $summary.SuccessfulNamedPipeWriteEvents))
$report.Add(('- RegSetInfoKey/KeySetHandleTagsInformation, не изменение постоянных значений реестра: **{0}**.' -f $registryHandleMetadata.Count))
$report.Add(('- Операции с временными `.ap-*.tmp`: **{0}**, оставшиеся файлы: **{1}**.' -f $temporaryMutations.Count,$remainingTemporary.Count))
$report.Add(('- Операции с временными `RCX*.tmp`: **{0}**, оставшиеся файлы: **{1}**.' -f $resourceTemporaryMutations.Count,$remainingResourceTemporary.Count))
$report.Add(('- Неоднозначные успешные события, требующие проверки: **{0}**.' -f $ambiguous.Count))
$report.Add('')
$report.Add('## Файлы с подтверждёнными изменениями')
$report.Add('')
$report.Add('| Путь | Область | Существует после проверки |')
$report.Add('|---|---|---|')
foreach($entry in $paths) { $report.Add(('| {0} | {1} | {2} |' -f (Format-Cell $entry.Path),$entry.Scope,$entry.ExistsAtAnalysis)) }
$report.Add('')
$report.Add('## Отдельно: метаданные NTFS')
$report.Add('')
if($ntfsMetadataMutations.Count -eq 0) { $report.Add('В записанном сценарии не обнаружены.') }
else {
    foreach($entry in $ntfsMetadataMutations) { $report.Add(('- `{0}`: {1}, {2}.' -f $entry.Path,$entry.Operation,$entry.Result)) }
}
$report.Add('')
$report.Add('## Подтверждённые изменения реестра')
$report.Add('')
if($registryMutations.Count -eq 0) { $report.Add('В записанном сценарии не обнаружены.') }
else {
    $report.Add('| Операция | Путь | Детали |'); $report.Add('|---|---|---|')
    foreach($entry in $registryMutations) { $report.Add(('| {0} | {1} | {2} |' -f $entry.Operation,(Format-Cell $entry.Path),(Format-Cell $entry.Detail))) }
}
$report.Add('')
$report.Add('## Границы вывода')
$report.Add('')
foreach($limitation in $summary.Limitations) { $report.Add(('- ' + $limitation)) }
$report.Add('- Журналы содержат локальные пути и системные события. Не публикуйте PML/CSV и application.json в GitHub.')
[IO.File]::WriteAllLines((Join-Path $SessionDirectory 'PortabilityReport.md'),$report,[Text.UTF8Encoding]::new($false))
[pscustomobject]$summary
