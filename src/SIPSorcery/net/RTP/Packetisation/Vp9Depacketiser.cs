//-----------------------------------------------------------------------------
// Filename: Vp9Depacketiser.cs
//
// Description: Reassembles RTP payloads using the VP9 RTP payload format. The
// VP9 payload descriptor is variable length (its size depends on the I, P, L, F
// and V flags) so the full descriptor is parsed to find the start of the encoded
// frame data, which is then concatenated across the packets of a frame.
//
// Based on the VP9 RTP payload format, RFC 9628 (was draft-ietf-payload-vp9).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 16 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Net;

public class Vp9Depacketiser
{
    private static readonly ILogger logger = LogFactory.CreateLogger<Vp9Depacketiser>();

    private uint _previousTimestamp;
    private readonly List<(int sequenceNumber, Range range)> _temporaryRtpPayloads = new();
    private readonly MemoryStream _payloadBuffer = new();

    /// <summary>
    /// Processes an RTP payload and writes the completed VP9 frame to the buffer writer
    /// when a full frame has been assembled.
    /// </summary>
    /// <param name="bufferWriter">The buffer writer to receive the reassembled frame.</param>
    /// <param name="rtpPayload">The RTP payload bytes (descriptor + payload data).</param>
    /// <param name="seqNum">The RTP sequence number.</param>
    /// <param name="timestamp">The RTP timestamp.</param>
    /// <param name="markerBit">The RTP marker bit (1 = last packet of frame).</param>
    /// <param name="isKeyFrame">Set to <c>true</c> when the assembled frame is a key frame.</param>
    /// <returns><c>true</c> when a complete frame has been written to <paramref name="bufferWriter"/>.</returns>
    public virtual bool ProcessRTPPayload(IBufferWriter<byte> bufferWriter, ReadOnlySpan<byte> rtpPayload, ushort seqNum, uint timestamp, int markerBit, out bool isKeyFrame)
    {
        isKeyFrame = false;

        // A change of timestamp before the marker bit means the previous frame's packets never
        // completed; discard them and start accumulating the new frame.
        if (_previousTimestamp != timestamp && _previousTimestamp > 0)
        {
            ClearPayloads();
            _previousTimestamp = 0;
        }

        var payloadOffset = (int)_payloadBuffer.Length;
        _payloadBuffer.Write(rtpPayload);
        _temporaryRtpPayloads.Add((seqNum, payloadOffset..(payloadOffset + rtpPayload.Length)));

        if (markerBit == 1)
        {
            if (_temporaryRtpPayloads.Count > 1)
            {
                _temporaryRtpPayloads.Sort((a, b) =>
                    (Math.Abs(b.sequenceNumber - a.sequenceNumber) > (0xFFFF - 2000)) ? -a.sequenceNumber.CompareTo(b.sequenceNumber) : a.sequenceNumber.CompareTo(b.sequenceNumber));
            }

            var payloadSpan = _payloadBuffer.GetBuffer().AsSpan(0, (int)_payloadBuffer.Length);
            var hasFrame = ProcessVp9PayloadFrame(bufferWriter, payloadSpan, _temporaryRtpPayloads, out isKeyFrame);
            ClearPayloads();
            _previousTimestamp = 0;

            return hasFrame;
        }

        _previousTimestamp = timestamp;
        return false;
    }

    private void ClearPayloads()
    {
        _payloadBuffer.SetLength(0);
        _temporaryRtpPayloads.Clear();
    }

    protected virtual bool ProcessVp9PayloadFrame(IBufferWriter<byte> bufferWriter, ReadOnlySpan<byte> payloadBuffer, List<(int sequenceNumber, Range range)> rtpPayloads, out bool isKeyFrame)
    {
        isKeyFrame = false;
        var hasOutput = false;

        foreach (var entry in rtpPayloads)
        {
            var payload = payloadBuffer[entry.range];

            if (payload.IsEmpty)
            {
                continue;
            }

            int descriptorLength = GetDescriptorLength(payload);
            if (descriptorLength <= 0 || descriptorLength >= payload.Length)
            {
                logger.LogVp9DepacketiserCouldNotParsePayloadDescriptor();
                continue;
            }

            // The B (start of frame) flag marks the packet that carries the VP9 uncompressed header,
            // which is where the key frame flag can be read. Only the first packet in a multi-packet
            // frame has the uncompressed header; later packets only have their descriptor.
            bool startOfFrame = (payload[0] & Vp9Packetiser.B_BIT) != 0;
            if (startOfFrame)
            {
                var frameStart = payload.Slice(descriptorLength).ToArray();
                isKeyFrame = Vp9Packetiser.IsKeyFrame(frameStart);
            }

            try
            {
                bufferWriter.Write(payload.Slice(descriptorLength));
                hasOutput = true;
            }
            catch (Exception ex)
            {
                logger.LogVp9DepacketiserFailedPayloadData(ex);
            }
        }

        return hasOutput;
    }

    /// <summary>
    /// Returns the length in bytes of the VP9 payload descriptor at the start of <paramref name="payload"/>,
    /// or -1 if it is malformed/truncated. Handles the picture ID (I), layer indices (L), flexible-mode
    /// reference diffs (F + P) and the scalability structure (V).
    /// </summary>
    private static int GetDescriptorLength(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 1)
        {
            return -1;
        }

        byte b0 = payload[0];
        bool i = (b0 & Vp9Packetiser.I_BIT) != 0;
        bool p = (b0 & Vp9Packetiser.P_BIT) != 0;
        bool l = (b0 & Vp9Packetiser.L_BIT) != 0;
        bool f = (b0 & Vp9Packetiser.F_BIT) != 0;
        bool v = (b0 & Vp9Packetiser.V_BIT) != 0;

        int len = 1;

        if (i)
        {
            if (payload.Length < len + 1) { return -1; }
            bool m = (payload[len] & 0x80) != 0; // M = 1 -> 15-bit picture ID over two bytes.
            len += m ? 2 : 1;
        }

        if (l)
        {
            if (payload.Length < len + 1) { return -1; }
            len += 1;          // TID/U/SID/D.
            if (!f) { len += 1; } // TL0PICIDX present in non-flexible mode.
        }

        if (f && p)
        {
            // 1-3 reference index diffs, each continued while its least significant (N) bit is set.
            while (true)
            {
                if (payload.Length < len + 1) { return -1; }
                bool more = (payload[len] & 0x01) != 0;
                len++;
                if (!more) { break; }
            }
        }

        if (v)
        {
            if (payload.Length < len + 1) { return -1; }
            byte ss = payload[len];
            len++;
            int numSpatialLayers = ((ss >> 5) & 0x07) + 1; // N_S + 1.
            bool y = (ss & 0x10) != 0;                     // Layer resolutions present.
            bool g = (ss & 0x08) != 0;                     // GOF description present.

            if (y) { len += 4 * numSpatialLayers; } // 2-byte width + 2-byte height per layer.

            if (g)
            {
                if (payload.Length < len + 1) { return -1; }
                int numFramesInGof = payload[len];
                len++;
                for (int j = 0; j < numFramesInGof; j++)
                {
                    if (payload.Length < len + 1) { return -1; }
                    int refCount = (payload[len] >> 2) & 0x03; // R (number of P_DIFF that follow).
                    len++;
                    len += refCount;
                }
            }

            if (payload.Length < len) { return -1; }
        }

        return len;
    }
}
