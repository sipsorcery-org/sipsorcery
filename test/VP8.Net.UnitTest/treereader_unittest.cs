//-----------------------------------------------------------------------------
// Filename: treereader_unittest.cs
//
// Description: Unit tests for logic in treereader.cs.
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

using vp8_reader = Vpx.Net.BOOL_DECODER;

namespace Vpx.Net.UnitTest
{
    public unsafe class treereader_unittest
    {
        /// <summary>
        /// Tests reading a bit from the treee reader.
        /// </summary>
        [Fact]
        public void ReadBitTest()
        {
            // Initialise a frame buffer to try the tree reader on.
            //frame_buffers fb = new frame_buffers();
            //VP8D_CONFIG oxcf = new VP8D_CONFIG();
            //var res = onyxd.vp8_create_decoder_instances(fb, oxcf);

            //Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, res);

            // Try the reader.
            //vp8_reader bc = fb.pbi[0].mbc[8];
            byte* buf = stackalloc byte[1] { 0x01 };
            vp8_reader bc = new vp8_reader { user_buffer = buf, user_buffer_end = buf + 1 };
            int bit = treereader.vp8_read_bit(ref bc);

            Assert.Equal(1, bit);
        }
    }
}
