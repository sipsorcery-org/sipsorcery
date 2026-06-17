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
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Net;

public class Vp9Depacketiser
{
    private static readonly ILogger logger = LogFactory.CreateLogger<Vp9Depacketiser>();

    private uint _previousTimestamp;
    private readonly List<KeyValuePair<int, byte[]>> _temporaryRtpPayloads = new List<KeyValuePair<int, byte[]>>();

    public virtual MemoryStream ProcessRTPPayload(byte[] rtpPayload, ushort seqNum, uint timestamp, int markerBit, out bool isKeyFrame)
    {
        isKeyFrame = false;

        // A change of timestamp before the marker bit means the previous frame's packets never
        // completed; discard them and start accumulating the new frame.
        if (_previousTimestamp != timestamp && _previousTimestamp > 0)
        {
            _temporaryRtpPayloads.Clear();
            _previousTimestamp = 0;
        }

        _temporaryRtpPayloads.Add(new KeyValuePair<int, byte[]>(seqNum, rtpPayload));

        if (markerBit == 1)
        {
            if (_temporaryRtpPayloads.Count > 1)
            {
                _temporaryRtpPayloads.Sort((a, b) =>
                    (Math.Abs(b.Key - a.Key) > (0xFFFF - 2000)) ? -a.Key.CompareTo(b.Key) : a.Key.CompareTo(b.Key));
            }

            byte[] frame = ProcessVp9PayloadFrame(_temporaryRtpPayloads, out isKeyFrame);
            _temporaryRtpPayloads.Clear();
            _previousTimestamp = 0;

            if (frame == null || frame.Length == 0)
            {
                return null;
            }

            var frameStream = new MemoryStream(frame.Length);
            frameStream.Write(frame, 0, frame.Length);
            frameStream.Position = 0;
            return frameStream;
        }

        _previousTimestamp = timestamp;
        return null;
    }

    protected virtual byte[] ProcessVp9PayloadFrame(List<KeyValuePair<int, byte[]>> rtpPayloads, out bool isKeyFrame)
    {
        isKeyFrame = false;
        using var frame = new MemoryStream();

        foreach (var rtpPayload in rtpPayloads)
        {
            var payload = rtpPayload.Value;
            if (payload == null || payload.Length == 0)
            {
                continue;
            }

            int descriptorLength = GetDescriptorLength(payload);
            if (descriptorLength <= 0 || descriptorLength >= payload.Length)
            {
                logger.LogWarning("VP9 depacketiser could not parse the payload descriptor, discarding packet.");
                continue;
            }

            // The B (start of frame) flag marks the packet that carries the VP9 uncompressed header,
            // which is where the key frame flag can be read.
            bool startOfFrame = (payload[0] & Vp9Packetiser.B_BIT) != 0;
            if (startOfFrame)
            {
                var frameStart = new byte[payload.Length - descriptorLength];
                Buffer.BlockCopy(payload, descriptorLength, frameStart, 0, frameStart.Length);
                isKeyFrame = Vp9Packetiser.IsKeyFrame(frameStart);
            }

            frame.Write(payload, descriptorLength, payload.Length - descriptorLength);
        }

        return frame.Length > 0 ? frame.ToArray() : null;
    }

    /// <summary>
    /// Returns the length in bytes of the VP9 payload descriptor at the start of <paramref name="payload"/>,
    /// or -1 if it is malformed/truncated. Handles the picture ID (I), layer indices (L), flexible-mode
    /// reference diffs (F + P) and the scalability structure (V).
    /// </summary>
    private static int GetDescriptorLength(byte[] payload)
    {
        if (payload == null || payload.Length < 1)
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
