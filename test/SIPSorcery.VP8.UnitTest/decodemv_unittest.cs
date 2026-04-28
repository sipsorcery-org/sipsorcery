//-----------------------------------------------------------------------------
// Filename: decodemv_unittest.cs
//
// Description: Unit tests for logic in decodemv.cs.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 15 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Xunit;

namespace Vpx.Net.UnitTest
{
    public class decodemv_unittest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public decodemv_unittest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = TestLogger.GetLogger(output).CreateLogger(this.GetType().Name);
        }

        [Fact]
        public unsafe void DecodeKeyFrameMotionVectorTest()
        {
            VP8D_COMP pbi = onyxd.create_decompressor(new VP8D_CONFIG { Width = 32, Height = 24 });
            VP8_COMMON pc = pbi.common;

            alloccommon.vp8_alloc_frame_buffers(pc, 32, 24);
            onyxd.swap_frame_buffers(pc);

            byte[] encData = HexStr.ParseHexStr("9019009d012a2000180000070885858899848802020275ba24f8de73c58dbdeeeb752712ff80fc8ee701f51cfee1f8e5c007f80ff0dfe73c003fa21e881d603fc07f8e7a287fa3ff25f023fab9fe6bfc4fc00ff1cfe65f3ff800ff46f00fbc5f6f3d5bfdb9cbc7f27fc6dfc88e101fc01f51bfca3f103f29f3817e19fd0ff1d3f243900fa07fe03fc6ff18bf93ed02ff2dfebdfcdff557fa07ba3fecdf8abeb97e10fe9bf8ddf403fc1ff8bff33feaffae5fd73ff9f801fd33f606fd1ff6c52ce5c70fb5b31d19c4d1585982a1d52c92d5044bc6aa90");
            fixed (byte* pEncData = encData)
            {
                pbi.fragments.ptrs[0] = pEncData;
                pbi.fragments.sizes[0] = (uint)encData.Length;
                pbi.fragments.count = 1;
            }

            decodemv.vp8_decode_mode_mvs(pbi);

            //DebugProbe.DumpMotionVectors(pbi.common.mip, pbi.common.mb_cols, pbi.common.mb_rows);
        }
    }
}
