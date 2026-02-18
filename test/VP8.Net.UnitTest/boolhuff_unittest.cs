//-----------------------------------------------------------------------------
// Filename: boolhuff_unittest.cs
//
// Description: Unit tests for bit encoding mechanism. Main logic is in 
// boolhuff and dboolhuff.cs.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 02 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public unsafe class boolhuff_unittest
    {
        /// <summary>
        /// Boolean entropy encode/decode test.
        /// </summary>
        /// <remarks>
        /// Based on https://github.com/webmproject/libvpx/blob/master/test/vp8_boolcoder_test.cc.
        /// </remarks>
        [Fact]
        public unsafe void TestBitIO()
        {
            Random rnd = new Random();
            for (int method = 0; method <= 7; ++method)
            {  // we generate various proba
                const int kBitsToTest = 1000;
                byte[] probas = new byte[kBitsToTest];

                for (int i = 0; i < kBitsToTest; ++i)
                {
                    int parity = i & 1;
                    probas[i] = (byte)(
                      (method == 0) ? 0 : (method == 1) ? 255 :
                      (method == 2) ? 128 :
                      (method == 3) ? rnd.Next(0, 256) :
                      (method == 4) ? (parity > 0 ? 0 : 255) :
                      // alternate between low and high proba:
                      (method == 5) ? (parity > 0 ? rnd.Next(0, 128) : 255 - rnd.Next(0, 128)) :
                      (method == 6) ?
                      (parity > 0 ? rnd.Next(0, 64) : 255 - rnd.Next(0, 64)) :
                      (parity > 0 ? rnd.Next(0, 32) : 255 - rnd.Next(0, 32)));
                }
                for (int bit_method = 0; bit_method <= 3; ++bit_method)
                {
                    const int random_seed = 6432;
                    const int kBufferSize = 10000;
                    rnd = new Random(random_seed);
                    BOOL_CODER bw = new BOOL_CODER();
                    byte[] bw_buffer = new byte[kBufferSize];
                    boolhuff.vp8_start_encode(ref bw, bw_buffer, kBufferSize);

                    int bit = (bit_method == 0) ? 0 : (bit_method == 1) ? 1 : 0;
                    for (int i = 0; i < kBitsToTest; ++i)
                    {
                        if (bit_method == 2)
                        {
                            bit = (i & 1);
                        }
                        else if (bit_method == 3)
                        {
                            bit = rnd.Next(0, 1);
                        }
                        boolhuff.vp8_encode_bool(ref bw, bit, probas[i]);
                    }

                    boolhuff.vp8_stop_encode(ref bw);
                    // vp8dx_bool_decoder_fill() may read into uninitialized data that
                    // isn't used meaningfully, but may trigger an MSan warning.
                    //memset(bw_buffer + bw.pos, 0, sizeof(VP8_BD_VALUE) - 1);
                    for (int i = (int)bw.pos; i < dboolhuff.VP8_BD_VALUE_SIZE; i++)
                    {
                        bw_buffer[i] = 0;
                    }

                    BOOL_DECODER br = new BOOL_DECODER();
                    //encrypt_buffer(bw_buffer, kBufferSize);
                    //vp8dx_start_decode(&br, bw_buffer, kBufferSize, test_decrypt_cb,
                    //  reinterpret_cast<void*>(bw_buffer));
                    dboolhuff.vp8dx_start_decode(ref br, bw_buffer, kBufferSize);

                    //bit_rnd.Reset(random_seed);
                    rnd = new Random(random_seed);
                    for (int i = 0; i < kBitsToTest; ++i)
                    {
                        if (bit_method == 2)
                        {
                            bit = (i & 1);
                        }
                        else if (bit_method == 3)
                        {
                            bit = rnd.Next(0, 1);
                        }
                        /*GTEST_ASSERT_EQ(vp8dx_decode_bool(&br, probas[i]), bit)
                          << "pos: " << i << " / " << kBitsToTest
                          << " bit_method: " << bit_method << " method: " << method;*/

                        Assert.Equal(dboolhuff.vp8dx_decode_bool(ref br, probas[i]), bit);
                    }
                }
            }
        }
    }
}
