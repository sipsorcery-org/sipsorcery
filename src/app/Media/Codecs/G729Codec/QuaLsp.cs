﻿/*
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
 * Functions related to the quantization of LSP's.
 *
 * @author Lubomir Marinov (translation of ITU-T C source code to Java)
 */
using System;

namespace SIPSorcery.Media.G729Codec
{
    internal class QuaLsp
    {

        /**
 * previous LSP vector(init)
 */
        private static readonly float[ /* M */] FREQ_PREV_RESET =
        {
            0.285599f,
            0.571199f,
            0.856798f,
            1.142397f,
            1.427997f,
            1.713596f,
            1.999195f,
            2.284795f,
            2.570394f,
            2.855993f
        }; /* PI*(float)(j+1)/(float)(M+1) */

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
 File : QUA_LSP.C
 Used for the floating point version of both
 G.729 main body and G.729A
*/

        /* static memory */
        /**
 * previous LSP vector
 */
        private readonly float[][] freq_prev = new float[Ld8k.MA_NP][ /* Ld8k.M */];

        public QuaLsp()
        {
            // need this to initialize freq_prev
            for (var i = 0; i < freq_prev.Length; i++)
            {
                freq_prev[i] = new float[Ld8k.M];
            }
        }

        /**
 * @param lsp       (i) : Unquantized LSP
 * @param lsp_q     (o) : Quantized LSP
 * @param ana       (o) : indexes
 */

        public void qua_lsp(
            float[] lsp,
            float[] lsp_q,
            int[] ana
        )
        {
            var M = Ld8k.M;

            int i;
            float[] lsf = new float[M], lsf_q = new float[M]; /* domain 0.0<= lsf <PI */

            /* Convert LSPs to LSFs */

            for (i = 0; i < M; i++)
            {
                lsf[i] = (float)Math.Acos(lsp[i]);
            }

            lsp_qua_cs(lsf, lsf_q, ana);

            /* Convert LSFs to LSPs */

            for (i = 0; i < M; i++)
            {
                lsp_q[i] = (float)Math.Cos(lsf_q[i]);
            }
        }

        /**
 * Set the previous LSP vector
 */

        public void lsp_encw_reset()
        {
            var M = Ld8k.M;
            var MA_NP = Ld8k.MA_NP;

            int i;
            for (i = 0; i < MA_NP; i++)
            {
                Util.copy(FREQ_PREV_RESET, freq_prev[i], M);
            }
        }

        /**
 * Lsp quantizer
 *
 * @param flsp_in       input : Original LSP parameters
 * @param lspq_out      output: Quantized LSP parameters
 * @param code          output: codes of the selected LSP
 */
        private void lsp_qua_cs(
            float[] flsp_in,
            float[] lspq_out,
            int[] code
        )
        {
            var M = Ld8k.M;
            var fg = TabLd8k.fg;
            var fg_sum = TabLd8k.fg_sum;
            var fg_sum_inv = TabLd8k.fg_sum_inv;
            var lspcb1 = TabLd8k.lspcb1;
            var lspcb2 = TabLd8k.lspcb2;

            var wegt = new float[M]; /* weight coef. */

            get_wegt(flsp_in, wegt);

            relspwed(
                flsp_in,
                wegt,
                lspq_out,
                lspcb1,
                lspcb2,
                fg,
                freq_prev,
                fg_sum,
                fg_sum_inv,
                code);
        }

