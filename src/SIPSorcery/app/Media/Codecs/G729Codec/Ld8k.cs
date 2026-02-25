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
namespace SIPSorcery.Media.G729Codec;

public class Ld8k
{
    /**
     * gain adjustment factor
     */
    public const float AGC_FAC = 0.9875f;

    /**
     * gain adjustment factor
     */
    public const float AGC_FAC1 = 1.0f - AGC_FAC;

    public const float ALPHA = -6.0f;

    public const float BETA = 1.0f;

    /**
     * Definition of zero-bit in bit-stream.
     */
    public const short BIT_0 = 0x007f;

    /*---------------------------------------------------------------------------*
     * Constants for bitstream packing                                           *
     *---------------------------------------------------------------------------*/
    /**
     * Definition of one-bit in bit-stream.
     */
    public const short BIT_1 = 0x0081;

    public const float CONST12 = 1.2f;

    /*---------------------------------------------------------------------------*
     * Constants for fixed codebook.                                            *
     *---------------------------------------------------------------------------*/
    /**
     * Size of correlation matrix
     */
    public const int DIM_RR = 616;

    /**
     * resolution for fractionnal delay
     */
    public const int F_UP_PST = 8;

    public const int L_INTER4 = 4;

    public const int UP_SAMP = 3;

    public const int FIR_SIZE_ANA = UP_SAMP * L_INTER4 + 1;

    public const int L_INTER10 = 10;

    public const int FIR_SIZE_SYN = UP_SAMP * L_INTER10 + 1;

    /**
     * Largest floating point number
     */
    public const float FLT_MAX_G729 = float.MaxValue;

    /**
     * Largest floating point number
     */
    public const float FLT_MIN_G729 = -FLT_MAX_G729;

    /**
     * maximum adaptive codebook gain
     */
    public const float GAIN_PIT_MAX = 1.2f;

    /**
     * LT weighting factor
     */
    public const float GAMMA_G = 0.5f;

    public const float GAMMA1_0 = 0.98f;

    public const float GAMMA1_1 = 0.94f;

    /*---------------------------------------------------------------------------
     * Constants for postfilter.
     *---------------------------------------------------------------------------
     */
    /* short term pst parameters :  */
    /**
     * denominator weighting factor
     */
    public const float GAMMA1_PST = 0.7f;

    public const float GAMMA2_0_H = 0.7f;

    public const float GAMMA2_0_L = 0.4f;

    public const float GAMMA2_1 = 0.6f;

    /**
     * numerator  weighting factor
     */
    public const float GAMMA2_PST = 0.55f;

    /**
     * tilt weighting factor when k1 &lt; 0
     */
    public const float GAMMA3_MINUS = 0.9f;

    /**
     * tilt weighting factor when k1 &gt; 0
     */
    public const float GAMMA3_PLUS = 0.2f;

    public const float GAP1 = 0.0012f;

    public const float GAP2 = 0.0006f;

    public const float GAP3 = 0.0392f;

    /**
     * Maximum pitch gain if taming is needed
     */
    public const float GP0999 = 0.9999f;

    /*--------------------------------------------------------------------------*
     * Constants for taming procedure.                           *
     *--------------------------------------------------------------------------*/
    /**
     * Maximum pitch gain if taming is needed
     */
    public const float GPCLIP = 0.95f;

    /**
     * Maximum pitch gain if taming is needed
     */
    public const float GPCLIP2 = 0.94f;

    /**
     * Resolution of lsp search.
     */
    public const int GRID_POINTS = 60;

    public const float INV_COEF = -0.032623f;

    public const int L_SUBFR = 40;

    public const float INV_L_SUBFR = 1.0f / L_SUBFR; /* =0.025 */

    /**
     * LPC update frame size
     */
    public const int L_FRAME = 80;
    /**
     * Length for pitch interpolation
     */

    /**
     * upsampling ration for pitch search
     */

    /**
     * Length of filter for interpolation.
     */
    public const int L_INTERPOL = 10 + 1;

    public const float L_LIMIT = 0.005f;

    /**
     * Samples of next frame needed for LPC ana.
     */
    public const int L_NEXT = 40;
    /**
     * Sub-frame size
     */

    /* long term pst parameters :   */
    /**
     * Sub-frame size + 1
     */
    public const int L_SUBFRP1 = L_SUBFR + 1;

    /**
     * Total size of speech buffer
     */
    public const int L_TOTAL = 240;

    /*---------------------------------------------------------------------------*
     * Constants for lpc analysis and lsp quantizer.                             *
     *---------------------------------------------------------------------------*/
    /**
     * LPC analysis window size.
     */
    public const int L_WINDOW = 240;

    public const int LH2_L = 16;

    public const int LH_UP_L = LH2_L / 2;

    public const int LH2_S = 4;

    public const int LH_UP_S = LH2_S / 2;
    /**
     * length of long interp. subfilters
     */

    public const int LH2_L_P1 = LH2_L + 1;
    /**
     * length of short interp. subfilters
     */

