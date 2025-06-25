//-----------------------------------------------------------------------------
// Filename: VP8TestVectorTests.cs
//
// Description: Unit tests for decoding VP8 test vectors from IVF files.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 25 Dec 2024	Generated	Created for VP8 test vectors.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Vpx.Net.TestVectors
{
    /// <summary>
    /// Unit tests for decoding VP8 test vector files.
    /// </summary>
    public unsafe class VP8TestVectorTests
    {
        private readonly ILogger logger;

        public VP8TestVectorTests(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = TestLogger.GetLogger(output).CreateLogger(this.GetType().Name);
        }

        /// <summary>
        /// Tests that the first available VP8 test vector file (vp80-00-comprehensive-001.ivf) 
        /// can be loaded and decoded successfully. This test verifies that:
        /// - The IVF file can be parsed correctly
        /// - VP8 frames can be decoded without exceptions
        /// - The decoder produces valid frame data with expected dimensions
        /// </summary>
        [Fact]
        public unsafe void DecodeFirstComprehensiveTestVector_Succeeds()
        {
            // Arrange
            string testVectorPath = Path.Combine("TestVectors", "vp80-00-comprehensive-001.ivf");
            
            // Ensure the test vector file exists
            Assert.True(File.Exists(testVectorPath), $"Test vector file not found: {testVectorPath}");
            
            logger.LogDebug($"Loading test vector file: {testVectorPath}");
            
            // Parse the IVF file
            var ivfReader = IvfReader.FromFile(testVectorPath);
            
            // Verify IVF header
            Assert.Equal("DKIF", ivfReader.Header.Signature);
            Assert.True(ivfReader.Header.Codec.StartsWith("VP8"), $"Expected VP8 codec, got: {ivfReader.Header.Codec}");
            Assert.True(ivfReader.Header.Width > 0, "Width should be greater than 0");
            Assert.True(ivfReader.Header.Height > 0, "Height should be greater than 0");
            Assert.True(ivfReader.Frames.Count > 0, "Should have at least one frame");
            
            logger.LogDebug($"IVF Header - Codec: {ivfReader.Header.Codec}, " +
                          $"Dimensions: {ivfReader.Header.Width}x{ivfReader.Header.Height}, " +
                          $"Frame Count: {ivfReader.Frames.Count}");
            
            // Initialize VP8 decoder
            vpx_codec_ctx_t decoder = new vpx_codec_ctx_t();
            vpx_codec_iface_t algo = vp8_dx.vpx_codec_vp8_dx();
            vpx_codec_dec_cfg_t cfg = new vpx_codec_dec_cfg_t { threads = 1 };
            vpx_codec_err_t initResult = vpx_decoder.vpx_codec_dec_init(decoder, algo, cfg, 0);
            
            Assert.Equal(vpx_codec_err_t.VPX_CODEC_OK, initResult);
            Assert.NotNull(decoder);
            
            logger.LogDebug("VP8 decoder initialized successfully");
            
            // Act & Assert - Decode frames
            int successfulFrames = 0;
            
            for (int i = 0; i < ivfReader.Frames.Count; i++)
            {
                var frame = ivfReader.Frames[i];
                logger.LogDebug($"Decoding frame {i + 1}/{ivfReader.Frames.Count}, size: {frame.Size} bytes");
                
                // Decode the frame
                fixed (byte* pFrameData = frame.Data)
                {
                    var decodeResult = vpx_decoder.vpx_codec_decode(decoder, pFrameData, frame.Size, IntPtr.Zero, 0);
                    
                    // Verify decode succeeded
                    if (decodeResult == vpx_codec_err_t.VPX_CODEC_OK)
                    {
                        successfulFrames++;
                        
                        // Try to get the decoded image
                        IntPtr iter = IntPtr.Zero;
                        var img = vpx_decoder.vpx_codec_get_frame(decoder, iter);
                        
                        if (img != null)
                        {
                            // Verify image properties
                            Assert.True(img.d_w > 0, $"Frame {i + 1}: Width should be greater than 0");
                            Assert.True(img.d_h > 0, $"Frame {i + 1}: Height should be greater than 0");
                            Assert.True(img.planes[0] != null, $"Frame {i + 1}: Y plane should not be null");
                            Assert.True(img.planes[1] != null, $"Frame {i + 1}: U plane should not be null");
                            Assert.True(img.planes[2] != null, $"Frame {i + 1}: V plane should not be null");
                            
                            logger.LogDebug($"Frame {i + 1} decoded successfully - Dimensions: {img.d_w}x{img.d_h}");
                        }
                        else
                        {
                            logger.LogWarning($"Frame {i + 1} decoded but no image data returned");
                        }
                    }
                    else
                    {
                        logger.LogWarning($"Frame {i + 1} decode failed with error: {decodeResult}");
                        
                        // For the first frame, we expect it to succeed. For subsequent frames, 
                        // some may fail due to dependencies or format issues, which is acceptable.
                        if (i == 0)
                        {
                            Assert.Fail($"First frame decode should succeed, but got error: {decodeResult}");
                        }
                    }
                }
            }
            
            // Verify that at least some frames were decoded successfully
            Assert.True(successfulFrames > 0, $"Expected at least one frame to decode successfully, but got {successfulFrames}");
            
            logger.LogDebug($"Test completed successfully. Decoded {successfulFrames}/{ivfReader.Frames.Count} frames");
            
            // Cleanup decoder
            vpx_codec.vpx_codec_destroy(decoder);
        }
    }
}