<#
.SYNOPSIS
    Bumps the NuGet package version on the six SIPSorcery library projects
    in this monorepo and prepends a TODO release-notes line to each.

.DESCRIPTION
    What it edits in every csproj:
      <Version>OLD</Version>                  -> <Version>NEW</Version>
      <AssemblyVersion>OLD</AssemblyVersion>  -> <AssemblyVersion>NEW</AssemblyVersion>
      <FileVersion>OLD</FileVersion>          -> <FileVersion>NEW</FileVersion>
      <PackageReleaseNotes>-vOLD: ...         -> <PackageReleaseNotes>-vNEW: TODO
                                                   -vOLD: ...

    After running, search the repo for "TODO" inside a PackageReleaseNotes
    block and replace each with the actual release-note text for the new
    version before tagging the release.

    This is the PowerShell port of scripts/bump-package-versions.sh and
    behaves identically. It needs PowerShell 5.1 or later (no external
    tools beyond an optional git for nicer --dry-run diffs).

    Per-file byte hygiene: the original UTF-8 BOM (present on some of the
    csproj files and not others) and the original line endings are both
    preserved, so a bump never shows up as a whole-file diff.

.PARAMETER NewVersion
    The version to bump every NuGet library project to. Must be a
    SemVer-shaped string, e.g. 10.0.7, 10.1.0-pre, 11.0.0-rc.1.

.PARAMETER DryRun
    Show the diff each file would receive without writing anything.

.PARAMETER Help
    Show usage and exit.

.EXAMPLE
    .\bump-package-versions.ps1 10.0.7
    Bump all six projects to 10.0.7.

.EXAMPLE
    .\bump-package-versions.ps1 -DryRun 10.0.7
    Preview the changes without writing.

.NOTES
    Author: Aaron Clauson + Claude Opus 5
    License: BSD-3-Clause (matches the rest of the repo).
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string] $NewVersion,

    [switch] $DryRun,

    [switch] $Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------- helpers ----------

$ProgName = Split-Path -Leaf $PSCommandPath

function Write-Err {
    param([string] $Message)
    [Console]::Error.WriteLine("${ProgName}: error: $Message")
}

function Show-Usage {
    param([switch] $ToStderr)

    $text = @"
Usage: $ProgName [-DryRun] <new-version>

Arguments:
  <new-version>    The version to bump every NuGet library project to.
                   Must be a SemVer-shaped string, e.g. 10.0.7,
                   10.1.0-pre, 11.0.0-rc.1.

Options:
  -DryRun          Show the diff each file would receive without
                   writing anything.
  -Help            Show this help and exit.

The six packages this script targets are:
  src/SIPSorcery
  src/SIPSorcery.OpenAI.Realtime
  src/SIPSorcery.VP8
  src/SIPSorceryMedia.Abstractions
  src/SIPSorceryMedia.FFmpeg
  src/SIPSorceryMedia.Windows

Each project's Version, AssemblyVersion and FileVersion fields are set
to <new-version>, and a new line "-v<new-version>: TODO" is prepended
to PackageReleaseNotes. After the script runs, edit each TODO with the
actual release note text before tagging.

Examples:
  $ProgName 10.0.7             # bump all six to 10.0.7
  $ProgName -DryRun 10.0.7     # preview the changes
"@

    if ($ToStderr) {
        [Console]::Error.WriteLine($text)
    }
    else {
        Write-Output $text
    }
}

# ---------- parse args ----------

if ($Help) {
    Show-Usage
    exit 0
}

if ([string]::IsNullOrWhiteSpace($NewVersion)) {
    Write-Err 'missing required <new-version> argument'
    Show-Usage -ToStderr
    exit 2
}

# Permissive SemVer validation: MAJOR.MINOR.PATCH with optional
# pre-release tail. Reject anything else so a typo doesn't get
# committed across all six projects.
if ($NewVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.-]+)?$') {
    Write-Err "version '$NewVersion' is not in MAJOR.MINOR.PATCH[-prerelease] form"
    exit 2
}

# ---------- locate repo root ----------

# The script is expected to live in <repo>/scripts/. Walking up from the
# script's own directory makes it work regardless of the caller's cwd
# (e.g. when invoked from a sibling tooling directory or via an absolute
# path from another location on disk).
$ScriptDir = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir '..')).Path

