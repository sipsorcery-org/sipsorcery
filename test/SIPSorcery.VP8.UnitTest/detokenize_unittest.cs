//-----------------------------------------------------------------------------
// Filename: detokenize_unittest.cs
//
// Description: Unit tests for logic in detokenize.cs.
// The coefficient tests can be matched against the C++ unit tests in 
// default_coef_probs_unittest.cpp.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 09 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public unsafe class detokenize_unittest
    {
        /// <summary>
        /// Checks that getting the zero index coefficients results in the correct array.
        /// </summary>
        [Fact]
        public void GetZeroIndexCoefficientTest()
        {
            byte[,] expected =
             {
                { 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128 },
                { 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128 },
                { 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128 }
            };

            byte[,] coef_probs = new byte[detokenize.NUM_CTX, detokenize.NUM_PROBAS];
            int coefIndex = entropy.COEF_BANDS * entropy.PREV_COEF_CONTEXTS * entropy.ENTROPY_NODES;

            Buffer.BlockCopy(default_coef_probs_c.default_coef_probs, coefIndex * 0, coef_probs, 0, detokenize.NUM_CTX * detokenize.NUM_PROBAS);

            for(int i=0; i< detokenize.NUM_CTX; i++)
            {
                for(int j=0; j< detokenize.NUM_PROBAS; j++)
                {
                    Assert.Equal(expected[i, j], coef_probs[i, j]);
                }
            }
        }

        /// <summary>
        /// Checks that getting the one index coefficients results in the correct array.
        /// </summary>
        [Fact]
        public void GetOneIndexCoefficientTest()
        {
            byte[,] expected =
             {
              { 198, 35, 237, 223, 193, 187, 162, 160, 145, 155, 62 },
                { 131, 45, 198, 221, 172, 176, 220, 157, 252, 221, 1 },
                { 68, 47, 146, 208, 149, 167, 221, 162, 255, 223, 128 }
            };

            byte[,] coef_probs = new byte[detokenize.NUM_CTX, detokenize.NUM_PROBAS];
            int coefIndex = entropy.COEF_BANDS * entropy.PREV_COEF_CONTEXTS * entropy.ENTROPY_NODES;

            Buffer.BlockCopy(default_coef_probs_c.default_coef_probs, coefIndex * 1, coef_probs, 0, detokenize.NUM_CTX * detokenize.NUM_PROBAS);

            for (int i = 0; i < detokenize.NUM_CTX; i++)
            {
                for (int j = 0; j < detokenize.NUM_PROBAS; j++)
                {
                    Assert.Equal(expected[i, j], coef_probs[i, j]);
                }
            }
        }
    }
}
