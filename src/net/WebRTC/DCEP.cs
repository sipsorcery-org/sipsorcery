﻿//-----------------------------------------------------------------------------
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
using System.Buffers.Binary;
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
    public partial struct DataChannelOpenMessage : IByteSerializable
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
        public string? Protocol;

        /// <summary>
        /// Parses the an DCEP open message from a buffer.
        /// </summary>
        /// <param name="buffer">The buffer to parse the message from.</param>
        /// <returns>A new DCEP open message instance.</returns>
        public static DataChannelOpenMessage Parse(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < DCEP_OPEN_FIXED_PARAMETERS_LENGTH)
            {
                throw new ApplicationException("The buffer did not contain the minimum number of bytes for a DCEP open message.");
            }

            var dcepOpen = new DataChannelOpenMessage();

            dcepOpen.MessageType = buffer[0];
            dcepOpen.ChannelType = buffer[1];
            dcepOpen.Priority = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(2));
            dcepOpen.Reliability = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(4));

            var labelLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(8));
            var protocolLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(10));

            if (labelLength > 0)
            {
                dcepOpen.Label = Encoding.UTF8.GetString(buffer.Slice(12, labelLength));
            }

            if (protocolLength > 0)
            {
                dcepOpen.Protocol = Encoding.UTF8.GetString(buffer.Slice(12 + labelLength, protocolLength));
            }

            return dcepOpen;
        }

        /// <inheritdoc/>
        public int GetByteCount()
        {
            var labelLength = (ushort)(Label is { } ? Encoding.UTF8.GetByteCount(Label) : 0);
            var protocolLength = (ushort)(Protocol is { } ? Encoding.UTF8.GetByteCount(Protocol) : 0);

            return DCEP_OPEN_FIXED_PARAMETERS_LENGTH + labelLength + protocolLength;
        }

        /// <inheritdoc/>
        public int WriteBytes(Span<byte> buffer)
        {
            buffer[0] = MessageType;
            buffer[1] = ChannelType;
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(2), Priority);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(4), Reliability);

            var labelLength = (ushort)(Label is { } ? Encoding.UTF8.GetByteCount(Label) : 0);
            var protocolLength = (ushort)(Protocol is { } ? Encoding.UTF8.GetByteCount(Protocol) : 0);

            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(8), labelLength);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(10), protocolLength);

            var len = (ushort)DCEP_OPEN_FIXED_PARAMETERS_LENGTH;

            if (labelLength > 0)
            {
                Encoding.UTF8.GetBytes(Label.AsSpan(), buffer.Slice(len));
                len += labelLength;
            }

            if (protocolLength > 0)
            {
                Encoding.UTF8.GetBytes(Protocol.AsSpan(), buffer.Slice(len));
                len += protocolLength;
            }

            return len;
        }
    }
}
