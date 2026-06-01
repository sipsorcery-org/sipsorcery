/******************************************************************************
* Filename: treereader_unittest.cpp
*
* Description:
* Unit tests for the logic in:
*  - treereader.h
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
#include "vp8/decoder/treereader.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace VpxUnitTests
{
	TEST_CLASS(treereader_unittest)
	{
	public:

		TEST_METHOD(ReadBitTest)
		{
			struct frame_buffers fb;
			int res = vp8_create_decoder_instances(&fb, NULL);

			Assert::AreEqual((int)VPX_CODEC_OK, res);

			vp8_reader bc = fb.pbi[0]->mbc[8];
			int bit = vp8_read_bit(&bc);

			Assert::AreEqual(1, bit);
		}
	};
}
