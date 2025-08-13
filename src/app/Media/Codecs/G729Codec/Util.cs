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
 * Auxiliary functions.
 *
 * @author Lubomir Marinov (translation of ITU-T C source code to Java)
 */
using System.IO;

namespace SIPSorcery.Media.G729Codec
{
    public class Util
    {

        /* Random generator  */
        private static short seed = 21845;

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
 File : UTIL.C
 Used for the floating point version of both
 G.729 main body and G.729A
*/

        /**
 * Assigns the value zero to element of the specified array of floats.
 * The number of components set to zero equal to the length argument.
 *
 * @param x     (o)    : vector to clear
 * @param L     (i)    : length of vector
 */
        public static void set_zero(
            float[] x,
            int L
        )
        {
            set_zero(x, 0, L);
        }

        /**
 * Assigns the value zero to element of the specified array of floats.
 * The number of components set to zero equal to the length argument.
 * The components at positions offset through offset+length-1 in the
 * array are set to zero.
 *
 * @param x          (o)    : vector to clear
 * @param offset     (i)    : offset of vector
 * @param length     (i)    : length of vector
 */
        public static void set_zero(float[] x, int offset, int length)
        {
            for (int i = offset, toIndex = offset + length; i < toIndex; i++)
            {
                x[i] = 0.0f;
            }
        }

        /**
 * Copies an array from the specified x array, to the specified y array.
 * The number of components copied is equal to the length argument.
 *
 * @param x     (i)   : input vector
 * @param y     (o)   : output vector
 * @param L     (i)   : vector length
 */
        public static void copy(
            float[] x,
            float[] y,
            int L
        )
        {
            copy(x, 0, y, L);
        }

        /**
 * Copies an array from the specified source array,
 * beginning at the specified destination array.
 * A subsequence of array components are copied from the source array referenced
 * by x to the destination array referenced by y.
 * The number of components copied is equal to the length argument.
 * The components at positions x_offset through x_offset+length-1 in the source
 * array are copied into positions 0 through length-1,
 * respectively, of the destination array.
 *
 * @param x         (i)   : input vector
 * @param x_offset  (i)   : input vector offset
 * @param y         (o)   : output vector
 * @param L         (i)   : vector length
 */
        public static void copy(float[] x, int x_offset, float[] y, int L)
        {
            copy(x, x_offset, y, 0, L);
        }

        /**
 * Copies an array from the specified source array,
 * beginning at the specified position,
 * to the specified position of the destination array.
 * A subsequence of array components are copied from the source array referenced
 * by x to the destination array referenced by y.
 * The number of components copied is equal to the length argument.
 * The components at positions x_offset through x_offset+length-1 in the source
 * array are copied into positions y_offset through y_offset+length-1,
 * respectively, of the destination array.
 *
 * @param x         (i)   : input vector
 * @param x_offset  (i)   : input vector offset
 * @param y         (o)   : output vector
 * @param y_offset  (i)   : output vector offset
 * @param L         (i)   : vector length
 */
        public static void copy(float[] x, int x_offset, float[] y, int y_offset, int L)
        {
            int i;

            for (i = 0; i < L; i++)
            {
                y[y_offset + i] = x[x_offset + i];
            }
        }

        /**
 * Return random short.
 *
 * @return random short
 */
        public static short random_g729()
        {
            seed = (short)(seed * 31821L + 13849L);

            return seed;
        }
    }
}
