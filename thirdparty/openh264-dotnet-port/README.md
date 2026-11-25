# OpenH264 → C# port (SIPSorcery.H264)

Tooling and records for porting [Cisco OpenH264](https://github.com/cisco/openh264)
to managed C# in [`src/SIPSorcery.H264`](../../src/SIPSorcery.H264).

- **Pinned upstream:** `78783f19` (2026-11-20), `v2.7.0-1`, BSD-2-Clause.
- **Scope:** Constrained Baseline (`profile-level-id=42e01f`, the profile WebRTC
  uses), decoder first, then encoder. Other profiles only if needed.
- **Plan and progress:** [`diary/2026-11-24-Claude-Opus-5.5/plan.md`](diary/2026-11-24-Claude-Opus-5.5/plan.md).

## Reference harness (`refharness/`)

A CMake build of the pinned openh264 checkout with **no assembly and no SIMD
defines**, so only the portable `*_c` kernels are compiled in. That is exactly
the code being ported, which makes this build the bit-exact oracle. It also
builds `h264ref`, a small tool around it.

Build it (Windows, VS 2022 C++ tools and CMake ≥ 3.20). `OPENH264_DIR` defaults
to an `openh264` checkout next to the `sipsorcery` one:

```
cmake -S refharness -B refharness/build -G "Visual Studio 17 2022" -A x64 [-DOPENH264_DIR=C:/dev/openh264]
cmake --build refharness/build --config Release
```

Then run it:

```
refharness/build/Release/h264ref conformance <openh264>/res   # all 51 upstream decoder vectors
refharness/build/Release/h264ref hash <file.264>               # SHA1 as upstream's test hashes it
refharness/build/Release/h264ref info <file.264>               # coding tools the stream uses
refharness/build/Release/h264ref yuv <file.264> <out.yuv>      # decoded frames, cropped I420
refharness/build/Release/h264ref kernels > reference/kernels.tsv  # kernel and table fixtures
refharness/build/Release/h264ref headers <openh264>/res > reference/headers.tsv                  # parser dumps
refharness/build/Release/h264ref headers-damaged <openh264>/res > reference/headers-damaged.tsv  # same, damaged streams
refharness/build/Release/h264ref headerdump <file.264>                 # one parser dump, in full
refharness/build/Release/h264ref headerdump-damaged <file.264> <0-2>   # one damaged variant's dump
refharness/build/Release/h264ref mbs <openh264>/res > reference/mbs.tsv                  # macroblock parse dumps
refharness/build/Release/h264ref mbs-damaged <openh264>/res > reference/mbs-damaged.tsv  # same, damaged streams
refharness/build/Release/h264ref mbdump <file.264>                     # one macroblock dump, in full
refharness/build/Release/h264ref mbdump-damaged <file.264> <0-2>       # one damaged variant's dump
```

`conformance` copies upstream's `test/api/BaseDecoderTest.cpp` decode loop and
the `decoder_test.cpp` hash table. It also classifies each vector with an
independent SPS/PPS/slice-header parser (`src/stream_info.cpp`): CAVLC versus
CABAC, FMO, ASO, redundant slices, weighted prediction, interlace, 8x8
transform, scaling matrices, B/SP/SI slices and SVC NALs. A vector counts as
Constrained Baseline when it uses none of these.

`kernels` writes one row per fixture case: group, kernel, case, SHA1. The inputs
are not stored. The C# tests regenerate them from the case string with the same
xorshift32 generator and buffer layouts (`src/fixture.h` and
`test/SIPSorcery.H264.UnitTest/KernelFixtures.cs` must stay in step), run the
ported kernel and compare the SHA1 of its whole output buffer, guard bytes
included. Each port step adds its kernels to `src/kernels_*.cpp`; regenerate the
file after changing them.

`headers` drives the decoder's NAL and access-unit parsers the way `WelsDecodeBs`
does, without reconstruction (`src/headers.cpp`), and dumps every NAL header,
slice header, parameter set change and access unit as text. Only each dump's
size and SHA1 go in the table. `test/SIPSorcery.H264.TestVectors/HeaderDump.cs`
is the same driver over the port and must stay in step with it. The clean
vectors never reach an error path, so `headers-damaged` repeats it on three
damaged copies of each vector (dropped, truncated and bit-flipped NALs, from
`fixture.h`'s generator). `headerdump` prints one dump in full, for diffing
against the C# dump the test writes on a mismatch.

`mbs` is the same driver, also decoding each access unit as
`DecodeCurrentAccessUnit` does (single-threaded, error concealment on but not
run): the picture comes from the DPB, each slice builds its reference list
with `InitRefPicList`, is parsed by `WelsDecodeSlice`, then reconstructed and
deblocked by `WelsTargetSliceConstruction`, and a reference picture is then
marked (`WelsMarkAsRef`) and padded. For each parsed slice it adds a line
with the frame_num gap check, the reference list, the parser's end state, the
SHA1 of a binary record of every macroblock the slice reached (type, QPs, CBP,
non-zero counts, coefficients, intra modes, sub-MB types, motion vectors,
reference indices, flags), the reconstruction's return code, macroblock count
and sample SHA1, and the completeness flag; for each access unit, the picture
and its SHA1, and for a reference picture the short- and long-term lists after
marking. CABAC and B slices are skipped (not ported yet), and so is the rest
of a stream once `PrefetchPic` finds no free picture (upstream resets the whole
decoder there, in the API layer). On the clean Constrained Baseline vectors
the pictures equal the real decoder's output frames (all but the two cropped
vectors, whose output can't be compared with the uncropped picture hash).
`mbs-damaged` and
`mbdump` work as their `headers` counterparts.

## Reference results (`reference/`)

- **`decoder-conformance.tsv`** is the output of `h264ref conformance` on the
  C-only build: **51/51 upstream hashes reproduced**, 35 vectors Constrained
  Baseline. `test/SIPSorcery.H264.TestVectors` reads it directly as its test
  table, and commits those 35 vectors (8.5 MB).
- **`kernels.tsv`** is the output of `h264ref kernels`: the fixtures for the
  ported common code, read by `test/SIPSorcery.H264.UnitTest`. As of C3 it
  covers the 16 `common_tables` tables, 9 `macros.h` helpers, the 7 `copy_mb`
  kernels, the `expand_pic` padding functions, the two `intra_pred_common`
  predictors, the `mc.cpp` C kernels, the `deblocking_common` edge kernels and
  the `sad_common` kernels (1250 rows: 675 motion compensation, 382
  deblocking, 56 SAD). D1 added the 15 decoder tables (`dectable` rows,
  1265 in all), and D2 the other 68 (the CAVLC VLC, scan and CBP tables; 1333
  rows). D3 added the decoder's `get_intra_predictor` (504 rows) and
  `decode_mb_aux` (132) kernels: 1969 rows.
- **`headers.tsv`** and **`headers-damaged.tsv`** are the outputs of `h264ref
  headers` and `headers-damaged`: the size and SHA1 of each vector's parser
  dump, clean (51 rows) and damaged (153 rows). Read by
  `test/SIPSorcery.H264.TestVectors` (`HeaderDumpTests`). The 16 vectors that
  aren't committed are checked when `OPENH264_RES` points at openh264's `res/`.
- **`mbs.tsv`** and **`mbs-damaged.tsv`** are the outputs of `h264ref mbs` and
  `mbs-damaged`: slices parsed, macroblock records, and the size and SHA1 of
  each vector's macroblock, reference and reconstruction dump. Read by
  `MbDumpTests`, the same way.
