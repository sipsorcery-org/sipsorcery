//-----------------------------------------------------------------------------
// Filename: decodeframe_unittest.cs
//
// Description: Unit tests for logic in decodeframe.cs.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// Halloween 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public unsafe class decodeframe_unittest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public decodeframe_unittest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = TestLogger.GetLogger(output).CreateLogger(this.GetType().Name);
        }

        /// <summary>
        /// Tests that doing the VP8 codec dequantizer is initialised correctly.
        /// </summary>
        [Fact]
        public unsafe void IntiailiseDeQuantizerTest()
        {
            VP8D_COMP pbi = new VP8D_COMP();
            decodeframe.vp8cx_init_de_quantizer(pbi);
        }

        /// <summary>
        /// Tests that copying the entropy MV default array, which is done as part a frame
        /// initialisation, works correctly.
        /// </summary>
        [Fact]
        public unsafe void CopyEntropyMvTest()
        {
            VP8D_COMP pbi = new VP8D_COMP();
            VP8_COMMON pc = pbi.common;
            Array.Copy(entropymv.vp8_default_mv_context, pc.fc.mvc, entropymv.vp8_default_mv_context.Length);

            Assert.Equal(entropymv.vp8_default_mv_context, pc.fc.mvc);
        }

        /// <summary>
        /// Tests that copying the entropy coefficient default array, which is done as part a frame
        /// initialisation, works correctly.
        /// </summary>
        [Fact]
        public unsafe void CopyEntropyCoefficientsTest()
        {
            VP8D_COMP pbi = new VP8D_COMP();
            VP8_COMMON pc = pbi.common;
            entropy.vp8_default_coef_probs(pc);

            //Assert.Equal(default_coef_probs_c.default_coef_probs, pc.fc.coef_probs);
        }

        /// <summary>
        /// Tests calling the init frame function.
        /// </summary>
        [Fact]
        public unsafe void InitFrameTest()
        {
            VP8D_COMP pbi = new VP8D_COMP();
            int res = alloccommon.vp8_alloc_frame_buffers(pbi.common, 640, 480);

            Assert.Equal(0, res);

            decodeframe.init_frame(pbi);

            Assert.Equal(-1, pbi.mb.fullpixel_mask);
        }

        /// <summary>
        /// Tests attempting to decode an invalid encoded frame throws an exception with
        /// the expected error code.
        /// </summary>
        [Fact]
        public unsafe void DecodeInvalidFrameTest()
        {
            VP8D_COMP pbi = onyxd.create_decompressor(new VP8D_CONFIG { Width = 640, Height = 480 });
            VP8_COMMON pc = pbi.common;

            alloccommon.vp8_alloc_frame_buffers(pc, 640, 480);
            onyxd.swap_frame_buffers(pc);

            byte[] encData = HexStr.ParseHexStr("5043009d012a8002e00102c708");
            fixed (byte* pEncData = encData)
            {
                pbi.fragments.ptrs[0] = pEncData;
                pbi.fragments.sizes[0] = (uint)encData.Length;
                pbi.fragments.count = 1;
            }

            var vpxExcp = Assert.Throws<VpxException>(() => onyxd.vp8dx_receive_compressed_data(pbi, 0));

            Assert.Equal(vpx_codec_err_t.VPX_CODEC_CORRUPT_FRAME, vpxExcp.ErrorCode);
        }
    }
}
