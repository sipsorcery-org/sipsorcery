//-----------------------------------------------------------------------------
// Filename: vpx_image_unittest.cs
//
// Description: Unit tests for logic in vpx_image.cs.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 25 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Xunit;

namespace Vpx.Net.UnitTest
{
    public class vpx_image_unittest
    {
        /// <summary>
        /// Tests that a vpx_image instance can be successfully created.
        /// </summary>
        [Fact]
        public void InstantiateImageUnitTest()
        {
            vpx_image_t img = vpx_image_t.vpx_img_alloc(null, vpx_img_fmt_t.VPX_IMG_FMT_I420, 640, 480, 1);

            Assert.NotNull(img);

            vpx_image_t.vpx_img_free(img);
        }

        /// <summary>
        /// Tests that the active rectangle (viewport) on an image can be adjusted.
        /// </summary>
        [Fact]
        public void SetRectangleUnitTest()
        {
            vpx_image_t img = vpx_image_t.vpx_img_alloc(null, vpx_img_fmt_t.VPX_IMG_FMT_I420, 640, 480, 1);

            int res = vpx_image_t.vpx_img_set_rect(img, 0, 0, 320, 240);

            Assert.Equal(0, res);

            vpx_image_t.vpx_img_free(img);
        }

        /// <summary>
        /// Tests that the flipping an image completes.
        /// </summary>
        [Fact]
        public void FlipUnitTest()
        {
            vpx_image_t img = vpx_image_t.vpx_img_alloc(null, vpx_img_fmt_t.VPX_IMG_FMT_I420, 640, 480, 1);

            vpx_image_t.vpx_img_flip(img);

            vpx_image_t.vpx_img_free(img);
        }
    }
}
