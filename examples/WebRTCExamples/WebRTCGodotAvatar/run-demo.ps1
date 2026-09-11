param(
    # Which avatar to stream: 'vrm' (default, 3D) or 'ren' (Live2D Cubism). Append ':<name>' to pick
    # a specific model: 'vrm:Alice' -> Models\Alice.vrm, 'live2d:Haru' -> Models\Live2D\Haru\...
    [string]$Avatar = 'vrm',

    # Voice gender: picks a matching default sherpa-tts voice. Omit to use the avatar's default
    # (VRM -> female, Live2D -> male).
    [ValidateSet('male', 'female')]
    [string]$Gender,

    # Exact voice to use: a folder under C:\tools\sherpa-tts, as the full folder name or the short
    # suffix (e.g. 'amy-medium' -> 'vits-piper-en_US-amy-medium'). Overrides -Gender.
    [string]$Voice
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# Parse '<kind>[:<name>]'.
$kind, $name = ($Avatar -split ':', 2)
$kind = $kind.Trim().ToLower()
if (-not $name) { $name = $null }
$isLive2D = $kind -in @('ren', 'live2d', 'cubism')

# Locate a Godot 4.7.x .NET (mono) editor. Set GODOT to override.
$godot = $env:GODOT
if (-not $godot -or -not (Test-Path $godot)) {
    $godot = Get-ChildItem 'C:\dev' -Recurse -Filter 'Godot_v4.7*-stable_mono_win64*.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike '*console*' } | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $godot) { throw 'Godot 4.7 .NET (mono) executable not found. Set the GODOT environment variable.' }

# The VRM importer is an editor plugin and does not run when the game is launched directly, so a VRM
# avatar needs a one-time headless editor import to produce its .scn. Live2D needs no import at all -
# gd_cubism reads the model and textures raw at runtime - so it launches directly.
if (-not $isLive2D) {
    $target = Join-Path $root '.godot\imported'
    # Gate on the selected model's imported .scn (named by the .vrm filename regardless of folder).
    $vrmName = if ($name) { $name } else { 'UserAvatar' }
    # The .vrm may sit under Models\vrm\ (optionally in a female|male gender folder) or legacy Models\.
    $vrmFile = @("Models\vrm\female\$vrmName.vrm", "Models\vrm\male\$vrmName.vrm", "Models\vrm\$vrmName.vrm", "Models\$vrmName.vrm") |
        ForEach-Object { Join-Path $root $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
    $imported = Get-ChildItem $target -Filter "$vrmName.vrm-*.scn" -ErrorAction SilentlyContinue |
        Where-Object { $_.Length -gt 1MB }
    if (-not $imported) {
        if (-not $vrmFile) {
            throw "$vrmName.vrm not found under Models\vrm\ (or Models\vrm\female|male\) - drop a .vrm avatar there (see README)."
        }
        Write-Host "Importing $vrmName.vrm (one-time, headless editor)..."
        Start-Process -FilePath $godot -ArgumentList '--path', $root, '--editor', '--headless' -PassThru | Out-Null
        # The .scn size varies with the model, so treat import as complete once it is present (> 1MB)
        # and its size is stable across two consecutive polls.
        $scn = $null
        $lastSize = -1
        for ($i = 0; $i -lt 90; $i++) {
            Start-Sleep 2
            $candidate = Get-ChildItem $target -Filter "$vrmName.vrm-*.scn" -ErrorAction SilentlyContinue |
                Where-Object { $_.Length -gt 1MB } | Select-Object -First 1
            if ($candidate) {
                if ($candidate.Length -eq $lastSize) { $scn = $candidate; break }
                $lastSize = $candidate.Length
            }
        }
        Get-Process 'Godot_v4.7-stable_mono_win64' -ErrorAction SilentlyContinue | Stop-Process -Force
        if (-not $scn) { throw "VRM import did not complete (no stable .scn produced for $vrmName.vrm)." }
        Write-Host "Import complete ($([math]::Round($scn.Length / 1MB, 1)) MB)."
    }
}

# Everything after '--' is forwarded to the game (OS.GetCmdlineUserArgs), where AvatarStreamer
# reads '--avatar'.
$gameArgs = @('--path', $root, '--', '--avatar', $Avatar)
if ($Gender) { $gameArgs += @('--gender', $Gender) }
if ($Voice) { $gameArgs += @('--voice', $Voice) }
Start-Process -FilePath $godot -ArgumentList $gameArgs
Write-Host "Godot launched with the '$Avatar' avatar$(if ($Voice) { " (voice: $Voice)" } elseif ($Gender) { " (gender: $Gender)" }). Browse to http://localhost:8081 and click Connect."
Write-Host "(The in-process LLM takes ~10-15s to load; the page answers once it is ready.)"
