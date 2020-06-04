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
 * Functions corr_xy2() and cor_h_x().
 *
 * @author Lubomir Marinov (translation of ITU-T C source code to Java)
 */
namespace GroovyCodecs.G729.Codec
{
    internal class CorFunc
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
 File : COR_FUNC.C
 Used for the floating point version of both
 G.729 main body and G.729A
*/

        /**
 * Compute the correlation products needed for gain computation.
 *
 * @param xn        input : target vector x[0:l_subfr]
 * @param y1        input : filtered adaptive codebook vector
 * @param y2        input : filtered 1st codebook innovation
 * @param g_coeff   output: <y2,y2> , -2<xn,y2> , and 2<y1,y2>
 */

        public static void corr_xy2(
            float[] xn,
            float[] y1,
            float[] y2,
            float[] g_coeff
        )
        {
            var L_SUBFR = Ld8k.L_SUBFR;

            float y2y2, xny2, y1y2;
            int i;

            y2y2 = 0.01f;
            for (i = 0; i < L_SUBFR; i++) y2y2 += y2[i] * y2[i];
            g_coeff[2] = y2y2;

            xny2 = 0.01f;
            for (i = 0; i < L_SUBFR; i++) xny2 += xn[i] * y2[i];
            g_coeff[3] = -2.0f * xny2;

            y1y2 = 0.01f;
            for (i = 0; i < L_SUBFR; i++) y1y2 += y1[i] * y2[i];
            g_coeff[4] = 2.0f * y1y2;
        }

        /**
 * Compute  correlations of input response h[] with the target vector X[].
 *
 * @param h     (i) :Impulse response of filters
 * @param x     (i) :Target vector
 * @param d     (o) :Correlations between h[] and x[]
 */

        public static void cor_h_x(
            float[] h,
            float[] x,
            float[] d
        )
        {
            var L_SUBFR = Ld8k.L_SUBFR;

            int i, j;
            float s;

            for (i = 0; i < L_SUBFR; i++)
            {
                s = 0.0f;
                for (j = i; j < L_SUBFR; j++)
                    s += x[j] * h[j - i];
                d[i] = s;
            }
        }
    }
}