        /**
 *
 * @param lsp            input: unquantized LSP parameters
 * @param wegt           input: weight coef.
 * @param lspq           output:quantized LSP parameters
 * @param lspcb1         input : first stage LSP codebook
 * @param lspcb2         input: Second stage LSP codebook
 * @param fg             input: MA prediction coef.
 * @param freq_prev      input: previous LSP vector
 * @param fg_sum         input: present MA prediction coef.
 * @param fg_sum_inv     input: inverse coef.
 * @param code_ana       output:codes of the selected LSP
 */
        private void relspwed(
            float[] lsp,
            float[] wegt,
            float[] lspq,
            float[][ /* M */] lspcb1,
            float[][ /* M */] lspcb2,
            float[ /* MODE */][ /* MA_NP */][ /* M */] fg,
            float[ /* MA_NP */][ /* M */] freq_prev,
            float[ /* MODE */][ /* M */] fg_sum,
            float[ /* MODE */][ /* M */] fg_sum_inv,
            int[] code_ana
        )
        {
            var GAP1 = Ld8k.GAP1;
            var GAP2 = Ld8k.GAP2;
            var M = Ld8k.M;
            var MODE = Ld8k.MODE;
            var NC = Ld8k.NC;
            var NC0_B = Ld8k.NC0_B;
            var NC1_B = Ld8k.NC1_B;

            int mode, j;
            int index, mode_index;
            var cand = new int[MODE];
            int cand_cur;
            int[] tindex1 = new int[MODE], tindex2 = new int[MODE];
            var tdist = new float[MODE];
            var rbuf = new float[M];
            var buf = new float[M];

            for (mode = 0; mode < MODE; mode++)
            {

                Lspgetq.lsp_prev_extract(lsp, rbuf, fg[mode], freq_prev, fg_sum_inv[mode]);

                /*----- search the first stage lsp codebook -----*/
                cand_cur = lsp_pre_select(rbuf, lspcb1);
                cand[mode] = cand_cur;

                /*----- search the second stage lsp codebook (lower 0-4) ----- */
                index = lsp_select_1(rbuf, lspcb1[cand_cur], wegt, lspcb2);

                tindex1[mode] = index;

                for (j = 0; j < NC; j++)
                {
                    buf[j] = lspcb1[cand_cur][j] + lspcb2[index][j];
                }

                Lspgetq.lsp_expand_1(buf, GAP1); /* check */

                /*----- search the second stage lsp codebook (Higher 5-9) ----- */
                index = lsp_select_2(rbuf, lspcb1[cand_cur], wegt, lspcb2);

                tindex2[mode] = index;

                for (j = NC; j < M; j++)
                {
                    buf[j] = lspcb1[cand_cur][j] + lspcb2[index][j];
                }

                Lspgetq.lsp_expand_2(buf, GAP1); /* check */

                /* check */
                Lspgetq.lsp_expand_1_2(buf, GAP2);

                tdist[mode] = lsp_get_tdist(wegt, buf, rbuf, fg_sum[mode]); /* calculate the distortion */

            } /* mode */

            mode_index = lsp_last_select(tdist); /* select the codes */

            /* pack codes for lsp parameters */
            code_ana[0] = (mode_index << NC0_B) | cand[mode_index];
            code_ana[1] = (tindex1[mode_index] << NC1_B) | tindex2[mode_index];

            /* reconstruct quantized LSP parameter and check the stabilty */
            Lspgetq.lsp_get_quant(
                lspcb1,
                lspcb2,
                cand[mode_index],
                tindex1[mode_index],
                tindex2[mode_index],
                fg[mode_index],
                freq_prev,
                lspq,
                fg_sum[mode_index]);
        }

        /**
 * Select the code of first stage lsp codebook
 *
 * @param rbuf      input : target vetor
 * @param lspcb1    input : first stage lsp codebook
 * @return          selected code
 */
        private int lsp_pre_select(
            float[] rbuf,
            float[][ /* M */] lspcb1
        )
        {
            var FLT_MAX_G729 = Ld8k.FLT_MAX_G729;
            var M = Ld8k.M;
            var NC0 = Ld8k.NC0;

            int i, j;
            float dmin, dist, temp;

            /* calculate the distortion */

            var cand = 0; /*output: selected code            */
            dmin = FLT_MAX_G729;
            for (i = 0; i < NC0; i++)
            {
                dist = 0.0f;
                for (j = 0; j < M; j++)
                {
                    temp = rbuf[j] - lspcb1[i][j];
                    dist += temp * temp;
                }

                if (dist < dmin)
                {
                    dmin = dist;
                    cand = i;
                }
            }

            return cand;
        }

        /**
 * Select the code of second stage lsp codebook (lower 0-4)
 *
 * @param rbuf      input : target vector
 * @param lspcb1    input : first stage lsp codebook
 * @param wegt      input : weight coef.
 * @param lspcb2    input : second stage lsp codebook
 * @return          selected codebook index
 */
        private int lsp_select_1(
            float[] rbuf,
            float[] lspcb1,
            float[] wegt,
            float[][ /* M */] lspcb2
        )
        {
            var FLT_MAX_G729 = Ld8k.FLT_MAX_G729;
            var M = Ld8k.M;
            var NC = Ld8k.NC;
            var NC1 = Ld8k.NC1;

            int j, k1;
            var buf = new float[M];
            float dist, dmin, tmp;

            for (j = 0; j < NC; j++)
            {
                buf[j] = rbuf[j] - lspcb1[j];
            }

            var index = 0; /*output: selected codebook index     */
            dmin = FLT_MAX_G729;
            for (k1 = 0; k1 < NC1; k1++)
            {
                /* calculate the distortion */
                dist = 0.0f;
                for (j = 0; j < NC; j++)
                {
                    tmp = buf[j] - lspcb2[k1][j];
                    dist += wegt[j] * tmp * tmp;
                }

                if (dist < dmin)
                {
                    dmin = dist;
                    index = k1;
                }
            }

            return index;
        }

