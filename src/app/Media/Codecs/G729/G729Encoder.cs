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
 * Main program of the ITU-T G.729   8 kbit/s encoder.
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
    public class G729Encoder : Ld8k
    {

        /**
         * Initialization of the coder.
         */

        private byte[] _leftover = new byte[0];

        /**
         * Init the Ld8k Coder
         */
        private readonly CodLd8k codLd8k = new CodLd8k();

        /**
         * Init the PreProc
         */
        private readonly PreProc preProc = new PreProc();

        /**
         *  Transmitted parameters
         */
        private readonly int[] prm = new int[PRM_SIZE];

        public G729Encoder()
        {
            preProc.init_pre_process();
            codLd8k.init_coder_ld8k(); /* Initialize the coder             */
        }

        private static void Fill<T>(T[] array, int start, int end, T value)
        {
            for (var i = start; i < end; i++)
                array[i] = value;
        }

        private void packetize(short[] serial, byte[] outFrame, int outFrameOffset)
        {
            Fill(outFrame, outFrameOffset, outFrameOffset + L_FRAME / 8, (byte)0);

            for (var s = 0; s < L_FRAME; s++)
                if (BIT_1 == serial[2 + s])
                {
                    var o = outFrameOffset + s / 8;
                    int out_ = outFrame[o];

                    out_ |= 1 << (7 - s % 8);
                    outFrame[o] = (byte)(out_ & 0xFF);
                }
        }

        /**
         * Process <code>L_FRAME</code> short of speech.
         *
         * @param sp16      input : speach short array
         * @param serial    output : serial array encoded in bits_ld8k
         */
        private void ProcessPacket(short[] sp16, short[] serial)
        {
            var new_speech = codLd8k.new_speech; /* Pointer to new speech data   */
            var new_speech_offset = codLd8k.new_speech_offset;

            for (var i = 0; i < L_FRAME; i++)
                new_speech[new_speech_offset + i] = sp16[i];

            preProc.pre_process(new_speech, new_speech_offset, L_FRAME);

            codLd8k.coder_ld8k(prm);

            Bits.prm2bits_ld8k(prm, serial);

        }

        /**
         * Usage : coder  speech_file  bitstream_file
         *
         * Format for speech_file:
         *  Speech is read form a binary file of 16 bits data.
         *
         * Format for bitstream_file:
         *   One word (2-bytes) to indicate erasure.
         *   One word (2 bytes) to indicate bit rate
         *   80 words (2-bytes) containing 80 bits.
         *
         * @param args speech_file  bitstream_file
         * @throws java.io.IOException
         */
        public byte[] Process(byte[] speech)
        {
            var sp16 = new short[L_FRAME]; /* Buffer to read 16 bits speech */
            var serial = new short[SERIAL_SIZE]; /* Output bit stream buffer      */
            var packet = new byte[L_FRAME / 8];
            var output = new MemoryStream();
            var buffer = new MemoryStream();

            buffer.Write(_leftover, 0, _leftover.Length);
            buffer.Write(speech, 0, speech.Length);
            var input = buffer.ToArray();

            /*-------------------------------------------------------------------------*
             * Loop for every analysis/transmission frame.                             *
             * -New L_FRAME data are read. (L_FRAME = number of speech data per frame) *
             * -Conversion of the speech data from 16 bit integer to real              *
             * -Call cod_ld8k to encode the speech.                                    *
             * -The compressed serial output stream is written to a file.              *
             * -The synthesis speech is written to a file                              *
             *-------------------------------------------------------------------------*
             */

            var frame = 0;
            try
            {
                // Iterate over each frame 
                int i;
                for (i = 0; i <= input.Length - L_FRAME * 2 /* must have a complete frame left */; i += L_FRAME * 2)
                {
                    frame++;
                    Buffer.BlockCopy(input, i, sp16, 0, L_FRAME * 2);
                    ProcessPacket(sp16, serial);
                    packetize(serial, packet, 0);
                    output.Write(packet, 0, packet.Length);
                }

                _leftover = new byte[input.Length - i];
                Array.Copy(input, i, _leftover, 0, _leftover.Length);
            }
            catch (Exception)
            {
                // No logging as we could get huge log files if any issues arises decoding
            }

            return output.ToArray();
        }

        // TODO: pad out _leftover with silence, and return one last frame
        public byte[] Flush()
        {
            var output = new MemoryStream();
            if (_leftover.Length > 0)
            {
                var sp16 = new short[L_FRAME]; /* Buffer to read 16 bits speech */
                var serial = new short[SERIAL_SIZE]; /* Output bit stream buffer      */
                var packet = new byte[L_FRAME / 8];

                Buffer.BlockCopy(_leftover, 0, sp16, 0, _leftover.Length);
                Fill(sp16, _leftover.Length / 2, sp16.Length, (short)0);
                ProcessPacket(sp16, serial);
                packetize(serial, packet, 0);
                output.Write(packet, 0, packet.Length);
            }

            return output.ToArray();
        }
    }
}