//-----------------------------------------------------------------------------
// Filename: DCEP.cs
//
// Description: Contains functions for working with the WebRTC Data
// Channel Establishment Protocol (DCEP).
//
// Remarks:
//
// - RFC8832 "WebRTC Data Channel Establishment Protocol"
//   https://tools.ietf.org/html/rfc8832
//   The Data Channel Establishment Protocol (DCEP) is designed to
//   provide, in the WebRTC data channel context, a simple in-
//   band method for opening symmetric data channels. DCEP messages
//   are sent within SCTP DATA chunks (this is the in-band bit) and 
//   uses a two-way handshake to open a data channel.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 24 MAr 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public enum DataChannelMessageTypes : byte
    {
        ACK = 0x02,
        OPEN = 0x03,
    }

    public enum DataChannelTypes : byte
    {
        /// <summary>
        /// The data channel provides a reliable in-order bidirectional communication.
        /// </summary>
        DATA_CHANNEL_RELIABLE = 0x00,

        /// <summary>
        /// The data channel provides a partially reliable in-order bidirectional
        /// communication. User messages will not be retransmitted more
        /// times than specified in the Reliability Parameter
        /// </summary>
        DATA_CHANNEL_PARTIAL_RELIABLE_REXMIT = 0x01,

        /// <summary>
        /// The data channel provides a partially reliable in-order bidirectional
        /// communication. User messages might not be transmitted or
        /// retransmitted after a specified lifetime given in milliseconds
        /// in the Reliability Parameter. This lifetime starts when
        /// providing the user message to the protocol stack.
        /// </summary>
        DATA_CHANNEL_PARTIAL_RELIABLE_TIMED = 0x02,

        /// <summary>
        /// The data channel provides a reliable unordered bidirectional communication.
        /// </summary>
        DATA_CHANNEL_RELIABLE_UNORDERED = 0x80,

        /// <summary>
        /// The data channel provides a partially reliable unordered bidirectional
        /// communication. User messages will not be retransmitted more
        /// times than specified in the Reliability Parameter.
        /// </summary>
        DATA_CHANNEL_PARTIAL_RELIABLE_REXMIT_UNORDERED = 0x81,

        /// <summary>
        /// The data channel provides a partially reliable unordered bidirectional
        /// communication. User messages might not be transmitted or
        /// retransmitted after a specified lifetime given in milliseconds
        /// in the Reliability Parameter. This lifetime starts when
        /// providing the user message to the protocol stack.
        /// </summary>
        DATA_CHANNEL_PARTIAL_RELIABLE_TIMED_UNORDERED = 0x82
    }

    /// <summary>
    /// Represents a Data Channel Establishment Protocol (DECP) OPEN message.
    /// This message is initially sent using the data channel on the stream
    /// used for user messages.
    /// </summary>
    /// <remarks>
    /// See https://tools.ietf.org/html/rfc8832#section-5.1
    /// </remarks>
    public struct DataChannelOpenMessage
    {
        public const int DCEP_OPEN_FIXED_PARAMETERS_LENGTH = 12;

        /// <summary>
        ///  This field holds the IANA-defined message type for the
        /// DATA_CHANNEL_OPEN message.The value of this field is 0x03.
        /// </summary>
        public byte MessageType; 

        /// <summary>
        /// This field specifies the type of data channel to be opened.
        /// For a list of the formal options <see cref="DataChannelTypes"/>.
        /// </summary>
        public byte ChannelType;

        /// <summary>
        /// The priority of the data channel.
        /// </summary>
        public ushort Priority;

        /// <summary>
        /// Used to set tolerance for partially reliable data channels.
        /// </summary>
        public uint Reliability;

        /// <summary>
        /// The name of the data channel. May be an empty string.
        /// </summary>
        public string Label;

        /// <summary>
        /// If it is a non-empty string, it specifies a protocol registered in the
        /// "WebSocket Subprotocol Name Registry" created in RFC6455.
        /// </summary>
        /// <remarks>
        /// The websocket subprotocol names and specification are available at
        /// https://tools.ietf.org/html/rfc7118
        /// </remarks>
        public string Protocol;

        /// <summary>
        /// Parses the an DCEP open message from a buffer.
        /// </summary>
        /// <param name="buffer">The buffer to parse the message from.</param>
        /// <param name="posn">The position in the buffer to start parsing from.</param>
        /// <returns>A new DCEP open message instance.</returns>
        public static DataChannelOpenMessage Parse(ReadOnlySpan<byte> buffer, int posn)
        {
            if (buffer.Length < DCEP_OPEN_FIXED_PARAMETERS_LENGTH)
            {
                throw new ApplicationException("The buffer did not contain the minimum number of bytes for a DCEP open message.");
            }

            var dcepOpen = new DataChannelOpenMessage();

            dcepOpen.MessageType = buffer[posn];
            dcepOpen.ChannelType = buffer[posn + 1];
            dcepOpen.Priority = NetConvert.ParseUInt16(buffer, posn + 2);
            dcepOpen.Reliability = NetConvert.ParseUInt32(buffer, posn + 4);

            ushort labelLength = NetConvert.ParseUInt16(buffer, posn + 8);
            ushort protocolLength = NetConvert.ParseUInt16(buffer, posn + 10);

            if (labelLength > 0)
            {
                dcepOpen.Label = buffer.Slice(12, labelLength).ToString(Encoding.UTF8);
            }

            if (protocolLength > 0)
            {
                dcepOpen.Protocol = buffer.Slice(12 + labelLength, protocolLength).ToString(Encoding.UTF8);
            }

            return dcepOpen;
        }

        /// <summary>
        /// Gets the length of the serialised DCEP OPEN message.
        /// </summary>
        /// <returns>The serialised length of this DECEP OPEN message.</returns>
        public int GetLength()
        {
            ushort labelLength = (ushort)(Label != null ? Encoding.UTF8.GetByteCount(Label) : 0);
            ushort protocolLength = (ushort)(Protocol != null ? Encoding.UTF8.GetByteCount(Protocol) : 0);

            return DCEP_OPEN_FIXED_PARAMETERS_LENGTH + labelLength + protocolLength;
        }

        /// <summary>
        /// Serialises a Data Channel Establishment Protocol (DECP) OPEN message to a 
        /// pre-allocated buffer.
        /// </summary>
        /// <param name="buffer">The buffer to write the serialised chunk bytes to. It
        /// must have the required space already allocated.</param>
        /// <param name="posn">The position in the buffer to write to.</param>
        /// <returns>The number of bytes, including padding, written to the buffer.</returns>
        public ushort WriteTo(byte[] buffer, int posn)
        {
            buffer[posn] = MessageType;
            buffer[posn + 1] = ChannelType;
            NetConvert.ToBuffer(Priority, buffer, posn + 2);
            NetConvert.ToBuffer(Reliability, buffer, posn + 4);

            ushort labelLength = (ushort)(Label != null ? Encoding.UTF8.GetByteCount(Label) : 0);
            ushort protocolLength = (ushort)(Protocol != null ? Encoding.UTF8.GetByteCount(Protocol) : 0);

            NetConvert.ToBuffer(labelLength, buffer, posn + 8);
            NetConvert.ToBuffer(protocolLength, buffer, posn + 10);

            posn += DCEP_OPEN_FIXED_PARAMETERS_LENGTH;

            if (labelLength > 0)
            {
                Buffer.BlockCopy(Encoding.UTF8.GetBytes(Label), 0, buffer, posn, labelLength);
                posn += labelLength;
            }

            if (protocolLength > 0)
            {
                Buffer.BlockCopy(Encoding.UTF8.GetBytes(Protocol), 0, buffer, posn, protocolLength);
                posn += protocolLength;
            }

            return (ushort)posn;
        }

        /// <summary>
        /// Serialises the DCEP OPEN message to a buffer.
        /// </summary>
        public byte[] GetBytes()
        {
            var buffer = new byte[GetLength()];
            WriteTo(buffer, 0);
            return buffer;
        }
    }
}
