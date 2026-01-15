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
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SIPSorcery.Media.G729Codec;
using SIPSorcery.Sys;

namespace SIPSorcery.Media;

public class G729Encoder : Ld8k
{

    /**
     * Initialization of the coder.
     */

    private readonly PooledSegmentedBuffer<byte> _leftover = new();

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

    /// <summary>
    /// Fills a span with a specified value from start to end index.
    /// </summary>
    private static void Fill<T>(Span<T> span, int start, int end, T value)
    {
        for (var i = start; i < end; i++)
        {
            span[i] = value;
        }
    }

    /// <summary>
    /// Writes the encoded serial bits to the output packet using spans.
    /// </summary>
    /// <param name="serial">Input serial bits as Span.</param>
    /// <param name="outFrame">Output packet as Span.</param>
    private void packetize(ReadOnlySpan<short> serial, Span<byte> outFrame)
    {
        Fill(outFrame, 0, L_FRAME / 8, (byte)0);

        for (var s = 0; s < L_FRAME; s++)
        {
            if (BIT_1 == serial[2 + s])
            {
                var o = s / 8;
                int out_ = outFrame[o];

                out_ |= 1 << (7 - s % 8);
                outFrame[o] = (byte)(out_ & 0xFF);
            }
        }
    }

    /**
     * Process <code>L_FRAME</code> short of speech.
     *
     * @param sp16      input : speach short array
     * @param serial    output : serial array encoded in bits_ld8k
     */
    private void ProcessPacket(ReadOnlySpan<short> sp16, Span<short> serial)
    {
        var new_speech = codLd8k.new_speech; /* Pointer to new speech data   */
        Debug.Assert(new_speech is { });
        var new_speech_offset = codLd8k.new_speech_offset;

        for (var i = 0; i < L_FRAME; i++)
        {
            new_speech[new_speech_offset + i] = sp16[i];
        }

        preProc.pre_process(new_speech.AsSpan(), new_speech_offset, L_FRAME);

        codLd8k.coder_ld8k(prm);

        Bits.prm2bits_ld8k(prm, serial);
    }

    /// <summary>
    /// Processes speech data using spans and writes output to an <see cref="IBufferWriter{T}"/> of <see langword="byte"/>.
    /// </summary>
    /// <param name="speech">Input speech data as <see cref="ReadOnlySpan{T}"/> of <see langword="byte"/>.</param>
    /// <param name="output">Output buffer writer for encoded bytes.</param>
    /// <remarks>
    /// Format for speech_file:
    ///  Speech is read form a binary file of 16 bits data.
    ///
    /// Format for bitstream_file:
    ///   One word (2-bytes) to indicate erasure.
    ///   One word (2 bytes) to indicate bit rate
    ///   80 words (2-bytes) containing 80 bits.
    /// </remarks>
    [SkipLocalsInit]
    public void Process(ReadOnlySpan<byte> speech, IBufferWriter<byte> output)
    {
        const int frameSizeInBytes = L_FRAME * 2;
        Span<byte> frameBytes = stackalloc byte[frameSizeInBytes];
        Span<short> serial = stackalloc short[SERIAL_SIZE];
        const int packetLength = L_FRAME / 8;
        Span<byte> packet = stackalloc byte[packetLength];

        // Combine leftover and new speech
        _leftover.Write(speech);
        var totalLength = (int)_leftover.Length;
        var framesToProcess = totalLength / frameSizeInBytes;
        var processedBytes = 0;
        var reader = new SequenceReader<byte>(_leftover.GetReadOnlySequence());

        /*-------------------------------------------------------------------------*
         * Loop for every analysis/transmission frame.                             *
         * -New L_FRAME data are read. (L_FRAME = number of speech data per frame) *
         * -Conversion of the speech data from 16 bit integer to real              *
         * -Call cod_ld8k to encode the speech.                                    *
         * -The compressed serial output stream is written to a file.              *
         * -The synthesis speech is written to a file                              *
         *-------------------------------------------------------------------------*
         */

        for (var frame = 0; frame < framesToProcess; frame++)
        {
            if (!reader.TryCopyTo(frameBytes))
            {
                break;
            }
            reader.Advance(frameSizeInBytes);
            processedBytes += frameSizeInBytes;

            var sp16 = MemoryMarshal.Cast<byte, short>(frameBytes);


            ProcessPacket(sp16, serial);
            packet.Clear();
            packetize(serial, packet);
            output.GetSpan(packetLength).Slice(0, packetLength).CopyTo(packet);
            output.Advance(packetLength);
        }

        // Slice leftover to only keep unprocessed bytes
        _leftover.Slice(processedBytes, totalLength - processedBytes);
    }

    /// <summary>
    /// Flushes any remaining buffered audio, encoding and writing to the provided output buffer.
    /// </summary>
    /// <param name="output">Output buffer writer for encoded bytes.</param>
    [SkipLocalsInit]
    public void Flush(IBufferWriter<byte> output)
    {
        if (_leftover.Length > 0)
        {
            const int frameSizeInBytes = L_FRAME * 2;
            Span<byte> frameBytes = stackalloc byte[frameSizeInBytes];
            var leftoverSeq = _leftover.GetReadOnlySequence();
            var leftoverLength = (int)_leftover.Length;
            Debug.Assert(leftoverLength <= frameSizeInBytes);
            // Copy leftover bytes into frameBytes
            leftoverSeq.Slice(0, leftoverLength).CopyTo(frameBytes.Slice(0, leftoverLength));
            // Zero-fill any missing bytes
            if (leftoverLength < frameSizeInBytes)
            {
                frameBytes.Slice(leftoverLength, frameSizeInBytes - leftoverLength).Clear();
            }
            var sp16 = MemoryMarshal.Cast<byte, short>(frameBytes);
            Span<short> serial = stackalloc short[SERIAL_SIZE];
            Span<byte> packet = stackalloc byte[L_FRAME / 8];
            ProcessPacket(sp16, serial);
            packet.Clear();
            packetize(serial, packet);
            output.GetSpan(packet.Length).Slice(0, packet.Length).CopyTo(packet);
            output.Advance(packet.Length);

            _leftover.Clear();
        }
    }
}
