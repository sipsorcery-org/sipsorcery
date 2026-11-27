<#
.SYNOPSIS
    Measures the SIPSorcery video pipeline across resolution presets: the maximum frame rate of each
    encoder and decoder alone, and of a real WebRTC session sending, receiving, or both. Writes
    markdown tables and the raw results.

.DESCRIPTION
    Two CLI commands do the measuring, each for what it is built for.

    "webrtc video-bench" times one component alone, in-process, with no network:

      ENCODE  - "video-bench --encoder <e> --codec <c>": the encoder runs flat out on a moving test
                pattern at the preset's bitrate. achievedFps is its maximum rate (RTP packetisation
                included). Measured for the managed vp8.net, av1.net and h264.net encoders (h264.net
                on 1 thread, as libwebrtc runs OpenH264, and on 4) and the ffmpeg encoder on H264,
                VP8, VP9, H265 and AV1.

      DECODE  - "video-bench --decoder <d> --codec <c>": a short clip is encoded once, untimed, then
                the decoder alone decodes it in a loop flat out. achievedFps is its maximum rate.
                Measured for the FFmpeg decoder on H264, VP8, VP9, H265 and AV1 (clips from the
                FFmpeg encoder; AV1 clips use a resolution based tile layout), for the managed
                vp8.net decoder on VP8 (clips from the vp8.net encoder, since it cannot decode
                FFmpeg's VP8), and for the managed h264.net decoder on the FFmpeg H264 clip (libx264
                Constrained Baseline), the same clip the FFmpeg H264 decoder gets.

    h264.net, the managed OpenH264 port, is for educational use only and is never packaged, so the
    published tool leaves it out. This script publishes the CLI from source with
    -p:IncludeH264Net=true to measure it; -SkipH264Net leaves it out.

    "webrtc loopback" runs a real WebRTC session in one process, publishing to an in-process WHIP
    receiver, for the PIPELINE table (ffmpeg encoder and decoder, -PipelineCodec):

      TRANSPORT - "--pre-encode --max-rate", no decode: pre-encoded frames replayed flat out and
                discarded by the receiver. The transport ceiling (packetise -> SRTP -> socket ->
                depacketise).

      SEND    - "--max-rate", no decode: live encode flat out, the receiver discards the frames.
                The maximum rate an app that only sends can publish at.

      RECEIVE - "--pre-encode --decode --video null": a pre-encoded stream decoded on receipt. The
                maximum rate an app that only receives can decode at.

      FULL CALL - "--decode --video null": live encode, send, receive and decode in one process.

    RECEIVE and FULL CALL are found by a search over --fps rather than by sending flat out: WebRTC
    has no backpressure, so a sender that outruns the decoder makes the receiver drop frames, which
    breaks the reference chain and stops later frames decoding. A rate passes when the sender
    delivers it and no more than -PipelineLossThreshold of the frames sent are lost, dropped or fail
    to decode. The search stops within -PipelinePrecision of the maximum.

    Both ends of the loopback run on one machine, so SEND also pays for depacketising and RECEIVE
    for replaying and encrypting. TRANSPORT shows how small that cost is next to the codecs.

    Numbers are machine specific, so treat them as a snapshot of this machine, not an absolute spec.

.NOTES
    Run from anywhere; paths are resolved relative to this script. Requires the .NET SDK and, for the
    ffmpeg encoder/decoder, the FFmpeg shared libraries (pass -FfmpegPath if they are not on PATH).
#>
[CmdletBinding()]
param(
    [string[]] $Presets = @('480p', '720p', '1080p', '1440p', '4k'),
    # The nominal frame rate the encoders run their rate control at, and the pass mark video-bench
    # compares against. The measured rates are maximums either way.
    [int]      $TargetFps = 30,
    # Realistic target bitrate (bps) per preset for the ffmpeg and av1.net encoders and the decode
    # clips. Ignored by vp8.net (fixed quantiser).
    [hashtable] $PresetBitrate = @{ '360p' = 700000; '480p' = 1500000; '720p' = 4000000; '1080p' = 8000000; '1440p' = 16000000; '2160p' = 25000000; '4k' = 25000000 },
    # The codec the pipeline table sends with (ffmpeg encoder and decoder); '' skips the table.
    [string]   $PipelineCodec = 'h264',
    # Frames the transport and receive probes pre-encode and replay.
    [int]      $PipelinePreEncodeFrames = 300,
    # Largest fraction of sent frames the receive and full-call probes may lose and still pass.
    [double]   $PipelineLossThreshold = 0.02,
    # The receive and full-call searches stop once the bounds are within this fraction of each other.
    [double]   $PipelinePrecision = 0.05,
    [int]      $DurationSeconds = 6,
    [int]      $Runs = 1,
    [string]   $FfmpegPath = '',
    [int]      $Port = 8080,
    [string]   $OutputDir = (Join-Path $PSScriptRoot 'results'),
    # Leaves out the h264.net columns, and builds the CLI as it is published, without SIPSorcery.H264.
    [switch]   $SkipH264Net
)

