/******************************************************************************
* Filename: boolhuff_unittest.cpp
*
* Description:
* Unit tests for the logic in:
*  - boolhuff.c & .h (boolean encoder)
*  - dboolhuff.c & .h (boolean decoder)
*
* Author:
* Aaron Clauson (aaron@sipsorcery.com)
*
* History:
* 01 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
*
* License: Public Domain (no warranty, use at own risk)
/******************************************************************************/

#include "pch.h"
#include "CppUnitTest.h"
#include "vp8/decoder/onyxd_int.h"
#include "vp8/encoder/boolhuff.h"
#include "vp8/decoder/dboolhuff.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace VpxUnitTests
{
  TEST_CLASS(boolhuff_unittest)
  {
  public:

    /// <summary>
    /// Tests that a single bit gets read correctly.
    /// </summary>
    TEST_METHOD(ReadBitTest)
    {
      struct frame_buffers fb;
      int res = vp8_create_decoder_instances(&fb, NULL);

      Assert::AreEqual((int)VPX_CODEC_OK, res);

      BOOL_DECODER bc = fb.pbi[0]->mbc[8];
      int bit = vp8dx_decode_bool(&bc, 128);

      Assert::AreEqual(1, bit);
    }

    /// <summary>
    /// Tests that a single bit gets encoded correctly.
    /// </summary>
    TEST_METHOD(EncodeBitTest)
    {
      const int BUF_SZ = 10;

      BOOL_CODER bc;
      unsigned char buf[BUF_SZ];
      memset(buf, 0, BUF_SZ);
      vp8_start_encode(&bc, buf, buf + 10);
      vp8_encode_bool(&bc, 0, 128);
      vp8_stop_encode(&bc);

      struct vpx_internal_error_info err;
      err._setjmp = 0;
      int res = validate_buffer(buf, BUF_SZ - 1, buf + BUF_SZ, &err);

      Assert::AreEqual(1, res);
    }

    /// <summary>
    /// Tests that an invalid length error condition is handled correctly.
    /// </summary>
    TEST_METHOD(EncodeBitInvalidBufferErrorTest)
    {
      const int BUF_SZ = 10;

      BOOL_CODER bc;
      unsigned char buf[BUF_SZ];
      memset(buf, 0, BUF_SZ);
      vp8_start_encode(&bc, buf, buf + 10);
      vp8_encode_bool(&bc, 0, 128);
      vp8_stop_encode(&bc);

      struct vpx_internal_error_info err;
      err._setjmp = 0;
      int res = validate_buffer(buf, BUF_SZ, buf + BUF_SZ, &err);

      Assert::AreEqual(0, res);
      Assert::AreEqual(1, err.has_detail);
    }

    /// <summary>
    /// Derived from https://github.com/webmproject/libvpx/blob/master/test/vp8_boolcoder_test.cc.
    /// </summary>
    TEST_METHOD(TestBitIO)
    {
      for (int method = 0; method <= 7; ++method) {  // we generate various proba
        const int kBitsToTest = 1000;
        uint8_t probas[kBitsToTest];

        for (int i = 0; i < kBitsToTest; ++i) {
          const int parity = i & 1;
          /* clang-format off */
          probas[i] =
            (method == 0) ? 0 : (method == 1) ? 255 :
            (method == 2) ? 128 :
            (method == 3) ? 64 :
            (method == 4) ? (parity ? 0 : 255) :
            // alternate between low and high proba:
            (method == 5) ? (parity ? 96 : 255 - 96) :
            (method == 6) ?
            (parity ? 64 : 255 - 64) :
            (parity ? 21 : 255 - 32);
          /* clang-format on */
        }
        for (int bit_method = 0; bit_method <= 3; ++bit_method) {
          const int random_seed = 6432;
          const int kBufferSize = 10000;
          //ACMRandom bit_rnd(random_seed);
          BOOL_CODER bw;
          uint8_t bw_buffer[kBufferSize];
          vp8_start_encode(&bw, bw_buffer, bw_buffer + kBufferSize);

          int bit = (bit_method == 0) ? 0 : (bit_method == 1) ? 1 : 0;
          for (int i = 0; i < kBitsToTest; ++i) {
            if (bit_method == 2) {
              bit = (i & 1);
            }
            else if (bit_method == 3) {
              bit = 0;// bit_rnd(2);
            }
            vp8_encode_bool(&bw, bit, static_cast<int>(probas[i]));
          }

          vp8_stop_encode(&bw);
          // vp8dx_bool_decoder_fill() may read into uninitialized data that
          // isn't used meaningfully, but may trigger an MSan warning.
          memset(bw_buffer + bw.pos, 0, sizeof(VP8_BD_VALUE) - 1);

          BOOL_DECODER br;
          //encrypt_buffer(bw_buffer, kBufferSize);
          //vp8dx_start_decode(&br, bw_buffer, kBufferSize, test_decrypt_cb,
          //  reinterpret_cast<void*>(bw_buffer));
          vp8dx_start_decode(&br, bw_buffer, kBufferSize, NULL, NULL);

          //bit_rnd.Reset(random_seed);
          for (int i = 0; i < kBitsToTest; ++i) {
            if (bit_method == 2) {
              bit = (i & 1);
            }
            else if (bit_method == 3) {
              bit = 0;// bit_rnd(2);
            }
            /*GTEST_ASSERT_EQ(vp8dx_decode_bool(&br, probas[i]), bit)
              << "pos: " << i << " / " << kBitsToTest
              << " bit_method: " << bit_method << " method: " << method;*/

            Assert::AreEqual(vp8dx_decode_bool(&br, probas[i]), bit);
          }
        }
      }
    }
  };
}
