param(
    [string]$Email,
    [string]$KeyPath,
    [string]$CommitMessage = 'Add Windows alarm application and personal account safeguards'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$expectedLogin = 'JaehongLIM2'
$expectedRemote = 'git@github.com:JaehongLIM2/alarm_program.git'

function Enable-RepositoryTrust([string]$RepositoryRoot) {
    # Git only accepts safe.directory from protected config. Process-level config is also
    # inherited by hooks and SSH helpers, unlike adding -c only to the initial command.
    # Trust precisely this known checkout; do not edit the global config or use '*'.
    $countText = [Environment]::GetEnvironmentVariable('GIT_CONFIG_COUNT', 'Process')
    $count = 0
    if ($countText -and (-not [int]::TryParse($countText, [ref]$count) -or $count -lt 0 -or $count -gt 1023)) {
        throw 'Invalid inherited GIT_CONFIG_COUNT.'
    }
    $keyName = 'GIT_CONFIG_KEY_' + $count
    $valueName = 'GIT_CONFIG_VALUE_' + $count
    $saved = @{}
    foreach ($name in @('GIT_CONFIG_COUNT', $keyName, $valueName)) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }
    $resolvedRoot = (Resolve-Path -LiteralPath $RepositoryRoot).ProviderPath.Replace('\', '/')
    [Environment]::SetEnvironmentVariable($keyName, 'safe.directory', 'Process')
    [Environment]::SetEnvironmentVariable($valueName, $resolvedRoot, 'Process')
    [Environment]::SetEnvironmentVariable('GIT_CONFIG_COUNT', [string]($count + 1), 'Process')
    return $saved
}

function Restore-RepositoryTrust([hashtable]$Saved) {
    foreach ($entry in $Saved.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
}

function Invoke-Git([string[]]$Arguments) {
    $output = & git @Arguments
    if ($LASTEXITCODE -ne 0) { throw ('Git command failed: ' + ($Arguments[0])) }
    return $output
}

$trustState = $null
Push-Location $root
try {
    $trustState = Enable-RepositoryTrust $root
    if ($env:GIT_SSH -or $env:GIT_SSH_COMMAND) { throw 'Remove SSH environment overrides before publishing this personal repository.' }
    foreach ($variable in @('GIT_AUTHOR_NAME', 'GIT_AUTHOR_EMAIL', 'GIT_COMMITTER_NAME', 'GIT_COMMITTER_EMAIL')) {
        if ([Environment]::GetEnvironmentVariable($variable)) { throw ('Remove the identity override first: ' + $variable) }
    }
    if (-not $Email) {
        $identityFile = Join-Path $root '.git-identity.local.json'
        if (Test-Path -LiteralPath $identityFile) { $Email = (Get-Content -LiteralPath $identityFile -Raw -Encoding UTF8 | ConvertFrom-Json).commitEmail }
    }
    if (-not $Email -or $Email -match '[<>\r\n]' -or $Email -notmatch '^[^\s@]+@[^\s@]+\.[^\s@]+$') { throw 'Supply the GitHub commit email using -Email or the ignored local identity file.' }
    if (-not $KeyPath) { $KeyPath = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.ssh\id_ed25519_personal' }
    $KeyPath = [System.IO.Path]::GetFullPath($KeyPath)
    if (-not (Test-Path -LiteralPath $KeyPath -PathType Leaf)) { throw 'The personal SSH key is missing or unreadable.' }
    $sshExe = Join-Path $env:WINDIR 'System32\OpenSSH\ssh.exe'
    if (-not (Test-Path -LiteralPath $sshExe)) { throw 'Windows OpenSSH client is required.' }

    # Verify the actual remote account before changing Git metadata or creating a commit.
    $sshArguments = @('-F', 'none', '-i', $KeyPath, '-o', 'IdentitiesOnly=yes', '-o', 'IdentityAgent=none',
        '-o', 'BatchMode=yes', '-o', 'PreferredAuthentications=publickey', '-o', 'PasswordAuthentication=no',
        '-o', 'KbdInteractiveAuthentication=no', '-o', 'StrictHostKeyChecking=yes', '-o', 'ConnectTimeout=15', '-T', 'git@github.com')
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $greeting = (& $sshExe @sshArguments 2>&1 | Out-String)
        $sshStatus = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    $expectedGreeting = "Hi JaehongLIM2! You've successfully authenticated, but GitHub does not provide shell access."
    if ($sshStatus -ne 1 -or -not $greeting.Contains($expectedGreeting)) { throw 'Cannot confirm SSH authentication as JaehongLIM2. No commit or push was performed; no alternate account was tried.' }
    Write-Output 'Verified GitHub SSH account: JaehongLIM2'

    if (-not (Test-Path -LiteralPath (Join-Path $root '.git'))) { Invoke-Git @('init', '-b', 'main') }
    $topLevel = (Invoke-Git @('rev-parse', '--show-toplevel')).Trim().Replace('\', '/')
    if ($topLevel -ine $root.Replace('\', '/')) { throw 'Not the intended repository root.' }
    $settings = [ordered]@{
        'user.name' = $expectedLogin
        'user.email' = $Email
        'user.useConfigOnly' = 'true'
        'commit.gpgSign' = 'false'
        'tag.gpgSign' = 'false'
        'accountGuard.login' = $expectedLogin
        'accountGuard.email' = $Email
        'accountGuard.sshKey' = $KeyPath.Replace('\', '/')
        'accountGuard.sshExecutable' = $sshExe.Replace('\', '/')
        'core.hooksPath' = '.githooks'
        'core.sshCommand' = 'sh .githooks/personal-ssh'
        'ssh.variant' = 'ssh'
        'push.default' = 'simple'
        'remote.pushDefault' = 'origin'
    }
    foreach ($setting in $settings.GetEnumerator()) { Invoke-Git @('config', '--local', $setting.Key, $setting.Value) }
    $remotes = @(Invoke-Git @('remote'))
    if ($remotes -notcontains 'origin') { Invoke-Git @('remote', 'add', 'origin', $expectedRemote) }
    $fetchUrl = (Invoke-Git @('remote', 'get-url', '--all', 'origin')) -join "`n"
    $pushUrl = (Invoke-Git @('remote', 'get-url', '--push', '--all', 'origin')) -join "`n"
    if ($fetchUrl -cne $expectedRemote -or $pushUrl -cne $expectedRemote) { throw 'Existing origin is different. It was not overwritten.' }

    & git rev-parse --verify --quiet HEAD | Out-Null
    $hasCommit = $LASTEXITCODE -eq 0
    $branch = (Invoke-Git @('symbolic-ref', '--short', 'HEAD')).Trim()
    if ($branch -cne 'main') { throw 'This publishing command requires main. No branch was renamed.' }
    $remoteHeads = @(Invoke-Git @('ls-remote', '--heads', 'origin'))
    if (-not $hasCommit -and $remoteHeads.Count -gt 0) { throw 'The remote is not empty. Fetch and review its existing history before an initial push; nothing was overwritten.' }
    if ($hasCommit -and $remoteHeads.Count -gt 0) {
        Invoke-Git @('fetch', 'origin')
        $remoteMain = $remoteHeads | Where-Object { $_ -match '\srefs/heads/main$' }
        if ($remoteMain) {
            $remoteOid = ($remoteMain -split '\s+')[0]
            & git merge-base --is-ancestor $remoteOid HEAD
            if ($LASTEXITCODE -ne 0) { throw 'Remote main is not an ancestor. Review and merge remote changes first; force push is not used.' }
        }
    }

    & (Join-Path $PSScriptRoot 'Test-RepositorySecurity.ps1')
    Invoke-Git @('add', '--', '.')
    Invoke-Git @('update-index', '--chmod=+x', '--', '.githooks/pre-commit', '.githooks/pre-push', '.githooks/personal-ssh')
    & (Join-Path $PSScriptRoot 'Test-RepositorySecurity.ps1') -Staged
    & git diff --cached --quiet
    $diffStatus = $LASTEXITCODE
    if ($diffStatus -eq 1) { Invoke-Git @('commit', '-m', $CommitMessage) }
    elseif ($diffStatus -ne 0) { throw 'Cannot inspect the staged changes.' }
    elseif (-not $hasCommit) { throw 'No reviewed files are available to commit.' }

    Invoke-Git @('push', '-u', 'origin', 'main')
    $localOid = (Invoke-Git @('rev-parse', 'HEAD')).Trim()
    $remoteTip = (Invoke-Git @('ls-remote', '--heads', 'origin', 'refs/heads/main')) -join "`n"
    if (($remoteTip -split '\s+')[0] -cne $localOid) { throw 'Push returned, but remote main verification failed.' }
    Write-Output ('Verified remote commit: https://github.com/JaehongLIM2/alarm_program/commit/' + $localOid)
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
} finally {
    if ($null -ne $trustState) { Restore-RepositoryTrust $trustState }
    Pop-Location
}