$ErrorActionPreference = 'Stop'
$listenUrl = "http://localhost:$Port/whip"

# Encode configs (label, video-bench --encoder, --codec, and any other arguments).
$encodeConfigs = @(
    @{ Label = 'vp8.net';     Encoder = 'vp8.net'; Codec = 'vp8'  },
    @{ Label = 'av1.net';     Encoder = 'av1.net'; Codec = 'av1'  },
    @{ Label = 'h264.net';    Encoder = 'h264.net'; Codec = 'h264' },
    @{ Label = 'h264.net (4 threads)'; Encoder = 'h264.net'; Codec = 'h264'; Extra = @('--threads', '4') },
    @{ Label = 'ffmpeg H264'; Encoder = 'ffmpeg';  Codec = 'h264' },
    @{ Label = 'ffmpeg VP8';  Encoder = 'ffmpeg';  Codec = 'vp8'  },
    @{ Label = 'ffmpeg VP9';  Encoder = 'ffmpeg';  Codec = 'vp9'  },
    @{ Label = 'ffmpeg H265'; Encoder = 'ffmpeg';  Codec = 'h265' },
    @{ Label = 'ffmpeg AV1';  Encoder = 'ffmpeg';  Codec = 'av1'  }
)
# Decode configs (label, video-bench --decoder, --codec, and --encoder for the clip). The managed
# vp8.net decoder gets a vp8.net clip: it cannot decode FFmpeg-encoded VP8 (a Vpx.Net inter-prediction bug).
$decodeConfigs = @(
    @{ Label = 'H264 (ffmpeg)'; Decoder = 'ffmpeg';  Codec = 'h264'; ClipEncoder = 'ffmpeg'  },
    @{ Label = 'VP8 (ffmpeg)';  Decoder = 'ffmpeg';  Codec = 'vp8';  ClipEncoder = 'ffmpeg'  },
    @{ Label = 'VP9 (ffmpeg)';  Decoder = 'ffmpeg';  Codec = 'vp9';  ClipEncoder = 'ffmpeg'  },
    @{ Label = 'H265 (ffmpeg)'; Decoder = 'ffmpeg';  Codec = 'h265'; ClipEncoder = 'ffmpeg'  },
    @{ Label = 'AV1 (ffmpeg)';  Decoder = 'ffmpeg';  Codec = 'av1';  ClipEncoder = 'ffmpeg'  },
    @{ Label = 'VP8 (vp8.net)'; Decoder = 'vp8.net'; Codec = 'vp8';  ClipEncoder = 'vp8.net' },
    @{ Label = 'H264 (h264.net)'; Decoder = 'h264.net'; Codec = 'h264'; ClipEncoder = 'ffmpeg' }
)
if ($SkipH264Net) {
    $encodeConfigs = @($encodeConfigs | Where-Object { $_.Encoder -ne 'h264.net' })
    $decodeConfigs = @($decodeConfigs | Where-Object { $_.Decoder -ne 'h264.net' })
}

# ---------------------------------------------------------------------------
# Build the CLI once in Release so per-run JIT/build noise is out of the loop.
# ---------------------------------------------------------------------------
$proj = Join-Path $PSScriptRoot '..' 'SIPSorcery.Cli.csproj'
# Publish to temp (not the output dir) so the results folder holds only the committable report.
$binDir = Join-Path ([System.IO.Path]::GetTempPath()) 'sipsorcery-vbench-bin'
Write-Host "Publishing CLI (Release) ..." -ForegroundColor Cyan
$publishArgs = @($proj, '-c', 'Release', '-o', $binDir, '--nologo')
if (-not $SkipH264Net) { $publishArgs += '-p:IncludeH264Net=true' }
dotnet publish @publishArgs | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Publishing the CLI failed." }
$exe = Join-Path $binDir 'SIPSorcery.Cli.exe'
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

