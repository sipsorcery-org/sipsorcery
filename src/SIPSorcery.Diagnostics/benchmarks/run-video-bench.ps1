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
                        the managed vp8.net encoder and the ffmpeg encoder on H264, VP8, VP9, H265 and AV1.

      DECODE breakpoint - publish with --decode --video null (decode in-process, discard) and sweep
                        --fps upward until the receiver drops more than -DropThreshold of frames. The
                        last rate under the threshold is the max sustainable decode rate. The frames
                        are pre-encoded once (-PreEncodeFrames) and the encoded bitstream is replayed,
                        so no encoding runs during the window and the breakpoint reflects the decoder
                        alone. Measured for the FFmpeg decoder on H264, VP8, VP9, H265 and AV1 (driven
                        by the FFmpeg encoder), and for the managed vp8.net decoder on VP8 (driven by
                        the vp8.net encoder, since it crashes on FFmpeg-encoded VP8; capped at <=1080p
                        as vp8.net encode is too slow to pre-encode above that).

      PLUMBING ceiling - publish flat out (--max-rate) with neither encoder nor decoder: pre-encoded
                        frames are replayed and the receiver discards them without decoding. The
                        reported publishedFps is the pure WebRTC transport ceiling (packetise -> SRTP ->
                        socket -> depacketise), the theoretical maximum the encode/decode stages sit under.

    Numbers are machine specific, so treat them as a snapshot of this machine, not an absolute spec.
    With -PreEncodeFrames 0 the decode probe encodes live, so encode and decode then share CPU.

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
    # Frames the decode probe pre-encodes once and then replays over the network, so no encoding runs
    # during the decode measurement (the encoder is fully out of the hot loop, isolating decode). 0
    # disables it and encodes live. Higher gives more distinct frames at the cost of longer per-cell
    # startup; pre-encode happens before connecting, so it does not eat the receiver's media window.
    [int]      $PreEncodeFrames = 300,
    [double]   $DropThreshold = 0.10,
    [int]      $DurationSeconds = 6,
    [int]      $Runs = 1,
    [string]   $FfmpegPath = '',
    [int]      $Port = 8080,
    [string]   $OutputDir = (Join-Path $PSScriptRoot 'results')
)

$ErrorActionPreference = 'Stop'
$listenUrl = "http://localhost:$Port/whip"

# The encode configs (label, --encoder, --codec) and the decode codecs to sweep. The ffmpeg encoder
# covers every codec; vp8.net is the managed VP8-only encoder.
$encodeConfigs = @(
    @{ Label = 'vp8.net';      Encoder = 'vp8.net'; Codec = $null   },
    @{ Label = 'ffmpeg H264';  Encoder = 'ffmpeg';  Codec = 'h264'  },
    @{ Label = 'ffmpeg VP8';   Encoder = 'ffmpeg';  Codec = 'vp8'   },
    @{ Label = 'ffmpeg VP9';   Encoder = 'ffmpeg';  Codec = 'vp9'   },
    @{ Label = 'ffmpeg H265';  Encoder = 'ffmpeg';  Codec = 'h265'  },
    @{ Label = 'ffmpeg AV1';   Encoder = 'ffmpeg';  Codec = 'av1'   }
)
# Decode configs (label, driving encoder, codec, decoder). The FFmpeg decoder handles every codec and
# is driven by the fast FFmpeg encoder. The managed vp8.net decoder is driven by the vp8.net encoder:
# it crashes on FFmpeg-encoded VP8 (a Vpx.Net inter-prediction bug) and only reliably decodes its own
# bitstream.
$decodeConfigs = @(
    @{ Label = 'H264 (ffmpeg)'; Encoder = 'ffmpeg';  Codec = 'h264'; Decoder = 'ffmpeg'  },
    @{ Label = 'VP8 (ffmpeg)';  Encoder = 'ffmpeg';  Codec = 'vp8';  Decoder = 'ffmpeg'  },
    @{ Label = 'VP9 (ffmpeg)';  Encoder = 'ffmpeg';  Codec = 'vp9';  Decoder = 'ffmpeg'  },
    @{ Label = 'H265 (ffmpeg)'; Encoder = 'ffmpeg';  Codec = 'h265'; Decoder = 'ffmpeg'  },
    @{ Label = 'AV1 (ffmpeg)';  Encoder = 'ffmpeg';  Codec = 'av1';  Decoder = 'ffmpeg'  },
    @{ Label = 'VP8 (vp8.net)'; Encoder = 'vp8.net'; Codec = 'vp8';  Decoder = 'vp8.net' }
)
# vp8.net encode is too slow to pre-encode above 1080p, so the vp8.net decode column is capped here
# (larger presets report n/a). The decode measurement itself is still valid; only the one-time
# pre-encode would be impractically slow.
$vp8netDecodePresets = @('360p', '480p', '720p', '1080p')