if (-not (Test-Path (Join-Path $RepoRoot 'SIPSorcery.slnx')) -and
    -not (Test-Path (Join-Path $RepoRoot 'SIPSorcery.slnf'))) {
    Write-Err "couldn't confirm repo root from $RepoRoot (no SIPSorcery.slnx / .slnf found)"
    Write-Err 'is this script still in <repo>/scripts/?'
    exit 1
}

# ---------- target projects ----------

$Projects = @(
    'src/SIPSorcery/SIPSorcery.csproj'
    'src/SIPSorcery.OpenAI.Realtime/SIPSorcery.OpenAI.Realtime.csproj'
    'src/SIPSorcery.VP8/SIPSorcery.VP8.csproj'
    'src/SIPSorceryMedia.Abstractions/SIPSorceryMedia.Abstractions.csproj'
    'src/SIPSorceryMedia.FFmpeg/SIPSorceryMedia.FFmpeg.csproj'
    'src/SIPSorceryMedia.Windows/SIPSorceryMedia.Windows.csproj'
)

# ---------- the transform ----------
#
# Takes the whole file text, returns the transformed text.
#
# Transform rules:
#   - <Version>X</Version>, <AssemblyVersion>X</AssemblyVersion>,
#     <FileVersion>X</FileVersion>: replace X with the new version.
#   - The line containing <PackageReleaseNotes>: split at the opening
#     tag, emit the prefix-up-to-and-including-the-tag plus a fresh
#     "-v<ver>: TODO", then re-emit the original suffix (which contains
#     the previous first version note) on its own line.
#
# Lines are split on LF only and any trailing CR is carried through
# untouched, so a CRLF file stays CRLF and an LF file stays LF -- the
# same thing the awk version does, except the inserted release-notes
# line also picks up the surrounding line's ending.

function Convert-ProjectContent {
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text,
        [Parameter(Mandatory)] [string] $Version
    )

    $tag = '<PackageReleaseNotes>'
    $out = [System.Collections.Generic.List[string]]::new()

    # One regex per version element. The Regex instance Replace overload
    # is used rather than the static one (or -replace) because only it
    # takes a replacement count -- awk's sub() replaces the first match
    # only and this keeps the two scripts behaving the same.
    $versionRxs = @(
        @{ Rx = [regex]'<Version>[^<]+</Version>'; To = "<Version>$Version</Version>" }
        @{ Rx = [regex]'<AssemblyVersion>[^<]+</AssemblyVersion>'; To = "<AssemblyVersion>$Version</AssemblyVersion>" }
        @{ Rx = [regex]'<FileVersion>[^<]+</FileVersion>'; To = "<FileVersion>$Version</FileVersion>" }
    )

    # String.Split on LF keeps every empty entry, unlike the -split
    # operator, whose second argument is a max-substrings count rather
    # than a "keep everything" flag.
    foreach ($raw in $Text.Split("`n")) {
        $eol = ''
        $line = $raw
        if ($line.EndsWith("`r")) {
            $eol = "`r"
            $line = $line.Substring(0, $line.Length - 1)
        }

        # Replace value-only versions.
        $replaced = $false
        foreach ($entry in $versionRxs) {
            if ($entry.Rx.IsMatch($line)) {
                $out.Add(($entry.Rx.Replace($line, $entry.To, 1) + $eol))
                $replaced = $true
                break
            }
        }
        if ($replaced) {
            continue
        }

        # Prepend a TODO line to <PackageReleaseNotes>.
        # Existing format: indent + "<PackageReleaseNotes>-vOLD: ..." on one line.
        # We split just after the opening tag; emit the tag plus our new
        # "-v<ver>: TODO" line, then re-emit any text that was on the rest
        # of the original line on its own line below.
        if ($line.Contains($tag) -and -not $line.Contains('</PackageReleaseNotes>')) {
            $pos = $line.IndexOf($tag)
            $head = $line.Substring(0, $pos + $tag.Length)
            $tail = $line.Substring($pos + $tag.Length)
            $out.Add("$head-v${Version}: TODO$eol")
            if ($tail.Length -gt 0) {
                $out.Add($tail + $eol)
            }
            continue
        }

        $out.Add($raw)
    }

    return ($out -join "`n")
}