# Runs the CLI and returns its parsed JSON result (or $null on failure).
function Invoke-Cli([string[]] $cliArgs) {
    if ($FfmpegPath) { $cliArgs += @('--ffmpeg-path', $FfmpegPath) }
    $stdout = & $exe @cliArgs 2>$null | Out-String
    if ([string]::IsNullOrWhiteSpace($stdout)) { return $null }
    try { return $stdout | ConvertFrom-Json } catch { return $null }
}

# ---------------------------------------------------------------------------
# Components alone: webrtc video-bench.
# ---------------------------------------------------------------------------

# The measured rate of a video-bench result, or NaN when the run did not measure one. A run that ran
# but missed -TargetFps still reports a valid maximum; one that failed outright (no frames, frames
# that decoded to no picture, an encoder error) does not.
function Get-BenchFps($r) {
    if (-not $r -or $null -eq $r.achievedFps -or [int]$r.framesProcessed -le 0) { return [double]::NaN }
    if ($r.success -or ($r.error -match 'only reached .* below the')) { return [double]$r.achievedFps }
    Write-Host "      failed: $($r.error)" -ForegroundColor Yellow
    return [double]::NaN
}

# Median of a video-bench measurement over $Runs.
function Measure-Bench([string[]] $benchArgs, [string] $preset) {
    $cliArgs = @('webrtc', 'video-bench', '--json', '--preset', $preset, '--fps', $TargetFps, '-d', $DurationSeconds) + $benchArgs
    $bitrate = Get-PresetBitrate $preset
    if ($bitrate -gt 0) { $cliArgs += @('--bitrate', $bitrate) }
    $samples = @()
    for ($i = 0; $i -lt $Runs; $i++) {
        $fps = Get-BenchFps (Invoke-Cli $cliArgs)
        if (-not [double]::IsNaN($fps)) { $samples += $fps }
    }
    if ($samples.Count -eq 0) { return [double]::NaN }
    return [math]::Round((Get-Median $samples), 1)
}

# ---------------------------------------------------------------------------
# The WebRTC pipeline: webrtc loopback.
# ---------------------------------------------------------------------------

# Runs one "webrtc loopback" session with the ffmpeg encoder and returns the parsed JSON (or $null).
function Invoke-Loopback([string] $preset, [int] $fps, [string[]] $extraArgs) {
    $cliArgs = @(
        'webrtc', 'loopback', '--json',
        '--listen', $listenUrl,
        '--encoder', 'ffmpeg',
        '--codec', $PipelineCodec,
        '--preset', $preset,
        '--fps', $fps,
        '--pattern', 'texture',
        '-d', $DurationSeconds,
        '-t', ($DurationSeconds + 60)
    ) + $extraArgs
    $bitrate = Get-PresetBitrate $preset
    if ($bitrate -gt 0) { $cliArgs += @('--bitrate', $bitrate) }
    $r = Invoke-Cli $cliArgs
    Start-Sleep -Milliseconds 300   # let the listener release before the next run
    return $r
}

# Median published rate of a flat-out (--max-rate) session with no decode: the transport ceiling
# when pre-encoded, the send ceiling when encoding live.
function Measure-FlatOut([string] $preset, [bool] $preEncode) {
    $extra = @('--max-rate')
    if ($preEncode) { $extra += @('--pre-encode', $PipelinePreEncodeFrames) }
    $samples = @()
    for ($i = 0; $i -lt $Runs; $i++) {
        # --fps only feeds the encoder's rate control here; --max-rate ignores it for pacing.
        $r = Invoke-Loopback $preset $TargetFps $extra
        if ($r -and $r.success -and $null -ne $r.publishedFps) { $samples += [double]$r.publishedFps }
    }
    if ($samples.Count -eq 0) { return [double]::NaN }
    return [math]::Round((Get-Median $samples), 1)
}

