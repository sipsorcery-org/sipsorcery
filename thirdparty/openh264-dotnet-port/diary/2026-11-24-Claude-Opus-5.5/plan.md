# OpenH264 → C# port plan (SIPSorcery.H264)

Model: `claude-opus-5-5` (Anthropic), commissioned by Aaron Clauson. 24 Nov 2026.

This is the planning entry for the port. Keep it current as PRs land, the way
`thirdparty/libvpx-dotnet-port/diary/2026-04-26-Claude-Opus-4.7/result.md`
was kept for the VP8 encoder.

## Progress

| Step | State | Notes |
|---|---|---|
| P0.1 Reference harness | **Done** (24 Nov) | `refharness/`: a C-only MSVC + CMake build reproduces **all 51** upstream decoder hashes. 35 vectors use only Constrained Baseline tools, which defines the decoder's acceptance set. Results are in `reference/decoder-conformance.tsv`. |
| P0.2 Skeleton | **Done** (24 Nov) | `src/SIPSorcery.H264` (api types, `CMemoryAlign`, `WelsLog`/`welsCodecTrace`, port helpers), `test/SIPSorcery.H264.UnitTest` (22 tests) and `test/SIPSorcery.H264.TestVectors` (the 35 committed vectors, with a decode test skipped until D6). |
| C1 Common tables, macros, copy, padding | **Done** (24 Nov) | `common_tables`, `macros.h`, `ls_defines.h`, `wels_const_common.h`, `wels_common_defs.h`, `copy_mb`, `expand_pic`. New `h264ref kernels` fixture mode (`reference/kernels.tsv`, 129 rows); all match. `utils` was already done in P0.2. UnitTest: 155 tests. |
| C2 Intra prediction (common), motion compensation | **Done** (24 Nov) | `intra_pred_common` (the two 16x16 luma predictors) and the C paths of `mc.cpp`: every luma quarter-pel and chroma eighth-pel position at each block size, the three half-pel filters at sizes 4 to 17, and sample averaging, all reached through `InitMcFunc`. `kernels.tsv` now has 812 rows (683 new); all match. UnitTest: 839 tests. |
| C3 Deblocking kernels, SAD | **Done** (25 Nov) | `deblocking_common` (the 12 luma, chroma and interleaved-chroma edge kernels and `WelsNonZeroCount_c`) and `sad_common` (7 SAD sizes and 7 "four neighbours" variants). `kernels.tsv` now has 1250 rows (438 new); all match. UnitTest: 1277 tests. This completes phase 1, except `crt_util_safe_x`, whose only decoder caller is `welsDecoderExt`, so it comes with D6. |
| D1 Bitstream reader, NAL/AU parsing | **Done** (25 Nov) | `bit_stream`, `dec_golomb`/`golomb_common`, `memmgr_nal_unit`, `au_parser` (start codes, NAL headers, SPS/subset SPS/PPS, VUI, scaling lists, SEI skip, prefix NAL) and the parsing half of `decoder_core` (slice header, reordering, weight table, ref pic marking, AU boundaries and AU start/end), plus the decoder's type graph (`decoder_context.h` and the headers it pulls in). ~7.9k lines. New `h264ref headers` and `headers-damaged` modes: a WelsDecodeBs-shaped driver dumps every NAL, slice header, parameter set change and AU on both sides. All 51 vectors match, clean and in 3 damaged forms each (`reference/headers.tsv`, `headers-damaged.tsv`), and the 15 decoder tables match (`dectable` rows in `kernels.tsv`). UnitTest: 1293. TestVectors: 123 + 1 skipped. |
| D2 CAVLC macroblock syntax | **Done** (25 Nov) | `parse_mb_syn_cavlc` (CAVLC paths), the rest of `decoder_data_tables` and `fmo`, `InitVlcTable`, `pic_queue`, the P-slice half of `mv_pred`, the DQ layer setup in `decoder_core`, and the parse half of `decode_slice` (`WelsDecodeSlice`, the I/P macroblock parsers, intra mode parsing, the two DC transforms the residual parser calls). ~5.2k lines. `decode_mb_aux` moved to D3: nothing in the parse calls it. New `h264ref mbs` and `mbs-damaged` modes: the D1 driver also parses each AU's slices and hashes every macroblock's parse results. All 51 vectors match, clean and in 3 damaged forms each (`reference/mbs.tsv`, `mbs-damaged.tsv`), and the 68 new decoder tables match (83 `dectable` rows). UnitTest: 1361. TestVectors: 211 + 1 skipped. |
| D3 I-slice reconstruction | **Done** (25 Nov) | `get_intra_predictor` (all 42 C predictors), `decode_mb_aux` (the 4x4 and 8x8 residual IDCTs, the block offset table), `InitPredFunc`, the intra half of `rec_mb`, and `WelsMbIntraPredictionConstruction`/`WelsTargetMbConstruction` in `decode_slice`. ~2k lines. `mbs` mode now also reconstructs every intra macroblock of each parsed slice (no deblocking) and hashes its samples: 167.8k intra MBs over the clean vectors, all matching, clean and damaged. `kernels.tsv`: 1969 rows (636 new). UnitTest: 1999. TestVectors: 211 + 1 skipped. |
| D4 P-slice reconstruction | **Done** (25 Nov) | `manage_dec_ref` (list init and reordering, sliding window, MMCO, long-term references), the inter half of `rec_mb` (`GetRefPic`, `BaseMC`, `WeightPrediction`, `GetInterPred`), `WelsMbInterPrediction`/`WelsMbInterConstruction` and `WelsBlockFuncInit` in `decode_slice`, the DPB memory in `decoder` (`CreatePicBuff`/`IncreasePicBuff`/`DecreasePicBuff`, `WelsRequestMem`, `SyncPictureResolutionExt`, `WelsFreeDynamicMemory`), and `AllocPicBuffOnNewSeqBegin`, `InitConstructAccessUnit`, `InitRefPicList` and `CheckRefPicturesComplete` in `decoder_core`. B-slice and CABAC reference paths fail loudly. ~2.3k lines. `mbs` mode drops its stand-ins for the real reference flow (DPB, `InitRefPicList`, `WelsMarkAsRef`, padding) and reconstructs every macroblock: 1.41M P-slice MBs over the clean vectors, 4,501 pictures marked, all matching, clean and damaged. On the 7 vectors with deblocking off, the C harness's pictures equal the real decoder's output, all 191 frames. UnitTest: 1999. TestVectors: 211 + 1 skipped. |
| D5 Deblocking | **Done** (25 Nov) | `deblocking` (boundary strengths for P slices with 4x4 and 8x8 transforms, the alpha/beta/tc0 tables, the edge filters, the intra and inter macroblock filters, the slice loop, the per-MB entry points, `DeblockingInit`) and `WelsTargetSliceConstruction` in `decode_slice`. B-slice strengths throw; they can't be reached. ~1.35k lines. `mbs` mode now calls the real `WelsTargetSliceConstruction`, so every slice is deblocked: 5,196 slices and 1.43M macroblocks over the clean vectors (2.06M with the damaged ones, which also reach filter mode 2), all matching, clean and damaged. On the 33 clean Constrained Baseline vectors without cropping, every picture equals the real decoder's output frame, 4,196 of 4,196. UnitTest: 1999. TestVectors: 211 + 1 skipped. |
| D6 Decoder API | **Done** (25 Nov) | The rest of `decoder_core` (`ConstructAccessUnit`, `DecodeCurrentAccessUnit`, `WelsDecodeConstructSlice`, `DecodeFrameConstruction`), of `decoder` (`WelsDecoderDefaults`, `WelsOpenDecoder`/`WelsCloseDecoder`, `DecoderConfigParam`, `WelsInitDecoder`/`WelsEndDecoder`, `InitDecFuncs`, `WelsDecodeBs`, the statistics updates), `error_concealment` (frame copy, slice copy, slice MV copy), `welsDecoderExt` (`CWelsDecoder`, `WelsCreateDecoder`/`WelsDestroyDecoder`, `WelsGetDecoderCapability`) and `measure_time` (`WelsTime`). Single threaded; parse-only mode is rejected. ~3.2k lines. The upstream-hash decode test is on: **all 35 Constrained Baseline vectors decode to upstream's SHA1 and frame count** (4,256 frames, no failed call). New `h264ref decode-damaged` mode (`reference/decode-damaged.tsv`): the decoder API over the 153 damaged variants. The 105 Constrained Baseline ones match in every column but one, which is the B-slice exception noted below. That includes the 3 that run the DPB out and recover through `ResetDecoder`. The other 16 vectors decode clean and damaged without crashing. UnitTest: 1999. TestVectors: 299. |
| D7 SIPSorcery wire-up | **Done** (25 Nov) | `H264Codec : IVideoEncoder` (decode only: `DecodeVideo` to I420, BGR or RGB, `DecodeVideoFaster` into a re-used buffer, `KeyFrameRequired`; the encode methods throw `NotSupportedException`), `H264NetVideoEndPoint : IVideoSink` (BGR pictures, `OnKeyFrameRequired` throttled to once a second) and `YuvToRgb` (BT.601/BT.709, limited/full range, from the SPS VUI). One access unit per call through `DecodeFrameNoDelay`. Two small non-upstream members on `CWelsDecoder`: `CurrentSps` and `ReleaseHeldPicture`. New example `examples/WebRTCExamples/WebRTCClientH264Net`. **All 35 Constrained Baseline vectors give upstream's SHA1 when fed one access unit at a time, and again after an RTP round trip through `H264Depacketiser`** (STAP-A, FU-A, reordering, sequence wrap). Checked live against Chromium's H.264 encoder: 640x480 at 30 fps decodes clean, and a dropped access unit leads to a PLI, an IDR from the browser, and clean decoding again one frame later. UnitTest: 2006. TestVectors: 412. |
| E1 Encoder bit writer, NAL encapsulation, parameter sets | **Done** (25 Nov) | `svc_enc_golomb`, `nal_encap`, `parameter_sets`, `param_svc` (`SWelsSvcCodingParam`, defaults, transcoding, temporal settings), `au_set`, `paraset_strategy` (all five id strategies), `wels_const`, `wels_common_basis`, `rc.h` and `svc_enc_slice_segment.h` constants and checks, part of `encoder_data_tables`, and from `encoder_ext` the parameter validation, slice and thread settings and the SPS/PPS writers. Partial `sWelsEncCtx` and `SWelsFuncPtrList`. ~4.6k lines. The refharness builds the encoder too: new `h264ref encoder-conformance` mode reproduces **all 16** upstream encoder hashes (`reference/encoder-conformance.tsv`). New `h264ref paramsets` mode (`reference/paramsets.tsv`): 66 configurations covering WebRTC's settings, the id strategies, levels, profiles, VUI, references, cropping, rate control, SVC and simulcast, and 10 rejects. **All 66 match**: init result, validated parameters, and every SPS, subset SPS and PPS byte (272 operations with parameter sets). Slice headers moved to E4. UnitTest: 2009. TestVectors: 480. |
| E2 Encoder kernels | **Done** (26 Nov) | `encode_mb_aux` (the quant tables and the quantisers, forward DCT, 4x4 and 2x2 Hadamard, zigzag scans, single-coefficient cost, non-zero count), the encoder's `decode_mb_aux` (dequantisation, inverse Hadamard, IDCT reconstruction, block offsets), `sample` (7 SATD sizes and the 5 combined predict-and-cost kernels), the encoder's `get_intra_predictor` (14 + 7 + 5 packed predictors), the rest of `encoder_data_tables` (cache scans, QP costs, mode maps, the CAVLC VLC tables), the four `WelsInit*Funcs` and the matching `SWelsFuncPtrList` slots. ~2.4k lines. The SSD in the plan's list is not in `sample.cpp`. The only SSD is the VAA's `pSsd16x16` from `processing`, which comes with E7. New kernel groups (`refharness/src/kernels_encoder.cpp`, its own object library): 13 `enctable`, 642 `encode_mb_aux`, 404 `enc_decode_mb_aux`, 156 `enc_get_intra_predictor`, 766 `sample`, 49 `enc_init` rows, **all match**. Planted bugs (a table byte, a loop bound, a dequant shift, boundaries, ties, clips, an int16 wrap) each fail their rows, after a second round of rows (see the notes). UnitTest: 4055. TestVectors: 480. |

