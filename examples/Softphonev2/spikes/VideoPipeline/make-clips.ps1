# Generates the test clips the far end streams. Needs ffmpeg (with libvpx and libx264) on PATH.
#
# The clips are synthetic (testsrc2 plus per-frame noise, so the encoder can't coast) and encoded
# the way a real-time sender would: constant bitrate, no B-frames or alt-ref frames, and a keyframe
# every second, so a decoder that joins mid-stream starts within a second. H.264 is constrained
# baseline (what most SIP video phones send) with SPS/PPS repeated before every keyframe, and an
# access unit delimiter so the far end can split frames.

$ErrorActionPreference = "Stop"
$media = Join-Path $PSScriptRoot "media"
New-Item -ItemType Directory -Force $media | Out-Null

$clips = @(
    @{ Name = "720p30";  Size = "1280x720";  Rate = "2M"; Level = "3.1" },
    @{ Name = "1080p30"; Size = "1920x1080"; Rate = "4M"; Level = "4.0" }
)

foreach ($c in $clips) {
    $src = "testsrc2=size=$($c.Size):rate=30,noise=alls=12:allf=t,format=yuv420p"

    ffmpeg -hide_banner -loglevel error -y -f lavfi -i $src -t 10 `
        -c:v libvpx -deadline realtime -cpu-used 4 -b:v $c.Rate -minrate $c.Rate -maxrate $c.Rate -bufsize 1M `
        -g 30 -keyint_min 30 -lag-in-frames 0 -auto-alt-ref 0 -error-resilient 1 `
        (Join-Path $media "vp8-$($c.Name).ivf")

    ffmpeg -hide_banner -loglevel error -y -f lavfi -i $src -t 10 `
        -c:v libx264 -profile:v baseline -level $c.Level -preset veryfast -tune zerolatency `
        -b:v $c.Rate -maxrate $c.Rate -bufsize 1M -g 30 -keyint_min 30 -bf 0 `
        -x264-params "repeat-headers=1" -bsf:v h264_metadata=aud=insert -f h264 `
        (Join-Path $media "h264-$($c.Name).h264")
}

Get-ChildItem $media | Format-Table Name, Length
