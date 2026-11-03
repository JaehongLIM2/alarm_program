$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
$destination = Join-Path $artifacts 'app'
$stage = Join-Path $artifacts ('publish-' + [Guid]::NewGuid().ToString('N'))
$backup = Join-Path $artifacts ('app-backup-' + [Guid]::NewGuid().ToString('N'))
# Never overwrite a live runtime or mix files from different .NET releases.
$expectedExe = Join-Path $destination 'WindowsAlarm.exe'
foreach ($process in @(Get-Process WindowsAlarm -ErrorAction SilentlyContinue)) {
    try {
        if ([string]::Equals($process.Path, $expectedExe, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Close Windows Alarm before publishing. The existing deployment was not changed.'
        }
    } finally { $process.Dispose() }
}
Push-Location $root
try {
    & dotnet publish src/WindowsAlarm/WindowsAlarm.csproj --configuration Release --runtime win-x64 --self-contained true --output $stage --nologo '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' '-p:DebugSymbols=false' '-p:DebugType=None'
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed. The existing deployment was not changed.' }
    Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $stage -Recurse
    if (Test-Path -LiteralPath $destination) { Move-Item -LiteralPath $destination -Destination $backup }
    try { Move-Item -LiteralPath $stage -Destination $destination }
    catch {
        if (Test-Path -LiteralPath $backup) { Move-Item -LiteralPath $backup -Destination $destination }
        throw
    }
    Write-Output 'Published a clean deployment to artifacts/app. Previous deployment retained as app-backup-*.'
} finally { Pop-Location }