Conventions settled in P0.2 (they refine section 4):

- **Free functions:** upstream's C++ namespaces become static partial classes
  (`WelsCommon`, later `WelsDec`/`WelsEnc`), one partial per ported file.
  `global using static` makes calls bare, as in C. Types sit at namespace level
  in `SIPSorcery.H264`. The `typedefs.h` names (`int32_t`, ...) are global
  aliases.
- **Native memory:** all codec state is unmanaged structs in native memory.
  Primitive arrays in structs are `fixed` buffers. Arrays of structs are inline
  arrays (`InlineArrayN<T>`). Arrays of pointers are `PtrArrayN<T>` of `Ptr<T>`.
  Pointers to C++ objects that other structs embed or copy (`welsCodecTrace`,
  `CMemoryAlign`) are native structs too, not classes.
- **Logging:** `WelsLog` takes a C# interpolated string through a handler that
  skips formatting when the level is filtered out. `SLogContext.pfLog` gets the
  finished message, and the port adds `SLogContext.pfIsEnabled` for the level
  check.
- **Packaging:** `IsPackable` is false until the patent question in section 8
  is settled.

Conventions added in C1:

- **Const tables** are native copies made once by `StaticTable.Of(count, [...])`
  (declared dimension first; missing initializers are zero-filled, as in C)
  and exposed as pointers. Multi-dimensional tables are pointers to nested
  inline arrays, so `g_kuiDequantCoeff[iQp][i]` and the row pointer
  `(uint16_t*)&g_kuiDequantCoeff[iQp]` read as in C.
- **Function pointers.** A function-pointer typedef is a `global using unsafe`
  alias in the file that ports its header (`PExpandPictureFunc`). Function
  pointers can't be generic arguments (no inline arrays, tuples or spans of
  them), so an array of them in a struct is `InlineArrayN<nint>` storage
  behind a property returning the alias pointer type. Call sites keep C's
  `p->pfExpandChromaPicture[i](...)`.
- **libc** calls go to `CRuntime` (`memcpy`, `memset`, `memmove`, `memcmp`).
- **Scope.** codec/common functions that upstream declares at global scope or
  `extern "C"` also live in `WelsCommon`. C++ reference parameters that are
  never written through (`const int32_t&`, `uint8_t*&` in `PadMB*_c`) are by
  value.
- **Macros.** Selection macros (`WELS_MAX`/`MIN`/`CLIP3`) are generic, so a
  signed/unsigned mix that C would convert to unsigned fails to compile
  rather than compare differently. Value macros (`WELS_ABS`) have overloads
  that follow C's integer promotion. `IS_*` type tests return bool.
- **Kernel fixtures.** `h264ref kernels` prints one row per case (group,
  kernel, case, SHA1). Inputs are regenerated from the case string on both
  sides with the same xorshift32 and buffer layouts (`refharness/src/fixture.h`
  and `test/SIPSorcery.H264.UnitTest/KernelFixtures.cs`), and the SHA1 covers
  the kernel's whole output buffer, guard bytes included. A negative control
  (one padding loop shortened by a row) failed exactly the 8 affected cases.
- **Line endings** are LF, like the rest of the repo.

Notes from C2:

- **Integer semantics kept.** `FilterInput8bitWithStride_c` sums in `uint32_t`
  and casts to `int32_t`, as upstream does (the wrap is what makes the negative
  5 tap work). `McHorVer22_c` keeps its `int16_t` intermediate row. A wider
  type would give the same answer for 8-bit input, but the port keeps C's
  types so the arithmetic is the same arithmetic.
- **Stack temporaries** are `stackalloc` (`uint8_t[256]`, `int16_t[22]`). C#
  zero-fills them and C doesn't, which is harmless: every element is written
  before it is read.
- **File-local function tables** (`pWelsMcFunc[16]`) are managed arrays of the
  alias pointer type (`static readonly PWelsMcWidthHeightFunc[]`), which C#
  allows where an inline array would not.
- **Negative control.** Making the 20 tap of `HorFilterInput16bit_c` 21 failed
  exactly the 71 cases predicted: the 36 `McHorVer22_c` rows and the 5
  quarter-pel positions that use the centre sample (22, 21, 23, 12, 32) at
  each of the 7 block sizes.

Notes from C3:

- **Smooth fixture planes.** Random bytes almost never pass the loop filter's
  |p0 - q0| < alpha, |p1 - p0| < beta gates, so the deblocking inputs are
  base plus bounded noise, swept over alpha/beta {17/4, 63/12, 255/18}, seven
  base/spread pairs and two tc0 sets (one with the -1 that skips an edge). The
  filters only clip when a sample sits within a few levels of 0 or 255, so two
  pairs put half the samples at exactly 0 or 255. Without them, removing
  `WelsClip1` from luma bS < 4 failed only 4 cases, all in one edge direction;
  with them it fails 16, in both. 11 cases are no-ops (no line passes the
  gates), which still checks that nothing is written.
- **Negative controls**, one at a time, each failing only the kernels it
  touches: luma bS < 4 p1 update, 52 cases; luma p0 clip removed, 16; chroma
  Cb q0 clip removed, 12; luma bS == 4 strong p2 rounding, 18; chroma bS == 4
  Cr p0 rounding, 26; SAD 4x4 one row short, exactly the 24 cases of the six
  4x4-based kernels.
- **Upstream file-local helpers.** `DeblockLumaLt4_c` and the five other
  strided helpers are not in the header and only called from their own file,
  so they are private, as with `mc.cpp`'s static functions. The
  `SDeblockingFunc` table that selects the kernels is decoder state
  (`decoder_context.h`) and comes with the decoder.
- **Assignments to `uint8_t`** that C narrows implicitly get an explicit
  `(uint8_t)` cast. Every one is already in range, so the cast never changes
  a value. It is there because C# requires it.

Notes from D1:

- **Decoder namespace.** Decoder types live in `SIPSorcery.H264.Decoder` and
  its free functions in `WelsDec` (a static partial class, one partial per
  file), mirroring upstream's `WelsDec` namespace. Files that grow with later
  steps (`decoder_core`, `decoder`, `fmo`, `decoder_data_tables`) carry only
  what D1 needs and say so in their header.
- **Macro conventions.** `WELS_READ_VERIFY` expands to
  `if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp;`.
  The `WELS_CHECK_SE_*_ERROR` macros are bool helpers wrapped in
  `if (...) return GENERATE_ERROR_NO(...)`; the NOLOG variants take no
  context. `DECODING_STATE` is an enum, so `iErrorCode |= (int32_t)dsX`.
  Enum comparisons and shift counts get explicit casts, and int/uint mixes
  are cast so they don't widen to `long`.
