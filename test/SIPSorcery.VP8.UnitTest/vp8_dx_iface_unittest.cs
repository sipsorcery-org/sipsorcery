//-----------------------------------------------------------------------------
// Filename: vp8_dx_iface_unittest.cs
//
// Description: Unit tests for logic in vp8_dx_iface.cs.
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

using Xunit;

namespace Vpx.Net.UnitTest
{
    public class vp8_dx_iface_unittest
    {
        /// <summary>
        /// Tests that the VP8 decoder decoder "interface" can be acquired.
        /// </summary>
        [Fact]
        public void GetVP8DecoderInterfaceTest()
        {
            vpx_codec_iface_t algo = vp8_dx.vpx_codec_vp8_dx();

            Assert.NotNull(algo);
        }

        /// <summary>
        /// Tests that the VP8 decoder decoder "interface" can be acquired.
        /// </summary>
        [Fact]
        public unsafe void UpdateFragmentsTest()
        {
            vpx_codec_alg_priv_t alg = new vpx_codec_alg_priv_t();
            vpx_codec_err_t err;
            ulong dataSz = 573;
            byte* data = (byte*)vpx_mem.vpx_malloc(dataSz);
            int res = vp8_dx.update_fragments(alg, data, (uint)dataSz, out err);

            Assert.Equal(1, res);
            Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, err);

            vpx_mem.vpx_free(data);
        }

        /// <summary>
        /// Tests initialsing the codec's YV12 buffers.
        /// </summary>
        [Fact]
        public unsafe void InitialiseCodecYV12BuffersTest()
        {
            vpx_codec_alg_priv_t alg = new vpx_codec_alg_priv_t();
            VP8D_CONFIG oxcf = new VP8D_CONFIG();
            var res = onyxd.vp8_create_decoder_instances(alg.yv12_frame_buffers, oxcf);

            Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, res);

            onyxd.vp8_remove_decoder_instances(alg.yv12_frame_buffers);
        }
    }
}
