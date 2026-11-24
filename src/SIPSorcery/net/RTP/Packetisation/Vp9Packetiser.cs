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
using System.Buffers;
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
    internal const int DESCRIPTOR_LENGTH = 3;

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
    /// <remarks>The payloads are the ones the <see cref="IBufferWriter{T}"/> overload writes, each in an array of its
    /// own.</remarks>
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

        for (int offset = 0; offset < frame.Length;)
        {
            int chunk = Math.Min(capacity, frame.Length - offset);

            var payload = new byte[DESCRIPTOR_LENGTH + chunk];
            WritePayloadDescriptor(payload, isKeyFrame, offset == 0, offset + chunk >= frame.Length, pictureId);
            Buffer.BlockCopy(frame, offset, payload, DESCRIPTOR_LENGTH, chunk);
            packets.Add(payload);

            offset += chunk;
        }

        return packets;
    }

    /// <summary>
    /// Fragments one encoded VP9 frame into RTP payloads, each prefixed with a VP9 payload descriptor, and writes
    /// them one after another into a buffer writer. The first payload has the B (start of frame) flag set and the
    /// last the E (end of frame) flag; the caller is responsible for setting the RTP marker bit on the last packet.
    /// </summary>
    /// <param name="frame">The encoded VP9 frame. Only read during the call.</param>
    /// <param name="maxPayloadSize">The maximum RTP payload size (descriptor included).</param>
    /// <param name="isKeyFrame">Whether the frame is a key frame; clears the P (inter-predicted) flag when true.</param>
    /// <param name="pictureId">The 15-bit picture ID to stamp on every fragment of this frame.</param>
    /// <param name="output">The payloads are written to it one after another, each with one
    /// <see cref="IBufferWriter{T}.GetSpan(int)"/> and one <see cref="IBufferWriter{T}.Advance(int)"/>.</param>
    /// <param name="payloadLengths">The length of each payload written is added to it, in order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> or <paramref name="payloadLengths"/> is
    /// null.</exception>
    /// <exception cref="ArgumentException"><paramref name="maxPayloadSize"/> leaves no room for the frame after the
    /// descriptor. An empty frame writes nothing and is not refused.</exception>
    public static void Packetize(ReadOnlySpan<byte> frame, int maxPayloadSize, bool isKeyFrame, int pictureId,
        IBufferWriter<byte> output, ICollection<int> payloadLengths)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (payloadLengths == null)
        {
            throw new ArgumentNullException(nameof(payloadLengths));
        }

        if (frame.IsEmpty)
        {
            return;
        }

        int capacity = maxPayloadSize - DESCRIPTOR_LENGTH;
        if (capacity <= 0)
        {
            throw new ArgumentException("The maximum RTP payload size is too small for VP9 packetisation.", nameof(maxPayloadSize));
        }

        for (int offset = 0; offset < frame.Length;)
        {
            int chunk = Math.Min(capacity, frame.Length - offset);
            int length = DESCRIPTOR_LENGTH + chunk;

            Span<byte> payload = output.GetSpan(length);
            WritePayloadDescriptor(payload, isKeyFrame, offset == 0, offset + chunk >= frame.Length, pictureId);
            frame.Slice(offset, chunk).CopyTo(payload.Slice(DESCRIPTOR_LENGTH));
            output.Advance(length);
            payloadLengths.Add(length);

            offset += chunk;
        }
    }

    /// <summary>
    /// Writes the VP9 payload descriptor that starts every payload: the flag octet and the 15-bit picture ID.
    /// </summary>
    /// <param name="destination">Where to write it, at least <see cref="DESCRIPTOR_LENGTH"/> bytes.</param>
    /// <param name="isKeyFrame">Whether the frame is a key frame; clears the P (inter-predicted) flag when true.</param>
    /// <param name="isFirst">Whether this is the frame's first payload, which sets the B flag.</param>
    /// <param name="isLast">Whether this is the frame's last payload, which sets the E flag.</param>
    /// <param name="pictureId">The 15-bit picture ID of the frame.</param>
    /// <returns>The number of bytes written, <see cref="DESCRIPTOR_LENGTH"/>.</returns>
    internal static int WritePayloadDescriptor(Span<byte> destination, bool isKeyFrame, bool isFirst, bool isLast, int pictureId)
    {
        int id = pictureId & MAX_PICTURE_ID;

        byte flags = I_BIT;
        if (!isKeyFrame) { flags |= P_BIT; }
        if (isFirst) { flags |= B_BIT; }
        if (isLast) { flags |= E_BIT; }

        destination[0] = flags;
        destination[1] = (byte)(0x80 | ((id >> 8) & 0x7F)); // M bit set, top 7 bits of the picture ID.
        destination[2] = (byte)(id & 0xFF);
        return DESCRIPTOR_LENGTH;
    }

    /// <summary>
    /// Determines whether an encoded VP9 frame is a key frame by parsing the start of its uncompressed
    /// header (frame_marker, profile, show_existing_frame, frame_type).
    /// </summary>
    public static bool IsKeyFrame(byte[] frame) => frame != null && IsKeyFrame(new ReadOnlySpan<byte>(frame));

    /// <summary>
    /// Determines whether an encoded VP9 frame is a key frame by parsing the start of its uncompressed
    /// header (frame_marker, profile, show_existing_frame, frame_type).
    /// </summary>
    public static bool IsKeyFrame(ReadOnlySpan<byte> frame)
    {
        if (frame.IsEmpty)
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
