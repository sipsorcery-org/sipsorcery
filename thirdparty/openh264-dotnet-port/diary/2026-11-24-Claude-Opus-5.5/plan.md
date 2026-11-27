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
| E3 CAVLC writer | **Done** (26 Nov) | `set_mb_syn_cavlc` (`CavlcParamCal_c`, `WriteBlockResidualCavlc`, the CAVLC and CABAC stash/pop pairs, `GetBsPos*`, `WelsWriteSliceEndSyn`, `InitCoeffFunc`), `svc_set_mb_syn_cavlc` (`WelsSpatialWriteMbPred`, `WelsSpatialWriteSubMbPred`, `CheckBitstreamBuffer`, `WelsSpatialWriteMbSyn`, `WelsWriteMbResidual`, the CBP maps), the `vlc_encoder.h` writers, and the structs they read (see the notes). CAVLC only: the CABAC paths throw. ~1.8k lines. New kernel groups (`refharness/src/cavlc_encoder.cpp`): 18 `vlc_encoder`, 1042 `set_mb_syn_cavlc` (546 random residual blocks, a 110-row level sweep and a 262-row count sweep among them) and 382 `svc_set_mb_syn_cavlc` rows (156 of them slices of 8 macroblocks), **all match**. Every residual block the writer accepts is also read back by the D2 parser, which uses the same bits and returns the same coefficients. 29 planted bugs each fail their rows. Two of them failed the residual rows only after the sweeps were added (see the notes). UnitTest: 6547. TestVectors: 480. |
| E4a I-slice macroblock coding | **Done** (26 Nov) | E4 is split in two (see the notes). E4a: the intra half of `md` and `svc_base_layer_md` (I16x16, I4x4 and its fast variant, chroma, the fine partition choice), `svc_encode_mb` (the I16x16, I4x4 and chroma transform, quantisation and reconstruction), the I-slice half of `svc_encode_slice` (`WelsISliceMdEnc` with its overflow retry, `UpdateMbNeighbor`, the macroblock cache, the slice boundary tables), `svc_mode_decision`'s background QP update, `ratectl`'s rate control off path, `picture`/`picture_handle`, the rest of `svc_enc_slice_segment`, and from `encoder_ext` `AllocStrideTables` and the fast/normal coding switch. ~3.3k lines. New kernel groups (`refharness/src/islice_encoder.cpp`): 204 `md`, 8 `svc_base_layer_md`, 10 `picture_handle`, 8 `encoder_ext` and 728 `svc_encode_slice` rows. 706 of those code whole frames (1 to 99 macroblocks, 1 to 9 slices, both coding modes, QP 0 to 51, three kinds of content) through `WelsISliceMdEnc` on a hand-built context, and hash the bitstream, the reconstruction and every macroblock's state. **All match**. Of 29 planted bugs, 26 fail rows, two of them only after threshold rows were added. The other three cannot change an I slice (see the notes). UnitTest: 7511. TestVectors: 480. |
| E4b The IDR through the API | **Done** (26 Nov) | The rest of E4. From `svc_encode_slice`: the slice header writers (base and extension, with the reference reordering and marking), `WelsCodeOneSlice`, and the slice lists (`InitSliceList`, `InitSliceInLayer`, `InitSliceThreadInfo`, the slice bitstream buffers). The encoder's `deblocking` (the intra and inter boundary strengths, the edge, macroblock and slice filters, `PerformDeblockingFilter`, `DeblockingInit`). From `encoder_ext`: the layer and macroblock list set-up (`InitMbInfo` to `InitDqLayers`, `InitMbListD`, with their frees), `WelsInitCurrentLayer`, `PrefetchReferencePicture`, `PreprocessSliceCoding` and `GetTemporalLevel`. `encoder`'s `InitFrameCoding` and the frame numbers. `ratectl`'s `GomRCInitForOneSlice`. ~2.5k lines. New `h264ref idr` mode (`reference/idr.tsv`): 47 configurations of one spatial layer with rate control off. They cover 4 kinds of content, QP 0 to 51, the three complexity modes, 9 sizes (16x16 to 1280x720, odd and cropped), fixed, raster-row and raster-list slicing, the three deblocking modes with offsets, temporal layers, LTR, three id strategies, the feature flags and the Main profile. `IdrTests.cs` drives the ported functions the way `InitializeExt` and the first `EncodeFrame` do. It does by hand, for an IDR, what the unported reference manager, VAA and preprocessing do (see the notes). **All 47 match** native byte for byte, including two rows that overflow the frame buffer and return 3. Every reconstruction equals the C# decoder's picture, and every row frees all it allocates. 3 planted bugs (deblocking, slice header and padding) each fail exactly the rows they should. UnitTest: 7511. TestVectors: 528. |
| E5a P-slice macroblock coding | **Done** (26 Nov) | E5 is split in two (see the notes). E5a: `mv_pred`, the camera half of `svc_motion_estimate` (the initial point, the diamond search, `WelsMotionEstimateSearch`, `WelsInitMeFunc`), the inter half of `md` (the inter neighbour caches, `MeRefineFracPixel`, `PredictSad`, `PredictSadSkip`, `MvdCostInit`), the P half of `svc_base_layer_md` (the skip, background skip, 16x16 and fine partition decisions with their VAA variant, the sub-pel refinement, intra in P), `svc_mode_decision`'s background skip, `svc_encode_mb`'s inter coding and skip reconstruction, the P half of `svc_encode_slice` (`WelsCodePSlice`, `WelsMdInterMbLoop`), the P pointers of `PreprocessSliceCoding`, `InitFunctionPointers`, `ratectl`'s adaptive quantisation branches, and the VAA structs. ~3.8k lines. New kernel groups (`refharness/src/pslice_encoder.cpp`): 112 `mv_pred`, 2 `md` and 230 `svc_encode_slice` rows. The 230 code whole P frames (1 to 99 macroblocks, 1 to 9 slices, the three complexity modes, background detection and AQ on and off, QP 0 to 51, two MV ranges, five kinds of motion, I and P references) through `WelsCodePSlice` on a hand-built context, and hash the bitstream, the reconstruction and every macroblock's state. **All match**. Of 15 planted bugs, 13 fail rows. The other two move a threshold by one (see the notes). UnitTest: 7857. TestVectors: 528. |
| E5b The P stream through the frame loop | **Done** (26 Nov) | The rest of E5. `ref_list_mgr_svc` for camera content: the `IWelsReferenceStrategy` (`CWelsReference_TemporalLayer`), the list reset, update and build, `WelsMarkPic` with the MMCO and LTR marking, `WelsUpdateRefSyntax` and `PrefetchNextBuffer`. Also `encoder`'s `DecideFrameType`, `encoder_ext`'s `GetMvMvdRange`, and `processing`'s VAA statistics (the five `vaacalcfuncs` C kernels and `CVAACalculation`). ~1.9k lines. `IdrTests.cs`'s `IdrEncoder` became `EncoderDriver.cs`, with the reference strategy, the LTR state, the VAA buffers, the MVD cost table, a hand port of the camera half of `CWelsPreProcess` for one spatial layer, and `WelsEncoderEncodeExt`'s whole frame loop (see the notes). New `vaacalc` kernel group (`refharness/src/kernels_processing.cpp`, 177 rows). New `h264ref stream` mode (`reference/stream.tsv`): 57 configurations, 506 frames (429 P) of eight kinds of moving content. They cover the complexity modes, QP 0 to 51, IDR periods, two to four temporal layers, LTR marking, reference counts, the slice modes, odd and cropped sizes, the deblocking modes, three id strategies and the Main profile. **All match** native byte for byte, frame by frame. Every reconstruction the encoder deblocked equals the C# decoder's picture, and every row frees all it allocates. Of 12 planted bugs, 9 fail rows. The other three can't be reached before E7 or E8 (see the notes). UnitTest: 8034. TestVectors: 586. |
| E6 Rate control | **Done** (26 Nov) | `ratectl` for camera content: the off, quality, bit rate (with and without post skip), buffer based and timestamp modes, the frame skip decisions, the two shifted max bit rate check windows and padding, with `encoder_ext`'s `WritePadding`. Also the camera half of `processing`'s complexity analysis (`CComplexityAnalysis`), which the GOM rate control reads. ~1.9k lines. `EncoderDriver` gains the frame loop's rate control hooks and a hand port of `AnalyzePictureComplexity` (see the notes). New `complexity` kernel group (361 rows; `kernels.tsv` 7281). New `h264ref rc` mode (`reference/rc.tsv`): 94 configurations, 4848 frames (2808 P, 1871 skipped) over the four modes. They run at 16 kbps to 2 Mbps, with timestamp schedules that pause, step back, jump to 0 and jitter. **All match** native byte for byte, frame by frame, with E5b's decoder check. Of 19 planted bugs, 14 fail rows. Four are equivalent and one can't be pinned (see the notes). UnitTest: 8395. TestVectors: 681. |
| E7 Preprocessing | **Done** (27 Nov) | The camera half of `processing`: `CVpFrameWork` with `CheckValid`, scene change detection for video, background detection, adaptive quantisation, the denoiser and the downsampler, with the processing allocator and constants (`memory`, `util`). The camera half of `wels_preprocess`: `CWelsPreProcess` with `CWelsPreProcessVideo` (the scaled pictures, `BuildSpatialPicList`, `SingleLayerPreprocess`, `DownsamplePadding`, `AnalyzeSpatialPic`, `UpdateSpatialPictures`), which replaces `EncoderDriver`'s hand ports. Also `encoder_ext`'s `AddPrefixNal` and `encoder`'s `InitPic`. ~3.9k lines. **The three upstream encoder hashes pass (`672a52fb…`, `08ade185…`, `e60f12e3…`), and so does the denoiser's (`913e49c7…`)**, through the base API and `InitializeExt` (`UpstreamEncoderHashTests`, which needs `OPENH264_RES`). New kernel groups (`refharness/src/kernels_processing.cpp`): 40 `scd`, 30 `bgd`, 93 `aq`, 65 `denoise`, 40 `downsample` and 95 `vpframework` rows (`kernels.tsv` 7644). New `h264ref preprocess` mode (`reference/preprocess.tsv`): 63 configurations, 1469 frames (900 P, 77 IDR with 14 mid stream, 465 skipped). They cover scene change detection, background detection, the denoiser and downsampling (half, quarter, general and odd sizes), alone and together, at fixed QP and in every rate control mode, with temporal layers, intra periods, LTR, slices, prefix NAL units and the base API. **All match** native byte for byte, frame by frame, with the decoder check at the coded size. Adaptive quantisation can't change a stream, as upstream's `ParamValidation` turns it off. Of 20 planted bugs, 18 fail rows, one of them only after two `vpframework` mutations were added. The other two are equivalent (see the notes). UnitTest: 8758. TestVectors: 749. |
| E8 API and wire-up | **Done** (27 Nov) | `welsEncoderExt`'s `CWelsH264SVCEncoder` whole (`Initialize`, `InitializeExt`, `EncodeFrame`, `EncodeParameterSets`, `ForceIntraFrame`, every `SetOption` and `GetOption` case, the statistics), the rest of `encoder_ext` that a single layer, single thread, CAVLC camera encoder runs (`WelsInitEncoderExt` and `WelsUninitEncoderExt`, `RequestMemorySvc` and `FreeMemorySvc`, the frame loop `WelsEncoderEncodeExt`, `WelsEncoderParamAdjust`, `ForceCodingIDR`, the rate and LTR setters) and `ref_list_mgr_svc`'s LTR feedback filters. Configurations the port doesn't code are refused at init. ~2.1k lines ported. `EncoderDriver` is now a thin wrapper over the API, and every earlier table and the four upstream hashes still match through it. New `h264ref api` mode (`reference/api.tsv`): 61 configurations, 994 scripted operations of frames, forced IDRs, parameter sets, re-inits, size changes and every option the camera encoder takes. **All match** native row by row. The three E5b survivors that needed the API now fail rows. `H264Codec` encodes (I420, NV12 and packed RGB through a new BT.601 `RgbToI420`, set up as libwebrtc sets up OpenH264), with 21 new tests of round trips, key frames, sizes, formats, bitrate and frame rate. The CLI's `--encoder h264.net` (whip, loopback, browser-test, video-bench), with a guard that stops the CLI tool package from shipping it. New example `examples/WebRTCExamples/WebRTCGetStartedH264Net`, and `TranscodeLoop` encodes with it. **Chromium decodes the stream** (`browser-test --encoder h264.net`, 640x360 at 30 fps, 440 of 440 frames, and a PLI answered with an IDR). Firefox and Safari aren't checked. About 65 frames a second at 640x480 on one thread. Of 17 planted bugs, 15 fail rows, three of them only after a driver check and two tests were added. The other two are equivalent (see the notes). UnitTest: 8758. TestVectors: 832. CLI UnitTest: 261. |
| P5.1 Benchmarks and profile | **Done** (27 Nov) | New `benchmarks/SIPSorcery.H264/H264Benchmarks`: BenchmarkDotNet encode and decode benchmarks at 640x480, 1280x720 and 1920x1080 on generated camera-like frames, with the memory diagnoser, and a `run` mode that encodes or decodes flat out in one process, for a profiler. The encoder is set up as `H264Codec` sets it up but stamps frames 1/30 s apart, so the rate control skips nothing and the output is the same every run. Baseline with the C kernels: encoding 37.5, 11.5 and 5.3 frames a second, decoding 281, 88 and 38. **The allocation check: after warm-up the encoder and decoder allocate nothing a frame.** The only per-frame allocations are the ones `H264Codec`'s `IVideoEncoder` return types bring (see the notes). The profile (see the notes) set the order of the steps after it. |
| P5.2 SIMD groundwork, SAD and SATD | **Done** (27 Nov) | `common/cpu.cs`: `WelsCPUFeatureDetect` from .NET's instruction set classes, masked by `WelsCpuFeatureMask` (0 gives the C kernels throughout), and `WelsCpuHasSimd128` (SSE2 on x86 and x64, AdvSimd on Arm64). The encoder, the decoder and the preprocessing now call it where upstream does, reporting no core count, as the C-only build. `common/simd.cs` (load, interleave and SAD helpers), `common/sad_common_simd.cs` (the 7 SAD sizes and the 7 four-neighbour variants) and `encoder/sample_simd.cs` (the 7 SATD sizes), all Vector128 with x86 and Arm branches where it has no portable form. `WelsInitSampleSadFunc` and scene change detection choose them in place of upstream's X86_ASM and HAVE_NEON blocks. The Combined3 slots stay NULL, as on upstream's C path. New `sad_common_simd_unittest` and `sample_simd_unittest`: every `sad` and SATD fixture row gives the C build's hash through the SIMD kernel, and each kernel equals the C# C kernel over 1500 random and extreme blocks at random strides. **Every table, hash and test still passes, now through the SIMD kernels where the API is used.** 6 planted bugs each fail rows (see the notes). Encoding is 2.5 to 2.7 times as fast: 97.0, 30.7 and 13.3 frames a second at 480p, 720p and 1080p (`video-bench`: 169, 52 and 27, from E8's 65, 23 and 12), so 720p is real time. UnitTest: 8963. TestVectors: 832. |
| P5.3 SIMD motion compensation | **Done** (27 Nov) | `common/mc_simd.cs`: the three half-pel filters, the twelve quarter-pel positions built from them as the C kernels build them, `PixelAvg`, the 16-wide copy, and chroma at 8 and 4 wide. All Vector128, with x86 and Arm branches where it has no portable form (the saturating packs, `pmaddwd` and the rounded average, now in `common/simd.cs`). `InitMcFunc` chooses them in place of upstream's X86_ASM and HAVE_NEON blocks. They take any width, in strips of 16, 8 and 4 columns, the last overlapping the one before when the width isn't made of those (the encoder's 17 and 9). New `mc_simd_unittest`: every `mc` fixture row gives the C build's hash through the SIMD table, and every slot equals the C# C slot, the samples round the block included, at every width and height its callers use and every fractional position. **Every table, hash and test still passes.** 8 planted bugs each fail rows (see the notes). Encoding is 1.6 times as fast again: 152, 48.9 and 21.3 frames a second at 480p, 720p and 1080p (`video-bench`: 216, 77 and 33). Decoding is 1.65 to 1.85 times as fast: 516, 146 and 66.9 (from 281, 88 and 38; `video-bench` 720p: 119). UnitTest: 9645. TestVectors: 832. |
| P5.4 SIMD deblocking | **Done** (27 Nov) | `common/deblocking_common_simd.cs`: the 12 loop filter edge kernels, luma and chroma, bS < 4 and bS = 4, across horizontal and vertical edges, and the single-plane chroma ones. Branchless: every sample along the edge is worked out in 16-bit lanes and the edge decisions pick between filtered and unfiltered. The vertical edges transpose 16 rows to columns and back, reading and writing only the columns C does. Both `DeblockingInit`s choose them in place of upstream's X86_ASM and HAVE_NEON blocks; the encoder's `DeblockingBSCalc` stays C. `expand_pic` gets no SIMD version: it is 0.5% of the decode, and its copies and fills are already the runtime's vectorised `CopyBlockUnaligned` and `InitBlockUnaligned`. New `deblocking_common_simd_unittest`: every edge filter `deblock` fixture row gives the C build's hash through the SIMD kernel; each kernel equals the C# C kernel over 3000 edges of every kind with random alpha, beta and tc0, the whole plane compared; and each table slot holds its own kernel. **Every table, hash and test still passes.** 11 planted bugs each fail tests (see the notes). Decoding is 1.6 times as fast, back to back with P5.3's build: 924, 291 and 126 frames a second at 480p, 720p and 1080p, from 585, 178 and 78.6 (`video-bench` 720p: 154 and 155 in two runs, from 119 and 124). Encoding is 10 to 14% faster: 175, 56.3 and 24.2, from 157, 49.2 and 22.0 (`video-bench`: 259, 82 and 38, from 222, 77 and 33). UnitTest: 10036. TestVectors: 832. |
| P5.5 SIMD transforms and quantisation | **Done** (27 Nov) | `common/transform_simd.cs`: the 4x4 inverse transform, two blocks at a time, which the encoder and decoder share. `encoder/encode_mb_aux_simd.cs`: the forward DCTs (one block and four), the 4x4 DC Hadamard and the four quantisers, `QuantFour4x4Max` included. `encoder/decode_mb_aux_simd.cs`: the dequantisers, the inverse DC Hadamard and the three reconstructions (one block, four, and the I16x16 DC-only one). `decoder/decode_mb_aux_simd.cs`: `IdctResAddPred` and the four-block version, which keeps the C test that skips a block with nothing to add. `WelsInitEncodingFuncs`, `WelsInitReconstructionFuncs` and the decoder's `InitPredFunc` choose them in place of upstream's X86_ASM and HAVE_NEON blocks. The 8x8 IDCT stays C, as upstream's does. All of them are exact: the notes say where 16 bits are enough and where the IDCT needs 32. `common/simd.cs` gains an unsigned high multiply and an upper 8-byte store. New `encode_mb_aux_simd_unittest`, `enc_decode_mb_aux_simd_unittest` and `decode_mb_aux_simd_unittest`: every fixture row of these kernels gives the C build's hash through the SIMD kernel; each kernel equals the C# C kernel over 3000 trials, with coefficients of every kind (any int16, extremes, typical, sparse), the tables' FF and MF and any others, and predictions of every kind at random strides, the buffers round the block compared; and each table slot holds its own kernel. **Every table, hash and test still passes.** 17 planted bugs each fail tests (see the notes). Encoding is 1.4 times as fast, back to back with P5.4's build: 248, 77.2 to 81.5 and 33.5 frames a second at 480p, 720p and 1080p, from 172, 55.0 to 55.1 and 23.8 (`video-bench`: 391, 130 and 54, from 239, 85 and 38). Decoding is 3 to 8% faster: 941, 309 and 130, from 914, 287 and 124 (`video-bench` 720p: 169 and 173 in two runs, from 153 and 157). UnitTest: 10790. TestVectors: 832. |

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
  decision in section 8. The CLI's `h264.net` option waited for the encoder
  (E8), since the CLI's receive paths already have FFmpeg's decoder. E8 added
  it, decoder included, to `video-bench`.

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

Notes from E3:

- **Structs.** The writer reads the slice, its macroblock cache and the
  macroblock, so E3 ports `SMB` (`svc_enc_macroblock.h`), `SMbCache` and
  `SDCTCoeff` (`mb_cache.h`), `SSlice` and its header structs (`slice.h`),
  `SCabacCtx` (`set_mb_syn_cabac.h`, the struct only), `SSliceCtx` and
  `SDynamicSlicingStack`, and a partial `SDqLayer` (`svc_enc_frame.h`)
  without the picture pointers (E4) and `pFeatureSearchPreparation` (E5).
  `sWelsEncCtx` gains `pCurDqLayer` and `eSliceType`, `SWelsFuncPtrList` the
  five CAVLC slots.
- **CABAC is not ported.** `InitCoeffFunc` and `WelsWriteSliceEndSyn` throw
  `NotSupportedException` for CABAC. The two CABAC stash/pop functions are
  plain copies, so they are ported and have rows of their own.
- **Undeclared upstream functions.** No header declares the four stash/pop
  functions or `CheckBitstreamBuffer` (upstream reaches them through
  `SWelsFuncPtrList`), so `cavlc_encoder.cpp` declares them.
- **Quirks kept.** `uiChromaQpIndexOffset` is `uint8_t`, so an offset of 250
  adds 250 and the chroma QP clips to 51, never subtracts 6. `sDeltaMv` stores
  the 16x16, 16x8 and 8x16 differences as `int16_t` (they wrap), while the
  sub-macroblock differences are `int` subtractions. `BsWriteUE` breaks for
  codes over 32 bits, so the fixtures keep each difference within ±16383. For
  8x8 they keep the predictors and the differences within ±8191, as an `int`
  difference of wrapped vectors would not fit. So the rows can't tell the
  `int16_t` and `int` differences apart there. The
  `vlc_encoder.h` writers are unused upstream (the residual writer has its
  own copies inline). They are ported and tested over every table entry.
  `g_kuiVlcLevelPrefix` is commented out upstream, so it is not ported.
- **Decoder round trip.** Every residual row the writer accepts is read back
  with the D2 parser (`WelsDec.WelsResidualBlockCavlc`). It must consume
  exactly the bits written and give back the coefficients. The parser
  dequantises as it reads, so the test gives it the scaling-list path with a
  flat scale: 16 gives the level back, as (level × 16 + 8) >> 4. For the chroma
  DC, 32 gives it back after the 2x2 transform, which the test then undoes.
  The nC comes from a cache whose left and top neighbours both hold the
  row's nc.
- **Planted bugs.** 29 bugs were planted one at a time:
  - In the residual writer: the prefix 14 and 15 escapes, the overflow
    shift, the suffix length's cap, start and increment, the trailing-ones
    adjustment and sign order, the total-zeros condition, the run-before
    loop, a zero-left map entry, and `CavlcParamCal_c`'s skip of the
    trailing zeros.
  - In the stash functions: the CAVLC pop's QP, and the CABAC copy length and
    bit position.
  - In `vlc_encoder.h`: the chroma DC coefficient-token table.
  - In the macroblock writer: the skip-run reset, the chroma QP offset read
    as signed (on both paths), an entry of each CBP map, the buffer check's
    `<`, an `int` motion vector difference for 16x16, `ue` in place of `te`
    for reference indices, the I16x16 type's chroma CBP, the last QP update,
    and three nC neighbour indices.

  Each plant fails rows. In the first pass, though, the prefix 14 escape and
  the suffix length's start failed only the slice rows. The random residual
  blocks never landed on level code 29 at suffix length 0, nor on exactly 10
  coefficients. Two sweeps were added:
  - A level sweep. For each suffix length 0 to 6, it covers the codes either
    side of each escape and of the overflow, and the levels either side of
    the increment threshold. Each is followed by a 1, which is written with
    the suffix length the level leaves.
  - A count sweep: every total and trailing-ones count, in each nC table and
    the chroma DC's.

  With the sweeps, the residual rows and the decoder round trip fail for both
  plants. `LevelSweep_DecidesTheOverflowAtEverySuffixLength` asserts that the
  sweep keeps reaching both answers.
- **What the rows can't see.** The fixtures set each block's count from its
  own coefficients, as the encoder does. So the residual writer's
  `iCalRunLevelFlag` (count > 0) only ever skips all-zero blocks, and `>= 0`
  would write the same bits.

Notes from E4a:

- **The split.** E4 is two steps. E4a ports the macroblock-level intra coding
  and checks it on hand-built contexts, the same on both sides. E4b brings the
  slice header writers, `WelsCodeOneSlice`, the encoder's deblocking,
  `InitFunctionPointers` and `PreprocessSliceCoding`, and the IDR byte match
  through the real API, with the reconstruction checked against the C#
  decoder.
- **The I-slice context.** `islice_encoder.cpp` (mirrored by
  `IsliceFixtures.cs`) sets what `WelsInitCurrentLayer`, `InitDqLayers`,
  `InitMbInfo`, `InitSliceInLayer` and `PreprocessSliceCoding` would set for
  one spatial layer with rate control off:
  - The function list comes from the individual init functions, as
    `InitFunctionPointers` also builds the parameter set strategy, which is
    E4b's.
  - `SetFastCodingFunc` and `SetNormalCodingFunc` are `static inline`
    upstream, so the fixture copies them.
  - The pictures come from `AllocPicture`. The stride tables come from
    `AllocStrideTables`, plus the encoder offsets that `InitDqLayers` adds.
  - The slice map comes from `InitSliceSegment` and the boundary tables from
    `InitSliceBoundaryInfo`. The neighbours come from `UpdateMbNeighbor`.

  The pictures (padding included), the macroblock arrays, the reference QPs
  and each slice's bitstream buffer start random. The hashes cover them
  whole, so a write anywhere it shouldn't be counts.
- **The frame rows.** They cover:
  - 4 sizes (1, 6, 15 and 99 macroblocks, one of them not a multiple of 16)
    and 3 slice maps (one slice, one per row, and three runs that start
    mid-row).
  - Both coding modes, 6 QPs and 3 kinds of content: noise, a gradient, and
    4x4 ramps.
  - The chroma QP offset as 0, 6, 12 and 250 (the `uint8_t` quirk of E3,
    which `WelsRcMbInitDisable` repeats).

  Both macroblock types win in every class. At QP 0 and 1 the levels of the
  ramps, and of some gradients, overflow the CAVLC escape, so the retry loop
  raises the QP of 434 macroblocks. A small bitstream buffer makes
  `CheckBitstreamBuffer` fail, so the loop raises the QP past 50 and gives up
  (48 slices). `WelsISliceMdEnc_RowsReachEveryPath` asserts both.
- **Undeclared upstream functions.** No header declares `AllocStrideTables`,
  `InitSliceSegment`, `UninitSliceSegment`, `PredIntra4x4Mode` or
  `AnalysisVaaInfoIntra_c`, so the fixture declares them.
- **Quirks kept.**
  - `InitSliceSegment` returns 1 for raster runs with MB counts:
    `AssignMbMapMultipleSlices` fills the map, then falls through to
    `return 1`.
  - `InitSliceInLayer` sizes the boundary tables by `sizeof (int32_t*)`.
  - `WelsGetFirstMbOfSlice` only reads `pFirstMbIdxOfSlice`, and returns -1
    without it. The first fixture omitted the tables, and the loop crashed on
    macroblock -1.
- **Planted bugs.** 27 bugs were planted one at a time in the first pass:
  - In the mode decision: the I4x4 lambda (on both searches), the most
    probable mode, and the I16x16 and chroma mode costs.
  - In the macroblock coding: the I16x16 DC deadzone, the chroma CBP bit,
    and the 8x8 count limit.
  - In the slice coding: a cache non-zero count, the chroma prediction
    buffer, the retry's QP step and limit, the right-top availability, and
    the row slice count.
  - Elsewhere:
    - the chroma QP offset read as signed;
    - the background QP store;
    - the `pfMdCost` choice;
    - the stride tables' temporal count and chroma line alignment;
    - the picture's chroma alignment;
    - the left mode and non-zero count loads into the neighbour cache;
    - the VAA shift, both intra variance thresholds and the inter sign bit;
    - the raster slice map's run loop.

  22 failed rows at once. The other five:
  - **Dead on the C path.** The lambda flip in the full I4x4 search landed
    in the `pfIntra4x4Combined3` branch, and the C build never takes that
    branch, because the kernel is NULL without assembly. A second plant on
    the loop that does run fails 337 rows.
  - **Inter only.** `iSingleCtr8x8 < 7` cannot change an intra macroblock,
    whose count is 0 or `INT_MAX`. Planting the `INT_MAX` instead fails 558
    rows.
  - **Equivalent.** `<=` in `AssignMbMapRaster`'s run loop changes nothing.
    Parameter validation makes the runs add up to the macroblock count, so
    the loop's `iMbIdx < kiTotalMbNum` test stops it anyway.
  - **Thresholds the random rows missed.** These were `>=` for `>` in
    `MdInterAnalysisVaaInfo_c`'s sign bits, and `>` for `>=` at the intra
    variance threshold. Two sets of `md` rows were added:
    - 8 tie rows, with two blocks on the average SAD.
    - 36 threshold rows. Three flat 4x4 blocks raised by (1, 7, 11),
      (0, 7, 11) and (0, 5, 12) put the variance at 149, 150 and 151,
      whatever the base.

    The tie rows now fail on the first plant and the threshold rows on the
    second.

  Some plants fail only one kind of row:
  - The temporal count and the chroma line alignment in `AllocStrideTables`
    fail only its own rows, because the I path reads neither the second
    temporal table nor the chroma decoder offsets.
  - The row slice count fails only the boundary rows, because
    `WelsISliceMdEnc` walks each slice by the map, not by the count.

Notes from E4b:

- **The driver.** The encoder API and its init and frame loops come with E8.
  So `IdrEncoder` (in `IdrTests.cs`) mirrors `InitializeExt`,
  `WelsInitEncoderExt`, `InitFunctionPointers`, `RequestMemorySvc` (with
  `AcquireLayersNals`), `BuildSpatialPicList` and `WelsEncoderEncodeExt` for
  the first frame of one spatial layer, over the ported functions.
  - It does by hand what the unported pieces do for an IDR:
    - `DecideFrameType` (VAA, E5) gives an IDR because of
      `bEncCurFrmAsIdrFlag`.
    - `MarkPic` clears every slice's `sRefMarking`.
    - `BuildRefList` is `WelsResetRefList` with no references.
    - `UpdateRefSyntax` sets the IDR marking flags.
  - The preprocessing is the copy and `Padding` of `WelsMoveMemoryWrapper`
    and `DownsamplePadding`, without the denoiser, the scaling or the scene
    change detection (which an IDR request skips).
  - Anything outside that scope throws `NotSupportedException`: CABAC,
    screen content, several layers, the prefix NAL, rate control, threads,
    `SM_SIZELIMITED_SLICE`, the denoiser, and downsampling the input.
  - `ParameterSetTests` now exposes its settings parser and the
    `InitializeInternal` checks, so both tables apply settings the same way.
- **The buffer sizes matter.** At QP 18 and 20, noise overflows the frame
  buffer: `WelsEncodeNal` wants 1.5 times the payload. Native returns
  `cmMallocMemeError` (3) with a skipped frame. The driver matches only
  because it sizes `pFrameBs`, the slice buffers and the NAL list exactly as
  `RequestMemorySvc` does.
- **What the reconstruction check sees.** One IDR's bytes do not depend on
  its deblocking. Only later P frames do, from E5. So the decoder comparison
  is E4b's only check of the deblocking. The edge kernels in `common/` are
  shared with the decoder and were checked against native in D5, so the
  comparison checks the encoder's own half: the boundary strengths, the
  alpha and beta indexing, the filter idc and the macroblock and slice
  loops. The first version of the comparison used `Array.FindIndex`, whose
  predicate gets the element, not the index. So it compared the wrong bytes,
  and passed when it should not have. The planted deblocking bug below was
  run after the fix.
- **Rows that come out the same.** These are expected, and they keep the
  table honest about what each setting reaches on an IDR:
  - `complexity-medium` is byte equal to `complexity-high`.
  - `deblock-idc2-single-slice`, `strategy-increasing`,
    `strategy-sps-listing` (its NALs, in separate layers), `features-off` and
    `profile-main` (the SPS aside) equal `scene`.
  - `slices-fixed-4-no-load-balancing` equals `slices-fixed-4`. Load
    balancing only sizes the slice buffers, and both sizes are large enough.
  - `deblock-idc0-slices` equals `slices-raster-rows`, because idc 0 is the
    default, so that row adds nothing.
  - The inter half of the deblocking is ported but runs only from E5.
- **Planted bugs.** Each was planted one at a time and restored with `cmp`:
  - The alpha index + 1 in `GET_ALPHA_BETA_FROM_QP` fails 39 rows, all on the
    reconstruction only. The other 9 rows are expected to pass: deblocking
    off, an alpha of 0 either way (QP 0, QP 10, and QP 26 with offset -12),
    a flat picture, one smooth macroblock, and the two overflow rows.
  - Writing `long_term_reference_flag` as 0 fails only `ltr`, whose VCL then
    hashes as `scene`'s.
  - Padding the right-hand U columns with 0x7f fails exactly the four sizes
    whose width is not a multiple of 16.

Notes from E5a:

- **The split.** E5 is two steps, like E4. E5a ports the macroblock-level
  inter coding and checks it on hand-built P contexts, the same on both sides.
  E5b brings `ref_list_mgr_svc` (camera, one reference), `DecideFrameType`
  and the multi-frame stream match through the frame loop, by extending
  `IdrEncoder`.
- **The P-slice context.** `pslice_encoder.cpp` (mirrored by
  `PsliceFixtures.cs`) builds on the I-slice context, which moved to
  `islice_ctx.h` so both files share it:
  - The function list is `InitFunctionPointers`, `WelsRcInitFuncPointers`
    and `PreprocessSliceCoding`, as the encoder builds it.
  - The MVD cost table and MV range are what `RequestMemorySvc` sets, for an
    MV range `GetMvMvdRange` could give. Rows use the camera's 64, or 6 so
    that the search window clamps.
  - The reference is the previous source plus coding noise, padded with
    `ExpandReferencingPicture`. Its macroblock arrays (type, QP, motion
    vector and skip SAD) are drawn.
  - The VAA state that the preprocessing (E7) would leave is computed or
    drawn: the 8x8 SADs of the two sources (what `VAACalcSad` takes), random
    background flags, and AQ offsets in [-6, 6]. The VAA planes are the
    current source and the previous one, which the background skips copy
    into.
  - The current source is the previous one moved, plus noise. The motion is
    none, one shift, a shift blended half a pixel, a shift per 8x8 block, or
    new content.
- **What the rows reach.** `WelsCodePSlice_RowsReachEveryPath` pins rows that
  reach:
  - every inter partition (16x16, 16x8, 8x16, 8x8) and both intra kinds;
  - sub-pel vectors, skips and background skips;
  - AQ QPs, the level overflow retry at QP 0, and the bitstream check giving
    up.

  Across all rows, 125 have sub-pel vectors, 14 have background skips and 71
  have QPs off the slice QP. A background skip is saved as
  `MB_TYPE_BACKGROUND` in the reference types before `WelsMdUpdateBGDInfo`
  makes it a skip, which is how the test counts them.
  - Camera P slices never code sub-8x8 partitions; the base layer stops at
    8x8. So only the `mv_pred` rows check the 4x4, 8x4 and 4x8 updates.
  - MEDIUM and HIGH complexity take the same camera path (the mode only
    switches screen content's scrolling detection), so the rows alternate
    them.
  - The QP 0 rows have AQ off, as the AQ floor (`iMinQp` 6) would lift every
    macroblock off 0.
- **Planted bugs.** Each was planted one at a time and restored from a copy.
  The rows that fail:
  - PredMv's top-only case taking A: 74.
  - PredInter8x16Mv's top-left index: 1, its own row.
  - The candidate rounding in `WelsMotionEstimateInitialPoint`: 42.
  - The diamond search's iteration count: 16.
  - A tie in the half-pel refinement: 3.
  - The inter luma's single-coefficient threshold: 2.
  - The ordinary skip test for background references: 91.
  - The bottom temporal predictor: 80.
  - The VAA chroma copy offset: 13.
  - The AQ QP ceiling: 13.
  - The background skip's chroma "too large" check off: 38.
  - Its invisible-cost threshold times 10: 2.
  - The VAA sign that skips the fine partitions: 33.

  The two that pass move a threshold by one: the chroma "too large" 640 to
  641, and the invisible cost 128 to 129. Random content rarely lands on a
  threshold, and the E5b streams add coverage.
- **Native determinism.** `WelsPSliceMdEnc`'s `SWelsMD sMd` is uninitialised
  in C, and the C# zeroes it. Every field the loop reads is written first.
  Three native runs give the same `kernels.tsv`.

Notes from E5b:

- **The driver.** `IdrTests.cs`'s `IdrEncoder` is now
  `test/SIPSorcery.H264.TestVectors/EncoderDriver.cs`, used by `IdrTests`
  and `StreamTests`. It still mirrors `InitializeExt`, `WelsInitEncoderExt`,
  `RequestMemorySvc` and `WelsEncoderEncodeExt`, now with the whole frame
  loop:
  - `PrepareEncodeFrame`: `DecideFrameType`, the temporal level, and the
    parameter sets on an IDR.
  - `MarkPic`, then `BuildRefList`. When `BuildRefList` fails, the frame is
    coded as an IDR, as upstream does.
  - `AfterBuildRefList`, `WelsUpdateRefSyntax`, the slices and the
    deblocking.
  - `UpdateRefList` for every frame above `NRI_PRI_LOWEST`, and the
    `CORRECTED` retry.

  The library's `InitFunctionPointers` replaces the driver's copy.
  `RequestMemorySvc` also allocates the reference strategy, the LTR state,
  the VAA buffers and the MVD cost table (`GetMvMvdRange`, `MvdCostInit`).
  The release frees them in upstream's order, so every row still frees all
  it allocates. After `ENC_RETURN_MEMALLOCERR` the API uninitialises the
  encoder, and later frames return 3, so the driver does the same.
- **The preprocessing.** `CWelsPreProcess` comes with E7. Until then the
  driver has a hand port of its camera half for one spatial layer:
  - `AllocSpatialPictures`: `2 + max(highest tid, 1)` pictures, plus the
    LTR ones.
  - `BuildSpatialPicList`: the IDR period flag, the source copy and the
    padding.
  - `AnalyzeSpatialPic`: the reference picture from `g_kuiRefTemporalIdx`
    (or the LTR's), then the VAA statistics through `CVAACalculation`.
  - `UpdateSpatialPictures`: the picture swaps, with the LTR swap.

  The driver throws `NotSupportedException` where an analysis feature
  would run: scene change detection on a T0 P frame, and background
  detection and adaptive quantisation on a P frame. It also throws for
  anything that scales. The VAA runs even with every analysis feature off.
  At LOW complexity (the fast mode), its 8x8 SADs feed
  `WelsMdInterFinePartitionVaa`, which skips the fine partitions when all
  four are small. So E5b ports `processing`'s VAA: the five C kernels in
  `vaacalcfuncs` and `CVAACalculation`. The `vaacalc` rows check them,
  including `CVAACalculation`'s dispatch over the SSD, variance and
  background flags and its invalid arguments.
- **The reference strategy.** `IWelsReferenceStrategy` is one native struct
  with a class tag, like E1's `IWelsParametersetStrategy`. Camera content
  is always `CWelsReference_TemporalLayer`, with or without LTR. Screen
  content (`CWelsReference_Screen`,
  `CWelsReference_LosslessWithLtr`) throws until Phase 4. Native forces
  `iLTRRefNum` for camera content, so only `iLtrMarkPeriod` changes an LTR
  stream.
- **The stream table.** `h264ref stream` (`reference/stream.tsv`) has 57
  configurations and 506 frames, 429 of them P. Some rows come out the same,
  as expected:
  - MEDIUM complexity gives the same stream as HIGH (`pan-medium` and
    `pan-high`, `block-medium` and `block-high`). Only screen content's
    scrolling detection tells them apart.
  - `cut` has no IDR at the cut, because scene change detection is off.

  Noise at QP 8 to 20 overflows the frame buffer natively (3, then an
  uninitialised encoder for the rest of the stream), so the noise rows keep
  the default QP and `fade-qp20` covers the low QPs on P frames instead.
  Two native runs give the same table.
- **What the reconstruction check sees.** A P frame's bytes now depend on
  its references' deblocking, so the stream match checks the encoder's
  deblocking too. The decoder comparison checks every frame whose
  reconstruction the encoder deblocked. Upstream deblocks only reference
  frames, so the top temporal layer's frames are skipped unless the filter
  is off: `temporal-3` compares 8 of its 16 frames.
- **Planted bugs.** Each of 12 was planted one at a time and restored from
  a copy. The rows that fail, of 57 streams and 48 IDR rows:
  - LTR marking one frame early (`uiLtrMarkInterval >=`): 3 LTR streams.
  - Keeping a second short-term reference after a T0 frame: 51.
  - A frame referring to a picture one temporal layer up: 5, the streams
    with three or four layers.
  - `DecideFrameType` ignoring the IDR period: 8, the streams with one.
  - The preprocessing's IDR period one frame early: the same 8.
  - The VAA SAD without its absolute value: 24 streams, and 26 of the 177
    `vaacalc` rows.
  - The VAA measured against the current picture: 30.
  - The spatial pictures never moving on: 30.
  - The VAA reference for the fourth frame of an 8-frame GOP: 1. A VAA bug
    only shows where an 8x8 SAD crosses the fine partition threshold.

  The three that pass can't be reached before E7 or E8 (all three fail
  `api.tsv` rows since E8):
  - The LTR index wrap in `HandleLTRMarkFeedback` needs the API's LTR
    marking feedback (E8).
  - `DecideFrameType` resetting `iCodingIndex` on an IDR. The IDR period
    must be a multiple of the GOP size, so a periodic IDR is already at the
    start of a GOP, and every use of the index masks it by the GOP size.
    Only a scene change IDR (E7) can come mid-GOP.
  - `UpdateSpatialPictures` not keeping an LTR's source picture. The VAA
    reads it only when recovering through the LTR from a lost frame, which
    the API's recovery request starts (E8).

Notes from E6:

- **Scope.** Camera content only. Screen content's rate control
  (`WelRcPictureInitScc` and the rest) is not ported, and the frame
  complexity throws for it (Phase 4). Background detection, adaptive
  quantisation and scene change detection stay off (E7). `SetOption`'s bit
  rate and frame rate changes come with the API (E8). `RcJudgeBitrateFpsUpdate`
  is ported and runs every frame, but nothing changes the rates yet. (E8's
  `set-bitrate`, `set-max-bitrate` and `set-frame-rate` rows change them.)
- **What is ported.** `ratectl.cs` has every camera mode:
  - off, quality, bit rate (with and without post skip), buffer based and
    timestamp;
  - the frame skip decisions (`WelsRcCheckFrameStatus`,
    `CheckFrameSkipBasedMaxbr`, `RcVBufferCalculationSkip`);
  - the two shifted max bit rate check windows, and the padding buffer;
  - `WelsRcInitModule`, `WelsRcFreeMemory` and `GetTimestampForRc`.

  `encoder_ext` gains `WritePadding`. `processing/ComplexityAnalysis.cs` is
  the camera half of `CComplexityAnalysis`: the frame SAD, the GOM SAD with
  or without the background, and the GOM variance.
- **C semantics kept.**
  - `RcConvertQStep2Qp` calls the double `log`: UCRT has no float overload,
    so the float argument widens.
  - `WELS_DIV_ROUND` has float divisor overloads.
  - The complexity analysis wraps in uint32.
  - `GetFrameSadExcludeBackground` returns int32, and the frame SAD
    zero-extends into the int64 complexity.
- **The driver.** `EncoderDriver` adds what the frame loop does with rate
  control on:
  - `WelsRcInitModule` after `RequestMemorySvc`. For rate control off it
    now runs `RcInitSequenceParameter` too, as upstream does. The off mode
    reads its QP limits only with adaptive quantisation.
  - The library's `WelsRcCheckFrameStatus` and `GetTimestampForRc` replace
    the driver's, and a skip calls `pfWelsUpdateBufferWhenSkip`.
  - `pfWelsUpdateMaxBrWindowStatus` after `BuildSpatialPicList`.
  - `AnalyzePictureComplexity` after `AfterBuildRefList`: a hand port of
    `CWelsPreProcess`'s, through `CComplexityAnalysis`.
  - `RcTraceFrameBits`, and padding as a non-VCL layer.
  - `ForceCodingIDR` clears `bCheckWindowStatusRefreshFlag`.
  - The last timestamp is set after every frame the API counts as done, as
    `UpdateStatistics` does.

  `WelsRcPostFrameSkipping` always returns false, so the stack back after a
  post skip is not ported. The driver throws if it ever returns true.
- **Upstream behaviours that shaped the table.**
  - Camera rate control resets the QP limits to 12 and 42 when either
    `iMinQp` or `iMaxQp` is 0 (`encoder_ext.cpp:375`). The default
    `iMinQp` is 0, so the nominal 0 to 51 never applies, and set limits are
    clipped to 12 to 51.
  - `bEnableFrameSkip` defaults to on.
  - A max bit rate above the target never decided a skip in the streams
    tried, so all but one of the max bit rate rows set it equal to the
    target.
  - A step back on the nominal clock is corrected to the last timestamp
    plus a frame period. That is the nominal schedule again, so the stream
    doesn't change. The step back row uses a 12 fps clock against a 10 fps
    frame rate instead. Jitter doesn't move the max bit rate decisions in
    bit rate mode, so only the timestamp mode has a jitter row.
  - The buffer based mode lowers the QP by 1 a frame for good
    (`bDeliveryFlag` only changes through `SetOption`). `buffer-mixed-overflow`
    ends in a VLC overflow (3), then an uninitialised encoder, on both sides.
  - A raster slice configuration with one slice becomes a single slice.
- **The table.** `h264ref rc` (`reference/rc.tsv`) has 94 configurations:
  7 quality, 65 bit rate, 3 buffer based and 19 timestamp. They have 4848
  frames: 2808 P, 168 IDR, 1871 skipped, and 93 with filler data. Two native
  runs give the same table. Only two rows write the same stream: the pan
  pauses of 950 ms and 1000 ms, either side of the timestamp mode's 1 s
  limit. The pause falls in a run of skips, but a limit moved by a
  millisecond either way changes one of them. The `ts.*` settings are the
  harness's timestamp schedule, not encoder parameters. `StreamTests` and
  `RcTests` share the C# check (`StreamCheck`, `TimestampSchedule`).
- **Planted bugs.** Each of 19 was planted one at a time and restored from
  a copy, checked by md5. The rows that fail, of 94:
  - A buffer full skip only below the predicted skip count (`<` for `<=`):
    21.
  - No `pfWelsUpdateBufferWhenSkip` after a skip: 45.
  - The max bit rate window skipping only 100 Mbit over: 9.
  - The shifted window's reset one frame late (`>` for `>=`): 1,
    `bitrate-pan-maxbr-64k-long`.
  - The even window's check off: 1. The odd window's: 6.
  - Padding counted in quarter bytes (`>>= 2`): 4, the padding rows.
  - The buffer based mode stepping the QP by 2: 2 of the 3 buffer rows.
  - The IDR bits per pixel bands 10% lower: 2.
  - The IDR QP floor one above the minimum: 1.
  - The timestamp delay limit at 900 ms: 2, the 950 ms pauses. At
    `>= 1000`: 1, `timestamp-pause-1000`.
  - The driver never setting the last timestamp: 5.
  - The GOM SAD in place of the GOM variance for I frames: 2.

  Four are equivalent:
  - The overshoot fix's equal weights branch off: with the weights equal,
    both branches give the remaining bits.
  - The timestamp check without its 0 case: `last >= ts` already covers a
    timestamp of 0 once there is a last one.
  - `RcUpdateIntraComplexity`'s alpha floor: `iAlpha` is never used, as
    upstream.
  - The driver's padding size reset: the size is worked out again for every
    coded frame.

  One isn't pinned: the timestamp mode's IDR target clip at 2/3 of the
  buffer for 3/4. It only differs when the buffer is about 30 to 32% full
  at an IDR at 30 fps, which no row hits.

Notes from E7:

- **Scope.** Camera content, one spatial layer as coded, CAVLC. The video
  processor's camera methods are ported: the VAA (E5b), the complexity
  analysis (E6), scene change detection for video, background detection,
  adaptive quantisation, the denoiser and the downsampler. Screen content's
  methods (the screen scene change detector, the scrolling detection, the
  screen complexity analysis), the image rotation and the `CWelsPreProcessScreen`
  class stay in Phase 4, and `CreatePreProcess` throws for screen content.
  `SingleLayerPreprocess` and `DownsamplePadding` are ported whole, so the
  code for more spatial layers is there, but the driver still throws for
  more than one layer (E8, or Phase 4's simulcast).
- **What is ported.**
  - `processing/WelsFrameWork.cs`: `CVpFrameWork` with `CheckValid`, and
    `WelsCreateVpInterface`/`WelsDestroyVpInterface`. The methods sit by
    value in the struct in place of the `IStrategy*` chain, and the virtual
    dispatch is a switch on the method (`GET_METHOD` masks the type with
    `0xff`, and the index is clamped to the camera methods).
  - `SceneChangeDetection.cs`, `BackgroundDetection.cs`,
    `AdaptiveQuantization.cs` (with `SampleVariance16x16_c`), `denoise.cs`
    (the bilateral luma, weighted average chroma and Gaussian edge filters)
    and `downsample.cs` (the dyadic, quarter, one-third and general
    bilinear kernels, and `CDownsampling` with its two half-size buffers).
  - `memory.cs` and `util.cs`: the processing library's zero-filled,
    16-byte aligned allocator, which is the common one, and its constants.
  - `wels_preprocess.cs`: `CWelsPreProcess` with the `CWelsPreProcessVideo`
    virtuals, the scaled picture set-up (`JudgeNeedOfScaling`,
    `WelsInitScaledPic`), `BuildSpatialPicList`, `SingleLayerPreprocess`,
    `DownsamplePadding`, `AnalyzeSpatialPic`, `AnalyzePictureComplexity`,
    `UpdateSpatialPictures`, `UpdateSrcList`, `WelsMoveMemoryWrapper` and
    `Padding`. `sWelsEncCtx` gains `pVpp`.
  - `encoder_ext`'s `AddPrefixNal` and `encoder`'s `InitPic` (which nothing
    upstream calls).
- **C semantics kept.**
  - The scene change thresholds are `float * int + 0.5f` in float, then
    `+ PESN` in double, then truncated.
  - The general bilinear downsamplers work out their scale as a float
    product rounded in double, and the fast one does its arithmetic in
    uint32, as C does with its unsigned operands.
  - `DownsampleHalfAverage`'s stride asserts are `Debug.Assert`s, so the
    Debug runs check them.
- **The driver.** `EncoderDriver` now calls the library's `CWelsPreProcess`
  through `pCtx->pVpp` (`WelsPreprocessCreate`, `AllocSpatialPictures`,
  `BuildSpatialPicList`, `AnalyzeSpatialPic`, `AnalyzePictureComplexity`,
  `UpdateSpatialPictures`, `WelsPreprocessDestroy`) in place of E5b's and
  E6's hand ports, several hundred lines fewer. It gains `Initialize` for the
  base API, and the prefix NAL unit before each slice when
  `bNeedPrefixNalFlag` is set (`!bSimulcastAVC && bAvcBased &&
  (bPrefixNalAddingCtrl || iSpatialLayerNum > 1)`). The stream check
  compares reconstructions at the coded size, which is smaller than the
  source when the preprocessing downsamples.
- **The upstream hashes.** `UpstreamEncoderHashTests` encodes the four rows
  of upstream's `encoder_test.cpp` table for camera content, a single slice,
  one layer and CAVLC as `BaseEncoderTest` does: every frame of the file at
  timestamp 0, hashing every frame that is not a skip. The three without the
  denoiser go through `Initialize` (`SEncParamBase`: the quality mode with
  the default scene change detection, background detection and adaptive
  quantisation), the denoiser's through `InitializeExt`. All four pass. The
  YUV files are not committed, so the rows run only when `OPENH264_RES`
  points at openh264's `res/`.
- **Upstream behaviours that shaped the table.**
  - `ParamValidation` turns adaptive quantisation off for every
    configuration ("algorithms needs to be refactored",
    `encoder_ext.cpp:301`), so the encoder never runs it. Only the `aq`
    kernel rows reach `CAdaptiveQuantization`, and the AQ settings in the
    table change nothing.
  - Scene change detection runs only on frames at the start of a GOP that
    aren't IDRs already, against the previous source picture (or the LTR's
    after a lost T0 frame). `DecideFrameType` ignores what it finds for the
    first 16 frames after an IDR (`VGOP_SIZE << 1`). In `cuts` the
    scrolling between cuts already measures as a large change, so the IDR
    comes at frame 17, the first frame allowed, and the cuts at frames 6,
    12 and 18 are coded as P frames. Only a large change
    makes an IDR. A medium change (`partcut`, the top 96 rows) is detected
    but changes nothing for camera content. Content that moves slowly
    (`block`, `camera`) never reaches the thresholds.
  - Background detection changes no stream of exactly static content, so
    the table has `camera`:
    a moving block over a background with fresh noise of up to 2 each way.
  - The denoiser runs on the source before the downsampling.
- **The table.** `h264ref preprocess` (`reference/preprocess.tsv`) has 63
  configurations and 1469 frames: 900 P, 77 IDR, 465 skipped, and 27
  uncoded after a frame buffer overflow uninitialises the encoder in two
  rows. 14 of the IDRs come mid stream, from scene changes or intra
  periods. No two configurations write the same stream: rows that came out
  the same as another (AQ alone, BGD on static content, SCD on slow
  content) were replaced. Two native runs give the same table.
  `PreprocessTests` reads it through the shared `StreamCheck`. The six
  kernel groups (363 rows) check each method on its own, including
  `CVpFrameWork`'s argument checks (`vpframework`, up to 24 mutations of
  the pixel maps for each of four methods).
- **Planted bugs.** Each of 20 was planted one at a time and restored from
  a copy, checked by md5. The rows that fail, of 363 kernel rows, 63
  preprocessed streams, the 4 upstream hashes and the E4b to E6 tables:
  - The large scene change threshold at `>` for `>=`: 8 `scd` rows. The
    medium one: 8. A high motion block at `>=` for `>`: 4. No stream lands
    exactly on a threshold.
  - Background detection's SAD bound at `<=` for `<`: 1 stream, `pp-fade`.
  - Its U edge check reading the current V plane: 10 `bgd` rows, 7
    streams and 3 of the upstream hashes (not `Static_152_100`'s).
  - Background detection run against an I frame reference: 39 streams and
    all 4 upstream hashes.
  - The denoiser's grey weight from 31 for 32, its chroma average rounded,
    and its luma line ends one block early: 28 to 34 kernel rows each, the
    8 denoised streams and the denoiser's upstream hash.
  - The half average downsampler without its last rounding: 15 rows and
    the 8 streams that halve. The fast general downsampler without its
    rounding: 10 rows and the 4 streams with a general ratio.
  - `CheckValid` letting a zero width through (`< 0` for `<= 0`): 3
    `vpframework` rows, after mutations 22 and 23 were added. Before them
    it passed, as a zero width with a left of 0 fails the left check too.
  - Adaptive quantisation's bit rate texture weight 8760 for 8750: 11 `aq`
    rows. No stream, as upstream turns AQ off.
  - Scene change detection on every frame, not only at a GOP start: 1,
    `cuts-temporal-3`. Against the picture before the previous one: 11.
  - The IDR period flag one frame late: 8 streams of the stream table, 9
    of the rate control table and 2 preprocessed ones. The preprocessing
    is now the library's for every table.
  - Denoising after the downsampling: 1, `scale-denoise`. Odd shrink sizes
    left odd for the padding: 1, `scale-odd`.

  Two are equivalent:
  - `BGD_DELTA_QP_THD` from 3 to 2: it is defined but never used, as
    upstream.
  - `AddPrefixNal` passing false for the IDR flag: `WelsWriteSVCPrefixNal`
    never reads it, as upstream.

  E5b's survivor, `DecideFrameType` not resetting `iCodingIndex` on an
  IDR, still passes with the reset taken out of the scene change branch.
  Scene change detection runs only at the start of a GOP, so its IDRs land
  where the index is already a multiple of the GOP size. Only
  `ForceIntraFrame` (E8) can put an IDR mid-GOP. (Since E8 it fails three
  `api.tsv` rows.)

Notes from E8:

- **Scope.** The API and the frame loop for one spatial layer, one thread,
  CAVLC and camera content, which ends Phase 3. `PortUnsupportedConfiguration`
  refuses at init, with an error in the log and `cmInitParaError`, what the
  port doesn't code: screen content, CABAC, more than one spatial layer or
  simulcast, more than one thread, and `SM_SIZELIMITED_SLICE`. The slice
  thread and dynamic slice branches that a refused configuration would reach
  throw `NotSupportedException`.
- **What is ported.**
  - `welsEncoderExt.cs`: `CWelsH264SVCEncoder` whole, as a native struct like
    E7's classes: `GetDefaultParams`, `Initialize`, `InitializeExt`,
    `Uninitialize`, `EncodeFrame`, `EncodeParameterSets`, `ForceIntraFrame`,
    every `SetOption` and `GetOption` case, `UpdateStatistics` and
    `LogStatistics`, with `WelsCreateSVCEncoder` and `WelsDestroySVCEncoder`.
    The trace callback is a `delegate*<void*, int, string, void>`. One port
    addition, `EncContext`, which the tests read.
  - `encoder_ext`: the frame rate, bit rate and LTR setters,
    `AcquireLayersNals`, `RequestMemorySvc` and `FreeMemorySvc`,
    `WelsInitEncoderExt` and `WelsUninitEncoderExt`, `ForceCodingIDR`,
    `StackBackEncoderStatus`, `ClearFrameBsInfo`, the frame loop
    `WelsEncoderEncodeExt` and `WelsEncoderParamAdjust`. About 2k lines.
  - `ref_list_mgr_svc`: `FilterLTRRecoveryRequest` and
    `FilterLTRMarkingFeedback`.
- **The driver.** `EncoderDriver` is now a thin wrapper over
  `CWelsH264SVCEncoder`, and E5b to E7's hand driven init, frame loop and
  release are gone. Every table (`idr`, `stream`, `rc`, `preprocess`,
  `paramsets`) and the four upstream hashes go through the API, and all
  still match byte for byte. The driver reads the memory left after
  `Uninitialize` from `FreeMemorySvc`'s log line, and checks that each
  frame's `iFrameSizeInBytes` is the size of its NAL units (added for a
  planted bug, below).
- **The table.** `h264ref api` (`reference/api.tsv`) has 61 configurations
  and 994 scripted operations: frames (in I420 and other formats, and at
  other sizes), forced IDRs, `EncodeParameterSets`, `Uninitialize` and
  re-init, and `SetOption`/`GetOption` of every option the camera encoder
  takes. The options are the IDR interval, the bit rates and max bit rates,
  the frame rate, the rate control mode, frame skip, padding, LTR with
  marking feedback and recovery requests, the id strategies, profile and
  level, the reference count, the complexity, the statistics, and base and
  extended parameter changes with and without a reset, plus bad arguments
  and an allocation failure. **All 61 match** native row by row: the return
  code, the frame type, the temporal ids, every NAL unit's hash, and the
  stream. Two native runs give the same table. `ApiTests` runs each script,
  decodes the stream with the port's decoder and checks that the release
  frees everything. Two streams (`parameter-sets`, `set-number-ref`) don't
  decode cleanly, in the port's decoder and in native `h264ref hash` alike,
  so the test expects it.
- **Upstream behaviours that shaped the table.**
  - `uiIdrPicId` is 1 after the first IDR, so LTR feedback and recovery
    requests have to give `uiIDRPicId=1` to count.
  - A recovery request makes an IDR only with LTR off, or with
    `LTR_RECOVERY_REQUEST` and `iLastCorrectFrameNum == -1`.
  - `SetOption(ENCODER_OPTION_IDR_INTERVAL)` doesn't check the value against
    the GOP size, so periodic IDRs can come mid-GOP. A forced IDR restarts
    the interval.
  - `ENC_RETURN_CORRECTED` (`cmUnknownReason`, an IDR frame type and no
    output) is reached by `ltr-recovery`'s op 22.
  - `WelsRcPostFrameSkipping` always returns false, so
    `StackBackEncoderStatus` never runs.
- **`H264Codec`'s encoder.** Set up as libwebrtc sets up OpenH264: camera
  content, `RC_BITRATE_MODE` with frame skip, no max bit rate, one slice,
  one thread, one temporal layer, and `SPS_LISTING` in place of the default
  `INCREASING_ID`, so every IDR carries the same parameter sets, ids
  included. The encoder is created on the first frame, and again, from an
  IDR, when the size changes. I420 goes in from the caller's planes and
  strides, NV12 has its chroma split, and BGRA, RGBA, BGR and RGB go through
  the new `RgbToI420` (libyuv's BT.601 limited range weights, chroma from the
  2x2 average), not AV1's `I420Converter`, which is internal to its own
  package. Timestamps count in ms from the first frame. Counted from the
  process start, they made the encoder log an input frame rate of 6.99. With no
  `TargetBitrateKbps`, the bitrate is 0.1 bits a pixel per frame, at least
  100 kbps, and follows the frame rate. Bitrate, frame rate and key frame
  interval changes after the first frame go through `SetOption`, and
  `ForceKeyFrame` through `ForceIntraFrame`. The output is the access unit,
  every layer's NAL units in order. The SDP stays `42e01f`, as for FFmpeg's
  encoder, and the SPS signals the lowest level that fits.
  `H264EncodeTests` (21 cases) decodes the output with the port's decoder,
  directly and through RTP packets (STAP-A and FU-A, as SIPSorcery sends
  them) and `H264Depacketiser`. It checks
  the key frames at the interval, when forced, after a size change and
  after an interval change, the parameter sets at each IDR, the odd size
  crop, the same stream from every pixel format and method, the libyuv
  colours, and the bitrate held through bitrate and frame rate changes.
- **The CLI.** `--encoder h264.net` for `webrtc whip` and `loopback`
  (`LibraryVideoPublisher`), a new `--encoder ffmpeg|h264.net` for
  `browser-test`'s echo mode, whose send loop now answers PLIs and FIRs with
  a key frame (SIPSorcery doesn't on its own), and `video-bench`'s encode
  stage, decoder
  and clip encoder. The CLI now references SIPSorcery.H264, which isn't
  packable, so a `RefuseToPackH264Net` target fails `dotnet pack` of the CLI
  tool unless `AllowH264NetInToolPackage=true`. That holds until the patent
  decision in section 8. `nuget.yml` packs the CLI on `v*` tags, so the next
  release tag needs that decision, the property or the reference taken out.
- **The example.** `examples/WebRTCExamples/WebRTCGetStartedH264Net`, from
  `WebRTCGetStarted`: a test pattern to a browser through `H264Codec`, with
  PLIs and FIRs answered with a key frame. In `SIPSorcery.slnx`, with a
  `.slnf`. Later folded, with `WebRTCGetStartedFFmpeg` and
  `WebRTCGetStartedLibvpx`, into the ASP.NET example `WebRTCGetStartedCodecs`,
  where h264.net is one of the encoders its page offers.
- **Interop.** In the desktop app's built-in Chromium, `webrtc browser-test
  --encoder h264.net` echoes 640x360 at 30 fps back. One run: 590 of 590
  frames received, 574 decoded, 16 dropped at the start, one PLI answered
  with an IDR, no freezes and no loss. Another: 440 of 440 decoded, none
  dropped, no PLI. libx264 through the same path: 590 of 590. FFmpeg's
  decoder decodes every frame of an `h264.net` clip (`video-bench`).
  `benchmarks/TranscodeLoop` now takes `--encoder h264.net` too. With VP8 in
  and H.264 out at 640x480 for 20 s, its WebView2 page decoded 570 frames
  with no freezes, drops, loss or PLIs, and the verdict passed. The median
  encode took 4.7 ms.
  Firefox and Safari aren't on this machine, so they aren't checked.
- **Speed.** `video-bench`, one thread, the C paths: encoding at 65 frames a
  second at 480p, 23 at 720p and 12 at 1080p; decoding 720p at 117. Real
  time up to about 480p. More needs Phase 5.
- **Planted bugs.** Each was planted one at a time and restored from a
  copy, checked by md5. Before `H264Codec`, the three E5b survivors that
  needed the API:
  - `DecideFrameType` not resetting `iCodingIndex` on an IDR: 3 rows,
    `set-idr-request-temporal-3`, `set-ltr-idr-request-temporal-3` and
    `set-idr-interval-temporal-3`.
  - The LTR index wrap in `HandleLTRMarkFeedback`: 2, `ltr-recovery` and
    `ltr-recovery-preprocess`.
  - `UpdateSpatialPictures` not keeping an LTR's source picture: 4
    `ltr-recovery` rows.

  Then 17 in E8's code. The rows that fail, of 61 API scripts, the E4b to
  E7 tables, the upstream hashes and `H264EncodeTests`:
  - `ForceCodingIDR` not flagging the IDR: 22 scripts and 2 encode tests.
  - `WelsEncoderParamAdjust` not carrying `uiMaxIdrPicId`: 8.
  - `SetOption`'s frame rate without `WelsEncoderApplyFrameRate`: 1.
  - The IDR interval set one more: 2. Read back one more: 1.
  - The target bit rate halved: 1 script and the bitrate test.
  - No `UpdateStatistics`: 5 scripts and 5 `rc` rows.
  - `ForceCodingIDR` not clearing `bCheckWindowStatusRefreshFlag`: 1.
  - `iFrameSizeInBytes` left at 0: 324 rows of every table, after the
    driver's check was added. Before it, nothing read the field.
  - `H264Codec` copying only the first layer: 10 encode tests.
  - `H264Codec` not passing on a bitrate change: the bitrate test. A frame
    rate or key frame interval change: no test, until
    `Encode_FrameRateChangeSharesTheBitrateOutAgain` and
    `Encode_KeyFrameIntervalChangeAppliesFromTheNextFrame` were added. The
    automatic bitrate not following the frame rate: the first of them.
  - `RgbToI420`'s green weight 128 for 129: 1 of the 5 libyuv rows, white.

  Two are equivalent:
  - `ForceCodingIDR` not resetting `iFrameNum` (or `iPOC`):
    `InitFrameCoding` resets both again for every IDR.
  - `StackBackEncoderStatus` not winding `uiIdrPicId` back: it never runs,
    as upstream.

Notes from P5.1:

- **The content.** `TestFrames` generates the frames: value-noise textures,
  a background panning 1.37 by 0.61 pixels a frame, a block a quarter of the
  frame moving -3.3 by 1.7, and noise of +-2 levels. 32 frames, played
  forwards then backwards. It is harder to code than `video-bench`'s
  pattern, which moves 8 whole pixels a frame, so the frame rates are lower
  than that tool's. `H264Codec`'s automatic bitrate gives 0.92, 2.8 and 6.2
  Mbps.
- **The timestamps.** `H264Codec` stamps each frame with its arrival time.
  Encoding flat out, faster than real time, that looks to the rate control
  like a higher frame rate, and it skips frames. `BenchEncoder` drives
  `CWelsH264SVCEncoder` with `H264Codec`'s parameters and stamps frame n at
  n / 30 s. No frame is skipped, and the output is the same every run.
  `video-bench` uses `H264Codec`, so its decode clip depends on how fast the
  encoder ran: after P5.2 its 720p decode reads 88 frames a second, against
  117 at E8, while `H264Benchmarks`' fixed clip decodes at the same speed
  with and without the SIMD kernels.
- **Allocations.** Over a 10 s run after a second of warm-up,
  `GC.GetAllocatedBytesForCurrentThread` grows by 40 bytes, the run loop's
  own, at every size, encoding and decoding. BenchmarkDotNet's memory
  diagnoser shows nothing for `EncodeFrame` and `DecodeFrame`. The
  allocation pass is done, for the codec. What `H264Codec` allocates a frame
  is what the `IVideoEncoder` return types bring. `EncodeVideo` returns a new
  array; the `IBufferWriter` overload allocates nothing. `DecodeVideo`
  returns a new picture. `DecodeVideoFaster` costs 168 bytes: the
  `List<RawImage>`, the `RawImage` and the enumerator a `foreach` over the
  `IEnumerable` boxes. They are left as they are; only an API change would
  remove them.
- **The profile.** EventPipe's sample profiler (the environment variables in
  the benchmark README), over 10 s of `run`. The share of the samples whose
  stack is in the codec, by the method on top. Encoding 1280x720 with the C
  kernels:

  | Kernel | Share |
  |---|---|
  | `WelsSampleSatd4x4_c`, nearly all from `WelsSampleSatd16x16_c` in `MeRefineFracPixel` | 25.2% |
  | `WelsSampleSad8x8_c`, from the 16x16 and four-neighbour SADs of the motion search, and from scene change detection | 25.0% |
  | `McHorVer22_c` | 6.8% |
  | `VAACalcSadBgd_c` | 3.8% |
  | `WelsDctT4_c` | 3.1% |
  | `WelsQuantFour4x4Max_c` | 3.0% |
  | the other luma MC positions, `PixelAvg_c` and `McChroma_c` | 8.0% |
  | `WelsIDctT4Rec_c` | 1.9% |
  | the luma deblocking edges | 2.1% |

  Decoding 1280x720:

  | Kernel | Share |
  |---|---|
  | `McHorVer22_c` | 28.8% |
  | the other luma MC positions and `McChroma_c` | 22.5% |
  | `DeblockLumaLt4V_c` and `DeblockLumaLt4H_c` | 17.5% |
  | the chroma deblocking edges | 4.8% |
  | `DeblockingBsMarginalMBAvcbase` | 2.6% |
  | `WelsResidualBlockCavlc` | 3.3% |
  | `IdctResAddPred_c` and `IdctFourResAddPred_` | 3.8% |

Notes from P5.2:

- **The flags.** Upstream's flags, from `cpu_core.h`: SSE2 and up from
  `Sse2.IsSupported` and the rest, or ARMv7, VFPv3 and NEON from
  `AdvSimd.Arm64.IsSupported`. The Arm bits reuse the x86 ones, as upstream
  builds for one or the other, so `WelsCpuHasSimd128` tests whichever the
  machine has. Nothing reads the flags but the function table set-ups, and no
  cache line flag is reported, so `iCacheLineSize` stays 16 as before. Only
  the API reaches `WelsCPUFeatureDetect`. The fixture tests that build
  function tables by hand pass 0, so they keep testing the C kernels, and the
  API tables (`stream`, `rc`, `preprocess`, `api`, the upstream hashes) now
  run through the SIMD ones.
- **The SATD kernels.** Four rows of 8 samples, two 4x4 blocks side by side,
  go through at a time: the butterfly down the columns lane by lane, a
  transpose by three rounds of interleaves, then the butterfly across the
  rows lane by lane. The 2D transform is linear and exact in 16 bits, so its
  coefficients don't depend on the order. Every coefficient of a block is a
  +-1 sum of all 16 of its differences, so all 16 share that sum's parity and
  their absolute sum is even. So upstream's `(sum + 1) >> 1` for each block
  is the halving of the total, done once at the end.
- **The SAD kernels.** `psadbw` on x86, `uabd` with `uadalp` on Arm. The
  16-wide four-neighbour kernels share the source rows and the up and down
  reference rows between the four sums. Each kernel reads only the samples
  its C kernel reads.
- **The Combined3 slots** stay NULL. Upstream's SSE2, SSSE3 and SSE4.1 builds
  fill some of them, which takes the intra mode decision down a different
  path, and the port is checked against the C path.
- **Not in the tables.** Scene change detection keeps its own 8x8 SAD
  pointer. After the table was switched it was still 5.8% of the 720p
  encode, so it takes the SIMD kernel too, where upstream takes
  `WelsSampleSad8x8_sse21`.
- **Planted bugs.** Each was planted one at a time and restored from a
  copy, checked by md5. The unit and test vector tests that fail:
  - SATD's transpose taking column 3 from the wrong half: 91 and 264.
  - SATD 16x16's right half one column left: 19 and 268.
  - SAD 16xN's odd rows against the even reference rows: 10 and 309.
  - The 16-wide four-neighbour SAD swapping up and down: 10 and 260.
  - The 8x8 SAD slot given the 8x4 kernel: 1 (the table test) and 307.
  - Scene change detection given the 8x4 SAD: 1 and 6.
- **The profile after**, encoding 1280x720: `McHorVer22_c` 16.3%,
  `VAACalcSadBgd_c` 10.2%, `WelsDctT4_c` 7.9%, `WelsQuantFour4x4Max_c`
  6.9%, `PixelAvg_c` 4.9%, `WelsIDctT4Rec_c` 4.8%, `McHorVer02_c` 4.5%,
  the luma deblocking edges 5.2%, `McHorVer20_c` 3.4%,
  `WelsSampleSatd16x16_simd` 3.3%, `McChroma_c` 2.7%,
  `WelsI16x16LumaPredPlane_c` 1.9%, `WelsSampleSad16x16_simd` 1.4%. MC is
  now about a third of the encode, and the transforms and quantisation about
  a fifth. Runs on this machine vary by about 10% from one to the next.

Notes from P5.3:

- **The arithmetic.** The 6-tap filter of 8-bit samples lies in
  -2550..10710, so 16-bit lanes hold it exactly, and `(x + 16) >> 5` with a
  saturating pack to bytes is `WelsClip1`'s. The centre position keeps
  `McHorVer22_c`'s order: the column filter kept at 16 bits, as its
  `int16_t iTmp` is, then the row filter of those in 32 bits. On x86 that is
  `pmaddwd`: `a05 - 5 a14` from the pair sums interleaved, and `20 a23 + 512`
  from `a23` interleaved with 512. `(x + 512) >> 10` lies in -210..464, so
  the 32-to-16 and 16-to-8 saturating packs give `WelsClip1`'s result.
  Chroma's four weights add up to 64, so its sums fit 16 bits (16352 at
  most).
- **The layout.** The row filter loads the six shifted runs of 16, 8 or 4
  samples directly, so it widens six vectors for 8 outputs and shuffles
  nothing else. The column filter goes down 8-column strips with its six rows
  widened in registers, one new load a row; a 16-column strip would need
  twelve registers for the rows. The 4-wide strips put two rows in a vector.
  The centre runs the column pass over the whole block into a 24 x 17 stack
  buffer before the row pass, so the row pass's unaligned loads read stores
  made well before them (the reason, not measured against the alternative).
- **Widths.** The encoder's sub-pel refinement asks for 17 and 9 columns and
  17 and 9 rows. The last strip overlaps the one before it and writes the
  same values again. That is safe because no filter can run in place, nor
  can the C kernels. `PixelAvg_c` can, so its leftover columns go one by
  one. Blocks under 4 wide, the centre past `McHorVer22_c`'s 17 columns or
  past 17 rows, and 2-wide chroma go to the C kernels.
- **Reads.** Each kernel reads only the samples its C kernel reads. Under 8
  wide, the centre's row pass works out lanes past the width from the rest
  of its own buffer, and doesn't store them.
- **Planted bugs.** Each was planted one at a time and restored from a
  copy, checked by md5. The unit and test vector tests that fail:
  - `Round5` adding 15: 124 and 346.
  - The centre rounding with 511: 3 and 170. It differs only where the sum
    is 512 more than a multiple of 1024.
  - The last 8-column strip one column late: 27 and none. It writes an 18th
    column into the encoder's refinement buffer, which nothing reads, and
    the decoder never asks for a width that needs it. The unit tests see the
    stray column in the samples round the block.
  - The centre under 8 wide reading one column early: 24 and 83.
  - 4-wide chroma carrying the wrong row down: 127 and 306.
  - The 4-wide column filter carrying the wrong row down: 32 and 74.
  - The 8-wide average ignoring its second source: 41 and 295.
  - The quarter-pel table swapping (1,2) and (1,3): 15 and 269.
- **The profile after**, 1280x720. Encoding: `VAACalcSadBgd_c` 15.4%,
  `WelsDctT4_c` 11.6%, `WelsQuantFour4x4Max_c` 10.3%, `WelsIDctT4Rec_c`
  7.2%, the luma deblocking edges 8.2%, `WelsSampleSatd16x16_simd` 4.1%,
  `WelsI16x16LumaPredPlane_c` 3.2%, the chroma deblocking edges 2.5%, and MC
  about 7% (the centre 3.8%, `McChroma_simd` 1.2%, the column filter 1.1%),
  from about a third. Decoding: `DeblockLumaLt4V_c` 15.9%,
  `DeblockLumaLt4H_c` 15.0%, the chroma deblocking edges 8.8%,
  `WelsResidualBlockCavlc` 6.2%, `IdctResAddPred_c` and
  `IdctFourResAddPred_` 6.2%, `DeblockingBsMarginalMBAvcbase` 5.1%, and MC
  15.4% (`GetInterPred` with what it calls), from about half. Deblocking is
  now nearly half the decode, so P5.4 comes next, as planned. In the encode,
  `VAACalcSadBgd_c`, which is P5.6, is now bigger than any other kernel, and
  the transforms and quantisation (P5.5) are about 30%.
- Under the profiler the 720p decode read 168 frames a second, against 146
  without it, so runs here can differ by 15%.

Notes from P5.4:

- **The arithmetic.** Each of p3..q3 is widened to 16-bit lanes, 8 samples a
  vector, two vectors for the 16 samples along a luma edge. Every sum the C
  kernels form is exact there. The bS < 4 delta, `((q0 - p0) * 4 + p1 - q1 +
  4) >> 3`, lies in -160..159 before its clip to tc. The saturating pack back
  to bytes clips as `WelsClip1` does. The one result C writes without
  `WelsClip1`, p1 (and q1) plus its clipped delta, is already in 0..255: the
  delta takes p1 to the average of p2 and `(p0 + q0 + 1) >> 1` at most.
- **Branchless.** The C kernels decide sample by sample whether to filter
  (`|p0 - q0| < alpha`, `|p1 - p0| < beta`, `|q1 - q0| < beta`, tc0 at least
  0 for luma and over 0 for chroma), whether p1 and q1 move (`|p2 - p0| <
  beta`), and at bS = 4 whether it's the strong filter (`|p0 - q0| <
  (alpha >> 2) + 2`). Here each decision is a mask, and the masks select
  between the filtered and unfiltered values. Where C leaves a sample alone,
  the same value is written back.
- **tc0.** Each of the four tc0 values covers 4 luma samples, or 2 Cb and 2 Cr
  samples. Chroma has Cb in lanes 0-7 and Cr in lanes 8-15, which share tc0.
  The single-plane chroma kernels (the decoder's `...V2`, `...H2`) run the
  two-plane kernels with the plane given twice. Both halves of each vector work
  out the same results, so the second store repeats the first.
- **The layout.** The V kernels filter across a horizontal edge, so each of
  p3..q3 is one row, loaded as it is. The H kernels filter across a vertical
  edge. They load 16 rows of 8 bytes, transpose them into 8 columns of 16
  with byte, word, dword and qword interleaves, filter as V does, and
  transpose back. They read and write only the columns C may:
  - luma bS < 4 reads -3..2 and writes -2..1.
  - luma bS = 4 reads -4..3 and writes -3..2.
  - chroma reads -2..1 and writes -1..0.
- **`expand_pic`.** `ExpandPictureLuma_c` and `ExpandPictureChroma_c` take
  0.5% of the 720p decode. Their row copies and edge fills are
  `Unsafe.CopyBlockUnaligned` and `InitBlockUnaligned`, which the runtime
  already vectorises, so there is no SIMD version.
- **Not ported.** The encoder's `DeblockingBSCalc` (upstream has SSE2 and NEON)
  is 0.9% of the encode. The decoder's `DeblockingBsMarginalMBAvcbase` is C
  upstream too, and is now 5.8% of the decode.
- **Planted bugs.** Each was planted one at a time and restored from a copy,
  checked by md5. The unit and test vector tests that fail:
  - Luma bS < 4 not raising tc for q1: 53 and 336.
  - Luma bS < 4 filtering where tc0 is -1: 35 and 318.
  - Chroma tc0 not spread two samples each: 147 and 367.
  - Luma bS = 4's strong limit one low: 6 and 286.
  - Luma bS = 4's p2 missing a p2 term: 34 and 363.
  - The 16 x 8 transpose swapping rows 2 and 3: 128 and 374.
  - Luma bS < 4 H writing rows 8-11 to rows 4-7: 43 and 382.
  - Luma bS = 4 H storing the wrong q1 and q2: 22 and 369.
  - Chroma H writing Cb's results to Cr: 65 and 365.
  - The encoder's table giving `LT4Hor` the V kernel: 1 and 313. It passed
    the unit tests at first: the table test only checked that the SIMD
    slots differ from the C ones. It now checks that each slot holds its own
    kernel.
  - The decoder's table giving `EQ4Hor2` the V2 kernel: 1 and 1. Only one
    vector reaches that slot, so here the table test is the check.
- **The profile after**, 1280x720. Decoding:
  - `WelsDeblockingFilterSlice` and all it calls: 21.7% of the samples,
    from 44.3%. Both counts include the 14% that encodes the benchmark's
    clip, so deblocking was about half the decode and is now a quarter.
  - The rest: `WelsResidualBlockCavlc` 8.3%, `IdctResAddPred_c` 6.2%
    (`IdctFourResAddPred_` another 1.9%), `McHorVer22_simd` 5.9%,
    `DeblockingBsMarginalMBAvcbase` 5.8%,
    `WelsActualDecodeMbCavlcPSlice` 5.7%, `DeblockLumaLt4H_simd` 5.4%,
    `DeblockChromaLt4H_simd` 1.8%.
  - The V kernels barely show. They have no loop and no call, so the
    sampler, which stops a thread at a safe point, counts their time in
    their callers (`DeblockingInterMb`, `WelsDeblockingMb`,
    `FilteringEdgeLuma*`).

  Encoding:
  - Deblocking: 5.2% from 13.6%.
  - The rest: `VAACalcSadBgd_c` 16.2%, `WelsDctT4_c` 11.8%,
    `WelsQuantFour4x4Max_c` 11.5%, `WelsIDctT4Rec_c` 6.8%,
    `WelsSampleSatd16x16_simd` 4.3%, `WelsI16x16LumaPredPlane_c` 3.0%.

  P5.5's transforms and quantisation are now 30% of the encode and 8% of the
  decode. P5.6's `VAACalcSadBgd_c` is the encoder's biggest kernel.
- **The baselines.** Built from P5.3's commit and run back to back with
  P5.4's, the 720p decode reads 178 frames a second. P5.3's notes measured
  146 on a slower run, so the speed-ups here are against the back-to-back
  numbers.

Notes from P5.5:

- **The inverse transform.** Two 4x4 blocks side by side go through at once,
  a row of each in one vector of 8 int16 lanes. The row pass runs in 16
  bits, which is what C does: it writes the row results back to the int16
  coefficients. The column pass widens to 32 bits, because the sum of four
  int16 values can need 18. After `(x + 32) >> 6` every value lies within
  ±1792, so the saturating pack to 16 bits loses nothing. The prediction is
  added in 16 bits and the unsigned saturating pack to bytes clips as
  `WelsClip1` does. The encoder's reconstructions and the decoder's
  `IdctResAddPred` use the same transform.
- **The forward DCT.** The residuals lie in -255..255 and the outputs within
  ±9180, so the whole transform is exact in 16 bits. The rows are
  transposed into columns, put through the butterfly, transposed back and
  put through it again.
- **Quantisation.** C takes `((|x| + ff) * mf) >> 16` in 32 bits and puts the
  sign back. Here |x| (32768 at most) and `|x| + ff` (65535 at most) are
  unsigned 16-bit values, and the unsigned high multiply gives the top half
  of the 32-bit product exactly. That equals C for every FF and MF in
  0..32767, where C's product can't overflow; the tables' values all are.
  `QuantFour4x4Max` reduces each block's largest |q| across the lanes with
  interleaves, which leaves the four maxima side by side for one 8-byte store.
- **The rest.** The dequantisers multiply in 16 bits and keep the low half,
  as C's int16 assignments do. The inverse DC Hadamard wraps in 16 bits as
  C's int16 temporaries do. The forward DC Hadamard gathers the 16 DCs from
  their blocks and works in 32 bits; the signed saturating pack is C's clip.
  The I16x16 DC-only reconstruction takes `(dc + 32) >> 6` in 32 bits and
  spreads each DC across its block's four columns.
- **The decoder's four blocks.** `IdctFourResAddPred_` skips a block when its
  non-zero count and DC are both 0. The SIMD version makes the same test for
  each block, runs the two-block transform when both blocks of a row need it
  and the one-block transform otherwise.
- **Not ported.** The encoder's 2x2 chroma DC Hadamard and quantiser (four
  values), the scans, `WelsCalculateSingleCtr4x4_c` (1.6% of the encode),
  `WelsGetNoneZeroCount_c` and the copies. Upstream has SSE2 versions of
  most; P5.6 can take them if the profile asks. The decoder's 8x8 IDCT has no
  SIMD version upstream either: only High profile streams use it, and the
  encoder never makes them.
- **Planted bugs.** Each was planted one at a time and restored from a copy,
  checked by md5. The unit and test vector tests that fail:
  - Quantisation taking |x| one low for negative x: 113 and 321.
  - `QuantFour4x4Max` missing its last reduction step: 28 and 310.
  - `QuantFour4x4` quantising block 1 twice and block 2 not at all: 81 and
    321.
  - The DCT's row 1 weights swapped (`s3 + s2 + s2`): 26 and 321.
  - The two-block DCT storing the right block's rows 0 and 1 over rows 2
    and 3: 13 and 326.
  - The DC Hadamard leaving rows 1 and 3 unrounded: 12 and 87.
  - The IDCT rounding with 31: 61 and 458.
  - The IDCT halving c3 unsigned: 176 and 453.
  - The IDCT's column sums kept to 16 bits: 30 and 2. Two real streams need
    the 32-bit column pass too.
  - The one-block IDCT storing row 3 to row 2: 123 and 455.
  - The I16x16 DC reconstruction giving rows 12-15 the DCs of rows 8-11: 37
    and 281.
  - `DequantFour4x4` leaving the last 8 coefficients: 41 and 310.
  - The inverse DC Hadamard's row pass negating t2: 33 and 313.
  - The decoder's four blocks testing block 3 on block 2's count: 17 and 339.
  - The decoder's right block, done alone, given the left block's
    coefficients: 16 and 379.
  - The encoder's table giving `pfDctT4` the four-block DCT: 1 and 0. Its
    first block is the one-block DCT of the same pixels, and its other 48
    outputs land in the macroblock's coefficient scratch, which the I4x4
    coder doesn't read, so the stream can't change. Only the table test
    sees it.
  - The decoder's table keeping `IdctResAddPred_c`: 1 and 0. The output is
    the same by design, so the table test is the check.
- **The profile after**, 1280x720. Encoding:
  - The transforms and quantisers: under 1% of the samples between them,
    from 31.7% (`WelsDctT4_c` 11.8%, which includes the four-block DCT that
    calls it, `WelsQuantFour4x4Max_c` 11.5%, `WelsIDctT4Rec_c` 6.8% and the
    rest 1.6%). Some of their time is counted in their callers: they have
    no loop and no call.
  - The rest: `VAACalcSadBgd_c` 22.5%, `WelsSampleSatd16x16_simd` 6.6%,
    `WelsMdInterMbLoop` 4.8%, `WelsI16x16LumaPredPlane_c` 4.4%,
    `McHorVer22_simd` 4.2%, `WelsEncInterY` 3.1%.

  Decoding:
  - The IDCTs: `IdctFourResAddPred_simd` 3.6%, from 8.1% for the C ones
    (`IdctResAddPred_c` 6.2% and `IdctFourResAddPred_` 1.9%).
  - The rest: `WelsResidualBlockCavlc` 9.3%, `McHorVer22_simd` 7.3%,
    `WelsActualDecodeMbCavlcPSlice` 6.6%, `DeblockLumaLt4H_simd` 5.6%,
    `DeblockingBsMarginalMBAvcbase` 5.6%.

  P5.6's `VAACalcSadBgd_c` is now nearly a quarter of the encode, and
  `WelsI16x16LumaPredPlane_c` is the biggest of the intra predictors.

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
  tripped through the C# D2 parser. *[~0.8k]* **Done**: ~1.8k lines, since the
  slice, macroblock and cache structs came with it.
- **E4** I-slices at fixed QP: intra mode decision (`md`,
  `svc_base_layer_md` I), `svc_encode_mb`, `svc_encode_slice`, the encoder's
  `deblocking` and `picture_handle`. Acceptance: the IDR bytes match native, and
  the reconstruction equals the C# decoder's output. *[~4k]*
  Split in two. **E4a done**: the macroblock-level intra coding, checked on
  hand-built contexts, ~3.3k lines. **E4b done**: the slice headers,
  `WelsCodeOneSlice`, the deblocking and the IDR match through the API, ~2.5k
  lines.
- **E5** P-slices at fixed QP: `svc_motion_estimate`, `mv_pred`,
  `svc_mode_decision`, `svc_base_layer_md` P and `ref_list_mgr_svc` (single
  reference). Acceptance: the stream matches native byte for byte. *[~4k]*
  Split in two. **E5a done**: the macroblock-level inter coding, checked on
  hand-built P contexts, ~3.8k lines. **E5b done**: the reference list
  manager, `DecideFrameType`, the VAA statistics and the stream match
  through the frame loop, ~1.9k lines.
- **E6** Rate control: `ratectl` (bitrate and quality modes, frame skip).
  Acceptance: the output matches native byte for byte with rate control on and
  preprocessing off. *[~1.5k]* **Done**: the camera modes of `ratectl` and
  the camera half of `processing`'s complexity analysis, ~1.9k lines, with
  94 rate controlled streams matching.
- **E7** Preprocessing: `processing/` and `wels_preprocess`. Acceptance: **the
  three upstream encoder hashes for CAMERA_VIDEO_REAL_TIME / SM_SINGLE_SLICE /
  1 layer / CAVLC** pass (`672a52fb…`, `08ade185…`, `e60f12e3…`). *[~4k]*
  **Done**: the camera halves of `processing` and `wels_preprocess`, ~3.9k
  lines. The three hashes pass, and so does the denoiser's (`913e49c7…`),
  with 63 preprocessed streams matching.
- **E8** Orchestration, API and wire-up: `encoder_ext`, `encoder` and
  `welsEncoderExt` (`InitializeExt`, `EncodeFrame`, `ForceIntraFrame`,
  `SetOption`), then `H264Codec.EncodeVideo`, the profile-level-id, the CLI's
  `--encoder h264.net`, an example, and interop with Chrome, Firefox and Safari.
  *[~3k]*
  **Done**: `welsEncoderExt` and the rest of `encoder_ext`, ~2.1k lines, with
  61 API scripts matching, `H264Codec`'s encoder, the CLI option and an
  example. Chromium decodes the stream. Firefox and Safari aren't checked.

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

Broken into steps (27 Nov), in the order the P5.1 profile puts them. Each
SIMD step keeps every existing table, hash and test passing with the new
kernels in the tables, and adds tests that each new kernel equals its C
kernel. The streams can't change: a kernel that doesn't equal its C kernel
is a bug.

| Step | What | Why first |
|---|---|---|
| P5.1 | `benchmarks/SIPSorcery.H264/H264Benchmarks`: BenchmarkDotNet encode and decode benchmarks at 480p, 720p and 1080p on camera-like content, a `run` mode for a profiler, and the baseline profile. The allocation check. | Measures everything after it. |
| P5.2 | The SIMD groundwork: `WelsCPUFeatureDetect` from .NET's ISA flags, a mask that turns the SIMD kernels off for tests and benchmarks, the kernel equality tests. Then SAD (7 sizes and the 7 four-neighbour variants) and SATD (7 sizes). | Half the encoder's time at 720p. |
| P5.3 | Motion compensation: the luma half-pel filters and quarter-pel positions, chroma, `PixelAvg`, the copies. | Over half the decoder's time, and a third of the encoder's once P5.2 is in. |
| P5.4 | Deblocking: the luma and chroma edge filters, both directions, and `expand_pic`. | A quarter of the decoder's time. |
| P5.5 | Transforms and quantisation: the encoder's DCTs, IDCT reconstruction, quantisers and Hadamard, and the decoder's `IdctResAddPred`. | A tenth of the encoder's time. |
| P5.6 | What's left in the profile then: the VAA and preprocessing kernels, intra prediction. | |
| P5.7 | Slice-level threading in the encoder, with `Task` (rule 7). Needs more than one slice, which is Phase 4's `SM_FIXEDSLCNUM_SLICE`, and costs some compression. libwebrtc asks OpenH264 for one slice. | Only after the single thread is fast. |

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
  chooses from resolution and frame rate (`au_set`). *E8 kept `42e01f`: the
  SPS carries the level, and a format parameter that changed with the frame
  size would need renegotiating.*
- **Pixel formats:** I420 natively. Reuse AV1's `I420Converter` for BGR/RGB
  input. It is a candidate to move into Abstractions so VP8, AV1 and H.264 share
  one copy. *E8 wrote `RgbToI420` instead, as AV1's is internal to its
  package. Moving one copy to Abstractions is still open.*
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
