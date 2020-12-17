/******************************************************************************
* Filename:detokenize_unittest.h
*
* Description:
* Unit tests for the logic in:
*  - detokenize.c
*
* Author:
* Aaron Clauson (aaron@sipsorcery.com)
*
* History:
* 07 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
*
* License: Public Domain (no warranty, use at own risk)
/******************************************************************************/

#include "pch.h"
#include "CppUnitTest.h"
#include "vp8/common/alloccommon.h"
#include "vp8/decoder/detokenize.h"
#include "vp8/decoder/onyxd_int.h"
#include "vp8/decoder/treereader.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace VpxUnitTests
{
	TEST_CLASS(detokenize_unittest)
	{
	public:

		TEST_METHOD(ResetMacroBlockTokensTest)
		{
			struct frame_buffers fb;
			int res = vp8_create_decoder_instances(&fb, NULL);
			Assert::AreEqual((int)VPX_CODEC_OK, res);

			res = vp8_alloc_frame_buffers(&fb.pbi[0]->common, 640, 480);
			Assert::AreEqual(0, res);

			fb.pbi[0]->mb.above_context = fb.pbi[0]->common.above_context;
			fb.pbi[0]->mb.left_context = &fb.pbi[0]->common.left_context;
			fb.pbi[0]->mb.mode_info_context = fb.pbi[0]->common.mi;

			vp8_reset_mb_tokens_context(&fb.pbi[0]->mb);
		}

		TEST_METHOD(DecodeMBTokensTest)
		{
			struct frame_buffers fb;
			int res = vp8_create_decoder_instances(&fb, NULL);
			Assert::AreEqual((int)VPX_CODEC_OK, res);

			res = vp8_alloc_frame_buffers(&fb.pbi[0]->common, 640, 480);
			Assert::AreEqual(0, res);

			fb.pbi[0]->mb.above_context = fb.pbi[0]->common.above_context;
			fb.pbi[0]->mb.left_context = &fb.pbi[0]->common.left_context;
			fb.pbi[0]->mb.mode_info_context = fb.pbi[0]->common.mi;
			fb.pbi[0]->mb.current_bc = &fb.pbi[0]->mbc[0];

			vp8_decode_mb_tokens(fb.pbi[0], &fb.pbi[0]->mb);
		}
	};
}
