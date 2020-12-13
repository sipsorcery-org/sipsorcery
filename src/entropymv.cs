//-----------------------------------------------------------------------------
// Filename: entropymv.cs
//
// Description: Port of:
//  - entropymv.h
//  - entropymv.c
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

/*
 *  Copyright (c) 2010 The WebM project authors. All Rights Reserved.
 *
 *  Use of this source code is governed by a BSD-style license
 *  that can be found in the LICENSE file in the root of the source
 *  tree. An additional intellectual property rights grant can be found
 *  in the file PATENTS.  All contributing project authors may
 *  be found in the AUTHORS file in the root of the source tree.
 */

namespace Vpx.Net
{
    enum MV_ENUM
    {
        mv_max = 1023,             /* max absolute value of a MV component */
        MVvals = (2 * mv_max) + 1, /* # possible values "" */
        mvfp_max = 255, /* max absolute value of a full pixel MV component */
        MVfpvals = (2 * mvfp_max) + 1, /* # possible full pixel MV values */

        mvlong_width = 10, /* Large MVs have 9 bit magnitudes */
        mvnum_short = 8,   /* magnitudes 0 through 7 */

        /* probability offsets for coding each MV component */

        mvpis_short = 0, /* short (<= 7) vs long (>= 8) */
        MVPsign,         /* sign for non-zero */
        MVPshort,        /* 8 short values = 7-position tree */

        MVPbits = MVPshort + mvnum_short - 1, /* mvlong_width long value bits */
        MVPcount = MVPbits + mvlong_width     /* (with independent probabilities) */
    };

    public class MV_CONTEXT
    {
        public byte[] prob = new byte[(int)MV_ENUM.MVPcount]; /* often come in row, col pairs */
    }

    public static class entropymv
    {
        public static MV_CONTEXT[] vp8_mv_update_probs = new MV_CONTEXT[]{
           new MV_CONTEXT{ prob =  new byte[] {
              237,
              246,
              253, 253, 254, 254, 254, 254, 254,
              254, 254, 254, 254, 254, 250, 250, 252, 254, 254
             } },
          new MV_CONTEXT{ prob =  new byte[] {
              231,
              243,
              245, 253, 254, 254, 254, 254, 254,
              254, 254, 254, 254, 254, 251, 251, 254, 254, 254
          } }
        };

        public static MV_CONTEXT[] vp8_default_mv_context = new MV_CONTEXT[]{
           new MV_CONTEXT{ prob =  new byte[] {
              /* row */
              162,                                            /* is short */
              128,                                            /* sign */
              225, 146, 172, 147, 214, 39, 156,               /* short tree */
              128, 129, 132, 75, 145, 178, 206, 239, 254, 254 /* long bits */
          } },

          new MV_CONTEXT{ prob =  new byte[] {
              /* same for column */
              164,                                            /* is short */
              128,                                            /**/
              204, 170, 119, 235, 140, 230, 228,              /**/
              128, 130, 130, 74, 148, 180, 203, 236, 254, 254 /* long bits */
          } }
        };
    }
}
