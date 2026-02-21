//-----------------------------------------------------------------------------
// Filename: yv12config_unittest.cs
//
// Description: Unit tests for logic in yv12config.cs.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 29 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Xunit;

namespace Vpx.Net.UnitTest
{
    public unsafe class yv12config_unittest
    {
        /// <summary>
        /// Tests that allocating a yv12config struct works correctly.
        /// </summary>
        [Fact]
        public void YV12ConfigAllocateTest()
        {
            YV12_BUFFER_CONFIG srcConfig = new YV12_BUFFER_CONFIG();

            int result = yv12config.vp8_yv12_alloc_frame_buffer(ref srcConfig, 640, 480, 0);

            Assert.Equal(0, result);

            yv12config.vp8_yv12_de_alloc_frame_buffer(ref srcConfig);
        }

        /// <summary>
        /// Tests that copying a yv12config struct works correctly (and will
        /// hopefully generate an error if a future addition breaks the default copy logic).
        /// </summary>
        [Fact]
        public void YV12ConfigCopyTest()
        {
            YV12_BUFFER_CONFIG srcConfig = new YV12_BUFFER_CONFIG();

            int result = yv12config.vp8_yv12_alloc_frame_buffer(ref srcConfig, 640, 480, 0);

            Assert.Equal(0, result);

            YV12_BUFFER_CONFIG dstConfig = srcConfig;

            Assert.Equal(srcConfig, dstConfig);

            //yv12config.vp8_yv12_de_alloc_frame_buffer(ref dstConfig);
        }

        /// <summary>
        /// Tests that resizing an existing yv12config struct works correctly (and will
        /// hopefully generate an error if a future addition breaks the default copy logic).
        /// </summary>
        [Fact]
        public void YV12ConfigResizeTest()
        {
            YV12_BUFFER_CONFIG srcConfig = new YV12_BUFFER_CONFIG();

            int result = yv12config.vp8_yv12_alloc_frame_buffer(ref srcConfig, 640, 480, 0);

            Assert.Equal(0, result);
            Assert.True(srcConfig.buffer_alloc != null);
            Assert.True(srcConfig.buffer_alloc_sz > 0);

            // The native memory buffer must be released before re-using a YV12 buffer with a different size.

            yv12config.vp8_yv12_de_alloc_frame_buffer(ref srcConfig);

            Assert.True(srcConfig.buffer_alloc == null);
            Assert.Equal(0UL, srcConfig.buffer_alloc_sz);

            // Re-uses the same buffer but with a different size frame.
            int reallocResult = yv12config.vp8_yv12_alloc_frame_buffer(ref srcConfig, 1280, 720, 0);

            Assert.Equal(0, reallocResult);
            Assert.True(srcConfig.buffer_alloc != null);
            Assert.True(srcConfig.buffer_alloc_sz > 0);

            yv12config.vp8_yv12_de_alloc_frame_buffer(ref srcConfig);
        }
    }
}
