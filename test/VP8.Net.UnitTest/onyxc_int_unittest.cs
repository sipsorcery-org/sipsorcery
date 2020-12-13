//-----------------------------------------------------------------------------
// Filename: onyxc_int_unittest.cs
//
// Description: Unit tests for logic in onyxc_int.cs.
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
    public unsafe class onyxc_int_unittest
    {
        /// <summary>
        /// Tests accessing the VP8_COMMON.mip array. It's a dynamically allocated array
        /// where each element is a class which is then comprised of structs and unions. The
        /// desire was to keep the data structure as close to the original C implementation as
        /// possible but that has presented challenges particularly with structs needing to 
        /// contain arrays of other mutable structs.
        /// </summary>
        [Fact]
        public unsafe void AccessModeInfoTest()
        {
            VP8_COMMON oci = new VP8_COMMON();
            int res = alloccommon.vp8_alloc_frame_buffers(oci, 640, 480);

            Assert.Equal(0, res);
            Assert.Equal(1271, oci.mip.Length);
            Assert.NotNull(oci.mip[0]);
            Assert.NotNull(oci.mip[oci.mip.Length - 1]);
            Assert.NotNull(oci.mi);

            alloccommon.vp8_de_alloc_frame_buffers(oci);
        }

        /// <summary>
        /// The typical way a MODE_INFO element in the VP8_COMMON.mip array is via the 
        /// VP8_COMMON.mi pointer. Because the mi pointer is used extensivelythe choice was to
        /// turn it into an index and use a fixed pointer for each access of come up with some kind 
        /// of wrapper. The latter choice of a wrapper has been used.
        /// </summary>
        [Fact]
        public unsafe void SetModeInfoTest()
        {
            VP8_COMMON oci = new VP8_COMMON();
            alloccommon.vp8_alloc_frame_buffers(oci, 640, 480);

            // Checking that setting a value through the oci.mi wrapper is recorded
            // on the underlying array.
            oci.mi.get().bmi[0].mv.as_mv.row = 3;
            Assert.Equal(3, oci.mip[oci.mi.Index].bmi[0].mv.as_mv.row);

            alloccommon.vp8_de_alloc_frame_buffers(oci);
        }
    }
}
