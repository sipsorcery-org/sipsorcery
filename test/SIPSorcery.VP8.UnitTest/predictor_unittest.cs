//-----------------------------------------------------------------------------
// Filename: predictor_unittest.cs
//
// Description: Unit tests for predictor functions in reconintra.cs and 
// reconintra4x4.cs.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public unsafe class predictor_unittest
    {
        /// <summary>
        /// Tests calling the predictor 16x16 functions.
        /// </summary>
        [Fact]
        public unsafe void Predictor16x16Test()
        {
            reconintra.vp8_init_intra_predictors();

            byte[] dst = new byte[16];
            byte[] above = new byte[16];

            fixed (byte* pDst = dst, pAbove = above)
            {
                reconintra.pred[(int)MB_PREDICTION_MODE.V_PRED, (int)reconintra.PredictionSizes.SIZE_16](pDst, 640, pAbove, null);
            }
        }
    }
}
