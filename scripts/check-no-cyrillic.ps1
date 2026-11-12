<#
.SYNOPSIS
    Fails if repository files (or a commit message) contain Cyrillic characters.
.DESCRIPTION
    Project rule: everything committed to this repository is English-only.
    Used by the git hooks in .githooks/ and by CI.
.PARAMETER Staged
    Check only files staged for commit (pre-commit mode). Default: all tracked files.
.PARAMETER MessageFile
    Check this text file (a commit message or a git log dump) instead of repository files.
#>
param(
    [switch]$Staged,
    [string]$MessageFile
)

$ErrorActionPreference = 'Stop'

# Cyrillic (U+0400..U+04FF) and Cyrillic Supplement (U+0500..U+052F). Built from code points so
# this file itself stays free of the characters it searches for.
$pattern = "[$([char]0x0400)-$([char]0x052F)]"

function Test-Text([string]$text, [string]$label) {
    $found = $false
    $lineNo = 0
    foreach ($line in $text -split "`n") {
        $lineNo++
        if ($line -cmatch $pattern) {
            Write-Host "${label}:${lineNo}: Cyrillic text"
            $found = $true
        }
    }
    return $found
}

if ($MessageFile) {
    $text = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $MessageFile), [System.Text.Encoding]::UTF8)
    if (Test-Text $text 'message') {
        Write-Host 'Commit message contains Cyrillic. Repository rule: English only.'
        exit 1
    }
    exit 0
}

Set-Location (git rev-parse --show-toplevel)

$files = if ($Staged) {
    git -c core.quotepath=false diff --cached --name-only --diff-filter=ACMR
} else {
    git -c core.quotepath=false ls-files
}

$failed = $false
foreach ($f in $files) {
    if ($f -cmatch $pattern) {
        Write-Host "Cyrillic in file name: $f"
        $failed = $true
    }
    if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { continue }

    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $f))
    if ($bytes -contains 0) { continue } # binary file

    if (Test-Text ([System.Text.Encoding]::UTF8.GetString($bytes)) $f) {
        $failed = $true
    }
}

if ($failed) {
    Write-Host 'Repository rule violated: no Cyrillic allowed. Translate to English.'
    exit 1
}
Write-Host "No Cyrillic found in $(@($files).Count) file(s)."