function Show-Diff {
    param(
        [Parameter(Mandatory)] [string] $OriginalPath,
        [Parameter(Mandatory)] [string] $NewPath
    )

    # git is the nicest unified diff available and this is always a git
    # checkout, but fall back to a plain changed-line listing if it isn't
    # on PATH for some reason.
    if (Get-Command git -ErrorAction SilentlyContinue) {
        & git --no-pager diff --no-index --unified=3 -- $OriginalPath $NewPath
        # git diff --no-index exits 1 when the files differ, which is the
        # expected case here; swallow it so $ErrorActionPreference and any
        # caller-side error handling stay quiet.
        $global:LASTEXITCODE = 0
        return
    }

    $before = [System.IO.File]::ReadAllText($OriginalPath).Split("`n")
    $after = [System.IO.File]::ReadAllText($NewPath).Split("`n")
    Compare-Object -ReferenceObject $before -DifferenceObject $after |
        ForEach-Object {
            $marker = if ($_.SideIndicator -eq '=>') { '+' } else { '-' }
            Write-Output "$marker$($_.InputObject)"
        }
}

# ---------- main loop ----------

$failed = [System.Collections.Generic.List[string]]::new()
$alreadyAt = [System.Collections.Generic.List[string]]::new()
$updated = [System.Collections.Generic.List[string]]::new()

foreach ($rel in $Projects) {
    $csproj = Join-Path $RepoRoot ($rel -replace '/', [System.IO.Path]::DirectorySeparatorChar)

    if (-not (Test-Path -LiteralPath $csproj -PathType Leaf)) {
        Write-Err "missing project file: $rel"
        $failed.Add($rel)
        continue
    }

    $bytes = [System.IO.File]::ReadAllBytes($csproj)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $original = [System.IO.File]::ReadAllText($csproj)

    # Detect "already at this version" by checking the first version note
    # on the existing PackageReleaseNotes line. If the script has already
    # been run once today (or the new version was already there for some
    # other reason), skip without prepending a duplicate TODO line.
    if ($original.Contains("<PackageReleaseNotes>-v${NewVersion}:")) {
        Write-Output ('skip   {0,-50} (release notes already start with -v{1})' -f $rel, $NewVersion)
        $alreadyAt.Add($rel)
        continue
    }

    $transformed = Convert-ProjectContent -Text $original -Version $NewVersion

    if ($transformed -ne $original) {
        if ($DryRun) {
            Write-Output ('would update {0} -- diff:' -f $rel)

            $tmp = [System.IO.Path]::GetTempFileName()
            try {
                $enc = New-Object System.Text.UTF8Encoding($hasBom)
                [System.IO.File]::WriteAllText($tmp, $transformed, $enc)
                Show-Diff -OriginalPath $csproj -NewPath $tmp
            }
            finally {
                Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
            }

            Write-Output ''
        }
        else {
            $enc = New-Object System.Text.UTF8Encoding($hasBom)
            [System.IO.File]::WriteAllText($csproj, $transformed, $enc)
            Write-Output ('update {0,-50} -> {1}' -f $rel, $NewVersion)
        }
        $updated.Add($rel)
    }
    else {
        Write-Output ('noop   {0,-50} (no matching tags found)' -f $rel)
    }
}

# ---------- summary ----------

Write-Output ''

if ($DryRun) {
    Write-Output "Dry run complete. $($updated.Count) file(s) would be modified."
    exit 0
}

Write-Output "$($updated.Count) of $($Projects.Count) project file(s) updated to v$NewVersion."
if ($alreadyAt.Count -gt 0) {
    Write-Output "$($alreadyAt.Count) file(s) already at -v$NewVersion (no changes)."
}
if ($failed.Count -gt 0) {
    Write-Output "$($failed.Count) file(s) failed -- see errors above."
    exit 1
}

Write-Output @'

Next steps:
  1. Edit each PackageReleaseNotes block and replace "TODO" with the
     real release notes for this version. Search for the marker:

       Select-String -Path src\*\*.csproj -Pattern TODO

  2. Diff to confirm only the intended changes:

       git diff -- src/

  3. Build and pack to confirm everything still produces .nupkgs:

       dotnet pack SIPSorcery.slnf --configuration Release

  4. Commit and tag.
'@

exit 0
