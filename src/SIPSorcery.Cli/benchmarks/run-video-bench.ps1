<#
.SYNOPSIS
    Sweeps the SIPSorcery WebRTC video pipeline across resolution presets and encoders to find the
    maximum sustainable frame rate for the ENCODE and DECODE stages, then writes a markdown table,
    an SVG chart and the raw results.

.DESCRIPTION
    Every measurement is one self-contained "webrtc loopback" run (it publishes a test pattern to an
    in-process WHIP receiver and receives it back), so no second terminal and no network are involved.

      ENCODE ceiling  - publish flat out (a very high --fps the encoder cannot reach) with no decode;
                        the reported publishedFps is the encoder's max sustainable rate. Measured for
                        vp8.net, ffmpeg H264 and ffmpeg VP8.

      DECODE breakpoint - publish with --decode --video null (decode in-process, discard) and sweep
                        --fps upward until the receiver drops more than -DropThreshold of frames. The
                        last rate under the threshold is the max sustainable decode rate. Driven by
                        the fast ffmpeg encoder (so the decoder, not the encoder, is the limit) for
                        H264 and VP8; the decoder is always the SIPSorcery (FFmpeg) decoder.

    Numbers are machine specific and the encode/decode stages share CPU on this one box, so treat
    them as a snapshot of this machine, not an absolute spec.

.NOTES
    Run from anywhere; paths are resolved relative to this script. Requires the .NET SDK and, for the
    ffmpeg encoder/decoder, the FFmpeg shared libraries (pass -FfmpegPath if they are not on PATH).
#>
[CmdletBinding()]
param(
    [string[]] $Presets = @('480p', '720p', '1080p', '1440p', '4k'),
    [int[]]    $FpsLadder = @(15, 30, 60, 90, 120),
    [int]      $EncodeProbeFps = 500,
    # Realistic target bitrate (bps) per preset for the ffmpeg encoder. Without this the encoder's
    # auto-bitrate scales with the probe fps (hundreds of Mbps at high fps), which makes every encode
    # measurement bitrate-bound rather than encoder-speed-bound. Ignored by vp8.net (fixed quantiser).
    [hashtable] $PresetBitrate = @{ '360p' = 700000; '480p' = 1500000; '720p' = 4000000; '1080p' = 8000000; '1440p' = 16000000; '2160p' = 25000000; '4k' = 25000000 },
    [double]   $DropThreshold = 0.10,
    [int]      $DurationSeconds = 6,
    [int]      $Runs = 1,
    [string]   $FfmpegPath = '',
    [int]      $Port = 8080,
    [string]   $OutputDir = (Join-Path $PSScriptRoot 'results')
)

$ErrorActionPreference = 'Stop'
$listenUrl = "http://localhost:$Port/whip"

# The encode configs (label, --encoder, --codec) and the decode codecs to sweep.
$encodeConfigs = @(
    @{ Label = 'vp8.net';      Encoder = 'vp8.net'; Codec = $null   },
    @{ Label = 'ffmpeg H264';  Encoder = 'ffmpeg';  Codec = 'h264'  },
    @{ Label = 'ffmpeg VP8';   Encoder = 'ffmpeg';  Codec = 'vp8'   }
)
$decodeConfigs = @(
    @{ Label = 'H264'; Codec = 'h264' },
    @{ Label = 'VP8';  Codec = 'vp8'  }
)

# ---------------------------------------------------------------------------
# Build the CLI once in Release so per-run JIT/build noise is out of the loop.
# ---------------------------------------------------------------------------
$proj = Join-Path $PSScriptRoot '..' 'SIPSorcery.Cli.csproj'
# Publish to temp (not the output dir) so the results folder holds only the committable report.
$binDir = Join-Path ([System.IO.Path]::GetTempPath()) 'sipsorcery-vbench-bin'
Write-Host "Publishing CLI (Release) ..." -ForegroundColor Cyan
dotnet publish $proj -c Release -o $binDir --nologo | Out-Null
$exe = Join-Path $binDir 'SIPSorcery.Cli.exe'
if (-not (Test-Path $exe)) { throw "Published CLI not found at $exe." }

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

