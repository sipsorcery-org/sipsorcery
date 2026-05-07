//-----------------------------------------------------------------------------
// Optional high-resolution phase timers for encoder diagnostics. When
// <see cref="Enabled"/> is true, scoped regions attribute wall-clock time
// to broad buckets (DCT, quantize, tokenize, etc.). Zero overhead when disabled.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Text;

namespace Vpx.Net
{
    /// <summary>Encode hot-path phase buckets for <see cref="EncodeProfiler"/>.</summary>
    public enum Vp8EncodeProfilePhase
    {
        SimdMemOps,
        Fdct,
        Walsh,
        Quantize,
        Tokenize,
        Reconstruct,
        PackTokens,
        StitchLastFrame,
        /// <summary>Keyframe or inter: first-partition header plus 1056 coef-update flags (before MB phase 1).</summary>
        FirstPartitionHeader,
        /// <summary>Per-MB scalar context shuffle (9-byte above load/store) and EOB skip checks only — does not double-count <see cref="SimdMemOps"/>.</summary>
        Phase1MbScalarCtx,
        /// <summary>Phase-2 bit writing in partition 0 after phase 1 (skip probs, KF/inter modes, etc.) before token partition.</summary>
        Phase2FirstPartitionBits,
    }

    /// <summary>
    /// Thread-local (static) encode timings. Enable around a keyed encode, then read
    /// <see cref="GetReport"/> or individual tick fields for BenchmarkDotNet / diagnostics.
    /// </summary>
    public static class EncodeProfiler
    {
        // ThreadStatic so BenchmarkDotNet host / parallel jobs cannot corrupt a single encode's totals.
        [ThreadStatic] private static long tSimdMemOps;
        [ThreadStatic] private static long tFdct;
        [ThreadStatic] private static long tWalsh;
        [ThreadStatic] private static long tQuantize;
        [ThreadStatic] private static long tTokenize;
        [ThreadStatic] private static long tReconstruct;
        [ThreadStatic] private static long tPack;
        [ThreadStatic] private static long tStitch;
        [ThreadStatic] private static long tFirstPartHdr;
        [ThreadStatic] private static long tPhase1ScalarCtx;
        [ThreadStatic] private static long tPhase2FirstPart;

        /// <summary>When false (default), <see cref="Scope"/> no-ops.</summary>
        public static bool Enabled { get; set; }

        public static void Reset()
        {
            tSimdMemOps = tFdct = tWalsh = tQuantize = tTokenize = tReconstruct = tPack = tStitch = 0;
            tFirstPartHdr = tPhase1ScalarCtx = tPhase2FirstPart = 0;
        }

        public readonly struct Scope : IDisposable
        {
            private readonly Vp8EncodeProfilePhase _phase;
            private readonly long _t0;

            public Scope(Vp8EncodeProfilePhase phase)
            {
                _phase = phase;
                if (!Enabled)
                {
                    _t0 = 0;
                    return;
                }

                _t0 = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (_t0 == 0) return;
                long dt = Stopwatch.GetTimestamp() - _t0;
                Add(_phase, dt);
            }
        }

        private static void Add(Vp8EncodeProfilePhase phase, long ticks)
        {
            switch (phase)
            {
                case Vp8EncodeProfilePhase.SimdMemOps: tSimdMemOps += ticks; break;
                case Vp8EncodeProfilePhase.Fdct: tFdct += ticks; break;
                case Vp8EncodeProfilePhase.Walsh: tWalsh += ticks; break;
                case Vp8EncodeProfilePhase.Quantize: tQuantize += ticks; break;
                case Vp8EncodeProfilePhase.Tokenize: tTokenize += ticks; break;
                case Vp8EncodeProfilePhase.Reconstruct: tReconstruct += ticks; break;
                case Vp8EncodeProfilePhase.PackTokens: tPack += ticks; break;
                case Vp8EncodeProfilePhase.StitchLastFrame: tStitch += ticks; break;
                case Vp8EncodeProfilePhase.FirstPartitionHeader: tFirstPartHdr += ticks; break;
                case Vp8EncodeProfilePhase.Phase1MbScalarCtx: tPhase1ScalarCtx += ticks; break;
                case Vp8EncodeProfilePhase.Phase2FirstPartitionBits: tPhase2FirstPart += ticks; break;
            }
        }

        /// <summary>Sum of all profiled bucket ticks (current thread). Nested MB work can make this exceed wall-clock.</summary>
        public static long GetScopedTotalTicks() =>
            tSimdMemOps + tFdct + tWalsh + tQuantize + tTokenize + tReconstruct + tPack + tStitch
            + tFirstPartHdr + tPhase1ScalarCtx + tPhase2FirstPart;

        /// <summary>One-line wall-clock vs scoped-sum (scoped may exceed wall when buckets overlap, e.g. SimdMemOps inside other work).</summary>
        public static string FormatWallClockCompare(double wallMilliseconds)
        {
            double scopedMs = GetScopedTotalTicks() * 1000.0 / Stopwatch.Frequency;
            return $"Wall-clock: {wallMilliseconds:F3} ms | Scoped sum: {scopedMs:F3} ms | Delta: {wallMilliseconds - scopedMs:F3} ms (negative => overlapping scopes or unscoped work)";
        }

        /// <summary>Human-readable breakdown; ticks are Stopwatch raw units (current thread only).</summary>
        public static string GetReport()
        {
            long total = GetScopedTotalTicks();
            double ToMs(long t) => t * 1000.0 / Stopwatch.Frequency;
            var sb = new StringBuilder(384);
            sb.AppendLine($"Vp8EncodeProfiler (scoped sum {ToMs(total):F3} ms — sum of buckets; can exceed wall if scopes overlap)");
            void Line(string name, long t)
            {
                if (total <= 0) sb.AppendLine($"  {name,-22} {ToMs(t):F3} ms");
                else sb.AppendLine($"  {name,-22} {ToMs(t):F3} ms  ({100.0 * t / total:F1}%)");
            }
            Line("FirstPartitionHeader", tFirstPartHdr);
            Line("Phase1MbScalarCtx", tPhase1ScalarCtx);
            Line("Phase2FirstPartitionBits", tPhase2FirstPart);
            Line("SimdMemOps", tSimdMemOps);
            Line("Fdct", tFdct);
            Line("Walsh", tWalsh);
            Line("Quantize", tQuantize);
            Line("Tokenize", tTokenize);
            Line("Reconstruct", tReconstruct);
            Line("PackTokens", tPack);
            Line("StitchLastFrame", tStitch);
            return sb.ToString();
        }
    }
}
