# VP8 encoder port — Claude Opus 4.7 (Apr 2026)

Model: `claude-opus-4-7` (Anthropic), commissioned by Aaron Clauson via Cowork.

This is a **living record** — kept up to date as further progress lands.

## Headline

The fifth AI attempt at this task is the first to clear the wall the previous
four ran into. `VP8Codec.EncodeVideo` produces a fully-decodable VP8 stream
(keyframe + inter) that Chrome renders correctly and continuously -- **the
WebRTCGetStartedVP8Net example streams audio + video to Chrome at the
default Q=32 / 30 fps with no audio loss and no video artefacts** (running
on master after the SRTP fix and the P-frame foundation series merged --
see "Followup work" below).

Encoder primitives are individually bit-exact-verified against libvpx C
reference output. The end-to-end stream against Chrome is the structural
signal that the foundation port worked.

Compared to the prior four entries in this diary, the difference is not the
model alone -- it's the working method. See "What worked" below.

## Cost

Aaron's commission of the work cost approximately **EUR 150 in
Anthropic credits** for the successful encoder write -- spanning the
seven-PR keyframe foundation series, the SRTP rollover-counter
investigation and fix, the five-PR P-frame foundation series, and the
follow-up cross-thread fix. Recorded here for future reference on what
this scope of port-and-debug work costs in 2026 dollars.

## Reframing of the original task

The task as originally posed was "implement a VP8 decoder to round-trip with
the existing encoder". The state of the repo on disk contradicted that
framing: the decoder has been working since Mar 2021 (`VP8Codec.DecodeVideo`
is fully wired, the existing decoder-side files cover the whole spec), and
it's the **encoder** that was missing — `VP8Codec.EncodeVideo` was a single
`throw new NotImplementedException(...)`. All four prior diary entries also
attempted the encoder, not the decoder. So the work below is the encoder
port.

## What landed in master

Seven foundation PRs in the VP8 series, plus two side-fixes that came out of
the same workstream:

