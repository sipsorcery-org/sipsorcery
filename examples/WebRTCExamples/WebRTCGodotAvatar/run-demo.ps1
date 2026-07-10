param(
    # Which avatar to stream: 'vrm' (default, 3D) or 'ren' (Live2D Cubism).
    [string]$Avatar = 'vrm'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# Locate a Godot 4.6.x .NET (mono) editor. Set GODOT to override.
$godot = $env:GODOT
if (-not $godot -or -not (Test-Path $godot)) {
    $godot = Get-ChildItem 'C:\dev' -Recurse -Filter 'Godot_v4.6*-stable_mono_win64*.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike '*console*' } | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $godot) { throw 'Godot 4.6 .NET (mono) executable not found. Set the GODOT environment variable.' }

# The VRM importer is an editor plugin and does not run when the game is launched directly, so on a
# clean checkout the model has no imported .scn. Do a one-time headless editor import first.
$imported = Get-ChildItem "$root\.godot\imported" -Filter 'UserAvatar.vrm-*.scn' -ErrorAction SilentlyContinue |
    Where-Object { $_.Length -gt 1MB }
if (-not $imported) {
    if (-not (Test-Path "$root\Models\UserAvatar.vrm")) {
        throw 'Models\UserAvatar.vrm not found - drop a .vrm avatar there (see README).'
    }
    Write-Host 'Importing UserAvatar.vrm (one-time, headless editor)...'
    Start-Process -FilePath $godot -ArgumentList '--path', $root, '--editor', '--headless' -PassThru | Out-Null
    $target = Join-Path $root '.godot\imported'
    $scn = $null
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep 2
        $scn = Get-ChildItem $target -Filter 'UserAvatar.vrm-*.scn' -ErrorAction SilentlyContinue |
            Where-Object { $_.Length -gt 40MB }
        if ($scn) { break }
    }
    Get-Process 'Godot_v4.6.3-stable_mono_win64' -ErrorAction SilentlyContinue | Stop-Process -Force
    if (-not $scn) { throw 'VRM import did not complete.' }
    Write-Host 'Import complete.'
}

# Everything after '--' is forwarded to the game (OS.GetCmdlineUserArgs), where AvatarStreamer
# reads '--avatar'.
Start-Process -FilePath $godot -ArgumentList '--path', $root, '--', '--avatar', $Avatar
Write-Host "Godot launched with the '$Avatar' avatar. Browse to http://localhost:8081 and click Connect."
Write-Host "(The in-process LLM takes ~10-15s to load; the page answers once it is ready.)"
