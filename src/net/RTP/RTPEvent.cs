//-----------------------------------------------------------------------------
// Filename: RTPEvent.cs
//
// Description: Represents an RTP DTMF event as specified in RFC2833.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 12 Nov 2019	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.ComponentModel;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class RTPEvent
    {
        public const int DTMF_PACKET_LENGTH = 4;   // The length of an RTP DTMF event packet.
        public const ushort DEFAULT_VOLUME = 10;
        public const int DUPLICATE_COUNT = 3;       // The number of packets to duplicate for the start and end of an event.

        /// <summary>
        /// The ID for the event. For a DTMF tone this is the digit/letter to represent.
        /// </summary>
        public byte EventID { get; private set; }

        /// <summary>
        /// If true the end of event flag will be set.
        /// </summary>
        public bool EndOfEvent { get; set; }

        /// <summary>
        /// The volume level to set.
        /// </summary>
        public ushort Volume { get; private set; }

        /// <summary>
        /// The duration for the full event.
        /// </summary>
        public ushort TotalDuration { get; private set; }

        /// <summary>
        /// The duration of the current event payload. This value is set in the RTP event data payload.
        /// </summary>
        public ushort Duration { get; set; }

        /// <summary>
        /// The ID of the event payload type. This gets set in the RTP header.
        /// </summary>
        public int PayloadTypeID { get; private set; }

        /// <summary>
        /// Create a new RTP event object.
        /// </summary>
        /// <param name="eventID">The ID for the event. For a DTMF tone this is the digit/letter to represent.</param>
        /// <param name="endOfEvent">If true the end of event flag will be set.</param>
        /// <param name="volume">The volume level to set.</param>
        /// <param name="totalDuration">The event duration.</param>
        /// <param name="payloadTypeID">The ID of the event payload type. This gets set in the RTP header.</param>
        public RTPEvent(byte eventID, bool endOfEvent, ushort volume, ushort totalDuration, int payloadTypeID)
        {
            EventID = eventID;
            EndOfEvent = endOfEvent;
            Volume = volume;
            TotalDuration = totalDuration;
            PayloadTypeID = payloadTypeID;
        }

        /// <summary>
        /// Gets the raw buffer for the event.
        /// </summary>
        /// <returns>A raw byte buffer for the event.</returns>
        [Obsolete("Use WriteEventPayload(Span<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public byte[] GetEventPayload()
        {
            byte[] payload = new byte[DTMF_PACKET_LENGTH];

            WriteEventPayload(payload);

            return payload;
        }

        /// <summary>
        /// Gets the raw buffer for the event.
        /// </summary>
        /// <returns>A raw byte buffer for the event.</returns>
        public void WriteEventPayload(Span<byte> destination)
        {
            destination[0] = EventID;
            destination[1] = (byte)(EndOfEvent ? 0x80 : 0x00);
            destination[1] += (byte)(Volume & 0x3F); // Volume uses 6 bits (0-63)

            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2, 2), Duration);
        }

        /// <summary>
        /// Extract and load an RTP Event from a packet buffer.
        /// </summary>
        /// <param name="packet">The packet buffer containing the RTP Event.</param>
        [Obsolete("Use RTPEvent(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public RTPEvent(byte[] packet)
            : this((ReadOnlySpan<byte>)packet)
        {
        }

        /// <summary>
        /// Extract and load an RTP Event from a packet buffer.
        /// </summary>
        /// <param name="packet">The packet buffer containing the RTP Event.</param>
        public RTPEvent(ReadOnlySpan<byte> packet)
        {
            if (packet.Length < DTMF_PACKET_LENGTH)
            {
                throw new ApplicationException("The packet did not contain the minimum number of bytes for an RTP Event packet.");
            }

            EventID = packet[0];
            EndOfEvent = (packet[1] & 0x80) > 1;
            Volume = (ushort)(packet[1] & 0xcf);

            Duration = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2));
        }
    }
}
