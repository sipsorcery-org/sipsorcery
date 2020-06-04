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
    internal class Lspgetq
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
 File : LSPGETQ.C
 Used for the floating point version of both
 G.729 main body and G.729A
*/

        /**
 * Reconstruct quantized LSP parameter and check the stabilty
 *
 * @param lspcb1        input : first stage LSP codebook
 * @param lspcb2        input : Second stage LSP codebook
 * @param code0         input : selected code of first stage
 * @param code1         input : selected code of second stage
 * @param code2         input : selected code of second stage
 * @param fg            input : MA prediction coef.
 * @param freq_prev     input : previous LSP vector
 * @param lspq          output: quantized LSP parameters
 * @param fg_sum        input : present MA prediction coef.
 */

        public static void lsp_get_quant(
            float[][ /* M */] lspcb1,
            float[][ /* M */] lspcb2,
            int code0,
            int code1,
            int code2,
            float[][ /* M */] fg,
            float[][ /* M */] freq_prev,
            float[] lspq,
            float[] fg_sum
        )
        {
            var GAP1 = Ld8k.GAP1;
            var GAP2 = Ld8k.GAP2;
            var M = Ld8k.M;
            var NC = Ld8k.NC;

            int j;
            var buf = new float[M];

            for (j = 0; j < NC; j++)
                buf[j] = lspcb1[code0][j] + lspcb2[code1][j];
            for (j = NC; j < M; j++)
                buf[j] = lspcb1[code0][j] + lspcb2[code2][j];

            /* check */
            lsp_expand_1_2(buf, GAP1);
            lsp_expand_1_2(buf, GAP2);

            /* reconstruct quantized LSP parameters */
            lsp_prev_compose(buf, lspq, fg, freq_prev, fg_sum);

            lsp_prev_update(buf, freq_prev);

            lsp_stability(lspq); /* check the stabilty */
        }

        /**
 * Check for lower (0-4)
 *
 * @param buf   in/out: lsp vectors
 * @param gap   input : gap
 */

        public static void lsp_expand_1(
            float[] buf, /* */
            float gap
        )
        {
            var NC = Ld8k.NC;

            int j;
            float diff, tmp;

            for (j = 1; j < NC; j++)
            {
                diff = buf[j - 1] - buf[j];
                tmp = (diff + gap) * 0.5f;
                if (tmp > 0)
                {
                    buf[j - 1] -= tmp;
                    buf[j] += tmp;
                }
            }
        }

        /**
 * Check for higher (5-9)
 *
 * @param buf   in/out: lsp vectors
 * @param gap   input : gap
 */

        public static void lsp_expand_2(
            float[] buf,
            float gap
        )
        {
            var M = Ld8k.M;
            var NC = Ld8k.NC;

            int j;
            float diff, tmp;

            for (j = NC; j < M; j++)
            {
                diff = buf[j - 1] - buf[j];
                tmp = (diff + gap) * 0.5f;
                if (tmp > 0)
                {
                    buf[j - 1] -= tmp;
                    buf[j] += tmp;
                }
            }
        }

        /**
 *
 * @param buf   in/out: LSP parameters
 * @param gap   input:  gap
 */

        public static void lsp_expand_1_2(
            float[] buf,
            float gap
        )
        {
            var M = Ld8k.M;

            int j;
            float diff, tmp;

            for (j = 1; j < M; j++)
            {
                diff = buf[j - 1] - buf[j];
                tmp = (diff + gap) * 0.5f;
                if (tmp > 0)
                {
                    buf[j - 1] -= tmp;
                    buf[j] += tmp;
                }
            }
        }

        /*
  Functions which use previous LSP parameter (freq_prev).
*/

        /**
 * Compose LSP parameter from elementary LSP with previous LSP.
 *
 * @param lsp_ele       (i) Q13 : LSP vectors
 * @param lsp           (o) Q13 : quantized LSP parameters
 * @param fg            (i) Q15 : MA prediction coef.
 * @param freq_prev     (i) Q13 : previous LSP vector
 * @param fg_sum        (i) Q15 : present MA prediction coef.
 */
        private static void lsp_prev_compose(
            float[] lsp_ele,
            float[] lsp,
            float[][ /* M */] fg,
            float[][ /* M */] freq_prev,
            float[] fg_sum
        )
        {
            var M = Ld8k.M;
            var MA_NP = Ld8k.MA_NP;

            int j, k;

            for (j = 0; j < M; j++)
            {
                lsp[j] = lsp_ele[j] * fg_sum[j];
                for (k = 0; k < MA_NP; k++) lsp[j] += freq_prev[k][j] * fg[k][j];
            }
        }

        /**
 * Extract elementary LSP from composed LSP with previous LSP
 *
 * @param lsp           (i) Q13 : unquantized LSP parameters
 * @param lsp_ele       (o) Q13 : target vector
 * @param fg            (i) Q15 : MA prediction coef.
 * @param freq_prev     (i) Q13 : previous LSP vector
 * @param fg_sum_inv    (i) Q12 : inverse previous LSP vector
 */

        public static void lsp_prev_extract(
            float[ /* M */] lsp,
            float[ /* M */] lsp_ele,
            float[ /* MA_NP */][ /* M */] fg,
            float[ /* MA_NP */][ /* M */] freq_prev,
            float[ /* M */] fg_sum_inv
        )
        {
            var M = Ld8k.M;
            var MA_NP = Ld8k.MA_NP;

            int j, k;

            /*----- compute target vectors for each MA coef.-----*/
            for (j = 0; j < M; j++)
            {
                lsp_ele[j] = lsp[j];
                for (k = 0; k < MA_NP; k++)
                    lsp_ele[j] -= freq_prev[k][j] * fg[k][j];
                lsp_ele[j] *= fg_sum_inv[j];
            }
        }

        /**
 * Update previous LSP parameter
 *
 * @param lsp_ele       input : LSP vectors
 * @param freq_prev     input/output: previous LSP vectors
 */

        public static void lsp_prev_update(
            float[ /* M */] lsp_ele,
            float[ /* MA_NP */][ /* M */] freq_prev
        )
        {
            var M = Ld8k.M;
            var MA_NP = Ld8k.MA_NP;

            int k;

            for (k = MA_NP - 1; k > 0; k--)
                Util.copy(freq_prev[k - 1], freq_prev[k], M);

            Util.copy(lsp_ele, freq_prev[0], M);
        }

        /**
 * Check stability of lsp coefficients
 *
 * @param buf in/out: LSP parameters
 */
        private static void lsp_stability(
            float[] buf
        )
        {
            var GAP3 = Ld8k.GAP3;
            var L_LIMIT = Ld8k.L_LIMIT;
            var M = Ld8k.M;
            var M_LIMIT = Ld8k.M_LIMIT;

            int j;
            float diff, tmp;

            for (j = 0; j < M - 1; j++)
            {
                diff = buf[j + 1] - buf[j];
                if (diff < 0.0f)
                {
                    tmp = buf[j + 1];
                    buf[j + 1] = buf[j];
                    buf[j] = tmp;
                }
            }

            if (buf[0] < L_LIMIT)
                buf[0] = L_LIMIT;
            for (j = 0; j < M - 1; j++)
            {
                diff = buf[j + 1] - buf[j];
                if (diff < GAP3)
                    buf[j + 1] = buf[j] + GAP3;
            }

            if (buf[M - 1] > M_LIMIT)
                buf[M - 1] = M_LIMIT;
        }
    }
}