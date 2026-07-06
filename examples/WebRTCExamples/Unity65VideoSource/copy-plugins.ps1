# Builds the SIPSorcery managed libraries for netstandard2.1 (the profile Unity's player
# consumes) and copies them, with their dependencies, into Assets/Plugins. Run once before
# opening the project in Unity, and again after changing the library source.
#
#   ./copy-plugins.ps1
#
# Video is VP8-encoded via SIPSorceryMedia.FFmpeg (FFmpeg.AutoGen over native FFmpeg 8
# libraries), so FFmpeg 8 shared must be installed on the machine - see README.md.

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path "$PSScriptRoot\..\..\.."
$plugins = "$PSScriptRoot\Assets\Plugins"
$staging = "$PSScriptRoot\obj\plugin-staging"

New-Item -ItemType Directory -Force $plugins | Out-Null
Remove-Item "$staging" -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $staging | Out-Null

# Unity 6000.5 ships its own Microsoft.Extensions.* / System.Diagnostics.DiagnosticSource
# assemblies (Editor\Data\BCLExtensions, all v8.0) and they always win over Assets/Plugins
# copies at compile time. SIPSorcery references the 10.0 packages, which makes every script
# compile fail with CS1705 (assembly uses a higher version than referenced assembly). Pin
# the whole build closure to the 8.0 package family so the compiled references line up
# with what the editor provides.
$compatTargets = "$staging\unity-compat.targets"
@'
<Project>
  <ItemGroup>
    <PackageReference Update="Microsoft.Extensions.Logging.Abstractions" Version="8.0.3" />
  </ItemGroup>
</Project>
'@ | Set-Content $compatTargets
$compatArg = "-p:CustomAfterMicrosoftCommonTargets=$compatTargets"

Write-Host "Publishing SIPSorcery (netstandard2.1) with dependencies..."
dotnet publish "$repoRoot\src\SIPSorcery\SIPSorcery.csproj" -c Release -f netstandard2.1 $compatArg -o "$staging" | Out-Null

Write-Host "Building SIPSorcery.VP8 (netstandard2.1)..."
dotnet build "$repoRoot\src\SIPSorcery.VP8\SIPSorcery.VP8.csproj" -c Release -p:TargetFrameworks=netstandard2.1 -f netstandard2.1 $compatArg | Out-Null
Copy-Item "$repoRoot\src\SIPSorcery.VP8\bin\Release\netstandard2.1\SIPSorcery.VP8.dll" "$staging"

Write-Host "Publishing SIPSorceryMedia.FFmpeg (netstandard2.1) with FFmpeg.AutoGen..."
dotnet publish "$repoRoot\src\SIPSorceryMedia.FFmpeg\SIPSorceryMedia.FFmpeg.csproj" -c Release -f netstandard2.1 $compatArg -o "$staging" | Out-Null

# Copy the managed assemblies, skipping reference/facade assemblies Unity provides itself.
$skip = @("netstandard.dll", "Microsoft.CSharp.dll")
$newSet = Get-ChildItem "$staging" -Filter "*.dll" | Where-Object { $skip -notcontains $_.Name }
$newSet | ForEach-Object { Copy-Item $_.FullName $plugins -Force }

# Remove DLLs from a previous run that are no longer in the dependency closure. Keep the
# .meta files of surviving DLLs - some carry hand-tuned import settings (Common.Logging*
# has Auto Reference off so its root "Common" namespace can't collide with Unity's
# Burst/Collections package code).
$keep = @($newSet.Name) + @("Tmds.LibC.dll")
Get-ChildItem $plugins -Filter "*.dll" | Where-Object { $keep -notcontains $_.Name } | ForEach-Object {
    Write-Host "Removing stale plugin $($_.Name)."
    Remove-Item $_.FullName -Force
    Remove-Item "$($_.FullName).meta" -Force -ErrorAction SilentlyContinue
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
