# Updates the SIPSorcery managed plugins in a Unity project's Assets/Plugins to the
# current repo build. SIPSorcery's dependency closure changes between versions (assemblies
# get renamed and added), so this replaces the WHOLE set rather than a single DLL.
#
#   ./update-unity-plugins.ps1 -PluginsDir "C:\path\to\YourProject\Assets\Plugins"
#   ./update-unity-plugins.ps1 -PluginsDir "...\Assets\Plugins" -IncludeFFmpeg
#
# It removes the existing *.dll (and their *.dll.meta) from the target folder, then copies
# the freshly published netstandard2.1 assemblies in. Unity regenerates the .meta files on
# its next import. WARNING: this assumes Assets/Plugins holds ONLY SIPSorcery-related
# managed plugins (true for the SIPSorcery example projects). If you keep other managed
# DLLs there, move them elsewhere first.

param(
    [Parameter(Mandatory = $true)] [string] $PluginsDir,
    [switch] $IncludeFFmpeg
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path "$PSScriptRoot\..\.."
$staging = Join-Path $env:TEMP "sipsorcery-unity-plugins"

if (-not (Test-Path $PluginsDir)) { throw "PluginsDir not found: $PluginsDir" }
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $staging | Out-Null

Write-Host "Publishing SIPSorcery (netstandard2.1) with dependencies..."
dotnet publish "$repoRoot\src\SIPSorcery\SIPSorcery.csproj" -c Release -f netstandard2.1 -o "$staging" | Out-Null

Write-Host "Building SIPSorcery.VP8 (netstandard2.1)..."
dotnet build "$repoRoot\src\SIPSorcery.VP8\SIPSorcery.VP8.csproj" -c Release -p:TargetFrameworks=netstandard2.1 -f netstandard2.1 | Out-Null
Copy-Item "$repoRoot\src\SIPSorcery.VP8\bin\Release\netstandard2.1\SIPSorcery.VP8.dll" "$staging"

# Tmds.LibC is a Linux-only runtime dependency of Makaretu.Dns.Multicast (a CORE SIPSorcery
# dependency for mDNS), so `dotnet publish` emits no Windows lib for it. Unity's reference
# validation still needs the assembly present or it rejects the whole chain up to
# SIPSorcery.dll; the reference assembly satisfies validation and never runs on Windows.
$tmds = Get-ChildItem "$env:USERPROFILE\.nuget\packages\tmds.libc" -Recurse -Filter "Tmds.LibC.dll" |
    Where-Object { $_.FullName -match "\\ref\\" } | Select-Object -First 1
if ($tmds) { Copy-Item $tmds.FullName $staging -Force } else { Write-Warning "Tmds.LibC.dll not found in the NuGet cache." }

if ($IncludeFFmpeg) {
    Write-Host "Publishing SIPSorceryMedia.FFmpeg (netstandard2.1)..."
    dotnet publish "$repoRoot\src\SIPSorceryMedia.FFmpeg\SIPSorceryMedia.FFmpeg.csproj" -c Release -f netstandard2.1 -o "$staging" | Out-Null
}

Write-Host "Clearing old DLLs from $PluginsDir ..."
Get-ChildItem $PluginsDir -Filter "*.dll" | ForEach-Object {
    Remove-Item $_.FullName -Force
    Remove-Item "$($_.FullName).meta" -Force -ErrorAction SilentlyContinue
}

# Assemblies Unity's scripting runtime already provides - shipping our own copies collides
# ("multiple precompiled assemblies with the same name"), so skip them.
$unityProvided = @("netstandard.dll", "Microsoft.CSharp.dll")

Write-Host "Copying the current matched set in..."
Get-ChildItem "$staging" -Filter "*.dll" | Where-Object { $unityProvided -notcontains $_.Name } | ForEach-Object {
    Copy-Item $_.FullName $PluginsDir -Force
}

Write-Host "`nPlugins now in ${PluginsDir}:"
Get-ChildItem $PluginsDir -Filter "*.dll" | Select-Object -ExpandProperty Name
Write-Host "`nDone. Switch back to Unity to let it reimport."