# Whether a decoding session paced at $fps holds that rate: the sender delivers it and no more than
# -PipelineLossThreshold of the frames sent are lost, dropped or fail to decode (median over $Runs).
function Test-DecodeRate([string] $preset, [int] $fps, [bool] $preEncode) {
    $extra = @('--decode', '--video', 'null')
    if ($preEncode) { $extra += @('--pre-encode', $PipelinePreEncodeFrames) }
    $losses = @()
    $note = ''
    for ($i = 0; $i -lt $Runs; $i++) {
        $r = Invoke-Loopback $preset $fps $extra
        if (-not ($r -and $r.success) -or $null -eq $r.publishedFps -or [double]$r.publishedFps -lt 0.95 * $fps) {
            $losses += 1.0   # the sender could not deliver the rate
            $note = if ($r -and $null -ne $r.publishedFps) { " (sender only reached $($r.publishedFps) fps)" } else { ' (session failed)' }
            continue
        }
        $published = if ($null -ne $r.publishedFrames) { [double]$r.publishedFrames } else { 0 }
        $decoded = if ($null -ne $r.videoFrames) { [double]$r.videoFrames } else { 0 }
        $losses += ($published -gt 0 ? [math]::Max(0, $published - $decoded) / $published : 1.0)
    }
    $loss = Get-Median $losses
    $pass = $loss -le $PipelineLossThreshold
    Write-Host ("      {0,5} fps -> {1:P1} lost {2}{3}" -f $fps, $loss, $(if ($pass) { 'pass' } else { 'fail' }), $note)
    return $pass
}

# The highest --fps up to $upper that Test-DecodeRate passes, to within -PipelinePrecision.
function Find-MaxDecodeRate([string] $preset, [double] $upper, [bool] $preEncode) {
    if ([double]::IsNaN($upper) -or $upper -lt 1) { return [double]::NaN }
    $hi = [int][math]::Floor($upper)
    if (Test-DecodeRate $preset $hi $preEncode) { return $hi }
    $lo = 0
    while (($hi - $lo) -gt [math]::Max(1, $PipelinePrecision * $hi)) {
        $mid = [int][math]::Floor(($lo + $hi) / 2)
        if (Test-DecodeRate $preset $mid $preEncode) { $lo = $mid } else { $hi = $mid }
    }
    return ($lo -gt 0 ? $lo : [double]::NaN)
}

# ---------------------------------------------------------------------------
# Run the sweep.
# ---------------------------------------------------------------------------
$pipelineDecodeLabel = ($decodeConfigs | Where-Object { $_.Codec -eq $PipelineCodec -and $_.Decoder -eq 'ffmpeg' } | Select-Object -First 1).Label

$rows = @()
foreach ($preset in $Presets) {
    Write-Host "Preset $preset" -ForegroundColor Cyan
    $row = [ordered]@{ Preset = $preset }

    foreach ($cfg in $encodeConfigs) {
        Write-Host "  encode $($cfg.Label) ..."
        $benchArgs = @('--encoder', $cfg.Encoder, '--codec', $cfg.Codec)
        if ($cfg.Extra) { $benchArgs += $cfg.Extra }
        $row["enc:$($cfg.Label)"] = Measure-Bench $benchArgs $preset
    }
    foreach ($cfg in $decodeConfigs) {
        Write-Host "  decode $($cfg.Label) ..."
        $row["dec:$($cfg.Label)"] = Measure-Bench @('--decoder', $cfg.Decoder, '--encoder', $cfg.ClipEncoder, '--codec', $cfg.Codec) $preset
    }

    if ($PipelineCodec) {
        Write-Host "  pipeline $PipelineCodec transport ..."
        $row['pipe:transport'] = Measure-FlatOut $preset $true
        Write-Host "  pipeline $PipelineCodec send ..."
        $row['pipe:send'] = Measure-FlatOut $preset $false

        # Receiving cannot beat the transport, nor (by much) the decoder alone.
        $receiveUpper = $row['pipe:transport']
        if ($pipelineDecodeLabel -and -not [double]::IsNaN([double]$row["dec:$pipelineDecodeLabel"])) {
            $receiveUpper = [math]::Min($receiveUpper, 1.1 * $row["dec:$pipelineDecodeLabel"])
        }
        Write-Host "  pipeline $PipelineCodec receive (search up to $([int]$receiveUpper) fps) ..."
        $row['pipe:receive'] = Find-MaxDecodeRate $preset $receiveUpper $true

        # A full call can neither send faster than the send ceiling nor decode faster than receive.
        $callUpper = [math]::Min([double]$row['pipe:send'], [double]$row['pipe:receive'])
        Write-Host "  pipeline $PipelineCodec full call (search up to $([int]$callUpper) fps) ..."
        $row['pipe:call'] = Find-MaxDecodeRate $preset $callUpper $false
    }

    $rows += [pscustomobject]$row
}

