/******************************************************************************
* Filename: default_coef_probs_unittest.cpp
*
* Description:
* Unit tests for the logic in:
*  - default_coef_probs.h
* Testing that retrieving slices of the default co-efficients array works the
* same in C and C#.
*
* Author:
* Aaron Clauson (aaron@sipsorcery.com)
*
* History:
* 09 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
*
* License: Public Domain (no warranty, use at own risk)
/******************************************************************************/

#include "pch.h"
#include "CppUnitTest.h"
//#include "../../vp8_minimal/vp8/common/default_coef_probs.h"
#include "vp8/common/entropy.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

#define NUM_CTX 3
#define NUM_PROBAS 11

/* for const-casting */
typedef const uint8_t (*ProbaArray)[NUM_CTX][NUM_PROBAS];

namespace VpxUnitTests
{

#include "vp8/common/default_coef_probs.h"

	TEST_CLASS(default_coef_probs_unittest)
	{
	public:

		/// <summary>
		/// Tests that using the array index to get a 2x2 slice of the default coefficients
		/// works as expected.
		/// </summary>
		TEST_METHOD(TestZeroIndexSlice)
		{
			ProbaArray coef_probs = default_coef_probs[0];

			for (int i = 0; i < NUM_CTX; i++) {
				for (int j = 0; j < NUM_PROBAS; j++) {
					Assert::AreEqual((uint8_t)128, (*coef_probs)[i][j]);
				}
			}
		}

		/// <summary>
		/// Tests that using the array index to get a 2x2 slice of the default coefficients
		/// works as expected.
		/// </summary>
		TEST_METHOD(TestOneIndexSlice)
		{
			uint8_t expected[NUM_CTX][NUM_PROBAS] = {
				{ 198, 35, 237, 223, 193, 187, 162, 160, 145, 155, 62 },
				{ 131, 45, 198, 221, 172, 176, 220, 157, 252, 221, 1 },
				{ 68, 47, 146, 208, 149, 167, 221, 162, 255, 223, 128 }
			};

			ProbaArray coef_probs = default_coef_probs[1];

			for (int i = 0; i < NUM_CTX; i++) {
				for (int j = 0; j < NUM_PROBAS; j++) {
					Assert::AreEqual(expected[i][j], (*coef_probs)[i][j]);
				}
			}
		}

		TEST_METHOD(TestSlicePointerArithmetic)
		{
			/* Block 1, Coeff Band 0 */
			uint8_t expected[NUM_CTX][NUM_PROBAS] = {
				{ 198, 35, 237, 223, 193, 187, 162, 160, 145, 155, 62 },
				{ 131, 45, 198, 221, 172, 176, 220, 157, 252, 221, 1 },
				{ 68, 47, 146, 208, 149, 167, 221, 162, 255, 223, 128 }
			};

			ProbaArray coef_probs = default_coef_probs[1];

			for (int i = 0; i < NUM_CTX; i++) {
				for (int j = 0; j < NUM_PROBAS; j++) {
					Assert::AreEqual(expected[i][j], (*coef_probs)[i][j]);
				}
			}

			uint8_t block1Coeff1Row2[NUM_CTX][NUM_PROBAS] = {
				{ 81, 99, 181, 242, 176, 190, 249, 202, 255, 255, 128 },
				{ 1, 129, 232, 253, 214, 197, 242, 196, 255, 255, 128 },
				{ 99, 121, 210, 250, 201, 198, 255, 202, 128, 128, 128 }
			};

			const uint8_t* q = coef_probs[1][2];

			for (int i = 0; i < NUM_CTX; i++) {
				for (int j = 0; j < NUM_PROBAS; j++) {
					Assert::AreEqual(block1Coeff1Row2[i][j], *q++);
				}
			}

			uint8_t expectedAfter[NUM_CTX][NUM_PROBAS] = {
				/* Block 2, Coeff Band 2 */
				{ 1, 24, 239, 251, 218, 219, 255, 205, 128, 128, 128 },
				{ 201, 51, 219, 255, 196, 186, 128, 128, 128, 128, 128 },
				{ 69, 46, 190, 239, 201, 218, 255, 228, 128, 128, 128 }
			};

			coef_probs = &default_coef_probs[2][2];

			for (int i = 0; i < NUM_CTX; i++) {
				for (int j = 0; j < NUM_PROBAS; j++) {
					Assert::AreEqual(expectedAfter[i][j], (*coef_probs)[i][j]);
				}
			}

			const uint8_t* p = coef_probs[0][0];

			for (int i = 0; i < NUM_CTX; i++) {
				for (int j = 0; j < NUM_PROBAS; j++) {
					Assert::AreEqual(expectedAfter[i][j], *p++);
				}
			}
		}
	};
}
