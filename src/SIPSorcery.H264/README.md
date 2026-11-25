# SIPSorcery.H264

A **pure C# H.264 video codec** for the
[SIPSorcery](https://www.nuget.org/packages/SIPSorcery) real-time
communications library, ported from Cisco's
[OpenH264](https://github.com/cisco/openh264). No native binaries, no PInvoke.

## Status

**Work in progress, not yet usable.** The port is being built as a series of
small PRs, each one checked bit-exact against a C-only build of OpenH264 (see
[`thirdparty/openh264-dotnet-port`](https://github.com/sipsorcery-org/sipsorcery/tree/master/thirdparty/openh264-dotnet-port)).

| Path | State |
|---|---|
| **Decoder** (Constrained Baseline) | In progress. Scaffolding, API types, memory, logging, and the shared code (tables, macros, block copy, picture padding, 16x16 luma prediction, motion compensation, deblocking edge filters, SAD) are in, each verified bit-exact against the C build. The bitstream reader and NAL, parameter set, slice header and access unit parsing are in too, and match the C build on every conformance vector, clean and damaged. Macroblock decoding is next. |
| **Encoder** (Constrained Baseline, WebRTC realtime) | Not started; follows the decoder. |

The target throughout is the profile WebRTC uses, Constrained Baseline
(`profile-level-id=42e01f`). Other profiles come later, and only if needed.

## Port conventions

- **File for file.** Each `.cs` file ports one OpenH264 source file and keeps its
  name, and the ported code keeps OpenH264's function and type names, so it
  can be read side by side with the C++.
- **Native memory.** Codec state is unmanaged structs in native memory, reached
  through pointers as upstream does, so the GC never moves or scans it.
- **Plain C kernels only.** The C# ports the `_c` reference kernels behind
  OpenH264's function tables. The tables are kept so SIMD versions can be added
  later.

## License

BSD 3-Clause License, see [LICENSE](https://github.com/sipsorcery-org/sipsorcery/blob/master/LICENSE.md).
The ported OpenH264 code is also covered by Cisco's BSD 2-Clause license,
included in the package as `OPENH264_LICENSE`.

H.264 is covered by patents. Cisco pays the royalties only for the OpenH264
binaries that Cisco itself distributes, so this package is **not** covered by
that arrangement.
