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
 * G.729 is now patent free. See:
 * <a href="http://www.sipro.com">SIPRO Lab Telecom</a>.
 */
/* ITU-T G.729 Software Package Release 2 (November 2006) */
/*
 * ITU-T G.729 Annex C - Reference C code for floating point
 * implementation of G.729
 * Version 1.01 of 15.September.98
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
/**
 * Main program of the G.729  8.0 kbit/s decoder.
 *
 * @author Lubomir Marinov (translation of ITU-T C source code to Java)
 */
/*
 * Converted to C# for GroovyG7xx, see https://github.com/jongoochgithub/GroovyCodecs
 */

using System;
using System.IO;
using GroovyCodecs.G729.Codec;

namespace GroovyCodecs.G729
{
    public class G729Decoder : Ld8k
    {

        /**
         * Synthesis parameters + BFI
         */
        private readonly float[] Az_dec = new float[2 * MP1];

        /**
         * DecLd8k reference
         */
        private readonly DecLd8k decLd8k = new DecLd8k();

        /**
         * Synthesis parameters + BFI
         */
        private readonly int[] parm = new int[PRM_SIZE + 1];

        /**
         * Postfil reference
         */
        private readonly Postfil postfil = new Postfil();

        /**
         * PostPro reference
         */
        private readonly PostPro postPro = new PostPro();

        /**
         *  postfilter output
         */
        private readonly float[] pst_out = new float[L_FRAME];

        /**
         * Synthesis
         */
        private readonly float[] synth;

        /**
         * Synthesis
         */
        private readonly float[] synth_buf = new float[L_FRAME + M];

        /**
         * Synthesis
         */
        private readonly int synth_offset;

        /**
         * voicing for previous subframe
         */
        private int voicing;

        /**
         * Initialization of decoder
         */

        public G729Decoder()
        {

            synth = synth_buf;
            synth_offset = M;

            decLd8k.init_decod_ld8k();
            postfil.init_post_filter();
            postPro.init_post_process();

            voicing = 60;
        }

        /**
         * Converts floats array into shorts array.
         *
         * @param floats
         * @param shorts
         */
        private static void floats2shorts(float[] floats, short[] shorts)
        {
            for (var i = 0; i < floats.Length; i++)
            {
                /* round and convert to int */
                var f = floats[i];
                if (f >= 0.0f)
                    f += 0.5f;
                else
                    f -= 0.5f;
                if (f > 32767.0f)
                    f = 32767.0f;
                if (f < -32768.0f)
                    f = -32768.0f;
                shorts[i] = (short)f;
            }
        }

        private void depacketize(byte[] inFrame, int inFrameOffset, short[] serial)
        {
            serial[0] = SYNC_WORD;
            serial[1] = SIZE_WORD;
            for (var s = 0; s < L_FRAME; s++)
            {
                int in_ = inFrame[inFrameOffset + s / 8];

                in_ &= 1 << (7 - s % 8);
                serial[2 + s] = 0 != in_ ? BIT_1 : BIT_0;
            }
        }

        /**
         * Process <code>SERIAL_SIZE</code> short of speech.
         *
         * @param serial    input : serial array encoded in bits_ld8k
         * @param sp16      output : speech short array
         */
        private void ProcessPacket(short[] serial, short[] sp16)
        {
            Bits.bits2prm_ld8k(serial, 2, parm, 1);

            /* the hardware detects frame erasures by checking if all bits
             * are set to zero
             */
            parm[0] = 0; /* No frame erasure */
            for (var i = 2; i < SERIAL_SIZE; i++)
                if (serial[i] == 0)
                    parm[0] = 1; /* frame erased     */

            /* check parity and put 1 in parm[4] if parity error */

            parm[4] = PParity.check_parity_pitch(parm[3], parm[4]);

            var t0_first = decLd8k.decod_ld8k(parm, voicing, synth, synth_offset, Az_dec); /* Decoder */

            /* Post-filter and decision on voicing parameter */
            voicing = 0;

            var ptr_Az = Az_dec; /* Decoded Az for post-filter */
            var ptr_Az_offset = 0;

            for (var i = 0; i < L_FRAME; i += L_SUBFR)
            {
                int sf_voic; /* voicing for subframe */

                sf_voic = postfil.post(t0_first, synth, synth_offset + i, ptr_Az, ptr_Az_offset, pst_out, i);
                if (sf_voic != 0)
                    voicing = sf_voic;
                ptr_Az_offset += MP1;
            }

            Util.copy(synth_buf, L_FRAME, synth_buf, M);

            postPro.post_process(pst_out, L_FRAME);

            floats2shorts(pst_out, sp16);
        }

        /**
         * Main decoder routine
         * Usage :Decoder bitstream_file  outputspeech_file
         *
         * Format for bitstream_file:
         * One (2-byte) synchronization word
         *   One (2-byte) size word,
         *   80 words (2-byte) containing 80 bits.
         *
         * Format for outputspeech_file:
         *   Synthesis is written to a binary file of 16 bits data.
         *
         * @param args bitstream_file  outputspeech_file
         * @throws java.io.IOException
         */
        public byte[] Process(byte[] source)
        {
            var serial = new short[SERIAL_SIZE]; /* Serial stream              */
            var sp16 = new short[L_FRAME]; /* Buffer to write 16 bits speech */
            var speech = new byte[L_FRAME * 2];
            var output = new MemoryStream();

            /*-----------------------------------------------------------------*
             *            Loop for each "L_FRAME" speech data                  *
             *-----------------------------------------------------------------*/

            var frame = 0;
            try
            {
                // Iterate over each frame 
                int i;
                for (i = 0; i <= source.Length - L_FRAME / 8 /* must have a complete frame left */; i += L_FRAME / 8)
                {
                    frame++;
                    depacketize(source, i, serial);
                    ProcessPacket(serial, sp16);
                    Buffer.BlockCopy(sp16, 0, speech, 0, speech.Length);
                    output.Write(speech, 0, speech.Length);
                }
            }
            catch (Exception)
            {
                // No logging as we could get huge log files if any issues arises decoding
            }

            return output.ToArray();
        }
    }
}