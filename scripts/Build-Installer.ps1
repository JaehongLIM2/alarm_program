$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -LiteralPath (Join-Path $root 'installer\dependencies.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$work = Join-Path $root 'artifacts\installer-build'
$downloads = Join-Path $work 'downloads'
New-Item -ItemType Directory -Path $downloads -Force | Out-Null

foreach ($name in @('dotnet', 'webview2', 'inno')) {
    $dependency = $manifest.$name
    if ($dependency.url -notmatch '^https://' -or $dependency.file -notmatch '^[A-Za-z0-9._-]+\.exe$' -or $dependency.sha256 -notmatch '^[a-f0-9]{64}$') {
        throw ('Invalid prerequisite manifest: ' + $name)
    }
    $path = Join-Path $downloads $dependency.file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Invoke-WebRequest -UseBasicParsing -Uri $dependency.url -OutFile $path
    }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash -ine $dependency.sha256) {
        throw ('Pinned prerequisite hash mismatch: ' + $name + '. Review a new download before updating dependencies.json; no installer was executed.')
    }
    if ($dependency.PSObject.Properties.Name -contains 'sha512') {
        if ((Get-FileHash -Algorithm SHA512 -LiteralPath $path).Hash -ine $dependency.sha512) { throw 'Official .NET SHA-512 mismatch.' }
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike ('*' + $dependency.publisher + '*')) {
        throw ('Prerequisite publisher verification failed: ' + $name)
    }
    Write-Output ('Verified official package: ' + $name)
}

$compilerDirectory = Join-Path $work ('tools\InnoSetup-' + $manifest.inno.version)
$compiler = Join-Path $compilerDirectory 'ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    # Official portable mode: no global installation, uninstaller, file association or shortcut.
    $arguments = @('/PORTABLE=1', '/CURRENTUSER', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS', ('/DIR="' + $compilerDirectory + '"'))
    $process = Start-Process -FilePath (Join-Path $downloads $manifest.inno.file) -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try { $process.WaitForExit(); if ($process.ExitCode -ne 0) { throw ('Portable compiler setup failed: ' + $process.ExitCode) } }
    finally { $process.Dispose() }
}
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'Portable Inno compiler was not prepared.' }

# A new staging directory prevents stale self-contained DLLs from entering this framework-dependent payload.
$stage = Join-Path $work ('staging\' + [Guid]::NewGuid().ToString('N'))
$appDirectory = Join-Path $stage 'app'
New-Item -ItemType Directory -Path $appDirectory -Force | Out-Null
Push-Location $root
try {
    & dotnet publish src/WindowsAlarm/WindowsAlarm.csproj --configuration Release --runtime win-x64 --no-self-contained --output $appDirectory --nologo `
        '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' '-p:DebugSymbols=false' '-p:DebugType=None' `
        '-p:RollForward=LatestPatch'
    if ($LASTEXITCODE -ne 0) { throw 'Framework-dependent application publish failed.' }
    $runtime = Get-Content -LiteralPath (Join-Path $appDirectory 'WindowsAlarm.runtimeconfig.json') -Raw | ConvertFrom-Json
    if ($runtime.runtimeOptions.includedFrameworks -or $runtime.runtimeOptions.rollForward -ne 'LatestPatch') { throw 'Unexpected installer runtime policy.' }
    # Configure shared .NET runtimes individually; a global RuntimeFrameworkVersion also
    # overrides the unrelated Windows SDK reference framework in Windows-targeted projects.
    foreach ($framework in $runtime.runtimeOptions.frameworks) {
        if ($framework.name -notin @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) { throw 'Unexpected shared framework.' }
        $framework.version = $manifest.dotnet.minimumVersion
    }
    [IO.File]::WriteAllText((Join-Path $appDirectory 'WindowsAlarm.runtimeconfig.json'), ($runtime | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath (Join-Path $appDirectory 'coreclr.dll')) { throw 'Runtime must be installed separately, not embedded in the app payload.' }

    # Inno source is copied as UTF-8 with BOM for consistent Korean text on any system code page.
    foreach ($file in @('WindowsAlarm.iss', 'Prerequisites.iss')) {
        $source = [IO.File]::ReadAllText((Join-Path $root ('installer\' + $file)), [Text.Encoding]::UTF8)
        [IO.File]::WriteAllText((Join-Path $stage $file), $source, [Text.UTF8Encoding]::new($true))
    }
    $defines = @('/Qp', ('/DRepoRoot=' + $root), ('/DAppDir=' + $appDirectory), ('/DDependencyDir=' + $downloads),
        ('/DDotNetVersion=' + $manifest.dotnet.minimumVersion), ('/DDotNetFile=' + $manifest.dotnet.file), ('/DDotNetSha256=' + $manifest.dotnet.sha256),
        ('/DWebViewVersion=' + $manifest.webview2.minimumVersion), ('/DWebViewFile=' + $manifest.webview2.file), ('/DWebViewSha256=' + $manifest.webview2.sha256),
        (Join-Path $stage 'WindowsAlarm.iss'))
    & $compiler @defines
    if ($LASTEXITCODE -ne 0) { throw 'Integrated installer compilation failed.' }
    $setup = Join-Path $root 'artifacts\installer\WindowsAlarm-Setup-win-x64.exe'
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $setup).Hash.ToLowerInvariant()
    ($hash + '  WindowsAlarm-Setup-win-x64.exe') | Set-Content -LiteralPath ($setup + '.sha256') -Encoding ASCII
    [pscustomobject]@{ AppDirectory = $appDirectory; Setup = $setup } | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $work 'last-build.json')
    Write-Output ('Integrated installer created; bytes=' + (Get-Item -LiteralPath $setup).Length)
} finally { Pop-Location }
