[CmdletBinding()]
param(
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = (& git rev-parse --show-toplevel 2>$null).Trim()
if ([string]::IsNullOrWhiteSpace($repositoryRoot)) {
    throw 'The current directory is not inside a Git repository.'
}

$violations = [System.Collections.Generic.List[string]]::new()

$expectedConfig = @{
    'core.autocrlf' = 'false'
    'core.eol' = 'lf'
    'core.safecrlf' = 'true'
}

foreach ($entry in $expectedConfig.GetEnumerator()) {
    $actual = (& git config --local --get $entry.Key 2>$null)
    if ($null -eq $actual) {
        $actual = ''
    }
    $actual = ([string]$actual).Trim().ToLowerInvariant()
    if ($actual -ne $entry.Value) {
        $violations.Add(
            "Repository-local $($entry.Key) must be '$($entry.Value)', but is '$actual'.")
    }
}

$eolRows = @(& git ls-files --eol)
foreach ($row in $eolRows) {
    $columns = $row -split "`t", 2
    if ($columns.Count -ne 2) {
        $violations.Add("Could not parse Git EOL metadata: $row")
        continue
    }

    $metadata = $columns[0].Trim()
    $path = $columns[1]

    if ($metadata -match 'attr/-text') {
        continue
    }

    if ($metadata -notmatch '^i/(?<index>\S+)\s+w/(?<worktree>\S+)\s+attr/(?<attribute>\S+)(?:\s+eol=(?<eol>\S+))?$') {
        $violations.Add("Could not parse Git EOL attributes for '$path': $metadata")
        continue
    }

    $indexEol = $Matches['index']
    $worktreeEol = $Matches['worktree']
    $declaredEol = $Matches['eol']

    if ($indexEol -eq '-text' -or $worktreeEol -eq '-text') {
        continue
    }

    if ($indexEol -notin @('lf', 'none')) {
        $violations.Add("'$path' has non-canonical index EOL '$indexEol'.")
    }

    if ($worktreeEol -eq 'none') {
        continue
    }

    if ($declaredEol -notin @('lf', 'crlf')) {
        $violations.Add("'$path' does not have an explicit LF/CRLF policy.")
        continue
    }

    if ($worktreeEol -ne $declaredEol) {
        $violations.Add(
            "'$path' uses '$worktreeEol' in the worktree but '$declaredEol' is required.")
    }
}

if (-not $AllowDirty) {
    $status = @(& git status --porcelain)
    if ($status.Count -gt 0) {
        $violations.Add('The working tree is not clean:')
        foreach ($line in $status) {
            $violations.Add("  $line")
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Error (
        "Repository hygiene validation failed:`n" +
        ($violations -join "`n"))
    exit 1
}

Write-Host 'Repository hygiene validation passed.'