        /**
 * Select the code of second stage lsp codebook (higher 5-9)
 *
 * @param rbuf      input : target vector
 * @param lspcb1    input : first stage lsp codebook
 * @param wegt      input : weighting coef.
 * @param lspcb2    input : second stage lsp codebook
 * @return          selected codebook index
 */
        private int lsp_select_2(
            float[] rbuf,
            float[] lspcb1,
            float[] wegt,
            float[][ /* M */] lspcb2
        )
        {
            var FLT_MAX_G729 = Ld8k.FLT_MAX_G729;
            var M = Ld8k.M;
            var NC = Ld8k.NC;
            var NC1 = Ld8k.NC1;

            int j, k1;
            var buf = new float[M];
            float dist, dmin, tmp;

            for (j = NC; j < M; j++)
            {
                buf[j] = rbuf[j] - lspcb1[j];
            }

            var index = 0; /*output: selected codebook index    */
            dmin = FLT_MAX_G729;
            for (k1 = 0; k1 < NC1; k1++)
            {
                dist = 0.0f;
                for (j = NC; j < M; j++)
                {
                    tmp = buf[j] - lspcb2[k1][j];
                    dist += wegt[j] * tmp * tmp;
                }

                if (dist < dmin)
                {
                    dmin = dist;
                    index = k1;
                }
            }

            return index;
        }

        /**
 * Calculate the distortion
 *
 * @param wegt      input : weight coef.
 * @param buf       input : candidate LSP vector
 * @param rbuf      input : target vector
 * @param fg_sum    input : present MA prediction coef.
 * @return          distortion
 */
        private float lsp_get_tdist(
            float[] wegt,
            float[] buf,
            float[] rbuf,
            float[] fg_sum
        )
        {
            var M = Ld8k.M;

            int j;
            float tmp;

            var tdist = 0.0f; /*output: distortion            */
            for (j = 0; j < M; j++)
            {
                tmp = (buf[j] - rbuf[j]) * fg_sum[j];
                tdist += wegt[j] * tmp * tmp;
            }

            return tdist;
        }

        /**
 * Select the mode
 *
 * @param tdist     distortion
 * @return          the selected mode
 */
        private int lsp_last_select(
            float[] tdist
        )
        {
            var mode_index = 0; /*output: the selected mode  */
            if (tdist[1] < tdist[0])
            {
                mode_index = 1;
            }

            return mode_index;
        }

        /**
 * Compute lsp weights
 *
 * @param flsp      input : M LSP parameters
 * @param wegt      output: M weighting coefficients
 */
        private void get_wegt(
            float[] flsp,
            float[] wegt
        )
        {
            var CONST12 = Ld8k.CONST12;
            var M = Ld8k.M;
            var PI04 = Ld8k.PI04;
            var PI92 = Ld8k.PI92;

            int i;
            float tmp;

            tmp = flsp[1] - PI04 - 1.0f;
            if (tmp > 0.0f)
            {
                wegt[0] = 1.0f;
            }
            else
            {
                wegt[0] = tmp * tmp * 10.0f + 1.0f;
            }

            for (i = 1; i < M - 1; i++)
            {
                tmp = flsp[i + 1] - flsp[i - 1] - 1.0f;
                if (tmp > 0.0f)
                {
                    wegt[i] = 1.0f;
                }
                else
                {
                    wegt[i] = tmp * tmp * 10.0f + 1.0f;
                }
            }

            tmp = PI92 - flsp[M - 2] - 1.0f;
            if (tmp > 0.0f)
            {
                wegt[M - 1] = 1.0f;
            }
            else
            {
                wegt[M - 1] = tmp * tmp * 10.0f + 1.0f;
            }

            wegt[4] *= CONST12;
            wegt[5] *= CONST12;
        }
    }
}