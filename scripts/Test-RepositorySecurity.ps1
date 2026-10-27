param([switch]$Staged)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $arguments = @('-c', 'core.quotepath=false', '-c', 'core.excludesFile=', 'ls-files', '-z', '--cached')
    if (-not $Staged) { $arguments += @('--others', '--exclude-standard') }
    $listed = & git @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate the exact Git file set.' }
    $files = (($listed -join "`n") -split '\x00' | Where-Object { $_ } | Sort-Object -Unique)
    $rules = [ordered]@{
        'private key' = '-----BEGIN [A-Z0-9 ]*PRIVATE KEY(?: BLOCK)?-----'
        'provider token' = '(?i)\b(?:gh[pousr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|ASIA[0-9A-Z]{16}|xox[baprs]-[A-Za-z0-9-]{20,}|AIza[0-9A-Za-z_-]{30,})\b'
        'credential in URL' = 'https?://[^/\s<>:"\x27]+:[^/\s<>"\x27]+@'
        'personal Windows profile path' = '(?i)\b[A-Z]:[\\/]+Users[\\/]+[^\\/\s"\x27<>]+'
        'Windows machine SID' = '\bS-1-5-21-\d+-\d+-\d+-\d+\b'
        'personal email in source' = '(?i)\b[A-Z0-9._%+-]+@(?:gmail|naver|outlook|hotmail|yahoo)\.(?:com|co\.jp)\b'
        'literal credential assignment' = '(?im)^\s*(?:"|\x27)?(?:password|passwd|api[_-]?key|access[_-]?token|client[_-]?secret|connectionstring)(?:"|\x27)?\s*[:=]\s*(?:"|\x27)(?!\s*(?:"|\x27))[^"\x27\r\n]{8,}'
    }
    $findings = [System.Collections.Generic.List[string]]::new()
    foreach ($file in $files) {
        if ($file -match '[\r\n\t]' -or $file -match '(?i)(^|/)(?:artifacts|bin|obj|logs|webview2|webview2-check|youtube-content|\.ssh|\.aws|\.codex|\.agents)(/|$)' -or
            $file -match '(?i)(^|/)(?:\.env(?:\..*)?|\.git-identity\.local\.json|settings\.json|schedule\.json|deployment-check\.json)$' -or
            $file -match '(?i)\.(?:exe|dll|pdb|zip|7z|rar|pem|key|ppk|pfx|p12|snk|jks|keystore|gpg|asc|mp3|wav|log)$' -or $file -match '\.corrupt-') {
            $findings.Add('Sensitive or generated file: ' + $file); continue
        }
        if ($Staged) {
            $size = & git cat-file -s (':' + $file)
            if ($LASTEXITCODE -ne 0 -or [long]$size -gt 1048576) { $findings.Add('Unreadable or oversized staged file: ' + $file); continue }
            $text = (& git show (':' + $file)) -join "`n"
            if ($LASTEXITCODE -ne 0) { $findings.Add('Unreadable staged file: ' + $file); continue }
        } else {
            $path = Join-Path $root $file
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
            if ((Get-Item -LiteralPath $path).Length -gt 1048576) { $findings.Add('Oversized file: ' + $file); continue }
            $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
        }
        if ($text.IndexOf([char]0) -ge 0) { $findings.Add('Binary file: ' + $file); continue }
        foreach ($rule in $rules.GetEnumerator()) {
            $match = [regex]::Match($text, $rule.Value)
            if ($match.Success) {
                $line = ($text.Substring(0, $match.Index) -split "`n").Count
                $findings.Add($rule.Key + ': ' + $file + ':' + $line)
            }
        }
    }
    if ($findings.Count -gt 0) { foreach ($finding in $findings) { [Console]::Error.WriteLine($finding) }; throw 'Security review failed. Matching values are intentionally not printed.' }
    Write-Output ('Security review passed: ' + $files.Count + ' files; no matching sensitive patterns or generated files.')
} finally { Pop-Location }
