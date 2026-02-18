/******************************************************************************
* Filename: vpx_mem_unittest.cpp
*
* Description:
* Unit tests for the logic in:
*  - vpx_mem.c
*  - vpx_mem.h
*
* Author:
* Aaron Clauson (aaron@sipsorcery.com)
*
* History:
* 29 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
*
* License: Public Domain (no warranty, use at own risk)
/******************************************************************************/

#include "pch.h"
#include "CppUnitTest.h"
#include "vpx_mem/vpx_mem.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace VpxUnitTests
{
	TEST_CLASS(vpx_mem_unittest)
	{
	public:

		/// <summary>
		/// Tests that a standard memory allocation and free works without errors.
		/// </summary>
		TEST_METHOD(VpxMemAllocateTest)
		{
			void* mem = vpx_malloc(100);

			Assert::IsNotNull(mem);

			vpx_free(mem);
		}

		/// <summary>
		/// Tests that an aligned memory allocation and free works without errors.
		/// </summary>
		TEST_METHOD(VpxMemAllocateAlignTest)
		{
			void* mem = vpx_memalign(16, 50);

			Assert::IsNotNull(mem);

			vpx_free(mem);
		}
	};
}
