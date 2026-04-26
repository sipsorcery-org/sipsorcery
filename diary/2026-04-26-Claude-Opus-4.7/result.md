# VP8 encoder port — Claude Opus 4.7 (Apr 2026)

Model: `claude-opus-4-7` (Anthropic), commissioned by Aaron Clauson via Cowork.

This is a **living record** — kept up to date as further progress lands.

## Headline

The fifth AI attempt at this task is the first to clear the wall the previous
four ran into. `VP8Codec.EncodeVideo` no longer throws; it produces a
syntactically valid VP8 keyframe that **Chrome accepts and renders frame after
frame without dropping the WebRTC stream** (38+ seconds observed during a
webcam test). For uniform / synthetic test inputs the encode → decode round
trip is byte-exact (or within ±2 of source on chroma at the fixed default
quantizer of Q=32). For real webcam content the picture is currently a
field of macroblock-aligned, structurally consistent — but wrongly coloured —
blocks. The bitstream is valid; the colours are wrong because of one known
bug (see "Known issues" below).

Compared to the prior four entries in this diary, the difference is not
the model alone — it's the working method. See "What worked" below.

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

## Capabilities of the encoder as it stands

Implemented:

- Keyframe-only encoding (every emitted frame is `KEY_FRAME`).
- Single-partition layout (`log2_nbr_of_dct_partitions = 0`).
- DC_PRED for both Y (16×16) and UV (8×8); no other intra modes.
- Forward DCT + Walsh, regular quantizer, full coefficient tokenizer
  including the `skip_eob_node` and CAT1..CAT6 paths.
- Per-MB above/left entropy contexts maintained internally, combined via
  libvpx's `VP8_COMBINEENTROPYCONTEXTS` rule (count of non-zero
  neighbours, in {0, 1, 2}).
- Default base quantizer of 32 (no rate control yet).
- Output is pure I420 in, byte stream out — same shape as the existing
  decoder.

Not yet implemented (and called out explicitly so the next session has a
clean starting list):

- Inter / P-frames. Every emitted frame is a key-frame.
- Motion estimation, motion vectors, reference frame management.
- Mode picking — DC_PRED is the only intra mode used.
- RD optimisation, segmentation, loop-filter level tuning.
- Rate control / target bitrate.
- Coefficient probability updates (1056 zero bits are written for "no
  update" — leaves the decoder using the default tables).
- `EncodeVideoFaster` / `DecodeVideoFaster` — still throw.

## Known issues

**Cross-MB entropy-context propagation bug.** This is the one defect
that's currently visible. Each macroblock's `above_context` /
`left_context` state is correctly threaded *within* a macroblock, but
it's reset to zero at each macroblock boundary instead of being threaded
across macroblocks at frame scope. For uniform-pixel inputs that's
invisible (every block reduces to a single EOB token, so context choice
doesn't change which bits are written), which is exactly what the unit
tests exercise. For real content with non-zero residuals everywhere, the
decoder reads the encoder's bits with a different probability row from
the one the encoder used, and every block past the first column / first
row of each MB gets the wrong context.

The visible symptom on a webcam stream is a stable, macroblock-aligned
field of solid-coloured blocks with a vague spatial gradient — DC
components survive (which is why the picture has the right rough
brightness shape), but everything else is corrupted by the
probability-row mismatch.

The fix is mapped out: lift the contexts to frame scope (one
`above_context` array indexed by MB column position, one `left_context`
reset at the start of each MB row), thread them through
`mb_encoder.EncodeMacroblockDcPred` instead of allocating fresh zero
arrays per call. Add a checkerboard-pattern frame test that deliberately
exercises non-uniform cross-MB content — that's the test the existing
suite was missing.

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

Immediate next PR (the bug fix):

- Lift `above_context` / `left_context` to frame scope in
  `frame_encoder.cs`; pass into `mb_encoder.EncodeMacroblockDcPred`
  by reference instead of letting `mb_encoder` allocate its own.
- Add `frame_encoder_unittest.cs` checkerboard test: 16×16 source with
  a half-MB-wide vertical stripe at Y=64 vs Y=192, encode → decode →
  assert mean-absolute-error < threshold. This test will fail on
  current master and pass after the fix.

Beyond that, the natural follow-up sequence:

1. All other intra modes (V_PRED, H_PRED, TM_PRED for Y; the 4 UV
   modes; B_PRED for 4×4 luma).
2. A trivial mode picker — pick the one that minimises sum-of-squared
   error on the residual.
3. Inter / P-frames: motion vector entropy, `vp8_pack_mb_row` for
   non-keyframe partitions, `last_frame` reference frame management,
   ZEROMV / NEAREST / NEAR / NEWMV. (This is where libvpx is biggest;
   would itself span several PRs.)
4. Optional: rate control loop.

Each of these is plausibly one PR of foundation-series scope.

## File header attribution convention

Every file added in this series carries:

```
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
```

Aaron asked for the model identifier inline so future readers can tell
which Claude version produced which file, against the possibility of
other model versions contributing later.
