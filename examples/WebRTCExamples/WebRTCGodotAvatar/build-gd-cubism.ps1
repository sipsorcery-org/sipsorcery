<#
.SYNOPSIS
    Builds the gd_cubism GDExtension native library and installs the addon into addons/gd_cubism.

.DESCRIPTION
    The Live2D (2D) avatar needs the gd_cubism GDExtension, whose native library links Live2D's
    Cubism Core and therefore cannot be redistributed - it has to be built from source against the
    Cubism SDK for Native (see README, "2b. gd_cubism GDExtension"). This script automates that:

      1. Clones https://github.com/MizunagiKB/gd_cubism (with the godot-cpp submodule) into
         ThirdParty/gd_cubism, unless -GdCubismSource points at an existing clone.
      2. Verifies the Cubism SDK for Native has been extracted into
         <source>/thirdparty/CubismSdkForNative-5-r.x (this you must download and accept the
         Live2D licence for - it cannot be scripted).
      3. Builds template_debug + template_release with SCons under the VS2022 x64 toolchain.
      4. Installs the built addon (wrappers + native binaries) into this project's addons/gd_cubism.

    The VRM (3D) avatar does not need any of this.

.PARAMETER GdCubismSource
    Path to an existing gd_cubism source checkout to build instead of cloning a fresh one.

.EXAMPLE
    ./build-gd-cubism.ps1
#>
param(
    [string]$GdCubismSource,

    # gd_cubism commit to build. Defaults to the revision known to compile against Cubism SDK for
    # Native 5-r.5. Newer gd_cubism revisions call CubismModel::GetDrawableRenderOrders(), which does
    # not exist in 5-r.5 and fails with C2039 - they need a newer SDK. Pass 'main' (and a newer SDK)
    # to build the latest instead.
    [string]$GdCubismRef = '3aaa3c9001808732c40aa3fa07460a95125d9ccc'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$gdCubism = if ($GdCubismSource) { $GdCubismSource } else { Join-Path $root 'ThirdParty\gd_cubism' }

# 1. Get the gd_cubism source (clone if absent).
if (-not (Test-Path -LiteralPath (Join-Path $gdCubism 'SConstruct'))) {
    if (Test-Path -LiteralPath $gdCubism) {
        throw "'$gdCubism' exists but is not a gd_cubism source checkout (no SConstruct). Remove it or pass -GdCubismSource."
    }
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'git was not found on PATH; needed to clone gd_cubism.'
    }
    Write-Host "Cloning gd_cubism into $gdCubism ..."
    git clone --recurse-submodules https://github.com/MizunagiKB/gd_cubism $gdCubism
    if ($LASTEXITCODE -ne 0) { throw 'git clone of gd_cubism failed.' }
}

# The source tree bundles its own copy of the addon's C# scripts. When it lives inside this Godot
# project (the default ThirdParty location) drop a .gdignore so the editor skips it; the .csproj
# also excludes ThirdParty/**/*.cs from compilation.
if (-not $GdCubismSource) {
    $gdignore = Join-Path $root 'ThirdParty\.gdignore'
    if (-not (Test-Path -LiteralPath $gdignore)) { New-Item -ItemType File -Path $gdignore -Force | Out-Null }
}

# 1b. Pin to the known-good revision (see -GdCubismRef) and sync godot-cpp to match it. The -f
# resets tracked files to the commit so the compatibility patch below applies to a clean tree.
if ($GdCubismRef) {
    Push-Location $gdCubism
    try {
        git -c advice.detachedHead=false checkout -f $GdCubismRef 2>$null
        if ($LASTEXITCODE -ne 0) {
            # A shallow clone may not contain the pinned commit yet; deepen and retry.
            git fetch --unshallow 2>$null
            git fetch origin $GdCubismRef 2>$null
            git -c advice.detachedHead=false checkout -f $GdCubismRef
            if ($LASTEXITCODE -ne 0) { throw "Could not checkout gd_cubism ref '$GdCubismRef'." }
        }
        git submodule update --init
        if ($LASTEXITCODE -ne 0) { throw 'git submodule update failed (godot-cpp).' }
    }
    finally {
        Pop-Location
    }
}