    /**
     * impulse response length
     */
    public const int LONG_H_ST = 20;

    /**
     * LPC order.
     */
    public const int M = 10;

    public const float M_LIMIT = 3.135f;

    /**
     * MA prediction order for LSP.
     */
    public const int MA_NP = 4;

    public const int MAX_TIME = 75;

    /*-------------------------------------------------------------------------
     * gain quantizer  constants
     *-------------------------------------------------------------------------
     */
    /**
     * Average innovation energy
     */
    public const float MEAN_ENER = 36.0f;

    /* Array sizes */
    public const int PIT_MAX = 143;

    public const int MEM_RES2 = PIT_MAX + 1 + LH_UP_L;

    /**
     * LT gain minimum
     */
    public const float MIN_GPLT = 1.0f / (1.0f + GAMMA_G);

    /**
     * Number of modes for MA prediction.
     */
    public const int MODE = 2;

    /**
     * LPC order+1.
     */
    public const int MP1 = M + 1;

    /**
     * Size of vectors for cross-correlation between 2 pulses
     */
    public const int MSIZE = 64;

    /**
     * Number of positions for each pulse
     */
    public const int NB_POS = 8;

    /**
     * LPC order / 2.
     */
    public const int NC = M / 2;

    /**
     * Number of entries in first stage.
     */
    public const int NC0_B = 7;

    public const int NC0 = 1 << NC0_B;
    /**
     * Number of bits in first stage.
     */

    /**
     * Number of entries in second stage.
     */
    public const int NC1_B = 5;

    public const int NC1 = 1 << NC1_B;
    /**
     * Number of bits in second stage.
     */

    /**
     * Pre-selecting order for #1
     */
    public const int NCAN1 = 4;

    /**
     * Pre-selecting order for #2
     */
    public const int NCAN2 = 8;

    /**
     * Codebook 1 size
     */
    public const int NCODE1_B = 3;

    public const int NCODE1 = 1 << NCODE1_B;
    /**
     * Number of Codebook-bit
     */

    /**
     * Codebook 2 size
     */
    public const int NCODE2_B = 4;

    public const int NCODE2 = 1 << NCODE2_B;
    /**
     * Number of Codebook-bit
     */

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
File : LD8K.H
Used for the floating point version of G.729 main body
(not for G.729A)
*/

    /*---------------------------------------------------------------------------
     * ld8k.h - include file for all ITU-T 8 kb/s CELP coder routines
     *---------------------------------------------------------------------------
     */

    public const float PI = 3.14159265358979323846f;

    /**
     * pi*0.04
     */
    public const float PI04 = PI * 0.04f;

    /**
     * pi*0.92
     */
    public const float PI92 = PI * 0.92f;
    /**
     * Maximum pitch lag in samples
     */

    /*----------------------------------------------------------------------------
     * Constants for long-term predictor
     *----------------------------------------------------------------------------
     */
    /**
     * Minimum pitch lag in samples
     */
    public const int PIT_MIN = 20;

    /**
     * Number of parameters per 10 ms frame.
     */
    public const int PRM_SIZE = 11;

    /**
     * Bits per frame.
     */
    public const int SERIAL_SIZE = 82;

    /**
     * Maximum value of pitch sharpening
     */
    public const float SHARPMAX = 0.7945f;

    /**
     * minimum value of pitch sharpening
     */
    public const float SHARPMIN = 0.2f;

    public const int SIZ_RES2 = MEM_RES2 + L_SUBFR;

    public const int SIZ_TAB_HUP_L = (F_UP_PST - 1) * LH2_L;

    public const int SIZ_TAB_HUP_S = (F_UP_PST - 1) * LH2_S;

    public const int SIZ_Y_UP = (F_UP_PST - 1) * L_SUBFRP1;

    /**
     * Size of bitstream frame.
     */
    public const short SIZE_WORD = 80;

    /**
     * Step betweem position of the same pulse.
     */
    public const int STEP = 5;

    /**
     * Definition of frame erasure flag.
     */
    public const short SYNC_WORD = 0x6b21;

    /**
     * threshold LT pst switch off
     */
    public const float THRESCRIT = 0.5f;

    /**
     * Error threshold taming
     */
    public const float THRESH_ERR = 60000.0f;

    public const float THRESH_H1 = 0.65f;

    public const float THRESH_H2 = 0.43f;

    /*-------------------------------------------------------------------------
     *  pwf constants
     *-------------------------------------------------------------------------
     */

    public const float THRESH_L1 = -1.74f;

    public const float THRESH_L2 = -1.52f;

    /*--------------------------------------------------------------------------*
     * Example values for threshold and approximated worst case complexity:     *
     *                                                                          *
     *     threshold=0.40   maxtime= 75   extra=30   Mips =  6.0                *
     *--------------------------------------------------------------------------*/
    public const float THRESHFCB = 0.40f;

    /**
     * Threshold to favor smaller pitch lags
     */
    public const float THRESHPIT = 0.85f;

    /**
     * resolution of fractional delays
     */
}