- **Upstream quirks kept**, because the dumps must match: `ParseVui` stores
  `BsGetUe`'s return code as `cpb_cnt_minus1`; `ParseSps` returns 0 for an
  unsupported profile; `iOffsetForRefFrame` is `int8_t`; the `memcmp` of
  whole `SSps`/`SPps` structs includes padding (always zero, since the
  temporaries are memset). `LONG_TERM_REF` is defined, as in upstream's
  build. Threading is not ported: `GetThreadCount` is treated as 1.
- **Header-dump oracle.** `refharness/src/headers.cpp` and
  `test/SIPSorcery.H264.TestVectors/HeaderDump.cs` are the same driver:
  WelsDecodeBs's loop (start codes, emulation prevention, raw-buffer wrap)
  calling the real `ParseNalHeader`/`ParseNonVclNal`/`DecodeFinishUpdate`,
  with `ConstructAccessUnit` replaced by `WelsDecodeInitAccessUnitStart`, a
  dump of the AU, and `WelsDecodeAccessUnitEnd`. It writes one text line per
  NAL, per parser-state snapshot, per slice header part, per changed
  parameter set slot and per AU (244k lines over the 51 vectors). Only the
  size and SHA1 are committed; on a mismatch the test writes its dump next
  to the test binary to diff against `h264ref headerdump`.
- **Damaged streams.** No clean vector reaches an error path (no non-zero
  error code or return anywhere in the clean dumps), and WebRTC streams lose
  packets. So `headers-damaged` runs each vector three more times with
  seeded damage (per 16 NALs: one dropped, one truncated, three with 1 to 3
  bit flips), the same xorshift32 on both sides. That reaches missing
  parameter sets, bitstream errors, lost references, `ParseNonVclNal`
  failures, the parameter set overwrite slots and the invalid-id counters.
  Both runs of the native build give identical output.
- **Negative controls**, one at a time: SAR width/height swapped, 1 vector
  (SarVui); MMCO `difference_of_pic_nums` + 1, 12; slice alpha offset not
  doubled, 3; AU boundary ignoring `pic_order_cnt_lsb`, 10; SPS-missing
  statistic not counted, 14 (damaged only); PPS invalid count not counted, 1
  (damaged only). One control survived and was expected to:
  `CheckAccessUnitBoundaryExt` is ported here but upstream calls it only from
  `DecodeCurrentAccessUnit`, so it is first exercised in D6.

Notes from D2:

- **Scope moved forward.** The parse needs more than the three files the plan
  listed: `WelsDecodeSlice` and the MB parsers live in `decode_slice` (D6 in
  the plan), P-slice MV prediction in `mv_pred` (D4), the picture's per-MB
  arrays in `pic_queue` (D4), and `FmoNextMb`/`FmoParamUpdate` in `fmo`
  (deferred, but `FmoParamUpdate` runs for every slice). Those parts came
  now. `decode_mb_aux` (IDCT) went the other way, to D3.
- **Not ported yet, and fail loudly.** CABAC: `WelsDecodeSlice` logs and
  returns `ERR_INFO_UNSUPPORTED_CABAC_EL` for a CABAC PPS, and the CABAC
  branches of the intra mode parsers return the same. B slices:
  `WelsDecodeMbCavlcBSlice` logs and returns `ERR_INFO_UNSUPPORTED_BIPRED`.
  The B-slice parts of `mv_pred` are left out with a comment. 8x8 transform
  CAVLC (`WelsResidualBlockCavlc8x8`, `ParseIntra8x8Mode`) is ported, since
  it shares the CAVLC code, and the High-profile CAVLC vectors exercise it.
- **Uninitialised C stack arrays.** The MB parsers' `ENFORCE_STACK_ALIGN`
  caches (`pNonZeroCount[48]`, `pIntraPredMode[48]`) and
  `iMotionVector[2][30][2]`/`iRefIndex[2][30]` are uninitialised in C and
  zero-filled `stackalloc` here. Every entry the parse reads is written
  first. The dumps would show it otherwise, since C's garbage would differ
  from C#'s zeros. Arrays C zero-initialises (`iSampleAvail`, `iRefIdx`)
  are memset explicitly.
- **Macroblock-dump oracle.** `mbs` mode runs the D1 driver and, before
  `WelsDecodeAccessUnitEnd`, does what `DecodeCurrentAccessUnit` does up to
  `WelsDecodeSlice`: DQ layer memory on a new sequence, `InitCurDqLayerData`,
  the per-picture memsets, and per slice the layer info, `FmoParamUpdate`,
  `WelsDqLayerDecodeStart` and `InitDqLayerInfo` (static upstream, so the
  harness carries a verbatim copy). The reference list is a stand-in: entry
  i of list 0 is a complete picture, an incomplete one or missing, from a
  fixed pattern over the AU number, so the missing and incomplete reference
  paths run. Each parsed slice dumps its end state (return code, MB count,
  bit position, QP, skip run, concealment flag, error code) and the SHA1 of
  a 935-byte record per macroblock reached (everything the CAVLC parsers
  write, coefficients included). Each AU dumps the SHA1 of `pDec`, which only
  I_PCM writes during the parse. Header lines stay in, so the parse's side
  effects on the context are checked too. Over the clean vectors that is
  5.5k slices and 1.46M macroblock records, and over the damaged copies 10.2k
  slices and 2.16M records. In the damaged copies sampled, about 1 in 5 parsed
  slices fails with an error code, spread over the parser's error returns.
- **Whole-table fixtures.** The dumps only check table entries a vector
  hits, so the 68 tables new in D2 also get `dectable` rows. All match,
  sizes included.
- **Negative controls.** Two were run together, in code paths no vector
  shares: the I_PCM Cr copy offset by one byte, and every P-skip reference
  treated as complete. 51 of the 88 CB tests failed. CVPCMNL1, the only
  I_PCM vector, failed clean and damaged, and only on its picture-hash lines.
  The other 26 clean and 23 damaged failures were the skip flag; in
  BA_MW_D's diff, only the per-slice MB hashes of AUs with an incomplete
  stand-in reference changed. Then alone: the P-slice "bitstream
  incomplete" bound loosened by one bit passed every clean vector and failed
  7 damaged ones.

Notes from D3:

- **Scope.** `get_intra_predictor` (14 I4x4, 14 I8x8, 7 chroma and 7 I16x16
  predictors), `decode_mb_aux` and `InitPredFunc` from `decoder`, with its
  `IdctFourResAddPred_` helper. From `rec_mb`, the intra half:
  `WelsFillRecNeededMbInfo`, `RecI4x4Mb`/`Luma`/`Chroma`, `RecI8x8Mb`/`Luma`,
  `RecI16x16Mb` and `RecChroma`. The inter half (`sMCRefMember`, `GetRefPic`,
  `BaseMC`, weighted and bi-prediction, `GetInterPred`) comes with D4. In
  `decode_slice`, `WelsMbIntraPredictionConstruction` and
  `WelsTargetMbConstruction`; the latter's inter branch logs and returns
  `ERR_INFO_MB_RECON_FAIL` until D4. `WelsTargetSliceConstruction` waits for
  D5, since most of it is deblocking and the MB-correct bookkeeping around it.
- **Kernel fixtures.** Two new `kernels.tsv` groups, `decode_mb_aux` (132
  rows) and `get_intra_predictor` (504). Each predictor runs over random
  planes at three strides, the 8x8 ones with every top-left and top-right
  availability. The IDCTs run at several strides over coefficient ranges up
  to the full `int16_t`, so the wrapping casts are exercised.
- **A fixture for a skip condition has to make the condition decide.** The
  first `IdctFourResAddPred_` fixture drew each block's coefficients from the
  seed alone, so nearly every block had a non-zero DC and ran whatever its
  `pNzc` flag said; a control that read `pNzc[2]` for `pNzc[4]` passed. The
  fixture now sets each block's coefficients explicitly (`modes`: all zero,
  zero DC only, or random) over all 16 flag combinations, and the same
  control fails exactly the 8 cases it should.
- **Oracle: `mbs` mode extended in place.** After each parsed slice of the
  target layer, the harness runs `ReconSlice`, a copy of
  `WelsTargetSliceConstruction`'s macroblock loop that calls
  `WelsTargetMbConstruction` for intra macroblocks only (I_PCM included,
  which returns at once since the parse already wrote its samples) and skips
  deblocking. The slice line gains the return code, the number of
  macroblocks reconstructed and the SHA1 of each one's 16x16 Y and 8x8 Cb/Cr
  samples. On a failure the driver does what `DecodeCurrentAccessUnit` does
  after a failed construction (reference lost at T0, `dsBitstreamError`,
  picture incomplete) and ends the AU. Inter macroblocks keep whatever the
  picture held, the same on both sides, so intra macroblocks that predict
  from them still compare. The per-AU picture hash now covers intra
  reconstruction as well as I_PCM. The driver also fills
  `iDecBlockOffsetArray` per AU and clears `iTotalNumMbRec` at its end, as
  `DecodeFrameConstruction` does. Over the clean vectors, 167,814 intra
  macroblocks are reconstructed; every reconstruction returns 0, clean and
  in the 10.2k damaged slices. `mbs.tsv` and `mbs-damaged.tsv` were
  regenerated: slice and record counts unchanged, dump sizes and SHA1s new.