# 1c. gd_cubism 3aaa3c9 was written against an older Cubism SDK; the Core/Framework API changed in
# 5-r.5 (GetDrawableRenderOrders removed, renderer ctor gained width/height, blend-mode enums moved,
# etc.). Apply the compatibility patch so it builds against 5-r.5. Only for the pinned default ref.
$patch = Join-Path $root 'patches\gd_cubism-sdk-5-r.5.patch'
if (($GdCubismRef -eq '3aaa3c9001808732c40aa3fa07460a95125d9ccc') -and (Test-Path -LiteralPath $patch)) {
    Push-Location $gdCubism
    try {
        git apply --whitespace=nowarn $patch
        if ($LASTEXITCODE -ne 0) { throw "Failed to apply Cubism SDK 5-r.5 compatibility patch ($patch)." }
        Write-Host 'Applied Cubism SDK 5-r.5 compatibility patch.'
    }
    finally {
        Pop-Location
    }
}

# 2. The Cubism SDK for Native is a licensed download and cannot be scripted.
$sdk = Get-ChildItem -LiteralPath (Join-Path $gdCubism 'thirdparty') -Directory -Filter 'CubismSdkForNative-*' -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $sdk) {
    throw @"
Cubism SDK for Native is required to build gd_cubism.

Download it from Live2D (https://www.live2d.com/en/sdk/download/native/), accept the licence, and
extract it here (5-r.5 matches the default pinned gd_cubism revision):
  $(Join-Path $gdCubism 'thirdparty\CubismSdkForNative-5-r.5')

Then rerun this script.
"@
}

# Locate the VS2022 x64 developer environment (needed - gd_cubism links Live2D's MSVC libraries).
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudioPath = if (Test-Path -LiteralPath $vswhere) {
    & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1
} else {
    $null
}
$devCmd = if ($visualStudioPath) { Join-Path $visualStudioPath 'Common7\Tools\VsDevCmd.bat' } else { $null }

if (-not $visualStudioPath -and -not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
    throw @'
The Microsoft C++ build tools were not found.

Install the "Desktop development with C++" workload in Visual Studio 2022 (or the Build Tools with
the MSVC v143 toolset), then rerun this script.
'@
}

function Invoke-SConsBuild {
    param([Parameter(Mandatory = $true)][string] $Target)

    if ($devCmd -and (Test-Path -LiteralPath $devCmd)) {
        $command = "`"$devCmd`" -arch=x64 -host_arch=x64 && python -m SCons platform=windows arch=x86_64 MSVC_VERSION=14.3 silence_msvc=no target=$Target"
        cmd.exe /d /s /c $command
    }
    else {
        python -m SCons platform=windows arch=x86_64 MSVC_VERSION=14.3 silence_msvc=no target=$Target
    }

    if ($LASTEXITCODE -ne 0) { throw "SCons failed while building $Target." }
}

Push-Location $gdCubism
try {
    # 3. Build.
    python -m pip install --user "scons>=4.10,<5"
    if ($LASTEXITCODE -ne 0) { throw 'Installing SCons failed.' }

    Invoke-SConsBuild -Target 'template_debug'
    Invoke-SConsBuild -Target 'template_release'
}
finally {
    Pop-Location
}

# 4. Install the built addon into this project.
$builtAddon = Join-Path $gdCubism 'demo\addons\gd_cubism'
$projectAddon = Join-Path $root 'addons\gd_cubism'
$builtBin = Join-Path $builtAddon 'bin'

foreach ($name in @('libgd_cubism.windows.debug.x86_64.dll', 'libgd_cubism.windows.release.x86_64.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $builtBin $name))) {
        throw "Build completed but expected native library is missing: $(Join-Path $builtBin $name)"
    }
}

Write-Host "Installing addon into $projectAddon ..."
New-Item -ItemType Directory -Path $projectAddon -Force | Out-Null
# robocopy exit codes 0-7 are success; treat >=8 as failure.
robocopy $builtAddon $projectAddon /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed copying the addon (exit $LASTEXITCODE)." }
$global:LASTEXITCODE = 0

Write-Host "gd_cubism installed. The Live2D avatar can now render: ./run-demo.ps1 -Avatar ren"
