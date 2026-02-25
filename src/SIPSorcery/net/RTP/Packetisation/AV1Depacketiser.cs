//-----------------------------------------------------------------------------
// Filename: AV1Depacketiser.cs
//
// Description: Reassembles RTP payloads using the AV1 RTP payload format.
//
// Based on the Alliance for Open Media RTP Payload Format for AV1:
// https://aomediacodec.github.io/av1-rtp-spec/
//
// Author(s):
// OpenAI
//
// History:
// 28 Mar 2026  OpenAI         Created, Vancouver.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;

namespace SIPSorcery.Net;

/// <summary>
/// Reassembles RTP payloads using the AV1 RTP payload format defined by
/// the Alliance for Open Media.
/// </summary>
public class AV1Depacketiser
{
    private const byte Z_MASK = 0x80;
    private const byte Y_MASK = 0x40;
    private const byte N_MASK = 0x08;

    private static readonly Comparison<(int sequenceNumber, Range range)> s_sequenceNumberComparison =
        (a, b) => (Math.Abs(b.sequenceNumber - a.sequenceNumber) > (0xFFFF - 2000))
            ? -a.sequenceNumber.CompareTo(b.sequenceNumber)
            : a.sequenceNumber.CompareTo(b.sequenceNumber);

    private uint _previousTimestamp;
    private readonly List<(int sequenceNumber, Range range)> _temporaryRtpPayloads = new();
    private readonly MemoryStream _payloadBuffer = new();
    private readonly MemoryStream _fragmentedObu = new();

    /// <summary>
    /// Processes an RTP payload and writes the completed AV1 frame to the buffer writer
    /// when a full frame has been assembled.
    /// </summary>
    /// <param name="bufferWriter">The buffer writer to receive the reassembled frame.</param>
    /// <param name="rtpPayload">The RTP payload bytes.</param>
    /// <param name="seqNum">The RTP sequence number.</param>
    /// <param name="timestamp">The RTP timestamp.</param>
    /// <param name="markerBit">The RTP marker bit (1 = last packet of frame).</param>
    /// <param name="isKeyFrame">Set to <c>true</c> when the assembled frame is a key frame.</param>
    /// <returns><c>true</c> when a complete frame has been written to <paramref name="bufferWriter"/>.</returns>
    public virtual bool ProcessRTPPayload(IBufferWriter<byte> bufferWriter, ReadOnlySpan<byte> rtpPayload, ushort seqNum, uint timestamp, int markerBit, out bool isKeyFrame)
    {
        if (_previousTimestamp != timestamp && _previousTimestamp > 0)
        {
            ClearPayloads();
            _previousTimestamp = 0;
            _fragmentedObu.SetLength(0);
        }

        var payloadOffset = (int)_payloadBuffer.Length;
        _payloadBuffer.Write(rtpPayload);
        _temporaryRtpPayloads.Add((seqNum, payloadOffset..(payloadOffset + rtpPayload.Length)));

        if (markerBit == 1)
        {
            if (_temporaryRtpPayloads.Count > 1)
            {
                _temporaryRtpPayloads.Sort(s_sequenceNumberComparison);
            }

            var payloadSpan = _payloadBuffer.GetBuffer().AsSpan(0, (int)_payloadBuffer.Length);
            var hasFrame = ProcessAV1PayloadFrame(bufferWriter, payloadSpan, _temporaryRtpPayloads, out isKeyFrame);
            ClearPayloads();
            _previousTimestamp = 0;
            _fragmentedObu.SetLength(0);

            return hasFrame;
        }

        isKeyFrame = false;
        _previousTimestamp = timestamp;
        return false;
    }

    private bool ProcessAV1PayloadFrame(IBufferWriter<byte> bufferWriter, ReadOnlySpan<byte> payloadBuffer, List<(int sequenceNumber, Range range)> rtpPayloads, out bool isKeyFrame)
    {
        var hasOutput = false;
        isKeyFrame = false;

        foreach (var entry in rtpPayloads)
        {
            var payload = payloadBuffer[entry.range];

            if (payload.IsEmpty)
            {
                continue;
            }

            var z = (payload[0] & Z_MASK) != 0;
            var y = (payload[0] & Y_MASK) != 0;
            var w = (payload[0] >> 4) & 0x03;
            var n = (payload[0] & N_MASK) != 0;

            if (n)
            {
                isKeyFrame = true;
            }

            hasOutput |= ParseAndProcessObuElements(bufferWriter, payload, w, z, y);
        }

        if (_fragmentedObu.Length > 0)
        {
            _fragmentedObu.SetLength(0);
        }

        return hasOutput;
    }