# ---------------------------------------------------------------------------
# Emit results.json and RESULTS.md.
# ---------------------------------------------------------------------------

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

function Add-Table([string[]] $columns, [string[]] $headers) {
    [void]$md.AppendLine('| Preset | ' + ($headers -join ' | ') + ' |')
    [void]$md.AppendLine('|' + ('---|' * ($headers.Count + 1)))
    foreach ($row in $rows) {
        $cells = $columns | ForEach-Object { $v = $row.$_; if ($null -eq $v -or [double]::IsNaN([double]$v)) { 'n/a' } else { $v } }
        [void]$md.AppendLine("| $($row.Preset) | " + ($cells -join ' | ') + ' |')
    }
    [void]$md.AppendLine('')
}

[void]$md.AppendLine('# Video pipeline capacity')
[void]$md.AppendLine('')
[void]$md.AppendLine('Frames per second, at a realistic per-preset bitrate. Every figure is a maximum rate.')
[void]$md.AppendLine('')
[void]$md.AppendLine('## Machine')
[void]$md.AppendLine('')
[void]$md.AppendLine('| CPU | Cores | Logical processors | Memory |')
[void]$md.AppendLine('| --- | --- | --- | --- |')
[void]$md.AppendLine("| $($machine.Model) | $($machine.Cores) | $($machine.LogicalProcessors) | $($machine.MemoryGB) GB |")
[void]$md.AppendLine('')
[void]$md.AppendLine('## Encode')
[void]$md.AppendLine('')
[void]$md.AppendLine('The encoder alone, in-process, flat out (webrtc video-bench --encoder).')
[void]$md.AppendLine('')
Add-Table @($encodeConfigs | ForEach-Object { "enc:$($_.Label)" }) @($encodeConfigs | ForEach-Object { $_.Label })
[void]$md.AppendLine('## Decode')
[void]$md.AppendLine('')
[void]$md.AppendLine('The decoder alone, in-process, flat out on a pre-encoded clip (webrtc video-bench --decoder).')
[void]$md.AppendLine('')
Add-Table @($decodeConfigs | ForEach-Object { "dec:$($_.Label)" }) @($decodeConfigs | ForEach-Object { $_.Label })
if ($PipelineCodec) {
    $codecName = $PipelineCodec.ToUpperInvariant()
    [void]$md.AppendLine("## WebRTC pipeline ($codecName, ffmpeg)")
    [void]$md.AppendLine('')
    [void]$md.AppendLine('A real WebRTC session in one process (webrtc loopback). Transport = pre-encoded frames sent flat out ' +
        'with no codec. Send = live encode sent flat out, frames discarded on receipt. Receive = a pre-encoded stream ' +
        'decoded on receipt. Full call = live encode, send, receive and decode. Receive and full call are the highest ' +
        "rates (to within $([int]($PipelinePrecision*100))%) at which no more than $([int]($PipelineLossThreshold*100))% " +
        'of frames are lost or fail to decode. Both ends run on this one machine.')
    [void]$md.AppendLine('')
    Add-Table @('pipe:transport', 'pipe:send', 'pipe:receive', 'pipe:call') @('Transport (no codec)', 'Send (encode)', 'Receive (decode)', 'Full call (encode + decode)')
}
[void]$md.AppendLine("_Generated $(Get-Date -Format 'yyyy-MM-dd HH:mm') with src/SIPSorcery.Cli/benchmarks/run-video-bench.ps1; duration ${DurationSeconds}s/run, $Runs run(s)/point._")
$md.ToString() | Set-Content (Join-Path $OutputDir 'RESULTS.md')

Write-Host ""
Write-Host "Done. Wrote results.json and RESULTS.md to $OutputDir" -ForegroundColor Green
Write-Host ($machine | Format-List | Out-String)
Write-Host ($rows | Format-Table -AutoSize | Out-String)