- **Negative controls.** Three, each alone, each failing all 35 clean CB
  tests: `RecChroma` adding the chroma residual only for `uiCbpC == 2`
  (dropping the DC-only case), the chroma DC-left and DC-top predictors
  swapped in `InitPredFunc`, and `RecI4x4Luma` taking the 4x4 modes in raster
  rather than scan order. In the first one's diff only the new reconstruction
  fields and the picture hash changed. In the kernels, a rounding change in a
  horizontal-up predictor and the `pNzc` index above were both caught.

Notes from D4:

- **Scope.** All of `manage_dec_ref` except `WelsInitBSliceRefList` and
  `WelsReorderRefList2` (B slices, and the CABAC lists of non-Baseline
  profiles). From `rec_mb`, the P half; `BiWeightPrediction`, `BiPrediction`
  and `GetInterBPred` wait with the B slices. `InitRefPicList` returns
  `ERR_INFO_UNSUPPORTED_BIPRED` for a B slice and
  `ERR_INFO_UNSUPPORTED_CABAC_EL` for a non-Baseline CABAC P slice, with a log
  line; `WelsMbInterConstruction` does the same for B macroblocks. The threaded
  decoder's parts (the reference row waits in `BaseMC`, the thread contexts in
  `WelsFreeDynamicMemory`) are left out. `DecodeCurrentAccessUnit` itself and
  `WelsTargetSliceConstruction` still wait for D6 and D5; the harness carries
  copies of both.
- **Oracle: `mbs` mode on the real reference flow.** The stand-in list and
  the single picture are gone. `AllocPicBuffOnNewSeqBegin` builds the DPB on a
  new sequence (the "Z" line gains its capacity), `PrefetchPic` supplies
  `pDec`, and each parsed slice runs the frame_num gap check and
  `InitRefPicList` (for the top dependency layer, base quality) before
  `WelsDecodeSlice`, as `DecodeCurrentAccessUnit` does. The "D" line gains
  `gap`, `rpl`, `rc` and `refs` (each entry as DPB index and frame_num or
  long-term index, `x` when incomplete), and `arc` after
  `CheckRefPicturesComplete`. `ReconSlice` now reconstructs every macroblock.
  The AU ends as upstream's does for the target layer: the completeness flags,
  `DecodeFrameConstruction`'s complete-IDR rule, then for a reference picture
  the `pRefPic` copy, `WelsMarkAsRef`, `ExpandReferencingPicture` and a new
  "K" line with the lists after marking, then the `iPrevFrameNum` update. The
  driver also picked up two error effects it had skipped: a failed
  `WelsDecodeSlice` now does `HandleReferenceLostL0`, and an `FmoParamUpdate`
  failure sets its error code. Concealment is still not run, so where upstream
  returns early (FMO or reconstruction failure) the AU ends unmarked.
  `mbs.tsv` and `mbs-damaged.tsv` were regenerated: slice and record counts
  unchanged.
- **The oracle checked against the real decoder.** The C-vs-C# comparison only
  shows the port agrees with the harness. On the 7 vectors whose slices all
  disable deblocking (CVPCMNL1_SVA_C, NL1_Sony_D, NLMQ1/2_JVC_C, SVA_CL1_E,
  SVA_NL1_B, SVA_NL2_E), every "Y" hash equals the SHA1 of the matching frame
  from `h264ref yuv`, 191 of 191, so the harness's flow is the decoder's.
- **Coverage.** Clean: 4,614 pictures, 4,501 marked, no marking errors. The 125
  list errors and the incomplete pictures come only from the three
  `cavlc_temporal_direct` vectors (their B references are skipped) and the two
  `BA_MW_D_*_LOST` vectors. Damaged: 534 frame_num gaps, 332 list errors, 11
  duplicate frame_num markings, 7,120 of 7,753 pictures incomplete.
- **An upstream leak, and where the harness stops.** `AddShortTermToList`
  replaces a picture with a duplicate frame_num in the short-term list without
  `SetUnRef`, so the old picture keeps `bUsedAsRef` for good. On damaged
  streams enough of these fill the DPB; `PrefetchPic` then returns NULL and
  leaves `iCurrentIdx == iCapacity`, and its next call reads
  `ppPic[iCapacity]`, past the buffer (the harness crashed on 3 damaged
  variants). Upstream survives because `DecodeFrame2` calls `ResetDecoder` on
  `dsOutOfMemory`. The harness has no API layer, so after the first failure it
  skips the rest of the stream ("skip 6", 682 AUs over the damaged set). The
  port keeps upstream's behaviour as is; D6 has to bring `ResetDecoder` along
  for this path, and its tests should include a stream that hits it.
- **Negative controls.** Three, each alone: `BaseMC` dropping the vertical
  quarter-pel bit (54 of 88 MB tests failed), the second 8x16 partition one
  pixel left (54), and the short-term list's 2nd and 3rd entries swapped after
  each insertion (41). One mutation survived and can't be seen by any oracle:
  moving `BaseMC`'s right clip bound by two pixels leaves the block inside
  the replicated padding, so the samples are identical.

Notes from D5:

