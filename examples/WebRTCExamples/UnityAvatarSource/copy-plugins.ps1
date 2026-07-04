# Builds the SIPSorcery managed libraries for netstandard2.1 (the profile Unity's player
# consumes) and copies them, with their dependencies, into Assets/Plugins. Run once before
# opening the project in Unity, and again after changing the library source.
#
#   ./copy-plugins.ps1
#
# Everything is pure managed code - including the VP8 encoder (SIPSorcery.VP8) - so there
# are no native plugins and the project runs on any Unity build target that supports
# .NET Standard 2.1.

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path "$PSScriptRoot\..\..\.."
$plugins = "$PSScriptRoot\Assets\Plugins"
$staging = "$PSScriptRoot\obj\plugin-staging"

New-Item -ItemType Directory -Force $plugins | Out-Null
Remove-Item "$staging" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Publishing SIPSorcery (netstandard2.1) with dependencies..."
dotnet publish "$repoRoot\src\SIPSorcery\SIPSorcery.csproj" -c Release -f netstandard2.1 -o "$staging" | Out-Null

Write-Host "Building SIPSorcery.VP8 (netstandard2.1)..."
dotnet build "$repoRoot\src\SIPSorcery.VP8\SIPSorcery.VP8.csproj" -c Release -p:TargetFrameworks=netstandard2.1 -f netstandard2.1 | Out-Null
Copy-Item "$repoRoot\src\SIPSorcery.VP8\bin\Release\netstandard2.1\SIPSorcery.VP8.dll" "$staging"

Write-Host "Publishing SIPSorceryMedia.FFmpeg (netstandard2.1) with FFmpeg.AutoGen..."
dotnet publish "$repoRoot\src\SIPSorceryMedia.FFmpeg\SIPSorceryMedia.FFmpeg.csproj" -c Release -f netstandard2.1 -o "$staging" | Out-Null

# Copy the managed assemblies, skipping reference/facade assemblies Unity provides itself.
$skip = @("netstandard.dll")
Get-ChildItem "$staging" -Filter "*.dll" | Where-Object { $skip -notcontains $_.Name } | ForEach-Object {
    Copy-Item $_.FullName $plugins -Force
}

# Tmds.LibC is a Linux-only runtime package (referenced by Makaretu.Dns.Multicast for mDNS),
# so publish emits no Windows lib for it - but Unity's reference validation still needs the
# assembly present or it refuses the whole chain up to SIPSorcery.dll. The reference assembly
# satisfies validation and the code path never executes on Windows.
$tmds = Get-ChildItem "$env:USERPROFILE\.nuget\packages\tmds.libc" -Recurse -Filter "Tmds.LibC.dll" |
    Where-Object { $_.FullName -match "\\ref\\" } | Select-Object -First 1
if ($tmds) { Copy-Item $tmds.FullName $plugins -Force } else { Write-Warning "Tmds.LibC.dll not found in the NuGet cache." }

Write-Host "Plugins in ${plugins}:"
Get-ChildItem $plugins -Filter "*.dll" | Select-Object -ExpandProperty Name
