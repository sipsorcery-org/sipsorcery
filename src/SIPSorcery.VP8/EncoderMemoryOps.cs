//-----------------------------------------------------------------------------
// Pluggable copy / residual helpers for the VP8 frame encoder. Two
// implementations (legacy nested loops vs span + SIMD) are swapped via
// IVp8FrameEncodePipeline — no per-MB feature flags.
//-----------------------------------------------------------------------------

using System;

namespace Vpx.Net
{
    internal interface IEncoderMemoryOps
    {
        /// <summary>
        /// Pipeline-level capability flag. When false (Legacy pipeline),
        /// every SIMD encoder kernel dispatcher in mb_encoder.cs falls
        /// back to the scalar reference (fdct/idct/walsh/quantize/dcpred)
        /// regardless of whether the host CPU supports the SIMD path.
        /// When true (Optimized pipeline) the SIMD modules are used when
        /// their CPU-feature gate is also satisfied.
        /// </summary>
        bool UseSimdEncoderKernels { get; }

        void CopyPlaneRect(byte[] src, int baseOffset, int srcStride, int x, int y, int w, int h, byte[] dst);

        void CopyRowAt(byte[] src, int offset, int count, byte[] dst);

        void CopyColumn(byte[] src, int srcStride, int columnIndex, int firstRow, int rows, byte[] dst);

        void CopyRowFrom2d(byte[] src, int srcStride, int srcRow, int srcCol, byte[] dst, int dstOffset, int count);

        void SubtractFlat(byte[] src, byte pred, short[] dst);

        void SubtractPerPixel(byte[] src, byte[] pred, short[] dst);
    }

    internal sealed class LegacyEncoderMemoryOps : IEncoderMemoryOps
    {
        public static readonly LegacyEncoderMemoryOps Instance = new LegacyEncoderMemoryOps();

        private LegacyEncoderMemoryOps() { }

        public bool UseSimdEncoderKernels => false;

        public void CopyPlaneRect(byte[] src, int baseOffset, int srcStride, int x, int y, int w, int h, byte[] dst)
        {
            for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
                dst[r * w + c] = src[baseOffset + (y + r) * srcStride + (x + c)];
        }

        public void CopyRowAt(byte[] src, int offset, int count, byte[] dst)
        {
            for (int i = 0; i < count; i++) dst[i] = src[offset + i];
        }

        public void CopyColumn(byte[] src, int srcStride, int columnIndex, int firstRow, int rows, byte[] dst)
        {
            for (int r = 0; r < rows; r++) dst[r] = src[(firstRow + r) * srcStride + columnIndex];
        }

        public void CopyRowFrom2d(byte[] src, int srcStride, int srcRow, int srcCol, byte[] dst, int dstOffset, int count)
        {
            for (int i = 0; i < count; i++) dst[dstOffset + i] = src[srcRow * srcStride + srcCol + i];
        }

        public void SubtractFlat(byte[] src, byte pred, short[] dst)
        {
            for (int i = 0; i < src.Length; i++) dst[i] = (short)(src[i] - pred);
        }

        public void SubtractPerPixel(byte[] src, byte[] pred, short[] dst)
        {
            for (int i = 0; i < src.Length; i++) dst[i] = (short)(src[i] - pred[i]);
        }
    }

    internal sealed class SpanSimdEncoderMemoryOps : IEncoderMemoryOps
    {
        public static readonly SpanSimdEncoderMemoryOps Instance = new SpanSimdEncoderMemoryOps();

        private SpanSimdEncoderMemoryOps() { }

        public bool UseSimdEncoderKernels => true;

        public void CopyPlaneRect(byte[] src, int baseOffset, int srcStride, int x, int y, int w, int h, byte[] dst)
        {
            using var _ = new EncodeProfiler.Scope(Vp8EncodeProfilePhase.SimdMemOps);
            EncoderSimdKernels.CopyPlaneRectSpan(src, baseOffset, srcStride, x, y, w, h, dst);
        }

        public void CopyRowAt(byte[] src, int offset, int count, byte[] dst)
        {
            using var _ = new EncodeProfiler.Scope(Vp8EncodeProfilePhase.SimdMemOps);
            EncoderSimdKernels.CopySpan(src, offset, dst, 0, count);
        }

        public void CopyColumn(byte[] src, int srcStride, int columnIndex, int firstRow, int rows, byte[] dst)
        {
            using var _ = new EncodeProfiler.Scope(Vp8EncodeProfilePhase.SimdMemOps);
            EncoderSimdKernels.CopyColumn(src, srcStride, columnIndex, firstRow, rows, dst);
        }

        public void CopyRowFrom2d(byte[] src, int srcStride, int srcRow, int srcCol, byte[] dst, int dstOffset, int count)
        {
            using var _ = new EncodeProfiler.Scope(Vp8EncodeProfilePhase.SimdMemOps);
            EncoderSimdKernels.CopySpan(src, srcRow * srcStride + srcCol, dst, dstOffset, count);
        }

        public void SubtractFlat(byte[] src, byte pred, short[] dst)
        {
            using var _ = new EncodeProfiler.Scope(Vp8EncodeProfilePhase.SimdMemOps);
            EncoderSimdKernels.SubtractFlatToShort(src, pred, dst);
        }

        public void SubtractPerPixel(byte[] src, byte[] pred, short[] dst)
        {
            using var _ = new EncodeProfiler.Scope(Vp8EncodeProfilePhase.SimdMemOps);
            EncoderSimdKernels.SubtractPerPixelToShort(src, pred, dst);
        }
    }
}
