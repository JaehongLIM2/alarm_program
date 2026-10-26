param([switch]$PrePush, [string]$RemoteName, [string]$RemoteUrl)
$ErrorActionPreference = 'Stop'

function Read-Git([string[]]$Arguments) {
    $value = & git @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Required repository Git setting is missing or unreadable.' }
    return ($value -join "`n").Trim()
}

try {
    $expectedLogin = 'JaehongLIM2'
    $expectedRemote = 'git@github.com:JaehongLIM2/alarm_program.git'
    $expectedEmail = Read-Git @('config', '--local', '--get', 'accountGuard.email')
    if ([string]::IsNullOrWhiteSpace($expectedEmail)) { throw 'Expected commit email is not configured.' }
    if ((Read-Git @('config', '--local', '--get', 'accountGuard.login')) -cne $expectedLogin) { throw 'Account guard is not configured for JaehongLIM2.' }
    foreach ($kind in @('GIT_AUTHOR_IDENT', 'GIT_COMMITTER_IDENT')) {
        $identity = Read-Git @('var', $kind)
        $pattern = '^' + [regex]::Escape($expectedLogin + ' <' + $expectedEmail + '>') + ' \d+ [+-]\d{4}$'
        if ($identity -cnotmatch $pattern) { throw 'Author and committer must both use the configured JaehongLIM2 identity.' }
    }
    if ((Read-Git @('config', '--get', 'core.hooksPath')) -cne '.githooks') { throw 'Repository hooks are not enabled.' }
    if ((Read-Git @('config', '--get', 'core.sshCommand')) -cne 'sh .githooks/personal-ssh') { throw 'Pinned personal SSH transport is not configured.' }
    if ($env:GIT_SSH -or $env:GIT_SSH_COMMAND) { throw 'SSH environment overrides are blocked in this repository.' }
    $fetchUrls = @(Read-Git @('remote', 'get-url', '--all', 'origin'))
    $pushUrls = @(Read-Git @('remote', 'get-url', '--push', '--all', 'origin'))
    if (($fetchUrls -join "`n") -cne $expectedRemote -or ($pushUrls -join "`n") -cne $expectedRemote) { throw 'Only the specified personal repository is allowed.' }

    if ($PrePush) {
        if ($RemoteName -cne 'origin' -or $RemoteUrl -cne $expectedRemote) { throw 'Push destination is not the approved origin.' }
        $updates = [Console]::In.ReadToEnd() -split '\r?\n' | Where-Object { $_ }
        foreach ($update in $updates) {
            $fields = $update -split ' '
            if ($fields.Count -ne 4) { throw 'Unexpected pre-push input.' }
            $localOid = $fields[1]; $remoteRef = $fields[2]; $remoteOid = $fields[3]
            if ($localOid -match '^0+$' -or $remoteRef -notlike 'refs/heads/*') { throw 'Branch deletion and tag pushes are blocked by this guard.' }
            $range = $localOid
            if ($remoteOid -notmatch '^0+$') {
                & git merge-base --is-ancestor $remoteOid $localOid
                if ($LASTEXITCODE -ne 0) { throw 'Non-fast-forward push blocked. Fetch and review remote changes first.' }
                $range = $remoteOid + '..' + $localOid
            }
            $commits = & git rev-list $range
            if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect commits being pushed.' }
            foreach ($commit in $commits) {
                $metadata = (Read-Git @('show', '-s', '--format=%an%x00%ae%x00%cn%x00%ce', $commit)) -split '\x00'
                if ($metadata.Count -ne 4 -or $metadata[0] -cne $expectedLogin -or $metadata[1] -cne $expectedEmail -or
                    $metadata[2] -cne $expectedLogin -or $metadata[3] -cne $expectedEmail) { throw 'A pushed commit belongs to a different author or committer.' }
            }
        }
    } else {
        & (Join-Path $PSScriptRoot '..\scripts\Test-RepositorySecurity.ps1') -Staged
        if (-not $?) { throw 'Staged-file security review failed.' }
    }
    exit 0
} catch {
    [Console]::Error.WriteLine('Account guard: ' + $_.Exception.Message)
    exit 1
}
