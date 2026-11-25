//-----------------------------------------------------------------------------
// Filename: DecoderHash.cs
//
// Description: Drives the ported decoder API the way openh264's
// BaseDecoderTest does (DecodeStream / HashStream in the reference harness,
// thirdparty/openh264-dotnet-port/refharness/src/main.cpp): split the stream on
// 4-byte start codes, DecodeFrame2 each chunk, signal end of stream, decode
// once more with no input, then FlushFrame what is left. Every output frame's
// Y, U and V rows go into one SHA1, as upstream's decoder_test.cpp hashes them.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (D6).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using SIPSorcery.H264.Decoder;

namespace SIPSorcery.H264.TestVectors;

/// <summary>DecodeResult in the harness: what one run over a stream produced.</summary>
public sealed class DecodeResult
{
    public int Chunks;
    public int Frames;
    public int ErrorChunks;
    public int FirstErrorState;
    public int ErrorStates;  // OR of every failing state
    public string Sha1;
}

public static unsafe class DecoderHash
{
    /// <summary>HashStream: decodes the chunks and returns the SHA1 of every output frame.</summary>
    public static DecodeResult HashChunks(IReadOnlyList<byte[]> chunks)
    {
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var r = DecodeChunks(chunks, (y, u, v) =>
        {
            AddPlane(sha1, y);
            AddPlane(sha1, u);
            AddPlane(sha1, v);
        });
        r.Sha1 = Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant();
        return r;
    }

    /// <summary>The harness's yuv mode: writes every output frame, cropped I420, to a file.</summary>
    public static DecodeResult DecodeToYuv(IReadOnlyList<byte[]> chunks, string path)
    {
        using var f = System.IO.File.Create(path);
        return DecodeChunks(chunks, (y, u, v) =>
        {
            foreach (Plane p in new[] { y, u, v })
            {
                byte* row = p.Data;
                for (int i = 0; i < p.Height; ++i, row += p.Stride)
                {
                    f.Write(new ReadOnlySpan<byte>(row, p.Width));
                }
            }
        });
    }

    public readonly struct Plane
    {
        public readonly byte* Data;
        public readonly int Width;
        public readonly int Height;
        public readonly int Stride;

        public Plane(byte* data, int width, int height, int stride)
        {
            Data = data;
            Width = width;
            Height = height;
            Stride = stride;
        }
    }

    public delegate void FrameCallback(Plane y, Plane u, Plane v);

    private static void AddPlane(IncrementalHash sha1, Plane p)
    {
        byte* row = p.Data;
        for (int i = 0; i < p.Height; ++i, row += p.Stride)
        {
            sha1.AppendData(new ReadOnlySpan<byte>(row, p.Width));
        }
    }

    private static void EmitFrame(SBufferInfo* bi, byte** data, FrameCallback cb, DecodeResult r)
    {
        if (bi->iBufferStatus != 1)
        {
            return;
        }
        ref SSysMEMBuffer sb = ref bi->UsrData.sSystemBuffer;
        var y = new Plane(data[0], sb.iWidth, sb.iHeight, sb.iStride[0]);
        var u = new Plane(data[1], sb.iWidth / 2, sb.iHeight / 2, sb.iStride[1]);
        var v = new Plane(data[2], sb.iWidth / 2, sb.iHeight / 2, sb.iStride[1]);
        ++r.Frames;
        cb(y, u, v);
    }

    /// <summary>
    /// The harness's trace-damaged mode: one line per DecodeFrame2/FlushFrame call (index, input
    /// length or -1 for FlushFrame, NAL type of the chunk, state, iBufferStatus, SHA1 of the output
    /// frame), to diff with <c>h264ref trace-damaged</c>.
    /// </summary>
    public static string TraceChunks(IReadOnlyList<byte[]> chunks)
    {
        var sb = new System.Text.StringBuilder();
        s_log = sb;
        try
        {
        DecodeChunks(chunks, (y, u, v) => { }, (call, len, st, bi, data) =>
        {
            int nal = call < chunks.Count && chunks[call].Length > 4 ? chunks[call][4] & 0x1f : -1;
            string h = "-";
            if (bi->iBufferStatus == 1)
            {
                using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                ref SSysMEMBuffer m = ref bi->UsrData.sSystemBuffer;
                AddPlane(sha1, new Plane(data[0], m.iWidth, m.iHeight, m.iStride[0]));
                AddPlane(sha1, new Plane(data[1], m.iWidth / 2, m.iHeight / 2, m.iStride[1]));
                AddPlane(sha1, new Plane(data[2], m.iWidth / 2, m.iHeight / 2, m.iStride[1]));
                h = Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant();
            }
            sb.Append($"{call}\t{len}\t{nal}\t0x{st:x}\t{bi->iBufferStatus}\t{h}\n");
        });
        }
        finally
        {
            s_log = null;
        }
        return sb.ToString();
    }

