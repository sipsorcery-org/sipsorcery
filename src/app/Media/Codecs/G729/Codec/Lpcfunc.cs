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
using System;

namespace GroovyCodecs.G729.Codec
{
    internal class Lpcfunc
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
 File : LPCFUNC.C
 Used for the floating point version of G.729 main body
 (not for G.729A)
*/

        /**
 * Convert LSPs to predictor coefficients a[]
 *
 * @param lsp       input : lsp[0:M-1]
 * @param a         output: predictor coeffs a[0:M], a[0] = 1.
 * @param a_offset  input: predictor coeffs a offset.
 */
        private static void lsp_az(
            float[] lsp,
            float[] a,
            int a_offset
        )
        {
            var M = Ld8k.M;
            var NC = Ld8k.NC;

            float[] f1 = new float[NC + 1], f2 = new float[NC + 1];
            int i, j;

            get_lsp_pol(lsp, 0, f1);
            get_lsp_pol(lsp, 1, f2);

            for (i = NC; i > 0; i--)
            {
                f1[i] += f1[i - 1];
                f2[i] -= f2[i - 1];
            }

            a[a_offset + 0] = 1.0f;
            for (i = 1, j = M; i <= NC; i++, j--)
            {
                a[a_offset + i] = 0.5f * (f1[i] + f2[i]);
                a[a_offset + j] = 0.5f * (f1[i] - f2[i]);
            }
        }

        /**
 * Find the polynomial F1(z) or F2(z) from the LSFs
 *
 * @param lsp           input : line spectral freq. (cosine domain)
 * @param lsp_offset    input : line spectral freq offset
 * @param f             output: the coefficients of F1 or F2
 */
        private static void get_lsp_pol(
            float[] lsp,
            int lsp_offset,
            float[] f
        )
        {
            var NC = Ld8k.NC;

            float b;
            int i, j;

            f[0] = 1.0f;
            b = -2.0f * lsp[lsp_offset + 0];
            f[1] = b;
            for (i = 2; i <= NC; i++)
            {
                b = -2.0f * lsp[lsp_offset + 2 * i - 2];
                f[i] = b * f[i - 1] + 2.0f * f[i - 2];
                for (j = i - 1; j > 1; j--)
                    f[j] += b * f[j - 1] + f[j - 2];
                f[1] += b;
            }
        }

        /**
 * Convert from lsf[0..M-1 to lsp[0..M-1]
 *
 * @param lsf   input :  lsf
 * @param lsp   output: lsp
 * @param m     input : length
 */
        private static void lsf_lsp(
            float[] lsf,
            float[] lsp,
            int m
        )
        {
            int i;
            for (i = 0; i < m; i++)
                lsp[i] = (float)Math.Cos(lsf[i]);
        }

        /**
 * Convert from lsp[0..M-1 to lsf[0..M-1]
 *
 * @param lsp   input :  lsp coefficients
 * @param lsf   output:  lsf (normalized frequencies
 * @param m     input: length
 */
        private static void lsp_lsf(
            float[] lsp,
            float[] lsf,
            int m
        )
        {
            int i;

            for (i = 0; i < m; i++)
                lsf[i] = (float)Math.Acos(lsp[i]);
        }

        /**
 * Weighting of LPC coefficients  ap[i]  =  a[i] * (gamma ** i)
 *
 * @param a          input : lpc coefficients a[0:m]
 * @param a_offset   input : lpc coefficients  offset
 * @param gamma      input : weighting factor
 * @param m          input : filter order
 * @param ap         output: weighted coefficients ap[0:m]
 */

        public static void weight_az(
            float[] a,
            int a_offset,
            float gamma,
            int m,
            float[] ap
        )
        {
            float fac;
            int i;

            ap[0] = a[a_offset + 0];
            fac = gamma;
            for (i = 1; i < m; i++)
            {
                ap[i] = fac * a[a_offset + i];
                fac *= gamma;
            }

            ap[m] = fac * a[a_offset + m];
        }

        /**
 * Interpolated M LSP parameters and convert to M+1 LPC coeffs
 *
 * @param lsp_old    input : LSPs for past frame (0:M-1)
 * @param lsp_new    input : LSPs for present frame (0:M-1)
 * @param az         output: filter parameters in 2 subfr (dim 2(m+1))
 */

        public static void int_qlpc(
            float[] lsp_old,
            float[] lsp_new,
            float[] az
        )
        {
            var M = Ld8k.M;

            int i;
            var lsp = new float[M];

            for (i = 0; i < M; i++)
                lsp[i] = lsp_old[i] * 0.5f + lsp_new[i] * 0.5f;

            lsp_az(lsp, az, 0);
            lsp_az(lsp_new, az, M + 1);
        }
        /**
 * Interpolated M LSP parameters and convert to M+1 LPC coeffs
 *
 * @param lsp_old   input : LSPs for past frame (0:M-1)
 * @param lsp_new   input : LSPs for present frame (0:M-1)
 * @param lsf_int   output: interpolated lsf coefficients
 * @param lsf_new   input : LSFs for present frame (0:M-1)
 * @param az        output: filter parameters in 2 subfr (dim 2(m+1))
 */

        public static void int_lpc(
            float[] lsp_old,
            float[] lsp_new,
            float[] lsf_int,
            float[] lsf_new,
            float[] az
        )
        {
            var M = Ld8k.M;

            int i;
            var lsp = new float[M];

            for (i = 0; i < M; i++)
                lsp[i] = lsp_old[i] * 0.5f + lsp_new[i] * 0.5f;

            lsp_az(lsp, az, 0);

            lsp_lsf(lsp, lsf_int, M);
            lsp_lsf(lsp_new, lsf_new, M);
        }
    }
}