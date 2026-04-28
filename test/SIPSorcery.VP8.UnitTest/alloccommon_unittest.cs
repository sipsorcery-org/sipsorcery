//-----------------------------------------------------------------------------
// Filename: alloccommon_unittest.cs
//
// Description: Unit tests for logic in alloccommon.cs.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 01 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Xunit;

namespace Vpx.Net.UnitTest
{
    public unsafe class alloccommon_unittest
    {
        /// <summary>
        /// Tests allocating the frame buffers on the VP8 common class.
        /// </summary>
        public unsafe void AllocateFrameBuffersTest()
        {
            VP8_COMMON oci = new VP8_COMMON();
            int res = alloccommon.vp8_alloc_frame_buffers(oci, 640, 480);

            Assert.Equal(0, res);

            alloccommon.vp8_de_alloc_frame_buffers(oci);
        }

        /// <summary>
        /// Sanity check that arrays are references.
        /// </summary>
        [Fact]
        public void CheckArrayDecrement()
        {
            VP8D_COMP pbi = onyxd.create_decompressor(new VP8D_CONFIG { Width = 640, Height = 480 });
            VP8_COMMON pc = pbi.common;

            pc.fb_idx_ref_cnt[0] = 1;
            pc.fb_idx_ref_cnt[0]--;

            Assert.Equal(0, pc.fb_idx_ref_cnt[0]);
        }
    }
}
