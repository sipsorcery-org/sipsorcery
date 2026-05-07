//-----------------------------------------------------------------------------
// Strategy-shaped entry for VP8 foundation encoders: legacy scalar memory
// ops vs span/SIMD ops. A single selection point — no per-MB flags.
//-----------------------------------------------------------------------------

using System;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Vpx.Net
{
    /// <summary>
    /// Reports whether the current CPU supports vector residual kernels used by
    /// <see cref="Vp8EncodePipelineKind.Optimized"/> (AVX2, SSE2, or AArch64 AdvSimd).
    /// </summary>
    public static class Vp8EncodeSimdCapabilities
    {
        /// <summary>
        /// True when at least one of AVX2 (x64), SSE2 (x64), or AdvSimd (ARM) is available.
        /// When false, <see cref="Vp8EncodePipelineKind.Optimized"/> falls back to the legacy pipeline.
        /// </summary>
        public static bool HasResidualAcceleration =>
            Avx2.IsSupported || Sse2.IsSupported || AdvSimd.IsSupported;
    }

    /// <summary>
    /// Selects which frame-encoder memory strategy <see cref="VP8Codec"/> uses.
    /// </summary>
    public enum Vp8EncodePipelineKind
    {
        /// <summary>Original nested-loop copies and scalar residuals.</summary>
        Legacy,
        /// <summary>
        /// Span row copies and hardware residual kernels when
        /// <see cref="Vp8EncodeSimdCapabilities.HasResidualAcceleration"/> is true;
        /// otherwise the same behavior as <see cref="Legacy"/>.
        /// </summary>
        Optimized,
    }

    /// <summary>Internal abstraction for keyframe/inter encode with shared buffers.</summary>
    internal interface IVp8FrameEncodePipeline
    {
        IEncoderMemoryOps MemoryOps { get; }

        /// <summary>
        /// log2 of the number of token partitions (0 -> 1 partition, 1 -> 2,
        /// 2 -> 4, 3 -> 8). Default 0 keeps the existing single-partition
        /// bit-exact behaviour. Higher values enable parallel pack-tokens
        /// over <c>1 &lt;&lt; Log2NumTokenPartitions</c> partitions.
        /// </summary>
        int Log2NumTokenPartitions { get; }

        byte[] EncodeKeyframe(byte[] srcY, byte[] srcU, byte[] srcV, int width, int height, int qIndex,
            FrameEncoderBuffers buffers);

        byte[] EncodeKeyframeContiguousI420(byte[] i420, int width, int height, int qIndex,
            FrameEncoderBuffers buffers);

        byte[] EncodeInterFrame(byte[] srcY, byte[] srcU, byte[] srcV, int width, int height, int qIndex,
            FrameEncoderBuffers buffers);

        byte[] EncodeInterFrameContiguousI420(byte[] i420, int width, int height, int qIndex,
            FrameEncoderBuffers buffers);

        /// <summary>Pooled variant — returns a slice over <see cref="FrameEncoderBuffers.OutBuf"/>; avoids allocating/copying the encoded frame blob (other small encode-time allocations may still occur).</summary>
        ArraySegment<byte> EncodeKeyframePooled(byte[] srcY, byte[] srcU, byte[] srcV, int width, int height, int qIndex,
            FrameEncoderBuffers buffers);

        /// <summary>Pooled variant — returns a slice over <see cref="FrameEncoderBuffers.OutBuf"/>; avoids allocating/copying the encoded frame blob (other small encode-time allocations may still occur).</summary>
        ArraySegment<byte> EncodeKeyframeContiguousI420Pooled(byte[] i420, int width, int height, int qIndex,
            FrameEncoderBuffers buffers);

        /// <summary>Pooled variant — returns a slice over <see cref="FrameEncoderBuffers.OutBuf"/>; avoids allocating/copying the encoded frame blob (other small encode-time allocations may still occur).</summary>
        ArraySegment<byte> EncodeInterFramePooled(byte[] srcY, byte[] srcU, byte[] srcV, int width, int height, int qIndex,
            FrameEncoderBuffers buffers);

        /// <summary>Pooled variant — returns a slice over <see cref="FrameEncoderBuffers.OutBuf"/>; avoids allocating/copying the encoded frame blob (other small encode-time allocations may still occur).</summary>
        ArraySegment<byte> EncodeInterFrameContiguousI420Pooled(byte[] i420, int width, int height, int qIndex,
            FrameEncoderBuffers buffers);
    }

    internal sealed class LegacyVp8FrameEncodePipeline : IVp8FrameEncodePipeline
    {
        public static readonly LegacyVp8FrameEncodePipeline Instance = new LegacyVp8FrameEncodePipeline(0);

        private readonly int _log2NumTokenPartitions;

        public LegacyVp8FrameEncodePipeline(int log2NumTokenPartitions)
        {
            _log2NumTokenPartitions = log2NumTokenPartitions;
        }

        public IEncoderMemoryOps MemoryOps => LegacyEncoderMemoryOps.Instance;
        public int Log2NumTokenPartitions => _log2NumTokenPartitions;

        public byte[] EncodeKeyframe(byte[] srcY, byte[] srcU, byte[] srcV, int width, int height,
            int qIndex, FrameEncoderBuffers buffers) =>
            frame_encoder.EncodeKeyframeWithBuffers(srcY, srcU, srcV, width, height, qIndex, buffers,
                LegacyEncoderMemoryOps.Instance, _log2NumTokenPartitions);

        public byte[] EncodeKeyframeContiguousI420(byte[] i420, int width, int height, int qIndex,
            FrameEncoderBuffers buffers) =>
            frame_encoder.EncodeKeyframeWithBuffersContiguousI420(i420, width, height, qIndex, buffers,
                LegacyEncoderMemoryOps.Instance, _log2NumTokenPartitions);

        public byte[] EncodeInterFrame(byte[] srcY, byte[] srcU, byte[] srcV, int width, int height,
            int qIndex, FrameEncoderBuffers buffers) =>
            frame_encoder.EncodeInterFrameWithBuffers(srcY, srcU, srcV, width, height, qIndex, buffers,
                LegacyEncoderMemoryOps.Instance, _log2NumTokenPartitions);

        public byte[] EncodeInterFrameContiguousI420(byte[] i420, int width, int height, int qIndex,
            FrameEncoderBuffers buffers) =>
            frame_encoder.EncodeInterFrameWithBuffersContiguousI420(i420, width, height, qIndex, buffers,
                LegacyEncoderMemoryOps.Instance, _log2NumTokenPartitions);

        public ArraySegment<byte> EncodeKeyframePooled(byte[] srcY, byte[] srcU, byte[] srcV, int width, int height,
            int qIndex, FrameEncoderBuffers buffers) =>
            frame_encoder.EncodeKeyframeWithBuffersPooled(srcY, srcU, srcV, width, height, qIndex, buffers,
                LegacyEncoderMemoryOps.Instance, _log2NumTokenPartitions);

        public ArraySegment<byte> EncodeKeyframeContiguousI420Pooled(byte[] i420, int width, int height, int qIndex,
            FrameEncoderBuffers buffers) =>
            frame_encoder.EncodeKeyframeWithBuffersContiguousI420Pooled(i420, width, height, qIndex, buffers,
                LegacyEncoderMemoryOps.Instance, _log2NumTokenPartitions);

        public ArraySegment<byte> EncodeInterFramePooled(byte[] srcY, byte[] srcU, byte[] srcV, int width, int height,
            int qIndex, FrameEncoderBuffers buffers) =>
            frame_encoder.EncodeInterFrameWithBuffersPooled(srcY, srcU, srcV, width, height, qIndex, buffers,
                LegacyEncoderMemoryOps.Instance, _log2NumTokenPartitions);

        public ArraySegment<byte> EncodeInterFrameContiguousI420Pooled(byte[] i420, int width, int height, int qIndex,
            FrameEncoderBuffers buffers) =>
            frame_encoder.EncodeInterFrameWithBuffersContiguousI420Pooled(i420, width, height, qIndex, buffers,
                LegacyEncoderMemoryOps.Instance, _log2NumTokenPartitions);
    }

    internal sealed class OptimizedVp8FrameEncodePipeline : IVp8FrameEncodePipeline
    {
        public static readonly OptimizedVp8FrameEncodePipeline Instance = new OptimizedVp8FrameEncodePipeline(0);

        private readonly int _log2NumTokenPartitions;

        public OptimizedVp8FrameEncodePipeline(int log2NumTokenPartitions)
        {
            _log2NumTokenPartitions = log2NumTokenPartitions;
        }

        public IEncoderMemoryOps MemoryOps => SpanSimdEncoderMemoryOps.Instance;
        public int Log2NumTokenPartitions => _log2NumTokenPartitions;

        public byte[] EncodeKeyframe(byte[] srcY, byte[] srcU, byte[] srcV, int width, int height,
            int qIndex, FrameEncoderBuffers buffers) =>
            frame_encoder.EncodeKeyframeWithBuffers(srcY, srcU, srcV, width, height, qIndex, buffers,
                SpanSimdEncoderMemoryOps.Instance, _log2NumTokenPartitions);

        public byte[] EncodeKeyframeContiguousI420(byte[] i420, int width, int height, int qIndex,
            FrameEncoderBuffers buffers) =>
            frame_encoder.EncodeKeyframeWithBuffersContiguousI420(i420, width, height, qIndex, buffers,
                SpanSimdEncoderMemoryOps.Instance, _log2NumTokenPartitions);

        public byte[] EncodeInterFrame(byte[] srcY, byte[] srcU, byte[] srcV, int width, int height,
            int qIndex, FrameEncoderBuffers buffers) =>
            frame_encoder.EncodeInterFrameWithBuffers(srcY, srcU, srcV, width, height, qIndex, buffers,
                SpanSimdEncoderMemoryOps.Instance, _log2NumTokenPartitions);

        public byte[] EncodeInterFrameContiguousI420(byte[] i420, int width, int height, int qIndex,
            FrameEncoderBuffers buffers) =>
            frame_encoder.EncodeInterFrameWithBuffersContiguousI420(i420, width, height, qIndex, buffers,
                SpanSimdEncoderMemoryOps.Instance, _log2NumTokenPartitions);

        public ArraySegment<byte> EncodeKeyframePooled(byte[] srcY, byte[] srcU, byte[] srcV, int width, int height,
            int qIndex, FrameEncoderBuffers buffers) =>
            frame_encoder.EncodeKeyframeWithBuffersPooled(srcY, srcU, srcV, width, height, qIndex, buffers,
                SpanSimdEncoderMemoryOps.Instance, _log2NumTokenPartitions);

        public ArraySegment<byte> EncodeKeyframeContiguousI420Pooled(byte[] i420, int width, int height, int qIndex,
            FrameEncoderBuffers buffers) =>
            frame_encoder.EncodeKeyframeWithBuffersContiguousI420Pooled(i420, width, height, qIndex, buffers,
                SpanSimdEncoderMemoryOps.Instance, _log2NumTokenPartitions);

        public ArraySegment<byte> EncodeInterFramePooled(byte[] srcY, byte[] srcU, byte[] srcV, int width, int height,
            int qIndex, FrameEncoderBuffers buffers) =>
            frame_encoder.EncodeInterFrameWithBuffersPooled(srcY, srcU, srcV, width, height, qIndex, buffers,
                SpanSimdEncoderMemoryOps.Instance, _log2NumTokenPartitions);

        public ArraySegment<byte> EncodeInterFrameContiguousI420Pooled(byte[] i420, int width, int height, int qIndex,
            FrameEncoderBuffers buffers) =>
            frame_encoder.EncodeInterFrameWithBuffersContiguousI420Pooled(i420, width, height, qIndex, buffers,
                SpanSimdEncoderMemoryOps.Instance, _log2NumTokenPartitions);
    }

    internal static class Vp8FrameEncodePipelineFactory
    {
        public static IVp8FrameEncodePipeline Create(Vp8EncodePipelineKind kind, int log2NumTokenPartitions = 0)
        {
            frame_encoder.ValidateLog2TokenPartitions(log2NumTokenPartitions);
            switch (kind)
            {
                case Vp8EncodePipelineKind.Legacy:
                    return log2NumTokenPartitions == 0
                        ? LegacyVp8FrameEncodePipeline.Instance
                        : new LegacyVp8FrameEncodePipeline(log2NumTokenPartitions);
                case Vp8EncodePipelineKind.Optimized:
                    if (!Vp8EncodeSimdCapabilities.HasResidualAcceleration)
                    {
                        return log2NumTokenPartitions == 0
                            ? (IVp8FrameEncodePipeline)LegacyVp8FrameEncodePipeline.Instance
                            : new LegacyVp8FrameEncodePipeline(log2NumTokenPartitions);
                    }
                    return log2NumTokenPartitions == 0
                        ? OptimizedVp8FrameEncodePipeline.Instance
                        : new OptimizedVp8FrameEncodePipeline(log2NumTokenPartitions);
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }
}
