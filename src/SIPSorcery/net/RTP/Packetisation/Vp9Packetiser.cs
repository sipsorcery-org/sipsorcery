//-----------------------------------------------------------------------------
// Filename: Vp9Packetiser.cs
//
// Description: Provides VP9 RTP packetisation helpers. The frames produced by the
// SIPSorcery encoders are single spatial/temporal layer, non-flexible streams, so
// the descriptor written here is the minimal flag octet plus a 15-bit picture ID:
//
//          0 1 2 3 4 5 6 7
//         +-+-+-+-+-+-+-+-+
//         |I|P|L|F|B|E|V|Z| (REQUIRED)
//         +-+-+-+-+-+-+-+-+
//    I:   |M| PICTURE ID  | (REQUIRED, M=1 -> 15-bit ID)
//         +-+-+-+-+-+-+-+-+
//    M:   | EXTENDED PID  |
//         +-+-+-+-+-+-+-+-+
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

namespace SIPSorcery.Net;

public static class Vp9Packetiser
{
    // Flag masks for the first (required) octet of the VP9 payload descriptor.
    public const byte I_BIT = 0x80; // Picture ID present.
    public const byte P_BIT = 0x40; // Inter-picture predicted frame.
    public const byte L_BIT = 0x20; // Layer indices present.
    public const byte F_BIT = 0x10; // Flexible mode.
    public const byte B_BIT = 0x08; // Start of a frame.
    public const byte E_BIT = 0x04; // End of a frame.
    public const byte V_BIT = 0x02; // Scalability structure (SS) present.
    public const byte Z_BIT = 0x01; // Not a reference frame for upper spatial layers.

    // Flag octet plus a 15-bit (2 byte) picture ID. The simple single-layer streams the
    // SIPSorcery encoders emit never set L/F/V, so the descriptor is always this length.
    private const int DESCRIPTOR_LENGTH = 3;

    public const int MAX_PICTURE_ID = 0x7FFF;

    /// <summary>
    /// Fragments one encoded VP9 frame into RTP payloads, each prefixed with a VP9 payload descriptor.
    /// The first payload has the B (start of frame) flag set and the last the E (end of frame) flag; the
    /// caller is responsible for setting the RTP marker bit on the last packet.
    /// </summary>
    /// <param name="frame">The encoded VP9 frame.</param>
    /// <param name="maxPayloadSize">The maximum RTP payload size (descriptor included).</param>
    /// <param name="isKeyFrame">Whether the frame is a key frame; clears the P (inter-predicted) flag when true.</param>
    /// <param name="pictureId">The 15-bit picture ID to stamp on every fragment of this frame.</param>
    public static List<byte[]> Packetize(byte[] frame, int maxPayloadSize, bool isKeyFrame, int pictureId)
    {
        var packets = new List<byte[]>();
        if (frame == null || frame.Length == 0)
        {
            return packets;
        }

        int capacity = maxPayloadSize - DESCRIPTOR_LENGTH;
        if (capacity <= 0)
        {
            throw new ArgumentException("The maximum RTP payload size is too small for VP9 packetisation.", nameof(maxPayloadSize));
        }

        int id = pictureId & MAX_PICTURE_ID;
        int offset = 0;
        bool first = true;

        while (offset < frame.Length)
        {
            int chunk = Math.Min(capacity, frame.Length - offset);
            bool last = offset + chunk >= frame.Length;

            byte flags = I_BIT;
            if (!isKeyFrame) { flags |= P_BIT; }
            if (first) { flags |= B_BIT; }
            if (last) { flags |= E_BIT; }

            var payload = new byte[DESCRIPTOR_LENGTH + chunk];
            payload[0] = flags;
            payload[1] = (byte)(0x80 | ((id >> 8) & 0x7F)); // M bit set, top 7 bits of the picture ID.
            payload[2] = (byte)(id & 0xFF);
            Buffer.BlockCopy(frame, offset, payload, DESCRIPTOR_LENGTH, chunk);
            packets.Add(payload);

            offset += chunk;
            first = false;
        }

        return packets;
    }

    /// <summary>
    /// Determines whether an encoded VP9 frame is a key frame by parsing the start of its uncompressed
    /// header (frame_marker, profile, show_existing_frame, frame_type).
    /// </summary>
    public static bool IsKeyFrame(byte[] frame)
    {
        if (frame == null || frame.Length < 1)
        {
            return false;
        }

        byte b0 = frame[0];

        // frame_marker is 2 bits and must be 0b10.
        if (((b0 >> 6) & 0x03) != 0x02)
        {
            return false;
        }

        int profile = (((b0 >> 4) & 0x01) << 1) | ((b0 >> 5) & 0x01); // profile_high << 1 | profile_low.

        // Bit cursor (from the MSB) sitting just past frame_marker (2) and profile (2).
        int bit = 4;
        if (profile == 3)
        {
            bit++; // reserved_zero bit.
        }

        bool showExistingFrame = ((b0 >> (7 - bit)) & 0x01) != 0;
        if (showExistingFrame)
        {
            return false; // Repeats a previously decoded frame; not a key frame.
        }

        bool frameType = ((b0 >> (7 - (bit + 1))) & 0x01) != 0; // 0 = KEY_FRAME, 1 = NON_KEY_FRAME.
        return !frameType;
    }
}
