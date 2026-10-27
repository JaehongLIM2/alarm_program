param(
    [Parameter(Mandatory = $true)][string]$InstallDirectory,
    [string]$DataDirectory = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'WindowsAlarm')
)
$ErrorActionPreference = 'Stop'
$expectedExe = [IO.Path]::GetFullPath((Join-Path $InstallDirectory 'WindowsAlarm.exe'))
$dataRoot = [IO.Path]::GetFullPath($DataDirectory)
$taskPrefix = 'WindowsAlarm-' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value + '-'
# Stop only this installed alarm normally. Never leave its in-memory shutdown active,
# and never force-terminate the process or unrelated browser sessions.
foreach ($running in @(Get-Process -Name WindowsAlarm -ErrorAction SilentlyContinue)) {
    try {
        if ([string]::Equals($running.Path, $expectedExe, [StringComparison]::OrdinalIgnoreCase) -and -not $running.HasExited) {
            if (-not $running.CloseMainWindow() -or -not $running.WaitForExit(5000)) {
                throw 'Close Windows Alarm before uninstalling. The process was not forced to exit.'
            }
        }
    } finally { $running.Dispose() }
}
$stateError = $null
$statePath = Join-Path $dataRoot 'schedule.json'
try {
    if (Test-Path -LiteralPath $statePath -PathType Leaf) {
        $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($null -eq $state) { throw 'Invalid schedule data.' }
        $state | Add-Member -MemberType NoteProperty -Name Phase -Value 4 -Force
        $state | Add-Member -MemberType NoteProperty -Name ShutdownCancelled -Value $true -Force
        $state | Add-Member -MemberType NoteProperty -Name ShutdownAt -Value $null -Force
        $temp = $statePath + '.uninstall.tmp'
        [IO.File]::WriteAllText($temp, ($state | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
        [IO.File]::Replace($temp, $statePath, [System.Management.Automation.Language.NullString]::Value)
    }
} catch { $stateError = $_.Exception.Message }

$service = $null; $folder = $null; $tasks = $null
$removed = 0
try {
    $service = New-Object -ComObject Schedule.Service
    $service.Connect()
    $folder = $service.GetFolder('\')
    $tasks = $folder.GetTasks(0)
    for ($index = $tasks.Count; $index -ge 1; $index--) {
        $task = $null; $definition = $null; $actions = $null
        try {
            $task = $tasks.Item($index)
            if (-not $task.Name.StartsWith($taskPrefix, [StringComparison]::OrdinalIgnoreCase)) { continue }
            $definition = $task.Definition
            $actions = $definition.Actions
            for ($actionIndex = 1; $actionIndex -le $actions.Count; $actionIndex++) {
                $action = $null
                try {
                    $action = $actions.Item($actionIndex)
                    if ($action.Type -eq 0 -and [string]::Equals([IO.Path]::GetFullPath($action.Path), $expectedExe, [StringComparison]::OrdinalIgnoreCase)) {
                        $folder.DeleteTask($task.Name, 0)
                        $removed++
                        break
                    }
                } finally { if ($null -ne $action) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($action) } }
            }
        } finally {
            foreach ($comObject in @($actions, $definition, $task)) {
                if ($null -ne $comObject) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($comObject) }
            }
        }
    }
} finally {
    foreach ($comObject in @($tasks, $folder, $service)) {
        if ($null -ne $comObject) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($comObject) }
    }
}
if ($stateError) { throw ('Tasks were removed, but schedule cancellation could not be saved: ' + $stateError) }
Write-Output ('Windows Alarm cleanup completed; matching tasks removed: ' + $removed)
