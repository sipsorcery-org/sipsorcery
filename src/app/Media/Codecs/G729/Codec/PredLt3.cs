/*
 * Copyright @ 2015 Atlassian Pty Ltd
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
/*
 * WARNING: The use of G.729 may require a license fee and/or royalty fee in
 * some countries and is licensed by
 * <a href="http://www.sipro.com">SIPRO Lab Telecom</a>.
 */

/**
 * @author Lubomir Marinov (translation of ITU-T C source code to Java)
 */
namespace GroovyCodecs.G729.Codec
{
    internal class PredLt3
    {

        /* ITU-T G.729 Software Package Release 2 (November 2006) */
        /*
   ITU-T G.729 Annex C - Reference C code for floating point
                         implementation of G.729
                         Version 1.01 of 15.September.98
*/

        /*
----------------------------------------------------------------------
                    COPYRIGHT NOTICE
----------------------------------------------------------------------
   ITU-T G.729 Annex C ANSI C source code
   Copyright (C) 1998, AT&T, France Telecom, NTT, University of
   Sherbrooke.  All rights reserved.

----------------------------------------------------------------------
*/

        /*
 File : PRED_LT3.C
 Used for the floating point version of both
 G.729 main body and G.729A
*/

        /**
 * Compute the result of long term prediction with fractional
 * interpolation of resolution 1/3.
 *
 * On return exc[0..L_subfr-1] contains the interpolated signal
 *   (adaptive codebook excitation)
 *
 * @param exc            in/out: excitation vector, exc[0:l_sub-1] = out
 * @param exc_offset     input: excitation vector offset
 * @param t0             input : pitch lag
 * @param frac           input : Fraction of pitch lag (-1, 0, 1)  / 3
 * @param l_subfr        input : length of subframe.
 */

        public static void pred_lt_3(
            float[] exc,
            int exc_offset,
            int t0,
            int frac,
            int l_subfr
        )
        {
            var L_INTER10 = Ld8k.L_INTER10;
            var UP_SAMP = Ld8k.UP_SAMP;
            var inter_3l = TabLd8k.inter_3l;

            int i, j, k;
            float s;
            int x0, x1, x2, c1, c2;

            x0 = exc_offset - t0;

            frac = -frac;
            if (frac < 0)
            {
                frac += UP_SAMP;
                x0--;
            }

            for (j = 0; j < l_subfr; j++)
            {
                x1 = x0;
                x0++;
                x2 = x0;
                c1 = frac;
                c2 = UP_SAMP - frac;

                s = 0.0f;
                for (i = 0, k = 0; i < L_INTER10; i++, k += UP_SAMP)
                    s += exc[x1 - i] * inter_3l[c1 + k] + exc[x2 + i] * inter_3l[c2 + k];

                exc[exc_offset + j] = s;
            }
        }
    }
}