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

using System;

/**
 * Bit stream manipulation routines.
 * <p>prm2bits_ld8k -converts encoder parameter vector into vector of serial bits</p>
 * <p>bits2prm_ld8k - converts serial received bits to encoder parameter vector</p>
 * <pre>
 * The transmitted parameters for 8000 bits/sec are:
 *
 *     LPC:     1st codebook           7+1 bit
 *              2nd codebook           5+5 bit
 *
 *     1st subframe:
 *          pitch period                 8 bit
 *          parity check on 1st period   1 bit
 *          codebook index1 (positions) 13 bit
 *          codebook index2 (signs)      4 bit
 *          pitch and codebook gains   4+3 bit
 *
 *     2nd subframe:
 *          pitch period (relative)      5 bit
 *          codebook index1 (positions) 13 bit
 *          codebook index2 (signs)      4 bit
 *          pitch and codebook gains   4+3 bit
 * </pre>
 *
 * @author Lubomir Marinov (translation of ITU-T C source code to Java)
 */
namespace SIPSorcery.Media.G729Codec;

internal static class Bits
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
File : BITS.C
Used for the floating point version of both
G.729 main body and G.729A
*/

    /// <summary>
    /// Converts encoder parameter vector into vector of serial bits using spans.
    /// </summary>
    /// <param name="prm">Input: encoded parameters as ReadOnlySpan</param>
    /// <param name="bits">Output: serial bits as Span</param>
    public static void prm2bits_ld8k(
        ReadOnlySpan<int> prm,
        Span<short> bits
    )
    {
        var j = 0;
        bits[j++] = Ld8k.SYNC_WORD; // At receiver this bit indicates BFI
        bits[j++] = Ld8k.SIZE_WORD; // Number of bits in this frame

        for (var i = 0; i < Ld8k.PRM_SIZE; i++)
        {
            int2bin(prm[i], TabLd8k.bitsno[i], bits, j);
            j += TabLd8k.bitsno[i];
        }

        static void int2bin(
            int value,
            int no_of_bits,
            Span<short> bitstream,
            int bitstream_offset
        )
        {
            var pt_bitstream = bitstream_offset + no_of_bits;
            for (var i = 0; i < no_of_bits; i++)
            {
                var bit = value & 0x0001; // get lsb
                bitstream[--pt_bitstream] = bit == 0 ? Ld8k.BIT_0 : Ld8k.BIT_1;
                value >>= 1;
            }
        }
    }

    /// <summary>
    /// Span-based version: Converts serial received bits to encoder parameter vector.
    /// </summary>
    /// <param name="bits">Input: serial bits as ReadOnlySpan</param>
    /// <param name="bits_offset">Input: serial bits offset</param>
    /// <param name="prm">Output: decoded parameters</param>
    /// <param name="prm_offset">Input: decoded parameters offset</param>
    public static void bits2prm_ld8k(
        ReadOnlySpan<short> bits,
        int bits_offset,
        Span<int> prm,
        int prm_offset
    )
    {
        for (var i = 0; i < Ld8k.PRM_SIZE; i++)
        {
            var bitCount = TabLd8k.bitsno[i];
            prm[i + prm_offset] = bin2int(bits.Slice(bits_offset, bitCount));
            bits_offset += bitCount;
        }

        static int bin2int(ReadOnlySpan<short> bitstream)
        {
            var value = 0;
            for (var i = 0; i < bitstream.Length; i++)
            {
                value <<= 1;
                if (bitstream[i] == Ld8k.BIT_1)
                {
                    value |= 1;
                }
            }
            return value;
        }
    }
}