- **Scope.** All of `deblocking.cpp` except the B-slice strength functions
  (`DeblockingBSliceBsMarginalMBAvcbase`, `DeblockingBSliceBSInsideMBNormal`,
  with their `ON_MB_BS`/`IN_SMB_EDGE_MV` macros), which throw
  `NotSupportedException`. They are unreachable: this port's B macroblock
  parser fails on a slice's first macroblock, so a B slice reaches
  `WelsTargetSliceConstruction` with `iTotalMbInCurSlice` 0, and that skips
  the filter. The 8x8-transform strength paths are ported in full although
  Constrained Baseline never uses them. `WelsDeblockingInitFilter` and
  `WelsDeblockingFilterMB` (the threaded decoder's per-MB entry points) are
  ported with `ref` parameters for the C++ references;
  `WelsDecodeAndConstructSlice`, their only caller, is not. `InitDecFuncs`
  waits for D6 with `WelsInitDecoder`; the harness calls its five parts.
- **Port details.** `nBS[2][4][4]` is a 32-byte stackalloc with a `Bs (nBS,
  dir, edge)` helper for `&nBS[dir][edge][0]`, so the `*(uint32_t*)` stores
  and tests stay as upstream writes them. The macros became small static
  helpers (`MB_BS_MV`, `SMB_EDGE_MV`, `BS_EDGE`, `GET_ALPHA_BETA_FROM_QP`,
  `TC0_TBL_LOOKUP`). The five tables were checked number for number against
  `deblocking.cpp` by a script.
- **Oracle.** `ReconSlice` on both sides is now a call to
  `WelsTargetSliceConstruction` followed by the sample record: the slice's
  macroblocks walked from `iFirstMbInSlice` (all of them, or after a failure
  those before the failing one, which upstream leaves in `iMbXyIndex`, and that
  one too for `ERR_INFO_MB_NUM_EXCEED_FAIL`). The samples are read after the
  call, so they include the slice's own filtering but not what later slices do
  across its edges; the "Y" line has the finished picture. `mbs.tsv` and
  `mbs-damaged.tsv` were regenerated. Only the deblocked vectors changed; the 7
  with deblocking off and the CABAC ones are byte-identical to D4's.
- **Checked against the real decoder.** On every clean Constrained Baseline
  vector without cropping (33 of 35), each "Y" hash equals the SHA1 of the
  matching `h264ref yuv` frame, 4,196 of 4,196, in decode order. CVFC1_Sony_C
  and Static crop their output, which the full-picture hash can't be compared
  with; D6's decode test covers them.
- **Coverage.** Clean: 5,196 slices deblocked with filter mode 0 (291 with it
  off), 1,615 of them with alpha or beta offsets (BA1_FT_C, CI1_FT_B, MPS_MW_A,
  MR1/2_MW_A). No clean vector uses mode 2 (no filtering across slice
  boundaries); 7 damaged slices do (CI1_FT_B, CVFC1_Sony_C, SVA_CL1_E), and
  one damaged slice carries the invalid mode 3, which the filter ignores.
- **Negative controls.** Nine, each alone, run against the 86 vector MB-dump
  tests (clean and damaged). Caught: chroma tc0 without its +1 (60 failed),
  the left-edge luma QP average without rounding (32), `BS_EDGE` without its
  coefficient shift (43), `SMB_EDGE_MV`'s threshold at 8 rather than 4
  quarter-samples (49), `MB_BS_MV`'s at 5 (45), the intra macroblock's internal
  luma strength 2 rather than 3 (55), and the beta table read one entry high
  (51). Filter mode 2 crossing slice boundaries is caught by one damaged
  variant only, which is all the coverage mode 2 has. Dropping the 8x8
  transform's inner-edge skip survives: no vector this port decodes uses the
  8x8 transform (it needs CABAC or High profile CAVLC with
  `transform_8x8_mode_flag`), so that path is unverified until Main/High.

Notes from D6:

- **Scope.** The single-threaded decoder end to end. `CWelsDecoder` ports
  `Initialize`/`Uninitialize`, `DecodeFrame`, `DecodeFrame2`,
  `DecodeFrameNoDelay`, `FlushFrame`, `DecodeFrameEx` (a no-op upstream too),
  `SetOption`/`GetOption` and the reordering buffer (`BufferingReadyPicture`,
  the two `ReleaseBufferedReadyPicture` variants, `ReorderPicturesInDisplay`).
  Not ported, each spot marked where it was:
  - The threaded decoder. `m_iThreadCount` stays 0, `GetThreadCount` is 0 and
    `DECODER_OPTION_NUM_OF_THREADS` logs a warning and is ignored.
  - Parse-only mode. `Initialize` rejects `bParseOnly` with
    `cmInitParaError`, so `DecodeParser` stops at its guard, and the
    parse-only branch of `DecodeFrameConstruction` throws.
  - `OUTPUT_BIT_STREAM`, which upstream builds with off. `crt_util_safe_x` was
    only needed for that, so it isn't needed at all.
  - `GetCPUCount`. `m_iCpuCount` stays 1; it only bounds the thread count.
  - `WelsCPUFeatureDetect`. `uiCpuFlag` stays 0, since only the C kernels are
    ported.
  Upstream defines `LONG_TERM_REF` for the decoder, so the LTR branches are
  the ones ported.
- **Port details.** `CWelsDecoder` is a struct in native memory, like
  `welsCodecTrace`, because the decoder context keeps pointers into it
  (`pLastDecPicInfo`, `pVlcTable`, `pPictInfoList`, `pPictReoderingStatus`,
  `pStreamSeqNum`). `New`/`Delete` run the constructor and destructor, and the
  methods take member addresses through a `This` pointer. `m_sPictInfoList[16]`
  is an `InlineArray16<SPictInfo>`, and `pCsDecoder` is null (the mutex is
  threads only). Four other details:
  - `VERSION_NUMBER` is version.h's non-generated "openh264 default: 1.4", as
    the reference build has it.
  - `WelsTime` uses `Stopwatch`, the same QueryPerformanceCounter formula. It
    only feeds `dDecTime`.
  - The statistics arithmetic keeps C's unsigned wrap with `(uint32_t)` casts.
  - For profile 66, `ReorderPicturesInDisplay` does nothing: Constrained
    Baseline output is immediate, and `FlushFrame` never has a picture.
- **Oracle.** `test/SIPSorcery.H264.TestVectors/DecoderHash.cs` drives the
  port the way the harness's `DecodeStream` drives upstream: split on 4-byte
  start codes, `DecodeFrame2` each chunk, end of stream, one more empty
  `DecodeFrame2`, then `FlushFrame` for what's left, all frames hashed as
  upstream's `decoder_test.cpp` does. The 35 vectors matched on the first run.
  For the error paths, the harness's `DecodeStream` became `DecodeChunks` over
  pre-split chunks (`conformance` output byte-identical afterwards) and gained
  four modes:
  - `decode-damaged`, the table.
  - `hash-damaged` and `yuv-damaged`, for one variant.
  - `trace-damaged`, one line per call: length, NAL type, state, buffer
    status, frame SHA1.
  `H264REF_TRACE_LEVEL` routes the decoder's own logs into the trace. The C#
  side has the same trace (`DecoderHash.TraceChunks`) and writes the YUV and
  trace on a mismatch when `H264_DUMP_DIR` is set.
- **The one damaged divergence, and why.** NRF_MW_E variant 2 matched in
  chunk, frame and error counts but not output. The YUV diff showed one output
  frame (16 of 93) that the port emitted as an exact copy of frame 15. The
  trace diff put the first difference at call 19, `0x24` against `0x26`
  (`dsRefLost`). With the logs on, the port says "B slice reference lists are
  not supported": the bit flips turn chunk 19's slice_type into B. Upstream
  decodes it as a B slice even in a Baseline stream; the port rejects it and
  conceals the whole picture. It is a non-reference picture, so nothing after
  it differs. It is listed in `DecodeDamagedTests.KnownDivergences`. The test
  still requires the chunk and frame counts to match, and requires the hash to
  differ, so the entry has to go when B slices are ported.
- **Coverage.**
  - Clean: 4,256 Constrained Baseline frames, including the two cropped
    vectors D5 couldn't compare (CVFC1_Sony_C, Static).
  - Damaged Constrained Baseline: 105 variants, 7,313 frames, with error
    concealment (slice copy), `dsBitstreamError`/`dsRefLost`/`dsNoParamSets`
    returns, and `ResetDecoder` on LS_SVA_D variant 1 and SVA_CL1_E variants 1
    and 2. Eight variants reset in all; the others are CABAC or B vectors.
  - Out of scope: test_scalinglist_jm (High profile CAVLC with scaling
    matrices and weighted prediction) and sps_subsetsps_bothVUI match
    upstream, clean and damaged. The CABAC and B vectors fail their slices as
    designed, and none crash.
- **Negative controls.** Two, each alone:
  - `ResetDecoder` not re-creating the context. The exhausted-DPB variants
    then crash the test host (access violation), so the run aborts.
    `ResetDecoder` is required, not just exercised.
  - `ImplementErrorCon` using frame copy instead of slice copy. 30 of the 35
    damaged-vector tests fail; the 35 clean hash tests still pass, as they
    should, since no clean vector conceals.

Notes from D7:

- **Scope.** The decoder wired into SIPSorcery. Three files outside the port
  proper, in `src/SIPSorcery.H264`:
  - `H264Codec`, an `IVideoEncoder` that only decodes so far. Each call takes
    one Annex B access unit, which is what `H264Depacketiser` and
    `RTCPeerConnection.OnVideoFrameReceived` deliver. It decodes with
    `DecodeFrameNoDelay`, so the picture comes out of the same call. Output is
    I420, BGR or RGB from `DecodeVideo` and BGR or RGB from
    `DecodeVideoFaster`, which converts into a native buffer the codec owns
    and re-uses. A `RawImage` describes one plane, so I420 is `DecodeVideo`
    only. Any other requested format gets BGR, the convention the other
    SIPSorcery decoders follow. Calls take a lock. Error concealment is slice
    copy, the mode the D6 damaged tests verified. The decoder's log lines go
    to the codec's `ILogger`: errors as warnings, warnings as debug.
  - `H264NetVideoEndPoint`, an `IVideoSink` modelled on
    `Vp8NetVideoEncoderEndPoint`'s sink half. It raises BGR pictures with
    packed rows (stride = 3 × width), and `OnKeyFrameRequired` at most once a
    second while the decoder needs a key frame. A source half follows the
    encoder.
  - `YuvToRgb`, the plane copy and the YUV to RGB conversion. It reads the
    matrix and range from the SPS VUI: BT.709 when matrix_coefficients is 1,
    otherwise BT.601; full range when video_full_range_flag is set, otherwise
    limited range. A stream that signals nothing is BT.601 limited, as
    browsers and FFmpeg's default scaler take it. It is 8-bit fixed point,
    within 1 of the equations (unit tested over a grid for all four matrices).
    `PixelConverter.I420toBGR` was not used: its blue coefficient is 2.302
    rather than 2.032, it ignores range, and it pads rows to 4 bytes, which a
    `VideoSample` cannot describe.
- **`KeyFrameRequired`.** Set when `DecodeFrameNoDelay` returns any state but
  `dsErrorFree` (ignoring `dsFramePending`): missing parameter sets, a lost
  reference, a damaged bitstream, or `dsDataErrorConcealed`. The decoder sets
  the last for every picture that predicts from a concealed one until the
  next IDR (`bAllRefComplete` in `WelsDecodeConstructSlice`), so the flag
  stays up exactly as long as the damage does.
- **The Main profile vector, and the two non-upstream members.**
  CVPCMNL1_SVA_C has profile_idc 77 (constraint flags 0xe0), although it uses
  only Constrained Baseline tools. Upstream switches its reordering buffer on
  for any profile_idc but 66 and 83, so each picture is held until the next
  one arrives: one picture late, and the last only on `FlushFrame`. Calling
  `FlushFrame` after each access unit is legal, because `DecodeFrameNoDelay`
  leaves the end-of-stream flag set, but it passes no decoder context. Single
  threaded, `m_pPicBuff` is null, so the picture's reference count never comes
  down and the picture buffer runs out: 4 of 30 frames. That is an upstream
  quirk, harmless at a real end of stream. `CWelsDecoder.ReleaseHeldPicture`
  makes the release `ReorderPicturesInDisplay` would make a picture later
  (`ReleaseBufferedReadyPictureNoReorder` with the context), only for streams
  without B slices. The vector then decodes to upstream's SHA1 with no delay.
  `CWelsDecoder.CurrentSps` gives `YuvToRgb` the active SPS. Both are marked
  "Not upstream".
- **Oracle.** No new harness mode. The hash is the same as D6's, but the
  stream is fed the way an RTP receiver feeds it:
  - `AccessUnits.Split` splits each vector into access units (H.264
    7.4.1.2.3): AUD; SEI, SPS, PPS or prefix NAL after a slice; a slice with
    first_mb_in_slice 0 after a slice. Constrained Baseline has no ASO, so the
    last rule is enough.
  - `ConstrainedBaselineVector_AccessUnitsDecodeToUpstreamHash`: all 35 give
    upstream's SHA1 and frame count, with `KeyFrameRequired` never set.
  - `ConstrainedBaselineVector_RtpRoundTripDecodesToUpstreamHash`: the same
    after `AccessUnits.Packetise` (SPS and PPS in a STAP-A, FU-A over 1200
    bytes, as browsers send) and SIPSorcery's `H264Depacketiser`, with two
    packets of each unit swapped and the sequence numbers wrapping. All 35.
  - `ConstrainedBaselineVector_OutputFormatsAgree`: I420, BGR, RGB and
    `DecodeVideoFaster` agree picture for picture.
  - Key frame signalling: joining after the IDR gives no pictures and the flag
    until the IDR comes. A lost reference picture conceals the next seven with
    the flag up, and re-sending the IDR clears it and decodes byte for byte as
    the first time. The endpoint asks once for a burst of 19 bad units.
  The TestVectors project now references `SIPSorcery` for the depacketiser.
- **Live check** (not automated). A scratch console version of the example
  served `capture.html` to the Claude desktop app's built-in browser
  (Chromium). Its canvas test pattern went out as H.264 42e01f, 640x480 at 30
  fps, packetization mode 1. 450 frames decoded clean, colours and moving
  marker correct. With one access unit dropped (unit 200): unit 201 came out
  concealed with `KeyFrameRequired` set, the endpoint sent a PLI (sender SSRC
  from `VideoRtcpSession`, as `KeyFrameRequester` learned the hard way), and
  the browser's next unit, 202, began with an SPS and IDR. Decoding was clean
  from there, and the browser's stats showed 1 PLI and 2 key frames.
- **Example.** `examples/WebRTCExamples/WebRTCClientH264Net` (net10.0-windows,
  WinForms), from `WebRTCClientVP8Net`: a recvonly peer connection offering
  only H.264 42e01f, `H264NetVideoEndPoint` into a picture box, PLIs on
  `OnKeyFrameRequired`. Its `capture.html` falls back to a canvas test pattern
  with no camera, and shows the browser's outbound codec, key frame and PLI
  counts. In `SIPSorcery.slnx`, with a `.slnf`.
- **Not done.** No `-pre` package: `IsPackable` stays false until the patent
  decision in section 8. The CLI's `h264.net` option waits for the encoder
  (E8), since the CLI's receive paths already have FFmpeg's decoder.

Notes from E1:

- **Encoder namespace.** `SIPSorcery.H264.Encoder`, free functions in
  `WelsEnc` (static partial, one per ported file). `WelsEnc` is not a global
  `using static`, because some of its constants clash with the decoder's
  (`MAX_PPS_COUNT` is 57 in the encoder). Types outside `WelsEnc` write
  `WelsEnc.X`.
- **C++ classes.** `SWelsSvcCodingParam : SEncParamExt` repeats the base
  fields first, in the same order and types, with `Base()` for the upcast.
  A unit test checks that the prefix matches `SEncParamExt` field for field.
  The `IWelsParametersetStrategy` hierarchy (an interface and five classes,
  all sharing `CWelsParametersetIdConstant`'s fields) is one native struct:
  a port-added class tag, and each virtual method switches on it to the
  override the vtable would pick. The bodies keep their class names
  (`CWelsParametersetSpsListing_GenerateNewSps`). The `_DEBUG`-only checks
  are left out, as in the Release reference.
- **Upstream quirks kept.** `ParamBaseTranscode` writes layer 0's profile,
  level, QP and max bitrate on every layer pass. `ParamValidation` truncates
  the loop filter settings to `int16_t` for `CheckInRangeCloseOpen`.
  `CWelsParametersetSpsPpsListing::OutputCurrentStructure` copies the PPS list
  from `pCtx->pPps`, not `pPPSArray`. `WelsWriteParameterSets` writes
  `pSpsArray[GetSpsIdx (i)]`, which for the non-listing strategies is SPS 0
  every time, so simulcast's `EncodeParameterSets` writes the base layer's
  SPS twice.
- **One deviation.** `WelsCheckRefFrameLimitationLevelIdcFirst` indexes
  `g_ksLevelLimits` by `uiLevelIdc - 1`, past the 17-entry table for levels
  above 1.3. The port clamps the index. The function is unreachable:
  `ParamValidationExt` rejects the non-real-time usage types that call it.
- **CPU detection.** The C-only `WelsCPUFeatureDetect` returns no features and
  no core count, so cores come from `DynamicDetectCpuCores`, which is
  `Environment.ProcessorCount` here. Only `iMultipleThreadIdc == 0` and
  `SM_FIXEDSLCNUM_SLICE` with `uiSliceNum == 0` read it. Every reference
  configuration sets one thread.
- **Oracle.** `h264ref paramsets` applies each configuration's settings to
  `GetDefaultParams` (`api=ext`) or to a zeroed `SEncParamBase` (`api=base`).
  It then runs a script of `EncodeParameterSets`, forced-IDR and plain
  frames, and prints, for each operation, the frame type and the non-VCL
  layers with their NAL bytes. It also prints the
  `ENCODER_OPTION_SVC_ENCODE_PARAM_EXT` parameters after init, and the
  defaults once. `ParameterSetTests` drives the ported functions as
  `InitializeInternal`, `WelsInitEncoderExt`, `RequestMemorySvc` and
  `InitDqLayers` do, keeping their parameter set work. For frames it uses
  the reference's frame type, because the frame type decision comes with
  rate control (E6). An IDR writes the parameter sets as
  `PrepareEncodeFrame` does. Corrupting one SPS byte or one parameter in the
  copied table fails exactly the affected configuration.
- **Slice headers** were in E1's acceptance line. They are written by
  `svc_encode_slice`'s `WelsSliceHeaderWrite`, which needs the slice and
  reference state, so they move to E4 and are covered by its IDR byte match.
- **Not done.** `encoder_ext`, `encoder` and `encoder_context` are partial:
  only the fields and functions above. The rest comes with E4 to E8. Init
  and frame orchestration are mirrored in the test until E8 ports them.

Notes from E2:

- **Name clashes with the decoder's aliases.** The decoder already has the
  global `PGetIntraPredFunc` (`byte*, int`, predicting in place). The
  encoder's typedef (`byte* pPred, byte* pRef, int`, a packed prediction) is
  `PGetIntraPredFuncEnc`. `PCopyFunc` has the same signature in both, so it
  is shared. Future encoder typedefs that collide get the same `Enc` suffix.
- **Function-pointer arrays in structs** (`pfSampleSad[MAX_BLOCK_TYPE]`,
  `pfGetLumaI4x4Pred[I4_PRED_A]`, ...) are private `InlineArrayN<nint>`
  storage behind a property that returns the alias pointer, so
  `p->pfGetLumaI4x4Pred[i] = &X` reads as in C.
- **Harness.** The encoder's headers clash with the decoder's
  (`decode_mb_aux.h`, `get_intra_predictor.h`, `wels_const.h`), so
  `kernels_encoder.cpp` builds as the `h264ref_enc_kernels` object library
  with only the encoder include paths, linked into `h264ref`. `sample.h`
  leaves `WelsSampleSatd8x4_c` and `WelsSampleSatd4x8_c` undeclared; the
  fixture declares them. Coefficient buffers carry 4 guard entries a side,
  and every hash covers the guards.
- **Decisions the fixtures must decide** (the D3 lesson). The first pass
  passed all its rows yet let four of eight planted bugs through: the skip's
  `>` never met its threshold, the combined kernels never tied, the Hadamard
  DC's lower clip was never reached, and one plant was a no-op. Added rows:
  the 2x2 skip at threshold + {-1, 0, 1}; a flat-neighbour target for the
  combined kernels, so all three modes predict the same block and the order
  and `<` decide; eight Hadamard seeds, which reach both ends of all four
  clips. `SkipRows_DecideBothWays` and `CombinedRows_PickEveryMode` assert
  that the rows keep reaching both answers and every mode.
- **Named slots are checked by behaviour.** Comparing function pointers is
  not reliable in .NET (CS8909), so each named `SWelsFuncPtrList` slot must
  reproduce a fixture row of the kernel upstream assigns it. The indexed
  slots have their own `enc_init` rows, called slot by slot in C and C#.
- **Combined3 kernels.** Upstream's C path sets the five
  `pfIntra*Combined3*` pointers to NULL, since only SIMD builds set them, and
  the port does the same. The mode decision (`svc_base_layer_md.cpp`) checks
  the pointer and, when it is NULL, falls back to its per-mode loop. Nothing on
  the C path calls the `_c` bodies. They are ported, file for file, and tested
  so that the file is complete.

## 1. Goal

A pure managed H.264 encoder and decoder in `src/SIPSorcery.H264`, ported from
Cisco's OpenH264 (`C:\dev\openh264`), built the same way as the in-tree managed
codecs:

- **Like SIPSorcery.VP8 (`vp8.net`):** a file-for-file port that lives in the
  repo and keeps the original names, so the C# can be read side by side with the
  C++. It is checked bit-exact against the C reference and built up as a series
  of small foundation PRs.
- **Like SIPSorcery.AV1 (`av1.net`):** a small, modern public API on top
  (`IVideoEncoder`, a bitrate property, a key-frame interval, stride-aware
  overloads, trimmable and AOT-safe), selectable from the CLI as `h264.net` next
  to `vp8.net` and `av1.net`.

It uses no native binaries and no P/Invoke at runtime.

## 2. Source baseline

- The source is openh264 `master` at `78783f19` (2026-11-20), `v2.7.0-1`,
  BSD-2-Clause, © Cisco. Pin this commit and record it in every ported file's
  header.
- The portable code to port, leaving out the SIMD and assembly folders
  (x86/arm/arm64/mips/loongarch), the build files and the console apps:

| Module | Contents | C/C++ lines |
|---|---|---|
| `codec/api/wels` | Public API types (`codec_api.h`, `codec_app_def.h`, `codec_def.h`) | 1.5k |
| `codec/common` | MC, intra prediction, deblocking helpers, picture padding, tables, memory, threads | 8.0k src + ~3.1k portable headers |
| `codec/decoder` | core (18.9k src + headers) + `plus/welsDecoderExt` (1.4k) | ~25.8k |
| `codec/encoder` | core (21.9k src + headers) + `plus/welsEncoderExt` (1.3k) | ~32.4k |
| `codec/processing` | scene change, background detection, adaptive quantisation, complexity, downsample, denoise | ~4.8k portable |

The total is about 75k lines of C++, including headers and comments. Expect
roughly 45–60k lines of C#, which is 2–3× the size of SIPSorcery.VP8 (about 20k)
today.

The C++ is fairly plain: 57 classes, 4 templates, macros, and function-pointer
tables for SIMD dispatch, with 181 `_c` reference kernels behind those tables.

## 3. Scope

**Decision (24 Nov 2026, Aaron): decoder first. Throughout the port, the
target is the realtime profile WebRTC uses, Constrained Baseline
(`profile-level-id=42e01f`). The other profiles wait until that works end to
end, and are added only if needed.**

### Decoder: Constrained Baseline, then other profiles only if needed

- **Target (the only scope until it ships):** Constrained Baseline. This covers
  CAVLC, I/P slices, multiple slices, multiple reference frames, LTR/MMCO,
  reference list modification, PCM and error concealment (WebRTC loses packets).
- **Deferred, only if needed:** the other Baseline features (FMO/ASO,
  redundant slices), and progressive Main/High (CABAC, the 8x8 transform,
  scaling matrices, B-slices with direct prediction). Ported code can leave
  these branches as `NotSupported` stubs that log and return an error, so the
  call graph still matches upstream and the stubs can be filled in later.
- **Out of scope:** interlaced video (upstream doesn't support it either) and
  upstream's threaded decode mode.

### Encoder: the WebRTC configuration first

- **First release:** the `CAMERA_VIDEO_REAL_TIME` usage type with one spatial
  layer, one temporal layer, Constrained Baseline, CAVLC and `SM_SINGLE_SLICE`.
  - Rate control: `RC_BITRATE_MODE`, plus `RC_OFF_MODE` (fixed QP) for tests.
  - The default preprocessing: scene-change detection, background detection,
    adaptive quantisation and complexity analysis. These are on by default, and
    the upstream hashes depend on them.
  - IDR on demand (PLI/FIR) and periodically, SPS/PPS sent with every IDR,
    bitrate and frame rate changeable while running, and a re-init when the
    resolution changes.
- **Later (Phase 4):** the other slice modes, temporal layers, LTR, CABAC/Main,
  screen-content mode, denoise, spatial simulcast and slice threading.

## 4. Project layout and porting rules

```
src/SIPSorcery.H264/
  SIPSorcery.H264.csproj         net8.0;net9.0;net10.0 (CLI global tool needs net8),
                                 AllowUnsafeBlocks, IsTrimmable, IsAotCompatible
  README.md
  H264Codec.cs                   IVideoEncoder facade (encode + decode), like VP8Codec
  H264NetVideoEncoderEndPoint.cs like Vp8NetVideoEncoderEndPoint
  api/         codec_api.h / codec_app_def.h / codec_def.h -> C# enums + structs
  common/      one .cs per codec/common/src/*.cpp (+ the headers it needs)
  decoder/     one .cs per codec/decoder/core/src/*.cpp, plus welsDecoderExt
  encoder/     one .cs per codec/encoder/core/src/*.cpp, plus welsEncoderExt
  processing/  one .cs per portable codec/processing/src/**/*.cpp
test/SIPSorcery.H264.UnitTest/      per-primitive / per-module tests over committed fixtures
test/SIPSorcery.H264.TestVectors/   upstream SHA1 conformance (decoder_test + encoder_test tables)
thirdparty/openh264-dotnet-port/    reference harness, fixture capture tools, this diary
```

Porting rules:

1. **File for file, name for name.** For example, `decode_slice.cpp` becomes
   `decoder/decode_slice.cs` and keeps `WelsDecodeSlice`, `SDqLayer` and
   `PWelsDecoderContext` (as `SWelsDecoderContext*`). The namespaces follow
   upstream: `SIPSorcery.H264.WelsCommon`, `.WelsDec`, `.WelsEnc` and `.WelsVP`.
   Only the facade is public, in `SIPSorcery.H264`.
2. **File headers.** Each file starts with a SIPSorcery header block in the VP8
   style ("Port of: decode_slice.cpp", the attribution line *Claude Opus 5.5
   (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron
   Clauson*, and BSD-3), followed by Cisco's BSD-2 notice word for word, which
   BSD-2 requires. Ship openh264's LICENSE in the nupkg as a third-party notice.
3. **Memory: unsafe pointers, not `ArrPtr<T>`.** OpenH264 does a lot of pointer
   arithmetic over padded planes, including negative offsets into the border that
   `expand_pic` fills. The direct port is `byte*` into memory from
   `NativeMemory.AlignedAlloc`. Wrap it in a `CMemoryAlign` port so the
   `WelsMallocz`/`WelsFree` call sites stay as they are, and have it track
   allocations so `Dispose` frees everything and tests can check for leaks.
   Per-MB scratch buffers (`ENFORCE_STACK_ALIGN_*`) become `stackalloc`.
4. **Keep the function-pointer tables** (`SDecodingFuncs`, `SWelsFuncPtrList`,
   `SMcFunc`, deblocking and intra prediction) as `delegate*<...>` fields filled
   with the `_c` kernels only. The call graph then matches upstream, and each
   kernel has one slot for a `System.Runtime.Intrinsics` version later, tested
   for equality against the C kernel (upstream's `test/encoder` and
   `test/common` use the same pattern).
5. **Macros** become `[MethodImpl(AggressiveInlining)]` helpers (`WELS_CLIP3`,
   `LD32`/`ST32` → `Unsafe.ReadUnaligned`, and so on), and `#define` constants
   become `const`.
6. **Integer and float semantics.** Keep the C integer widths, signed shifts,
   integer promotions and `uint8_t` wrap-around. Keep `float` versus `double`
   exactly as upstream has it in `ratectl`, `encoder_ext`, `AdaptiveQuantization`
   and `downsamplefuncs`, and don't reorder floating-point operations.
7. **Single-threaded first.** The decoder is single-threaded by upstream default,
   and the encoder runs with `iMultipleThreadIdc = 1`. Threading comes in Phase 5
   with `Task`, not with a port of `WelsThreadLib`.
8. **Logging.** `welsCodecTrace` maps to `ILogger` through
   `SIPSorcery.LogFactory`, gated by level so it costs nothing when off.
9. **Not ported:** `.asm`/`.S` files, the architecture folders, `cpu.cpp`
   (replaced by a C-only flag), the console apps, the GMP plugin, the DLL
   entry/`.def` files and the build systems.

## 5. Verification

The VP8 diary's lesson was to use small PRs and to prove every primitive
bit-exact against C before integrating it. OpenH264 provides more oracles than
libvpx did:

1. **Native reference build (P0.1).** Build the pinned openh264 with MSVC and
   CMake. Both are on this machine (VS 2022 and VS 18 `cl.exe`, plus CMake). Turn
   assembly off so only the `_c` paths run, which is exactly the code being
   ported. Then build upstream's `test/api` gtests and confirm the C-only build
   reproduces **all 51 decoder hashes** (`decoder_test.cpp`) and **all 16 encoder
   hashes** (`encoder_test.cpp`). That proves the reference is the right oracle.
2. **Primitive fixtures.** A small C harness links the reference static libs and
   runs the kernels: IDCT/DCT, quant/dequant, intra predictors, luma 6-tap and
   chroma bilinear MC, deblocking edge filters, SAD/SATD, the CAVLC/CABAC coders
   and the bit writer. It uses seeded random inputs plus edge cases and writes
   input/output fixtures into the unit-test project. CI never needs a C compiler,
   because the fixtures are the contract.
3. **Per-frame and per-MB diffs.** A harness mode dumps every reconstructed frame
   before and after deblocking, and the C# test finds the first macroblock that
   differs. An optional instrumentation patch, compiled only into the harness,
   dumps that MB's syntax: mb_type, QP, MVs, ref_idx, CBP and coefficients. This
   is a systematic version of the VP8 diary's "dump the plane bytes and read the
   pattern" step.
4. **End-to-end hashes.** The TestVectors project ports both upstream tables:
   SHA1 over the cropped Y/U/V rows for the decoder, and SHA1 over all NAL output
   for the encoder. These hashes define "done" for each decoder tier and for the
   encoder. D1's header parser tags each vector by entropy mode, slice types and
   transform/scaling use, so the tiers come from what each stream actually
   contains, not from guesses based on file names.
5. **Round trips.** Encode in C# and decode with both the C# decoder and the
   native one. The encoder's reconstruction must equal the decoder's output
   exactly, because H.264 decoding is deterministic.
6. **Interop.** Test against Chrome, Firefox (which itself uses the OpenH264
   plugin) and Safari through the examples, and decode the encoder's output with
   FFmpeg.

**Test vectors.** The upstream bitstreams total about 52 MB. One CABAC/PCM
vector is 19 MB and several B-frame vectors are 3–5 MB each. Commit the small
ones (SIPSorcery.VP8.TestVectors already commits 61 IVF vectors). Load the large
ones from an `OPENH264_RES` path and skip them when it isn't set. The YUV inputs
for the first encoder hashes are small: 0.8 MB, 0.1 MB and 0.2 MB. The 6.6 MB
720p input stays external.

## 6. PR series

Each PR ports one slice of the code, adds bit-exact tests, builds green and
merges. Approximate C# sizes are in brackets.

### Phase 0: scaffolding

- **P0.1 Reference harness.** A C-only CMake build of the pinned openh264,
  upstream's `test/api` hashes reproduced, and the skeleton of the fixture
  capture tool. *(Tooling only.)*
- **P0.2 Skeleton.** The csproj, both test projects, the solution and CI wiring,
  the `api/` types, `CMemoryAlign` and logging. *[~2k]*

### Phase 1: common

- **C1** `common_tables`, `macros`/`wels_common_defs`, `utils`, `copy_mb`,
  `expand_pic`. *[~1.5k]*
- **C2** `intra_pred_common` and the C paths of `mc.cpp` (quarter-pel 6-tap
  luma, eighth-pel chroma, averaging). This is the biggest exactness risk in
  common. *[~1.5k]*
- **C3** `deblocking_common`, `sad_common`. *[~0.6k]*

### Phase 2: decoder

- **D1** Bitstream reader and NAL/AU parsing: `bit_stream`, `memmgr_nal_unit`
  and `au_parser` (emulation prevention, SPS/PPS/subset SPS, VUI, slice header).
  Acceptance: the header dumps match native for every vector. *[~2.5k]*
- **D2** CAVLC macroblock syntax: `parse_mb_syn_cavlc`, `decoder_data_tables`,
  and the parse parts of `decode_slice`, `mv_pred`, `pic_queue` and `fmo`.
  Acceptance: the per-MB syntax dumps match. *[~2k; was ~5.2k, see the D2
  notes]*
- **D3** I-slice reconstruction: `get_intra_predictor`, `decode_mb_aux` and
  the intra/IDCT/dequant/PCM parts of `rec_mb`. Acceptance: the planes before
  deblocking match on intra-only content. *[~2k]*
- **D4** P-slices: `mv_pred`, `manage_dec_ref`, `pic_queue` and the MC path in
  `rec_mb`. Acceptance: the planes before deblocking match on P vectors.
  *[~2.5k; ~2.3k, see the D4 notes]*
- **D5** Deblocking (`decoder/deblocking.cpp`). Acceptance: the first SHA1
  matches. *[~1.2k; ~1.35k. The upstream hashes need the API layer, so D5's
  acceptance became the harness pictures against the real decoder's frames,
  see the D5 notes]*
- **D6** Orchestration and API: `decode_slice`, `decoder_core`, `decoder`,
  `error_concealment` and `welsDecoderExt` (`CWelsDecoder`,
  `DecodeFrameNoDelay`/`DecodeFrame2`). Acceptance: every Constrained Baseline
  vector passes its hash. *[~6k]*
- **D7** SIPSorcery wire-up: `H264Codec.DecodeVideo` (Annex B access units from
  `H264Depacketiser` → I420/BGR `VideoSample`) and a `WebRTCClientH264Net`
  example that decodes Chrome's stream. This could ship as the first `-pre`
  package.
- **Deferred, only if needed:** FMO/ASO (`fmo`), CABAC (`cabac_decoder`,
  `parse_mb_syn_cabac`), and Main/High (8x8 transform, scaling lists, B-slices
  and direct prediction). Each has upstream hashes ready as acceptance tests.

### Phase 3: encoder (WebRTC configuration)

- **E1** Bit writer, NAL encapsulation and parameter sets: `nal_encap`, `au_set`
  and `paraset_strategy` (single-layer path). Acceptance: the SPS/PPS/slice
  headers match native byte for byte. *[~2k]* **Done**: all strategies and
  multi-layer paths too, ~4.6k lines. Slice headers moved to E4 (see the E1
  notes).
- **E2** Kernels: `encode_mb_aux`, the encoder's `decode_mb_aux`,
  `encoder_data_tables`, `sample` (SAD/SATD/SSD) and the encoder's
  `get_intra_predictor`. *[~2k]*
- **E3** CAVLC writer: `set_mb_syn_cavlc`, `svc_set_mb_syn_cavlc`, also round-
  tripped through the C# D2 parser. *[~0.8k]*
- **E4** I-slices at fixed QP: intra mode decision (`md`,
  `svc_base_layer_md` I), `svc_encode_mb`, `svc_encode_slice`, the encoder's
  `deblocking` and `picture_handle`. Acceptance: the IDR bytes match native, and
  the reconstruction equals the C# decoder's output. *[~4k]*
- **E5** P-slices at fixed QP: `svc_motion_estimate`, `mv_pred`,
  `svc_mode_decision`, `svc_base_layer_md` P and `ref_list_mgr_svc` (single
  reference). Acceptance: the stream matches native byte for byte. *[~4k]*
- **E6** Rate control: `ratectl` (bitrate and quality modes, frame skip).
  Acceptance: the output matches native byte for byte with rate control on and
  preprocessing off. *[~1.5k]*
- **E7** Preprocessing: `processing/` and `wels_preprocess`. Acceptance: **the
  three upstream encoder hashes for CAMERA_VIDEO_REAL_TIME / SM_SINGLE_SLICE /
  1 layer / CAVLC** pass (`672a52fb…`, `08ade185…`, `e60f12e3…`). *[~4k]*
- **E8** Orchestration, API and wire-up: `encoder_ext`, `encoder` and
  `welsEncoderExt` (`InitializeExt`, `EncodeFrame`, `ForceIntraFrame`,
  `SetOption`), then `H264Codec.EncodeVideo`, the profile-level-id, the CLI's
  `--encoder h264.net`, an example, and interop with Chrome, Firefox and Safari.
  *[~3k]*

That makes about 22 PRs to the end of Phase 3.

### Phase 4: breadth (each optional, one PR each)

- The `SM_RASTER_SLICE`, `SM_SIZELIMITED_SLICE` and `SM_FIXEDSLCNUM_SLICE` slice
  modes. Upstream hashes exist for these.
- Temporal layers (for WebRTC's L1T2 and L1T3 modes).
- LTR, CABAC encode, denoise, screen-content mode and spatial simulcast.
  Upstream hashes exist for each.

### Phase 5: performance

- An allocation pass like VP8 #1576, aiming for zero per-frame GC allocations
  after warm-up.
- `System.Runtime.Intrinsics` kernels (Vector128/256, AdvSimd) for MC, SAD/SATD,
  DCT/IDCT, deblocking, intra prediction and `expand_pic`. Each goes behind the
  function tables with a test that it equals the C kernel.
- Slice-level threading and a BenchmarkDotNet project.

## 7. SIPSorcery integration

- **`H264Codec : IVideoEncoder, IDisposable`** handles encode and decode, like
  `VP8Codec`. It exposes `TargetBitrateKbps`, `FrameRate`,
  `KeyframeIntervalFrames` and `ForceKeyFrame()`. It has `byte[]`,
  `IBufferWriter<byte>` and span-plus-stride overloads, and it implements
  `EncodeVideoFaster`/`DecodeVideoFaster`.
- **Encoder output** is an Annex B access unit with SPS/PPS before every IDR,
  which `H264Packetiser.ParseNals` already handles. **Decoder input** is what
  `H264Depacketiser` already produces (Annex B).
- **SDP:** advertise `packetization-mode=1;profile-level-id=42e01f`, as the
  FFmpeg helper does, and raise the level byte to match the level openh264
  chooses from resolution and frame rate (`au_set`).
- **Pixel formats:** I420 natively. Reuse AV1's `I420Converter` for BGR/RGB
  input. It is a candidate to move into Abstractions so VP8, AV1 and H.264 share
  one copy.
- **CLI:** add `h264.net` next to `ENCODER_VP8_NET` and `ENCODER_AV1_NET` in
  `LibraryVideoPublisher` and `WebRtcVideoBenchCommand`, and feed it the bitrate
  the same way `AV1VideoEncoder.TargetBitrateKbps` gets it.

## 8. Risks and open decisions

1. **Patents: decide before publishing a NuGet package.** The source is BSD-2,
   which is compatible with SIPSorcery's BSD-3. But H.264 is covered by patents,
   and Cisco pays the AVC pool royalties only for the binary module that Cisco
   builds and distributes. Source builds aren't covered, so neither is a C# port.
   Publishing SIPSorcery.H264 passes that exposure to the package's users, as
   building FFmpeg or libx264 from source does today. The options are to publish
   with a clear notice, keep it source-only or unlisted, or wait for the
   remaining pool patents to expire in the markets that matter. That is a legal
   question rather than an engineering one, and the port doesn't have to wait
   for the answer.
2. **Decided: decoder first, Constrained Baseline only** (Aaron, 24 Nov 2026).
   The hashed Constrained Baseline vectors prove the parts of `common` (MC,
   intra prediction, IDCT, deblocking) that the encoder reuses, and the C#
   decoder then becomes a CI oracle for the encoder, as libvpx's existing
   decoder was for the VP8 encoder.
3. **Bit-exact rate control.** The rate-control and AQ code uses `float`.
   Upstream's hashes are the same on x86/ARM and MSVC/GCC, which suggests the
   results are deterministic. If exact matching with rate control on still turns
   out fragile, the fallback is exact matching at fixed QP plus statistical
   checks on rate control (bitrate within a tolerance).
4. **Performance.** A straight port of the C paths will run several times slower
   than openh264 with its assembly. The VP8 decoder is already described as
   "functional but slow". Keeping the function tables leaves the SIMD slots ready
   for Phase 5.
5. **Upstream drift.** Pin `78783f19`, record any re-sync here, and re-sync only
   deliberately.

## 9. Next steps

1. P0.1: build the C-only reference and reproduce the upstream hashes. This also
   proves the MSVC and CMake toolchain on this machine.
2. P0.2: the skeleton and the API types.
3. C1 to C3, then D1 (or E1 if the encoder goes first).