function Get-Median([double[]] $values) {
    if ($values.Count -eq 0) { return [double]::NaN }
    $sorted = $values | Sort-Object
    return $sorted[[int][math]::Floor(($sorted.Count - 1) / 2)]
}

# Runs one whip-server --publish measurement and returns the parsed JSON (or $null on failure).
function Get-PresetBitrate([string] $preset) {
    if ($PresetBitrate.ContainsKey($preset)) { return [int]$PresetBitrate[$preset] } else { return 0 }
}

function Invoke-Cell([string] $encoder, [string] $codec, [string] $preset, [int] $fps, [bool] $decode, [int] $bitrate = 0) {
    $cliArgs = @(
        'webrtc', 'loopback', '--json',
        '--listen', $listenUrl,
        '--encoder', $encoder,
        '--preset', $preset,
        '--fps', $fps,
        '-d', $DurationSeconds,
        '-t', ($DurationSeconds + 20)
    )
    if ($codec) { $cliArgs += @('--codec', $codec) }
    if ($bitrate -gt 0) { $cliArgs += @('--bitrate', $bitrate) }
    if ($decode) { $cliArgs += @('--decode', '--video', 'null') }
    if ($FfmpegPath) { $cliArgs += @('--ffmpeg-path', $FfmpegPath) }

    $stdout = & $exe @cliArgs 2>$null | Out-String
    Start-Sleep -Milliseconds 300   # let the listener release before the next run
    if ([string]::IsNullOrWhiteSpace($stdout)) { return $null }
    try { return $stdout | ConvertFrom-Json } catch { return $null }
}

# Median publishedFps over $Runs at a flat-out target (the encode ceiling).
function Measure-EncodeCeiling($cfg, [string] $preset) {
    $bitrate = Get-PresetBitrate $preset
    $samples = @()
    for ($i = 0; $i -lt $Runs; $i++) {
        $r = Invoke-Cell $cfg.Encoder $cfg.Codec $preset $EncodeProbeFps $false $bitrate
        if ($r -and $r.success -and $null -ne $r.publishedFps) { $samples += [double]$r.publishedFps }
    }
    if ($samples.Count -eq 0) { return [double]::NaN }
    return [math]::Round((Get-Median $samples), 1)
}

# Sweep --fps with the ffmpeg encoder + in-process decode; return the highest rate whose median drop
# stays at or below the threshold (and where the sender actually delivered the rate).
function Measure-DecodeBreakpoint([string] $codec, [string] $preset) {
    $bitrate = Get-PresetBitrate $preset
    $best = 0
    foreach ($fps in ($FpsLadder | Sort-Object)) {
        $dropSamples = @()
        $delivered = $true
        for ($i = 0; $i -lt $Runs; $i++) {
            $r = Invoke-Cell 'ffmpeg' $codec $preset $fps $true $bitrate
            if (-not ($r -and $r.success)) { $delivered = $false; break }
            # The decode test is only valid if the sender kept up (decoder actually stressed at fps).
            if ($null -eq $r.publishedFps -or [double]$r.publishedFps -lt 0.9 * $fps) { $delivered = $false; break }
            $written = if ($null -ne $r.videoFrames) { [double]$r.videoFrames } else { 0 }
            $dropped = if ($null -ne $r.videoFramesDropped) { [double]$r.videoFramesDropped } else { 0 }
            $total = $written + $dropped
            $dropSamples += ($total -gt 0 ? ($dropped / $total) : 0)
        }
        if (-not $delivered) { break }     # encoder/transport can no longer feed this rate
        $medianDrop = Get-Median $dropSamples
        Write-Host ("    {0} {1} @ {2,4} fps -> {3:P1} drop" -f $preset, $codec, $fps, $medianDrop)
        if ($medianDrop -le $DropThreshold) { $best = $fps } else { break }
    }
    return $best
}

