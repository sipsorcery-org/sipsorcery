/******************************************************************************
* Filename: decodemv_unittest.cpp
*
* Description:
* Unit tests for the logic in:
*  - decodemv.c
*  - decodemv.h
*
* Author:
* Aaron Clauson (aaron@sipsorcery.com)
*
* History:
* 15 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
*
* License: Public Domain (no warranty, use at own risk)
/******************************************************************************/

#include "pch.h"
#include "imgutils.h"
#include "strutils.h"
#include "CppUnitTest.h"
#include "vp8/common/alloccommon.h"
#include "vp8/decoder/decodemv.h"
#include "vp8/decoder/onyxd_int.h"
#include "vpx/internal/vpx_codec_internal.h"
#include "vpx/vp8cx.h"
#include "vpx_mem/vpx_mem.h"

#include <fstream>
#include <iostream>
#include <streambuf>
#include <string>
#include <vector>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace VpxUnitTests
{
	TEST_CLASS(decodemv_unittest)
	{
	public:

		TEST_METHOD(DecodeKeyFrameMovementVectorTest)
		{
      vpx_codec_enc_cfg_t vpxConfig;

      // Initialise codec configuration.
      vpx_codec_err_t res = vpx_codec_enc_config_default(vpx_codec_vp8_cx(), &vpxConfig, 0);
      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      vpx_codec_ctx_t decoder;
      res = vpx_codec_dec_init(&decoder, vpx_codec_vp8_dx(), NULL, 0);

      Assert::AreEqual((int)VPX_CODEC_OK, (int)res);

      std::string encFrameHex = "5043009d012a8002e00102c708";
      std::vector<uint8_t> encFramefData = ParseHex(encFrameHex);

      VP8D_COMP* pbi = (VP8D_COMP * )vpx_memalign(32, sizeof(VP8D_COMP));
      memset(pbi, 0, sizeof(VP8D_COMP));
      vp8_create_common(&pbi->common);
      
      int allocRes = vp8_alloc_frame_buffers(&pbi->common, 640, 480);
      Assert::AreEqual(0, allocRes);

      vp8_decode_mode_mvs(pbi);
		}
	};
}