| PR | Title | What it adds |
| --- | --- | --- |
| [#1562](https://github.com/sipsorcery-org/sipsorcery/pull/1562) | Forward DCT + quantize kernels (PR 1 of N) | `dct.cs`, `quantize.cs`, `vp8_default_zig_zag1d` table |
| [#1566](https://github.com/sipsorcery-org/sipsorcery/pull/1566) | Keyframe header writer (PR 2 of N) | `bitstream.cs` keyframe path of `vp8_pack_bitstream` up through `refresh_entropy_probs` |
| [#1567](https://github.com/sipsorcery-org/sipsorcery/pull/1567) | Coefficient tokenizer (PR 3 of N) | `tokenize.cs`, `dct_value_tokens.cs`, plus token tables in `entropy.cs` |
| [#1568](https://github.com/sipsorcery-org/sipsorcery/pull/1568) | Token bitstream writer (PR 4 of N) | `vp8_pack_tokens` appended to `bitstream.cs` |
| [#1569](https://github.com/sipsorcery-org/sipsorcery/pull/1569) | Quantizer table builder (PR 5 of N) | `quantizer_init.cs` — turns a Q index into the six per-block-type tables |
| [#1570](https://github.com/sipsorcery-org/sipsorcery/pull/1570) | Per-macroblock encode pipeline (PR 6 of N) | `mb_encoder.EncodeMacroblockDcPred` ties together predict → residual → fdct → walsh → quantize → tokenize → reconstruct for one MB |
| [#1571](https://github.com/sipsorcery-org/sipsorcery/pull/1571) | Frame orchestration + `EncodeVideo` wire-up + round-trip (PR 7 of N) | `frame_encoder.cs`, the `vp8_block2above` / `vp8_block2left` tables, and the `VP8Codec.EncodeVideo` wiring; first frame to round-trip and first frame Chrome accepts |

## Followup work (Apr 26-27)

After the seven-PR foundation series merged the diary's original "Known
issues" section flagged the cross-MB entropy-context bug. Working the
issue list end-to-end uncovered three more layers of problems beneath
it that the original diary entry didn't anticipate. Each new layer was
only visible after the one above was fixed, and the final answer
turned out to be a SIPSorcery library bug, not a VP8.Net bug.

### Encoder follow-ups

| PR | Title | What it did |
| --- | --- | --- |
| [#1574](https://github.com/sipsorcery-org/sipsorcery/pull/1574) | VP8 encoder: thread entropy contexts at frame scope (cross-MB fix) | Fixes the bug the original diary flagged. Lifts above/left context arrays out of per-MB allocation into frame scope; `mb_encoder.EncodeMacroblockDcPred` now accepts them by reference. Webcam content stops looking like macroblock confetti. |
| [#1575](https://github.com/sipsorcery-org/sipsorcery/pull/1575) | VP8 encoder: emit per-MB skip flag (mb_no_skip_coeff = 1) | For MBs whose 25 transformed blocks are all EOB-only, write a 1-bit skip flag and suppress the entire token stream in partition 1. Mirrors libvpx's per-MB skip optimisation. |
| [#1576](https://github.com/sipsorcery-org/sipsorcery/pull/1576) | VP8 encoder: allocation hygiene pass — eliminate per-frame GC pressure | ~18 MB → 0.01 MB allocations per 640×480 frame. ThreadStatic per-thread state pool, prob-row caching in `tokenize.SliceProbRow`, MbEncoderScratch pool, stackalloc for inner buffers. **Zero Gen 2 GCs in 500 frames** under microbenchmark (vs 30+ before). |

### Example-app follow-ups

| PR | Title | What it did |
| --- | --- | --- |
| [#1577](https://github.com/sipsorcery-org/sipsorcery/pull/1577) | WebRTCGetStartedVP8Net: pass VP8Codec directly to test source | Profiling found ~42 MB/sec of `byte[]` allocations from a needless I420→BGR→I420 round-trip in the example's wiring (the `OnVideoSourceRawSample` event path). Fixed by passing the codec directly to `VideoTestPatternSource`'s constructor, which has always supported the direct path. **Eliminates 6 Gen 2 GCs/sec** observed on the live stream. |
| [#1578](https://github.com/sipsorcery-org/sipsorcery/pull/1578) | VP8: tunable BaseQIndex + Q=96/15fps workaround in GetStartedVP8Net | Adds `VP8Codec.BaseQIndex` so apps can trade quality for bitrate. Workaround that survived ~3 minutes of audio (vs 15-45s without it) by reducing burst pressure. Later partially superseded by the SRTP fix below; the BaseQIndex API stays. |

### SRTP rollover investigation (the actual root cause)

After the encoder optimisations and example-wiring fix landed, the
WebRTCGetStartedVP8Net stream still lost audio after a variable interval
(15-45s at default Q=32/30fps; ~3 min at Q=96/15fps). Multiple
hypotheses were tried and falsified:

- **Burst pressure on the receiver** — disproved by an
  experimental RTP pacer (#1579, reverted in #1580): pacing reduced
  bursts by 6× but audio still died at 25-45 s. The non-linear scaling
  of survival time vs frame rate didn't fit a burst-overflow model
  either.
- **Music-source EOF** — disproved by inspecting the embedded
  `Macroform_-_Simplicity.raw` file: 200.6 s long at 8 kHz Int16,
  whereas audio died at 165 s with 35 s of music remaining.
- **Various Chrome-side renderer / decoder issues** — disproved by
  webrtc-internals data: `packetsLost = 0` on both streams at the
  failure moment. Chrome wasn't dropping packets; it just stopped
  *counting* them.

Diagnostics added in [#1581](https://github.com/sipsorcery-org/sipsorcery/pull/1581)
(per-second source counters + RTCP SR `PacketCount`) and
[#1582](https://github.com/sipsorcery-org/sipsorcery/pull/1582)
(RTP sequence-number logging) captured the smoking-gun timeline:

```
10:22:46  video src: 1440 frames seq~65443  ← pre-wrap
10:22:47  video src: 1455 frames seq~117    ← WRAP
10:22:47  audio src: 4800 packets seq~41801 ← audio at 41801, nowhere near wrap
          (Chrome stops counting audio at this exact moment;
           jitterBufferFlushes increments to 1.)
```

Audio failed at the moment **video's** RTP sequence number wrapped —
even though audio's own sequence was nowhere near 65535. Aaron pointed
out (correctly) that under WebRTC bundle the audio and video share an
SRTP context. Code review of `SrtpContext.cs` found a single shared
`Roc` (rollover counter) field on the context, used and incremented
in `ProtectRtp` regardless of which SSRC's packet was being encrypted.
Per RFC 3711 §3.2.1 the ROC is per-SSRC; the shared field is a bug.
Wrap on any one stream desynchronises the keystream for every other
stream sharing the context.

The asymmetry that made the bug observable: the receive path
(`UnprotectRtp`) was correct — it derives ROC per-SSRC via
`ssrcContext.S_l` from the existing per-SSRC `ReplayProtection`
dictionary. So sender encrypted audio with `roc=1` (post video wrap),
receiver decrypted with `roc=0` (audio's per-SSRC inferred ROC), HMAC
mismatch, packet silently dropped. Sender-side counters and RTCP SR
`PacketCount` kept incrementing, while Chrome's `packetsReceived`
flatlined.

| PR | Title | What it did |
| --- | --- | --- |
| [#1581](https://github.com/sipsorcery-org/sipsorcery/pull/1581) | Add per-second source + RTCP packet-count diagnostics | Per-second `src` counter + `pc.OnSendReport` logging. Decision matrix: `src grows / rtcp grows / packetsReceived stalls` ⇒ packets leave the .NET app but Chrome silently drops them. |
| [#1582](https://github.com/sipsorcery-org/sipsorcery/pull/1582) | Log RTP sequence number alongside packet counter | Added `LocalTrack.SeqNum` to the per-second log so the wrap moment is visible in the trace. The line where seq jumps from ~65535 to a small number lined up exactly with audio's death timestamp at Chrome. |
| [#1584](https://github.com/sipsorcery-org/sipsorcery/pull/1584) | SRTP (send): per-SSRC rollover counter (RFC 3711 §3.2.1) | The fix. Adds `OutboundRoc` per-SSRC on `SsrcSrtpContext`; `ProtectRtp` now reads / increments via `ReplayProtection.TryGetValue(ssrc, ...)` instead of `context.Roc`. Marks the legacy `Roc` property obsolete. Plus a regression test in `test/unit/net/SRTP/SrtpContextRolloverUnitTest.cs` that demonstrates the bug and its fix. |

After #1584 the WebRTCGetStartedVP8Net example streams cleanly to
Chrome at default settings for 7+ minutes (and counting). The bug was
in SIPSorcery's SRTP path — affecting any multi-SSRC outbound RTP
session, WebRTC bundle or otherwise. The reason VP8.Net surfaced it
where the libvpx-based example doesn't is purely packet rate: VP8.Net's
keyframe-only stream wraps the video sequence number every 30-50
seconds; libvpx's mostly-P-frame stream wraps roughly an order of
magnitude less often, so the bug is statistically much rarer to hit.

### P-frame foundation series (Apr 27-28)

With the SRTP fix landed and audio + video stable for arbitrarily long
sessions, the next thing on the roadmap was inter (P) frames. Same
foundation-series shape as the original encoder port: a sequence of
small, independently-testable PRs each porting one libvpx primitive,
with the orchestration ticked over to "real" inter encoding only in the
final PR. Five PRs, plus a follow-up cross-thread bug fix.

| PR | Title | What it did |
| --- | --- | --- |
| [#1586](https://github.com/sipsorcery-org/sipsorcery/pull/1586) | P-frame foundation: reference frame storage + key/inter cadence (PR 1 of 5) | `FrameEncoderBuffers.LastFrameY/U/V`, `VP8Codec.KeyframeIntervalFrames`, `_framesSinceLastKeyframe` counter. Inter branch in `EncodeVideo` is wired but still falls through to `EncodeKeyframe` -- decision logic in place, behaviour unchanged. |
| [#1587](https://github.com/sipsorcery-org/sipsorcery/pull/1587) | P-frame foundation: inter (P-frame) header writer (PR 2 of 5) | `bitstream.StartInterFrameHeader` + `FinishInterFrameFirstPartition`. Frame tag with `key_frame_flag = 1`, no start code, no dimensions. Compressed first-partition prefix through `refresh_last_frame`. Bit-exact round-trip tests against the existing decoder's frame-tag parser. |
| [#1588](https://github.com/sipsorcery-org/sipsorcery/pull/1588) | P-frame foundation: ZEROMV inter MB encoder (PR 3 of 5) | `mb_encoder.EncodeMacroblockZeroMvLast`. Same DCT/Walsh/quantize/tokenize pipeline as DC_PRED but the prediction is the same-position 16x16 + 8x8 + 8x8 samples from the previous frame's reconstruction. |
| [#1589](https://github.com/sipsorcery-org/sipsorcery/pull/1589) | P-frame foundation: per-MB inter mode + ref bits writer (PR 4 of 5) | `bitstream.WriteInterMbRefAndMode`, `WriteInterMode`, `vp8_treed_write`, `WriteInterMbZeroMvLast`. The inter-mode tree path bits, walking `vp8_mv_ref_tree` for any of ZEROMV / NEAREST / NEAR / NEW / SPLITMV. Round-trip tested for every (ref_frame, mode) combination against the decoder's `vp8_treed_read`. |
| [#1591](https://github.com/sipsorcery-org/sipsorcery/pull/1591) | P-frame foundation: `EncodeInterFrame` orchestration + `EncodeVideo` wire-up (PR 5 of 5) | `frame_encoder.EncodeInterFrame`. `VP8Codec.EncodeVideo` inter branch now actually emits a P-frame instead of falling through. Round-trip tests at Q=4/16/32 vs source PSNR. |
| [#1592](https://github.com/sipsorcery-org/sipsorcery/pull/1592) | VP8: fix cross-thread inter-frame encoding (regression from #1591) | Lifts `FrameEncoderBuffers` from `[ThreadStatic]` on `frame_encoder` to a per-instance field on `VP8Codec`, so the LAST_FRAME reference survives the .NET thread pool moving the work between worker threads on each Timer tick. |

The single bug surfaced during the series was caught by per-MB pixel
dumping under a moving-content round-trip test. The decoder picks a
row of `vp8_mode_contexts` based on `cnt[CNT_INTRA]`, which it
computes by walking the above/left/aboveleft neighbours' inter state.
For an all-ZEROMV LAST_FRAME stream that's deterministic by MB
position: `(0, 0)` -> 0, edges -> 2, interior -> 5. The encoder
initially used row 0 for every MB; the decoder used different rows;
the boolean coder desynced on the third MB of the first row, and the
test caught it as a flat-DC-PRED block where ZEROMV inter should
have been. Fixed in PR 5 itself.

The cross-thread bug found by Aaron's first test run after #1591
merged is a useful illustration of the foundation-series discipline:
the unit tests passed because they all ran on one thread, but the
example app's `Timer`-driven dispatch path tripped a real defect
the moment inter encoding required cross-call state. Fixed and
regression-tested in #1592 -- a `Task.Factory.StartNew(LongRunning)`
test that asserts the keyframe and inter calls land on different
ManagedThreadIds *and* both encode successfully.

## Capabilities of the encoder as it stands

Implemented:

- Keyframe + inter-frame (P-frame) encoding. `VP8Codec.KeyframeIntervalFrames`
  controls cadence (default 30 -> 1 keyframe/sec at 30 fps); intermediate
  frames are inter.
- Inter mode: ZEROMV referencing LAST_FRAME for every macroblock. Same-position
  16x16 Y + 8x8 U + 8x8 V samples from the previous frame's reconstruction.
- Single-partition layout (`log2_nbr_of_dct_partitions = 0`).
- DC_PRED for both Y (16x16) and UV (8x8); no other intra modes.
- Forward DCT + Walsh, regular quantizer, full coefficient tokenizer
  including the `skip_eob_node` and CAT1..CAT6 paths.
- Per-MB above/left entropy contexts maintained internally, combined via
  libvpx's `VP8_COMBINEENTROPYCONTEXTS` rule (count of non-zero
  neighbours, in {0, 1, 2}).
- Per-MB inter mode context: `cnt[CNT_INTRA]` computed correctly across
  MB position so the encoder's `vp8_mode_contexts` row matches the
  decoder's.
- Per-MB skip optimisation: skippable MBs (all 25 transformed blocks
  EOB-only) suppress their tokens in partition 1.
- Default base quantizer of 32 (no rate control yet).
- Cross-thread-safe encoding: `FrameEncoderBuffers` is per-codec-instance,
  so `Timer`-dispatched stream sources work correctly.
- Output is pure I420 in, byte stream out -- same shape as the existing
  decoder.

Not yet implemented:

- **Real motion estimation.** Every inter MB is ZEROMV. Source content
  with actual motion gets encoded as residuals against a stationary
  prediction, which works correctness-wise but loses most of the
  compression benefit a real motion-compensated encoder would give.
- NEWMV (encoded motion vectors), NEAREST / NEAR (predicted MVs),
  SPLITMV (4x4 sub-MB partitions).
- GOLDEN / ALTREF reference frames -- only LAST_FRAME is supported.
- Mode picking -- DC_PRED is the only intra mode used. Other intra
  modes (V_PRED, H_PRED, TM_PRED, B_PRED) and an RD-style picker.
- RD optimisation, segmentation, loop-filter level tuning.
- Rate control / target bitrate.
- Coefficient probability updates (1056 zero bits are written for "no
  update" -- leaves the decoder using the default tables).
- `EncodeVideoFaster` / `DecodeVideoFaster` -- still throw.

## Known issues

(All previously listed defects have been resolved. The original
cross-MB entropy-context bug was fixed by #1574; the multi-second
audio-loss-on-Chrome problem was traced to a SIPSorcery library bug
in the SRTP send path and fixed by #1584. Anything remaining is in
the "Roadmap" section below as a planned addition rather than a
defect.)

## What worked (compared to the four prior attempts)

All four prior attempts in this diary tried to produce the encoder in a
single shot. None of them produced a bitstream Chrome would accept. The
shape that broke that streak this time:

1. **Foundation-PR-series scope**, not one big PR. Seven small PRs each
   doing one tractable, independently-testable thing, instead of one
   thousands-of-lines drop. Each PR: ports the next libvpx primitive,
   adds bit-exact unit tests against C reference output captured from
   compiling the relevant libvpx slice standalone in the sandbox, builds
   green, no regressions, merge.

2. **Bit-exact verification at every layer.** Every primitive (boolhuff
   already in tree was first verified, then DCT, Walsh, quantize,
   `vp8_tokenize_block`, `vp8_pack_tokens`, `vp8cx_init_quantizer`, the
   keyframe header writer) has unit tests that compare its output
   byte-for-byte against `libvpx`'s C output on the same inputs. By the
   time the integration step (#1571) ran, every primitive below it was
   already known-correct, so when something *did* go wrong (and one bug
   did surface — the per-MB-context one fixed inside #1571 itself), it
   could only be in the orchestration / glue layer, not the maths.

3. **Honest scoping.** Frame header writer, tokenizer, quantizer init,
   per-MB pipeline, frame orchestration each shipped as separate PRs
   even though one or two could plausibly have been combined, because
   the failure mode of "one giant PR partway broken" was exactly the
   one to avoid.

4. **Diagnostic structure when the wiring bug surfaced.** The single
   bug that surfaced in the integration PR (#1571) — the per-block
   `initialContext = 0` issue — was diagnosed in minutes by dumping
   the decoded plane bytes and reading the structural pattern
   (`99,99,99,99,57,57,57,57` across U row 0). That's only possible
   to read as a clue if every other layer is already known-correct.

## Roadmap

With keyframe encoding, P-frame foundation, and the SRTP fix all in
place, the encoder is functional for production streaming at default
settings. Remaining items are quality / efficiency enhancements rather
than correctness fixes:

1. **Real motion estimation**: NEWMV with a motion-vector search, plus
   the NEAREST / NEAR predicted-MV modes that depend on neighbour MV
   accumulation. The biggest single compression-quality lever left --
   for typical webcam content with bounded motion this would drop the
   inter bitrate by another order of magnitude vs ZEROMV. Itself a
   foundation-series-shaped sequence of PRs (MV entropy coding, search
   primitive, mode picker integration).
2. **Other intra modes** (V_PRED, H_PRED, TM_PRED for Y; the 4 UV
   modes; B_PRED for 4x4 luma). Improves compression on detailed
   content; the current DC_PRED-only encoder is the lowest-quality
   intra option.
3. **A trivial mode picker** -- pick the one that minimises
   sum-of-squared error on the residual. Pairs with (1) and (2).
4. **Loop filter on the encoder side** (currently `FilterLevel = 0`,
   so the bitstream signals "filter off"; turning it on would
   reduce blocking artefacts at lower quality settings).
5. **Optional: rate control loop.** Useful only if production
   deployments want guaranteed bitrate caps.

Each of these is plausibly one PR of foundation-series scope.

## File header attribution convention

Every file added in this series carries:

```
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
```

Aaron asked for the model identifier inline so future readers can tell
which Claude version produced which file, against the possibility of
other model versions contributing later.
