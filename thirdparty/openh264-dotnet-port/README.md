# OpenH264 → C# port (SIPSorcery.H264)

Tooling and records for porting [Cisco OpenH264](https://github.com/cisco/openh264)
to managed C# in [`src/SIPSorcery.H264`](../../src/SIPSorcery.H264).

- **Pinned upstream:** `78783f19` (2026-11-20), `v2.7.0-1`, BSD-2-Clause.
- **Scope:** Constrained Baseline (`profile-level-id=42e01f`, the profile WebRTC
  uses), decoder first, then encoder. Other profiles only if needed.
- **Plan and progress:** [`diary/2026-11-24-Claude-Opus-5.5/plan.md`](diary/2026-11-24-Claude-Opus-5.5/plan.md).
- **Use:** for educational purposes only, and not packaged or published.
  H.264 is covered by patents, and anyone wishing to use the port in a live
  application should check their patent exposure first. See
  [`src/SIPSorcery.H264/README.md`](../../src/SIPSorcery.H264/README.md).

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
refharness/build/Release/h264ref decode-damaged <openh264>/res > reference/decode-damaged.tsv  # full decoder, damaged streams
refharness/build/Release/h264ref hash-damaged <file.264> <0-2>         # one damaged variant's output SHA1
refharness/build/Release/h264ref yuv-damaged <file.264> <0-2> <out.yuv>  # one damaged variant's frames
refharness/build/Release/h264ref trace-damaged <file.264> <0-2>        # one line per DecodeFrame2/FlushFrame call
refharness/build/Release/h264ref encoder-conformance <openh264>/res > reference/encoder-conformance.tsv  # all 16 upstream encoder hashes
refharness/build/Release/h264ref paramsets > reference/paramsets.tsv   # encoder validation and SPS/PPS output
refharness/build/Release/h264ref idr > reference/idr.tsv               # first frame (IDR) at a fixed QP
refharness/build/Release/h264ref idr-dump <name> <out.264>        # one idr row's bytes
refharness/build/Release/h264ref stream > reference/stream.tsv         # P streams at a fixed QP, frame by frame
refharness/build/Release/h264ref stream-dump <name> <out.264>     # one stream row's bytes
refharness/build/Release/h264ref rc > reference/rc.tsv                 # rate controlled streams, frame by frame
refharness/build/Release/h264ref rc-dump <name> <out.264>         # one rc row's bytes
refharness/build/Release/h264ref preprocess > reference/preprocess.tsv # preprocessed streams, frame by frame
refharness/build/Release/h264ref preprocess-dump <name> <out.264> # one preprocess row's bytes
refharness/build/Release/h264ref api > reference/api.tsv               # scripted encoder API calls, op by op
refharness/build/Release/h264ref api-dump <name> <out.264>        # one api row's bytes
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

`decode-damaged` runs the real decoder API, the same `BaseDecoderTest` loop as
`conformance` (error concealment `ERROR_CON_SLICE_COPY`), over the same damaged
copies, so it reaches what no clean vector does: concealment, the
`DECODING_STATE` error returns and `ResetDecoder` (eight variants run the DPB
out of pictures, three of them on committed vectors). Each row has the chunk,
frame and failed-call counts, the first failing state, the OR of all of them
and the output SHA1. When a variant differs, `yuv-damaged` writes its frames and
`trace-damaged` prints one line per `DecodeFrame2`/`FlushFrame` call (input
length, NAL type, state, buffer status, frame SHA1); with
`H264REF_TRACE_LEVEL=8` the decoder's own log lines go in between. The C#
tests write the same files on a mismatch when `H264_DUMP_DIR` is set, so the
two can be diffed directly.

`encoder-conformance` encodes every row of upstream's `test/api/encoder_test.cpp`
table (`kFileParamArray`) the way `BaseEncoderTest` does and compares the
SHA1s, which shows the C-only build is the encoder oracle too (`src/encoder.cpp`).

`paramsets` runs a table of encoder configurations (`kParamConfigs` in
`src/encoder.cpp`): WebRTC's settings, the five SPS/PPS id strategies, levels,
profiles, VUI, reference counts, cropping, rate control modes, SVC and
simulcast, and settings init rejects. Each configuration's `key=value` settings
go on top of `GetDefaultParams` (`api=ext`) or a zeroed `SEncParamBase`
(`api=base`). Then a script runs: `P` is `EncodeParameterSets`, `I` is
`ForceIntraFrame` then `EncodeFrame`, and `F` is `EncodeFrame`. The harness
prints the init result, the parameters `GetOption
(ENCODER_OPTION_SVC_ENCODE_PARAM_EXT)` reports after init, and, for each
operation, the frame type and every non-VCL layer's NAL bytes.
`test/SIPSorcery.H264.TestVectors/ParameterSetTests.cs` has the same settings
table and output format, and the two must stay in step.

`idr` encodes the first frame, an IDR, of a table of configurations
(`kIdrConfigs` in `src/encoder.cpp`): one spatial layer with rate control
off, over synthetic pictures (flat, gradient, noise and a scene of mixed
regions, from a hash, so no files are needed). The settings work as in
`paramsets`. It prints the `InitializeExt` and `EncodeFrame` results, the
frame type, each layer's NAL lengths and short SHA1s, and the whole frame's
SHA1 and size. `idr-dump` writes one row's frame to a file.
`test/SIPSorcery.H264.TestVectors/IdrTests.cs` has the same content
generator and output format, and writes its own bytes on a mismatch when
`H264_DUMP_DIR` is set.

`stream` encodes several frames of each of a table of configurations
(`kStreamConfigs`): one spatial layer of camera content with rate control,
background detection, adaptive quantisation and scene change detection off.
The content moves (`StreamSample`: still, a pan, a moving block, moving and
fresh noise, a fade, a cut, and smooth waves moving by a fraction of a pixel),
and the settings cover the complexity modes, QP 0 to 51, IDR periods,
temporal layers, LTR marking, reference counts, the slice modes, odd and
cropped sizes, the deblocking modes, three id strategies and the Main
profile. For each frame it prints the `EncodeFrame` result, the frame type,
the temporal ids, each layer's NAL lengths and short SHA1s and the frame's
SHA1 and size, then the whole stream's SHA1 and size. `stream-dump` writes
one row's stream to a file. `test/SIPSorcery.H264.TestVectors/StreamTests.cs`
has the same content generator and output format.

`rc` prints the same table for rate controlled streams (`kRcConfigs`), still
with the analysis features off: the quality, bit rate, buffer based and
timestamp modes over QCIF and other sizes, at bit rates from 16 kbps to
2 Mbps. The settings cover frame skipping on and off, the max bit rate
windows, the IDR bit ratio, the overshoot fix, the QP limits, temporal layers,
slices, padding, frame rates and LTR. A `mixed` content alternates panning,
fresh noise, still and moving-block segments, so the rate control meets easy,
impossible and moderate frames in turn. The `ts.*` settings are not encoder
parameters. They set each frame's timestamp (`TimestampSchedule`): the clock
(`ts.num`/`ts.den` ms a frame, 1000/30 by default), jitter, a pause, a step
back and a timestamp of 0. `rc-dump` writes one row's stream.
`test/SIPSorcery.H264.TestVectors/RcTests.cs` reads the table through
`StreamTests.cs`'s shared check.

`preprocess` prints the same table again with the preprocessing on
(`kPreprocessConfigs`): scene change detection, background detection, the
denoiser and downsampling, each on its own and together, at fixed QP and in
every rate control mode. Three more contents come in: `camera` (a moving block
over a background with fresh noise of up to 2 either way, so background
detection has something to find), `cuts` (a cut every 6 frames) and `partcut`
(a medium change over the top rows for four frames). The settings cover
temporal layers, intra periods, LTR, several slices, prefix NAL units
(`bPrefixNalAddingCtrl`) and the base API, and sources larger than the coded
size: half, quarter and general ratios, and odd sizes either side.
Adaptive quantisation is in the settings of some rows, but upstream's
`ParamValidation` turns it off whatever they say, so only the `aq` kernel rows
reach it. `preprocess-dump` writes one row's stream.
`test/SIPSorcery.H264.TestVectors/PreprocessTests.cs` reads the table through
the shared check, which compares reconstructions at the coded size.

`api` runs a script of encoder API calls for each of a table of
configurations (`kApiConfigs`), with the settings and contents of the tables
above. The script's operations are space separated, and `op*n` repeats one:
`F` is `EncodeFrame` of the next frame (`F:fmt=n` in another colour format,
`F:WxH` at another size), `I` and `i` are `ForceIntraFrame(true)` and
`(false)`, `P` is `EncodeParameterSets`, `U` is `Uninitialize`, `R` is the
configuration's `Initialize` or `InitializeExt` again, and `S:key=value` and
`G:key` are `SetOption` and `GetOption` (`ApiSet` and `ApiGet` list the keys).
The configurations cover forced IDRs in every mode, and every option the
camera encoder takes changed mid-stream: the IDR interval, the bit rates, the
frame rate, the rate control mode, frame skip, padding, LTR with marking
feedback and recovery requests, the id strategies, profile and level, the
reference count, the complexity, the statistics, base and extended parameter
changes, bad arguments and an allocation failure. For each operation it
prints the return code and what came out: for `F` and `P` the frame type,
the temporal ids, each layer's NAL lengths and short SHA1s and the output's
SHA1 and size, and for `G` the value. Then it prints the whole stream's SHA1
and size. `api-dump` writes one row's output. The test is
`test/SIPSorcery.H264.TestVectors/ApiTests.cs`, whose script runner mirrors
the C++ one, so the two must stay in step.

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
  The encoder steps add theirs: the kernels (E2, `src/kernels_encoder.cpp`),
  the CAVLC writer (E3, `src/cavlc_encoder.cpp`), and the I-slice (E4a) and
  P-slice (E5a) coding of whole frames on hand-built contexts
  (`src/islice_encoder.cpp` and `src/pslice_encoder.cpp`, which share the
  context in `src/islice_ctx.h`): 6743 rows. E5b added the video processing
  VAA statistics (`src/kernels_processing.cpp`, 177 `vaacalc` rows): 6920.
  E6 added the camera half of the complexity analysis (361 `complexity`
  rows, same file): 7281. E7 added the rest of the camera preprocessing, in
  the same file: scene change detection (40 `scd` rows), background detection
  (30 `bgd`), adaptive quantisation (93 `aq`), the denoiser (65 `denoise`),
  downsampling (40 `downsample`) and `CVpFrameWork`'s dispatch and argument
  checks (95 `vpframework`): 7644.
- **`headers.tsv`** and **`headers-damaged.tsv`** are the outputs of `h264ref
  headers` and `headers-damaged`: the size and SHA1 of each vector's parser
  dump, clean (51 rows) and damaged (153 rows). Read by
  `test/SIPSorcery.H264.TestVectors` (`HeaderDumpTests`). The 16 vectors that
  aren't committed are checked when `OPENH264_RES` points at openh264's `res/`.
- **`mbs.tsv`** and **`mbs-damaged.tsv`** are the outputs of `h264ref mbs` and
  `mbs-damaged`: slices parsed, macroblock records, and the size and SHA1 of
  each vector's macroblock, reference and reconstruction dump. Read by
  `MbDumpTests`, the same way.
- **`decode-damaged.tsv`** is the output of `h264ref decode-damaged`: the
  decoder API over the 153 damaged variants. Read by `DecodeDamagedTests`: the
  committed vectors must match in every column, the others (CABAC, B slices,
  High profile, none of them ported) only have to decode without crashing.
- **`encoder-conformance.tsv`** is the output of `h264ref encoder-conformance`:
  **16/16 upstream encoder hashes reproduced**. From E7 on, the port must
  reproduce its rows too (`UpstreamEncoderHashTests`): the three for camera
  content, a single slice, one layer and CAVLC, and the same with the
  denoiser, all four passing. They need upstream's YUV files, which are not
  committed, so they run when `OPENH264_RES` points at openh264's `res/`.
- **`paramsets.tsv`** is the output of `h264ref paramsets`: 66 configurations
  with their init results, validated parameters and parameter set bytes. Read
  by `ParameterSetTests` (E1). All 66 match.
- **`idr.tsv`** is the output of `h264ref idr`: 47 configurations with their
  first frame. Read by `IdrTests` (E4b), which also decodes each frame with
  the ported decoder and compares it with the encoder's reconstruction. All
  47 match.
- **`stream.tsv`** is the output of `h264ref stream`: 57 configurations, 506
  frames (429 P, 77 IDR). Read by `StreamTests` (E5b), which also decodes
  each stream with the ported decoder and compares its pictures with the
  encoder's reconstructions wherever the encoder deblocked them (the
  reference pictures, or every picture with the filter off). All 57 match.
- **`rc.tsv`** is the output of `h264ref rc`: 94 rate controlled
  configurations, 4848 frames (2808 P, 168 IDR, 1871 skipped, 93 with
  padding). Read by `RcTests` (E6), with the same decoder check. All 94
  match.
- **`preprocess.tsv`** is the output of `h264ref preprocess`: 63 preprocessed
  configurations, 1469 frames (900 P, 77 IDR, 14 of them scene change or
  intra period IDRs mid stream, 465 skipped, and 27 uncoded after a frame
  buffer overflow uninitialises the encoder, in two rows). Read by
  `PreprocessTests` (E7),
  with the same decoder check. All 63 match, and no two write the same stream.
- **`api.tsv`** is the output of `h264ref api`: 61 scripted configurations
  and 994 operations. Read by `ApiTests` (E8), which compares every operation,
  then the stream, then decodes the stream with the ported decoder and checks
  that the release frees everything. All 61 match. From E8 on, every table
  above runs through the ported API (`EncoderDriver`) too.