# ---------------------------------------------------------------------------
# Build the CLI once in Release so per-run JIT/build noise is out of the loop.
# ---------------------------------------------------------------------------
$proj = Join-Path $PSScriptRoot '..' 'SIPSorcery.Diagnostics.csproj'
# Publish to temp (not the output dir) so the results folder holds only the committable report.
$binDir = Join-Path ([System.IO.Path]::GetTempPath()) 'sipsorcery-vbench-bin'
Write-Host "Publishing CLI (Release) ..." -ForegroundColor Cyan
dotnet publish $proj -c Release -o $binDir --nologo | Out-Null
$exe = Join-Path $binDir 'SIPSorcery.Diagnostics.exe'
if (-not (Test-Path $exe)) { throw "Published CLI not found at $exe." }

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

function Get-Median([double[]] $values) {
    if ($values.Count -eq 0) { return [double]::NaN }
    $sorted = $values | Sort-Object
    return $sorted[[int][math]::Floor(($sorted.Count - 1) / 2)]
}

function Get-PresetBitrate([string] $preset) {
    if ($PresetBitrate.ContainsKey($preset)) { return [int]$PresetBitrate[$preset] } else { return 0 }
}

# Runs one "webrtc loopback" measurement and returns the parsed JSON (or $null on failure).
function Invoke-Cell([string] $encoder, [string] $codec, [string] $preset, [int] $fps, [bool] $decode, [int] $bitrate = 0, [int] $preEncode = 0, [bool] $maxRate = $false, [string] $decoder = 'ffmpeg') {
    $cliArgs = @(
        'webrtc', 'loopback', '--json',
        '--listen', $listenUrl,
        '--encoder', $encoder,
        '--preset', $preset,
        '--fps', $fps,
        '-d', $DurationSeconds,
        '-t', ($DurationSeconds + 60)
    )
    if ($codec) { $cliArgs += @('--codec', $codec) }
    if ($bitrate -gt 0) { $cliArgs += @('--bitrate', $bitrate) }
    if ($preEncode -gt 0) { $cliArgs += @('--pre-encode', $preEncode) }
    if ($maxRate) { $cliArgs += '--max-rate' }
    if ($decode) { $cliArgs += @('--decode', '--decoder', $decoder, '--video', 'null') }
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

# Sweep --fps with in-process decode; return the highest rate whose median drop stays at or below the
# threshold (and where the sender actually delivered the rate). When $PreEncodeFrames > 0 the frames
# are encoded once up front (ffmpeg) and replayed, so no encoding runs during the window and the
# breakpoint reflects the decoder alone rather than encode+decode sharing CPU.
function Measure-DecodeBreakpoint([string] $encoder, [string] $codec, [string] $decoder, [string] $preset) {
    $bitrate = Get-PresetBitrate $preset
    $best = 0
    foreach ($fps in ($FpsLadder | Sort-Object)) {
        $dropSamples = @()
        $delivered = $true
        for ($i = 0; $i -lt $Runs; $i++) {
            $r = Invoke-Cell $encoder $codec $preset $fps $true $bitrate $PreEncodeFrames $false $decoder
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
        Write-Host ("    {0} {1}/{2} @ {3,4} fps -> {4:P1} drop" -f $preset, $codec, $decoder, $fps, $medianDrop)
        if ($medianDrop -le $DropThreshold) { $best = $fps } else { break }
    }
    return $best
}

# Median publishedFps flat out with neither encoder nor decoder in the loop: frames are pre-encoded
# (ffmpeg) and replayed, the receiver discards them without decoding. This is the pure WebRTC plumbing
# ceiling - packetise -> SRTP -> socket -> SRTP -> depacketise - i.e. the theoretical maximum the
# encode/decode stages sit under. Uses --max-rate because the plumbing far outruns the paced ladder.
function Measure-PlumbingCeiling([string] $preset) {
    $bitrate = Get-PresetBitrate $preset
    # Always replay a pre-encoded ring (so there is no encoder) even if -PreEncodeFrames is 0.
    $preEncode = if ($PreEncodeFrames -gt 0) { $PreEncodeFrames } else { 300 }
    $samples = @()
    for ($i = 0; $i -lt $Runs; $i++) {
        $r = Invoke-Cell 'ffmpeg' 'h264' $preset $EncodeProbeFps $false $bitrate $preEncode $true
        if ($r -and $r.success -and $null -ne $r.publishedFps) { $samples += [double]$r.publishedFps }
    }
    if ($samples.Count -eq 0) { return [double]::NaN }
    return [math]::Round((Get-Median $samples), 1)
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
        if ($cfg.Decoder -eq 'vp8.net' -and $preset -notin $vp8netDecodePresets) {
            Write-Host "  decode $($cfg.Label) ... n/a (vp8.net encode impractical above 1080p)"
            $row["dec:$($cfg.Label)"] = [double]::NaN
            continue
        }
        Write-Host "  decode $($cfg.Label) ..."
        $row["dec:$($cfg.Label)"] = Measure-DecodeBreakpoint $cfg.Encoder $cfg.Codec $cfg.Decoder $preset
    }
    Write-Host "  plumbing (no codec) ..."
    $row["plumbing"] = Measure-PlumbingCeiling $preset

    $rows += [pscustomobject]$row
}

# ---------------------------------------------------------------------------
# Emit results.json and RESULTS.md.
# ---------------------------------------------------------------------------
$columns = @('enc:vp8.net', 'enc:ffmpeg H264', 'enc:ffmpeg VP8', 'enc:ffmpeg VP9', 'enc:ffmpeg H265', 'enc:ffmpeg AV1',
             'dec:H264 (ffmpeg)', 'dec:VP8 (ffmpeg)', 'dec:VP9 (ffmpeg)', 'dec:H265 (ffmpeg)', 'dec:AV1 (ffmpeg)', 'dec:VP8 (vp8.net)', 'plumbing')
$headers = @('Encode vp8.net', 'Encode ffmpeg H264', 'Encode ffmpeg VP8', 'Encode ffmpeg VP9', 'Encode ffmpeg H265', 'Encode ffmpeg AV1',
             'Decode H264 (ffmpeg)', 'Decode VP8 (ffmpeg)', 'Decode VP9 (ffmpeg)', 'Decode H265 (ffmpeg)', 'Decode AV1 (ffmpeg)', 'Decode VP8 (vp8.net)', 'Plumbing (no codec)')

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
[void]$md.AppendLine("Maximum sustainable frame rate (fps), at a realistic per-preset bitrate. " +
    "Encode = encoder ceiling (publish flat out). Decode = highest rate under $([int]($DropThreshold*100))% " +
    "received-frame loss for the named decoder (FFmpeg, or managed vp8.net for the vp8.net column, which " +
    "is capped at <=1080p)" +
    $(if ($PreEncodeFrames -gt 0) { ", fed a pre-encoded bitstream so no encoding competes for CPU. " } else { ", with frames encoded live (encode and decode share CPU). " }) +
    "Plumbing (no codec) = the transport ceiling with neither encoder nor decoder (pre-encoded frames " +
    "replayed flat out, received and discarded): packetise -> SRTP -> socket -> depacketise only.")
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
