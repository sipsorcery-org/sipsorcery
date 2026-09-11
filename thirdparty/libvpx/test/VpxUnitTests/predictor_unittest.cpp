/******************************************************************************
* Filename: predictor_unittest.cpp
*
* Description:
* Unit tests for the prediction logic in:
*  - reconintra.c
*  - reconintra4x4.h
*  - And associated headers.
* 
* Author:
* Aaron Clauson (aaron@sipsorcery.com)
*
* History:
* 13 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
*
* License: Public Domain (no warranty, use at own risk)
/******************************************************************************/

#include "pch.h"
#include "CppUnitTest.h"
#include "vpx_scale/yv12config.h"
#include "vp8/common/reconintra.h"
#include "vp8/common/reconintra4x4.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace VpxUnitTests
{
	TEST_CLASS(predictor_unittest)
	{
	public:

		TEST_METHOD(Predictor16x16Test)
		{
			vp8_init_intra_predictors();

			unsigned char dst[16];
			unsigned char above[16];

			

		}
	};
}
