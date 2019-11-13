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
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class RTPEvent
    {
        public const ushort DEFAULT_VOLUME = 10;
        public const int DUPLICATE_COUNT = 3;  // The number of packets to duplicate for the start and end of an event.

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
        public RTPEvent(byte eventID, bool endOfEvent, ushort volume, ushort totalDduration, int payloadTypeID)
        {
            EventID = eventID;
            EndOfEvent = endOfEvent;
            Volume = volume;
            TotalDuration = totalDduration;
            PayloadTypeID = payloadTypeID;
        }
        
        /// <summary>
        /// Gets the raw buffer for the event.
        /// </summary>
        /// <returns>A raw byte buffer for the event.</returns>
        public byte[] GetEventPayload()
        {
            byte[] payload = new byte[4];

            payload[0] = EventID;
            payload[1] = (byte)(EndOfEvent ? 0x80 : 0x00);
            payload[1] += (byte)(Volume & 0xcf); // The Volume field uses 6 bits.

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(Duration)), 0, payload, 2, 2);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(Duration), 0, payload, 2, 2);

            }

            return payload;
        }
    }
}