# ---------------------------------------------------------------------------
# Run the sweep.
# ---------------------------------------------------------------------------
$rows = @()
foreach ($preset in $Presets) {
    Write-Host "Preset $preset" -ForegroundColor Cyan
    $row = [ordered]@{ Preset = $preset }

    foreach ($cfg in $encodeConfigs) {
        Write-Host "  encode $($cfg.Label) ..."
        $row["enc:$($cfg.Label)"] = Measure-EncodeCeiling $cfg $preset
    }
    foreach ($cfg in $decodeConfigs) {
        Write-Host "  decode $($cfg.Label) ..."
        $row["dec:$($cfg.Label)"] = Measure-DecodeBreakpoint $cfg.Codec $preset
    }

    $rows += [pscustomobject]$row
}

# ---------------------------------------------------------------------------
# Emit results.json and RESULTS.md.
# ---------------------------------------------------------------------------
$columns = @('enc:vp8.net', 'enc:ffmpeg H264', 'enc:ffmpeg VP8', 'dec:H264', 'dec:VP8')
$headers = @('Encode vp8.net', 'Encode ffmpeg H264', 'Encode ffmpeg VP8', 'Decode H264', 'Decode VP8')

# Capture the machine the benchmark ran on so the numbers have context.
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$mem = (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB
$machine = [PSCustomObject]@{
    Model             = $cpu.Name
    Cores             = $cpu.NumberOfCores
    LogicalProcessors = $cpu.NumberOfLogicalProcessors
    MemoryGB          = [math]::Round($mem, 1)
}

[PSCustomObject]@{ Machine = $machine; Results = $rows } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutputDir 'results.json')

$md = [System.Text.StringBuilder]::new()
[void]$md.AppendLine('# Video pipeline capacity')
[void]$md.AppendLine('')
[void]$md.AppendLine("Maximum sustainable frame rate (fps), encoded at a realistic per-preset bitrate. " +
    "Encode = encoder ceiling (publish flat out). Decode = highest rate under $([int]($DropThreshold*100))% " +
    "received-frame loss (SIPSorcery FFmpeg decoder, driven by ffmpeg).")
[void]$md.AppendLine('')
[void]$md.AppendLine('## Machine')
[void]$md.AppendLine('')
[void]$md.AppendLine('| CPU | Cores | Logical processors | Memory |')
[void]$md.AppendLine('| --- | --- | --- | --- |')
[void]$md.AppendLine("| $($machine.Model) | $($machine.Cores) | $($machine.LogicalProcessors) | $($machine.MemoryGB) GB |")
[void]$md.AppendLine('')
[void]$md.AppendLine('## Results')
[void]$md.AppendLine('')
[void]$md.AppendLine('| Preset | ' + ($headers -join ' | ') + ' |')
[void]$md.AppendLine('|' + ('---|' * ($headers.Count + 1)))
foreach ($row in $rows) {
    $cells = $columns | ForEach-Object { $v = $row.$_; if ($null -eq $v -or [double]::IsNaN([double]$v)) { 'n/a' } else { $v } }
    [void]$md.AppendLine("| $($row.Preset) | " + ($cells -join ' | ') + ' |')
}
[void]$md.AppendLine('')
[void]$md.AppendLine("_Generated $(Get-Date -Format 'yyyy-MM-dd HH:mm'); duration ${DurationSeconds}s/run, $Runs run(s)/point._")
$md.ToString() | Set-Content (Join-Path $OutputDir 'RESULTS.md')

Write-Host ""
Write-Host "Done. Wrote results.json and RESULTS.md to $OutputDir" -ForegroundColor Green
Write-Host ($machine | Format-List | Out-String)
Write-Host ($rows | Format-Table -AutoSize | Out-String)