    internal delegate void CallTrace(int call, int len, int state, SBufferInfo* bi, byte** data);

    /// <summary>DecodeStream: BaseDecoderTest::SetUp / DecodeFile / DecodeFrame / FlushFrame.</summary>
    public static DecodeResult DecodeChunks(IReadOnlyList<byte[]> chunks, FrameCallback cb) => DecodeChunks(chunks, cb, null);

    internal static DecodeResult DecodeChunks(IReadOnlyList<byte[]> chunks, FrameCallback cb, CallTrace trace)
    {
        var r = new DecodeResult();
        int call = 0;
        CWelsDecoder* created = null;
        if (WelsDec.WelsCreateDecoder(&created) != 0 || created == null)
        {
            throw new InvalidOperationException("WelsCreateDecoder failed.");
        }
        CWelsDecoder* dec = created;
        try
        {
            int quiet = WELS_LOG_QUIET;
            if (int.TryParse(Environment.GetEnvironmentVariable("H264REF_TRACE_LEVEL"), out int level))
            {
                // Debugging aid, as in the harness, e.g. 8 for WELS_LOG_DEBUG: the decoder's log
                // lines go to the trace (TraceChunks) or stderr.
                quiet = level;
                delegate*<void*, int, string, void> logToTrace = &LogToTrace;
                dec->SetOption(DECODER_OPTION.DECODER_OPTION_TRACE_CALLBACK, &logToTrace);
            }
            dec->SetOption(DECODER_OPTION.DECODER_OPTION_TRACE_LEVEL, &quiet);

            SDecodingParam p = default;
            p.uiTargetDqLayer = byte.MaxValue;
            p.eEcActiveIdc = ERROR_CON_IDC.ERROR_CON_SLICE_COPY;
            p.sVideoProperty.eVideoBsType = VIDEO_BITSTREAM_TYPE.VIDEO_BITSTREAM_DEFAULT;
            if (dec->Initialize(&p) != 0)
            {
                throw new InvalidOperationException("CWelsDecoder.Initialize failed.");
            }

            void NoteError(DECODING_STATE st)
            {
                if (st == DECODING_STATE.dsErrorFree)
                {
                    return;
                }
                if (r.ErrorChunks++ == 0)
                {
                    r.FirstErrorState = (int)st;
                }
                r.ErrorStates |= (int)st;
            }

            void Decode(byte* src, int len)
            {
                byte** data = stackalloc byte*[3];
                data[0] = data[1] = data[2] = null;
                SBufferInfo bi = default;
                DECODING_STATE st = dec->DecodeFrame2(src, len, data, &bi);
                NoteError(st);
                trace?.Invoke(call, len, (int)st, &bi, data);
                ++call;
                EmitFrame(&bi, data, cb, r);
            }

            foreach (byte[] chunk in chunks)
            {
                ++r.Chunks;
                fixed (byte* src = chunk)
                {
                    Decode(src, chunk.Length);
                }
            }

            int eos = 1;
            dec->SetOption(DECODER_OPTION.DECODER_OPTION_END_OF_STREAM, &eos);
            Decode(null, 0);
            int remaining = 0;
            dec->GetOption(DECODER_OPTION.DECODER_OPTION_NUM_OF_FRAMES_REMAINING_IN_BUFFER, &remaining);
            for (int i = 0; i < remaining; ++i)
            {
                byte** data = stackalloc byte*[3];
                data[0] = data[1] = data[2] = null;
                SBufferInfo bi = default;
                DECODING_STATE st = dec->FlushFrame(data, &bi);
                NoteError(st);
                trace?.Invoke(call, -1, (int)st, &bi, data);
                ++call;
                EmitFrame(&bi, data, cb, r);
            }

            dec->Uninitialize();
        }
        finally
        {
            WelsDec.WelsDestroyDecoder(dec);
        }
        return r;
    }

    private const int WELS_LOG_QUIET = 0x00;

    [ThreadStatic]
    private static System.Text.StringBuilder s_log;

    private static void LogToTrace(void* ctx, int level, string message)
    {
        if (s_log != null)
        {
            s_log.Append($"log\t{message}\n");
        }
        else
        {
            Console.Error.WriteLine(message);
        }
    }
}