    private void ClearPayloads()
    {
        _payloadBuffer.SetLength(0);
        _temporaryRtpPayloads.Clear();
    }

    /// <summary>
    /// Parses OBU element boundaries from the RTP payload and writes completed OBUs
    /// directly to <paramref name="bufferWriter"/> using the z/y fragmentation flags.
    /// </summary>
    /// <returns><c>true</c> if any completed OBU was written.</returns>
    private bool ParseAndProcessObuElements(IBufferWriter<byte> bufferWriter, ReadOnlySpan<byte> payload, int w, bool z, bool y)
    {
        // Phase 1: Parse element boundaries on the stack (no heap allocation).
        // w is a 2-bit field (0–3). For w==0, count is variable but bounded by payload size.
        // REVIEW: If a payload contains more than 16 OBU elements (w==0 path), elements
        // beyond the 16th are silently dropped. This is unlikely in practice but could
        // cause data loss with unusual encoders.
        Span<int> elemOffsets = stackalloc int[16];
        Span<int> elemLengths = stackalloc int[16];
        var elemCount = 0;
        var offset = 1;

        if (w == 0)
        {
            while (offset < payload.Length && elemCount < elemOffsets.Length)
            {
                if (!TryReadLeb128Span(payload, ref offset, out var len) || offset + len > payload.Length)
                {
                    break;
                }

                elemOffsets[elemCount] = offset;
                elemLengths[elemCount] = len;
                elemCount++;
                offset += len;
            }
        }
        else
        {
            for (var i = 0; i < w && offset < payload.Length && elemCount < elemOffsets.Length; i++)
            {
                int len;
                if (i == w - 1)
                {
                    len = payload.Length - offset;
                }
                else if (!TryReadLeb128Span(payload, ref offset, out len))
                {
                    break;
                }

                if (offset + len > payload.Length)
                {
                    break;
                }

                elemOffsets[elemCount] = offset;
                elemLengths[elemCount] = len;
                elemCount++;
                offset += len;
            }
        }

        if (elemCount == 0)
        {
            return false;
        }

        // Phase 2: Apply z/y fragmentation flags directly from payload slices.
        var wrote = false;
        var startIndex = 0;
        var endExclusive = elemCount;

        if (z)
        {
            var first = payload.Slice(elemOffsets[0], elemLengths[0]);
            _fragmentedObu.Write(first);

            if (!(y && elemCount == 1))
            {
                wrote |= WriteCompletedObu(bufferWriter, _fragmentedObu.GetBuffer().AsSpan(0, (int)_fragmentedObu.Length));
                _fragmentedObu.SetLength(0);
            }

            startIndex = 1;
        }

        if (y && elemCount > startIndex)
        {
            endExclusive = elemCount - 1;
        }

        for (var i = startIndex; i < endExclusive; i++)
        {
            wrote |= WriteCompletedObu(bufferWriter, payload.Slice(elemOffsets[i], elemLengths[i]));
        }

        if (y && elemCount > startIndex)
        {
            var last = payload.Slice(elemOffsets[elemCount - 1], elemLengths[elemCount - 1]);
            _fragmentedObu.Write(last);
        }

        return wrote;
    }

    /// <summary>
    /// Reads a LEB128-encoded unsigned integer from a span.
    /// </summary>
    /// <remarks>
    /// REVIEW: The loop limit of 8 bytes can shift beyond 32 bits (shift reaches 35 on the
    /// 6th byte), overflowing the <see cref="int"/> accumulator. Cap at 5 iterations for
    /// 32-bit safety, or use <see langword="long"/> if larger values are needed.
    /// </remarks>
    private static bool TryReadLeb128Span(ReadOnlySpan<byte> buffer, ref int offset, out int value)
    {
        value = 0;
        var shift = 0;
        var bytesRead = 0;

        while (offset < buffer.Length && bytesRead < 8)
        {
            var current = buffer[offset++];
            bytesRead++;
            value |= (current & 0x7f) << shift;

            if ((current & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// Writes a completed OBU directly to the buffer writer, filtering out
    /// TemporalDelimiter and TileList OBU types.
    /// </summary>
    private static bool WriteCompletedObu(IBufferWriter<byte> bufferWriter, ReadOnlySpan<byte> obu)
    {
        if (obu.IsEmpty)
        {
            return false;
        }

        var obuType = AV1Packetiser.GetObuType(obu);
        if (obuType is (AV1Packetiser.AV1ObuType.TemporalDelimiter or AV1Packetiser.AV1ObuType.TileList))
        {
            return false;
        }

        bufferWriter.Write(obu);
        return true;
    }
}
