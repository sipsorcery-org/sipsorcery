//-----------------------------------------------------------------------------
// Warm regression / diagnostic: run one keyframe encode with profiling enabled
// and assert the phase report contains the expected buckets (Fdct, SimdMemOps,
// PackTokens, etc.). Uses the SIMD memory-ops path to exercise SimdMemOps timing.
//-----------------------------------------------------------------------------

using System;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public class encode_profiler_unittest
    {
        [Fact]
        public void EncodeProfiler_KeyframeContiguousI420_AttributesPhases()
        {
            const int W = 64, H = 64;
            int ySize = W * H;
            int cSize = (W / 2) * (H / 2);
            var i420 = new byte[ySize + 2 * cSize];
            for (int i = 0; i < i420.Length; i++)
                i420[i] = (byte)((i * 7 + 13) & 0xFF);

            EncodeProfiler.Reset();
            EncodeProfiler.Enabled = true;
            try
            {
                var buffers = new FrameEncoderBuffers();
                _ = frame_encoder.EncodeKeyframeWithBuffersContiguousI420(
                    i420, W, H, qIndex: 40, buffers, SpanSimdEncoderMemoryOps.Instance);
            }
            finally
            {
                EncodeProfiler.Enabled = false;
            }

            string report = EncodeProfiler.GetReport();
            Assert.Contains("Vp8EncodeProfiler", report);
            Assert.Contains("FirstPartitionHeader", report);
            Assert.Contains("Phase1MbScalarCtx", report);
            Assert.Contains("Phase2FirstPartitionBits", report);
            Assert.Contains("Fdct", report);
            Assert.Contains("SimdMemOps", report);
            Assert.Contains("Quantize", report);
            Assert.Contains("Tokenize", report);
            Assert.Contains("PackTokens", report);
        }
    }
}
