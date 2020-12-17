/******************************************************************************
* Filename: blockd_unittest.cpp
*
* Description: Unit testes relating to macro blocks.
*
* Author:
* Aaron Clauson (aaron@sipsorcery.com)
*
* History:
* 11 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
*
* License: Public Domain (no warranty, use at own risk)
/******************************************************************************/

#include "pch.h"
#include "strutils.h"
#include "CppUnitTest.h"
#include "vp8/common/alloccommon.h"
#include "vp8/common/reconintra.h"
#include "vp8/decoder/onyxd_int.h"
#include "vp8/encoder/block.h"
#include "vpx/vp8cx.h"

#include <iostream>
#include <string>
#include <vector>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace VpxUnitTests
{
  TEST_CLASS(blockd_unittest)
  {
  public:

    TEST_METHOD(InitialiseMacroBlockTest)
    {
      struct frame_buffers fb;
      int res = vp8_create_decoder_instances(&fb, NULL);
      Assert::AreEqual((int)VPX_CODEC_OK, res);

      res = vp8_alloc_frame_buffers(&fb.pbi[0]->common, 640, 480);
      Assert::AreEqual(0, res);

      Assert::IsTrue(fb.pbi[0]->mb.block[24].qcoeff != 0);
    }

    TEST_METHOD(MacroBlockLayoutTest)
    {
      MACROBLOCKD mb;
      vp8_init_intra_predictors();
      Assert::IsTrue(true);
    }
  };
}
