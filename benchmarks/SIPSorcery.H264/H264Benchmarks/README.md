# H264Benchmarks

Benchmarks for [SIPSorcery.H264](../../../src/SIPSorcery.H264), the C# port of
OpenH264.

The frames are generated, not read from a file, so nothing needs downloading.
They look like a camera's: a textured background panning at a fraction of a
pixel a frame, a textured block crossing it on a path of its own, and a little
sensor noise. That gives motion estimation, sub-pixel interpolation and the
residual coding real work, which a test pattern moving a whole number of pixels
a frame doesn't. It is harder to code than the CLI's `video-bench` pattern, so
the frame rates here are lower than that tool's.

The encoder is set up as `H264Codec` sets it up, with libwebrtc's settings, but
its timestamps advance 1/30 s a frame however fast the frames are encoded. With
`H264Codec`'s arrival times, encoding faster than real time would look to the
rate control like a higher frame rate, and it would skip frames. So the output
is the same on every run, and nothing is skipped.

## BenchmarkDotNet

```
dotnet run -c Release -- --filter *
```

- `EncodeBenchmarks.EncodeFrame`: one frame an operation, at 640x480, 1280x720
  and 1920x1080, with a key frame a second.
- `DecodeBenchmarks.DecodeFrame`: one access unit an operation, round and round
  a clip of an IDR and P frames, to the decoder's I420 picture.
- `DecodeBenchmarks.DecodeFrameToBgr`: the same through
  `H264Codec.DecodeVideoFaster` to BGR, as `H264NetVideoEndPoint` does.

The memory diagnoser is on, so the tables show what each operation allocates.

## The run mode, and profiling

```
dotnet run -c Release -- run encode 1280x720 10
dotnet run -c Release -- run decode 1280x720 10 --c
```

`run` encodes or decodes flat out for the given number of seconds (10 by
default) after a second of warm-up. It reports the frame rate, the bitrate,
any frames the rate control skipped, and what was allocated. `--c` turns the
SIMD kernels off (`WelsCpuFeatureMask = 0`), for comparing them with the C
kernels.

It runs in one process, so a sampling profiler can watch it. Without any tool
installed, the runtime's EventPipe can write a trace:

```powershell
$env:DOTNET_EnableEventPipe = "1"
$env:DOTNET_EventPipeOutputPath = "encode.nettrace"
$env:DOTNET_EventPipeConfig = "Microsoft-DotNETCore-SampleProfiler:0:5,Microsoft-Windows-DotNETRuntime:0x4c14fccbd:5"
dotnet bin/Release/net10.0/H264Benchmarks.dll run encode 1280x720 10
```

Open `encode.nettrace` in PerfView or Visual Studio, or convert it for
speedscope with `dotnet-trace convert`.
