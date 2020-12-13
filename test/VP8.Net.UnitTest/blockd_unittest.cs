//-----------------------------------------------------------------------------
// Filename: blockd_unittest.cs
//
// Description: Unit tests for logic in blockd.cs.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 04 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Xunit;

namespace Vpx.Net.UnitTest
{
    public unsafe class blockd_unittest
    {
        /// <summary>
        /// Checks that setting properties on the b_mode_info struct
        /// behaves as a union.
        /// </summary>
        [Fact]
        public void CheckBModeInfoUnionTest()
        {
            b_mode_info b = new b_mode_info();

            b.as_mode = B_PREDICTION_MODE.B_RD_PRED;
            Assert.Equal(5U, b.mv.as_int);

            b.mv.as_int = 1;
            Assert.Equal(B_PREDICTION_MODE.B_TM_PRED, b.as_mode);
        }

        [Fact]
        public void InitialiseMacroBlockTest()
        {
            frame_buffers fb = new frame_buffers();
            VP8D_CONFIG config = new VP8D_CONFIG();
            var errRes = onyxd.vp8_create_decoder_instances(fb, config);

            Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, errRes);

            int res = alloccommon.vp8_alloc_frame_buffers(fb.pbi[0].common, 640, 480);

            Assert.Equal(0, res);
            Assert.NotNull(fb.pbi[0].mb.block[24].qcoeff);
            //Assert.NotEqual(0, fb.pbi[0].mb.block[24].qcoeff.get());
        }
    }
}
