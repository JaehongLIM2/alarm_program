$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dependencies = Get-Content -LiteralPath (Join-Path $root 'installer\dependencies.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$build = Get-Content -LiteralPath (Join-Path $root 'artifacts\installer-build\last-build.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$testRoot = Join-Path $root ('artifacts\installer-tests\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$reportPath = Join-Path $testRoot 'verify.ini'
$setupLog = Join-Path $testRoot 'setup.log'
$beforeProgram = Test-Path -LiteralPath (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\WindowsAlarm')
$beforeRegistration = Test-Path -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A49DDE6A-1C37-4F62-AC69-DC8351065B5A}_is1'
$arguments = @('/VERIFYONLY', ('/VERIFYREPORT="' + $reportPath + '"'), '/VERYSILENT', '/SUPPRESSMSGBOXES', '/SP-', '/NORESTART', ('/LOG="' + $setupLog + '"'))
$process = Start-Process -FilePath $build.Setup -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit(55000)) { throw 'Installer verification timed out.' }
    # InitializeSetup=False intentionally exits with code 1 before installation starts.
    if ($process.ExitCode -ne 1) { throw ('Unexpected verify-only exit code: ' + $process.ExitCode) }
} finally { $process.Dispose() }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-StringData
if ($report.Verified -ne '1' -or $report.VersionRules -ne '1') { throw 'Compiled installer payload or version rules failed.' }
if ($beforeProgram -ne (Test-Path -LiteralPath (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\WindowsAlarm')) -or
    $beforeRegistration -ne (Test-Path -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A49DDE6A-1C37-4F62-AC69-DC8351065B5A}_is1')) {
    throw 'Verify-only mode changed installed app state.'
}
Write-Output ('PASS compiled installer: both bundled offline packages extracted and hash-verified; version boundaries; no app install. Required .NET=' + $report.DotNetRequired + ', WebView2=' + $report.WebView2Required)

$runtime = Get-Content -LiteralPath (Join-Path $build.AppDirectory 'WindowsAlarm.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtime.runtimeOptions.includedFrameworks -or $runtime.runtimeOptions.rollForward -ne 'LatestPatch' -or
    @($runtime.runtimeOptions.frameworks).Count -ne 2 -or
    @($runtime.runtimeOptions.frameworks | Where-Object version -ne $dependencies.dotnet.minimumVersion).Count -ne 0 -or
    (Test-Path -LiteralPath (Join-Path $build.AppDirectory 'coreclr.dll'))) { throw 'Unexpected framework-dependent payload.' }
Write-Output 'PASS installed app payload: shared Desktop/Core runtime 10.0.14, LatestPatch; .NET runtime not embedded.'

# Uses this test process's SDK/runtime environment, not a new system-wide runtime install.
$appCheck = Join-Path $testRoot 'app-check'
$process = Start-Process -FilePath (Join-Path $build.AppDirectory 'WindowsAlarm.exe') -ArgumentList @('--deployment-check', '--data-dir', ('"' + $appCheck + '"')) -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit(55000) -or $process.ExitCode -ne 0) { throw 'Framework-dependent deployment check failed.' }
    $appReport = Get-Content -LiteralPath (Join-Path $appCheck 'deployment-check.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $appReport.Success) { throw 'Application dependency check failed.' }
} finally { $process.Dispose() }
Write-Output 'PASS framework-dependent app: WPF, embedded resources, Alerts API, actual WebView2 native control/message bridge.'

$testInstall = Join-Path $testRoot 'installed-app'
$testData = Join-Path $testRoot 'user-data'
New-Item -ItemType Directory -Path $testInstall, $testData -Force | Out-Null
$testExe = Join-Path $testInstall 'WindowsAlarm.exe'
Copy-Item -LiteralPath (Join-Path $build.AppDirectory 'WindowsAlarm.exe') -Destination $testExe
'{"Phase":0,"ShutdownCancelled":false,"ShutdownAt":"2026-10-28T08:00:00+09:00","Settings":{"Hour":7,"Minute":30}}' |
    Set-Content -LiteralPath (Join-Path $testData 'schedule.json') -Encoding UTF8
'{"Hour":7,"Minute":30}' | Set-Content -LiteralPath (Join-Path $testData 'settings.json') -Encoding UTF8
$settingsHash = (Get-FileHash -LiteralPath (Join-Path $testData 'settings.json')).Hash
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$ownName = 'WindowsAlarm-' + $sid + '-InstallTest-' + [Guid]::NewGuid().ToString('N')
$otherName = 'WindowsAlarm-' + $sid + '-OtherTest-' + [Guid]::NewGuid().ToString('N')
$service = $null; $folder = $null
function Add-TestTask([string]$Name, [string]$Executable) {
    $definition = $service.NewTask(0)
    try {
        $definition.Principal.UserId = $sid
        $definition.Principal.LogonType = 3
        $definition.Principal.RunLevel = 0
        $definition.Settings.WakeToRun = $false
        $definition.Settings.AllowDemandStart = $false
        $definition.Settings.AllowHardTerminate = $false
        $trigger = $definition.Triggers.Create(1)
        $trigger.StartBoundary = [DateTime]::UtcNow.AddDays(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
        $action = $definition.Actions.Create(0)
        $action.Path = $Executable
        $registered = $folder.RegisterTaskDefinition($Name, $definition, 6, $sid, $null, 3)
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($registered)
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($definition) }
}
try {
    $service = New-Object -ComObject Schedule.Service
    $service.Connect()
    $folder = $service.GetFolder('\')
    Add-TestTask $ownName $testExe
    Add-TestTask $otherName (Join-Path $build.AppDirectory 'WindowsAlarm.exe')
    & (Join-Path $PSScriptRoot 'Remove-InstalledAlarm.ps1') -InstallDirectory $testInstall -DataDirectory $testData
    $stillExists = $false
    try { $task = $folder.GetTask($ownName); $stillExists = $true; [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($task) } catch { }
    if ($stillExists) { throw 'Own scheduled task survived cleanup.' }
    $task = $folder.GetTask($otherName)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($task)
    $state = Get-Content -LiteralPath (Join-Path $testData 'schedule.json') -Raw | ConvertFrom-Json
    if ($state.Phase -ne 4 -or -not $state.ShutdownCancelled -or $null -ne $state.ShutdownAt -or $state.Settings.Hour -ne 7 -or
        (Get-FileHash -LiteralPath (Join-Path $testData 'settings.json')).Hash -ne $settingsHash) { throw 'Uninstall state cleanup or settings preservation failed.' }
    Write-Output 'PASS uninstall cleanup: own matching executable task removed; other task/settings preserved; shutdown state cancelled.'
} finally {
    if ($null -ne $folder) {
        foreach ($name in @($ownName, $otherName)) { try { $folder.DeleteTask($name, 0) } catch { } }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($folder)
    }
    if ($null -ne $service) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($service) }
}
Write-Output 'Installer verification completed; system runtime installers were not executed.'
