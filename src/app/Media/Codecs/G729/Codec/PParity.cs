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
    internal class PParity
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
 File : P_PARITY.C
 Used for the floating point version of both
 G.729 main body and G.729A
*/

        /**
 * Compute parity bit for first 6 MSBs
 *
 * @param pitch_index   input : index for which parity is computed
 * @return              parity bit (XOR of 6 MSB bits)
 */

        public static int parity_pitch(
            int pitch_index
        )
        {
            int temp, sum, i, bit;

            temp = pitch_index >> 1;

            sum = 1;
            for (i = 0; i <= 5; i++)
            {
                temp >>= 1;
                bit = temp & 1;
                sum = sum + bit;
            }

            sum = sum & 1;
            return sum;
        }

        /**
 * Check parity of index with transmitted parity
 *
 * @param pitch_index   input : index of parameter
 * @param parity        input : parity bit
 * @return              0 = no error, 1= error
 */

        public static int check_parity_pitch(
            int pitch_index,
            int parity
        )
        {
            int temp, sum, i, bit;
            temp = pitch_index >> 1;

            sum = 1;
            for (i = 0; i <= 5; i++)
            {
                temp >>= 1;
                bit = temp & 1;
                sum = sum + bit;
            }

            sum += parity;
            sum = sum & 1;
            return sum;
        }
    }
}