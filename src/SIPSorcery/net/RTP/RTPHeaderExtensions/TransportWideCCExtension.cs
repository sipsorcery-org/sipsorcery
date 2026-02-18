/*
 * File: TransportWideCCExtension.cs
 * 
 * Description:
 *   Implements the Transport Wide Congestion Control (TWCC) RTP header extension.
 *   This extension carries a 16-bit sequence number and adheres to the IETF draft:
 *   http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
 *   It provides functionality to marshal and unmarshal the TWCC header extension.
 * 
 * Author:        Sean Tearney
 * Date:          2025-02-22
 * 
 * License:       BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
 * 
 * Change Log:
 *   2025-02-20  Initial creation.
 */
using System;

namespace SIPSorcery.Net
{
    /// <summary>
    /// TransportWideCCExtension implements the Transport Wide Congestion Control (TWCC)
    /// RTP header extension as defined in:
    /// http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
    /// 
    /// This extension carries a 16-bit sequence number (2 bytes of payload).
    /// The one-byte header is constructed as (id &lt;&lt; 4) | (extensionSize - 1).
    /// </summary>
    public class TransportWideCCExtension : RTPHeaderExtension
    {
        //

        public const string RTP_HEADER_EXTENSION_URI = "http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01";
        //public const string RTP_HEADER_EXTENSION_URI_ALT = "http://www.webrtc.org/experiments/rtp-hdrext/transport-wide-cc-02";


        internal const int RTP_HEADER_EXTENSION_SIZE = 2; // TWCC payload: 2 bytes for sequence number.

        /// <summary>
        /// The TWCC sequence number.
        /// </summary>
        public ushort SequenceNumber { get; private set; }

        /// <summary>
        /// Constructs a TWCC header extension with the negotiated extension id.
        /// </summary>
        /// <param name="id">The negotiated header extension id.</param>
        public TransportWideCCExtension(int id)
            : base(id, RTP_HEADER_EXTENSION_URI, RTP_HEADER_EXTENSION_SIZE, RTPHeaderExtensionType.OneByte)
        {
        }


        /// <summary>
        /// Generic setter override. Expects a ushort representing the sequence number.
        /// </summary>
        /// <param name="value">The TWCC sequence number as an object (ushort).</param>
        public override void Set(object value)
        {
            if (value is ushort seq)
            {
                SequenceNumber = seq;
            }
            else
            {
                throw new ArgumentException("Value must be a ushort representing the TWCC sequence number", nameof(value));
            }
        }

        /// <summary>
        /// Marshals the TWCC header extension to a byte array.
        /// The first byte is the one-byte header (with id and length) and
        /// the following two bytes are the sequence number in network (big-endian) order.
        /// </summary>
        /// <returns>A byte array containing the marshalled TWCC header extension.</returns>
        public override byte[] Marshal()
        {
            // Construct the one-byte header. (id << 4) | (extensionSize - 1)
            byte headerByte = (byte)((Id << 4) | (RTP_HEADER_EXTENSION_SIZE - 1));

            // Convert the sequence number to a 2-byte array in big-endian order.
            byte[] seqBytes = BitConverter.GetBytes(SequenceNumber);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(seqBytes);
            }

            return new byte[] { headerByte, seqBytes[0], seqBytes[1] };
        }

        /// <summary>
        /// Unmarshals the TWCC header extension from the provided data.
        /// </summary>
        /// <param name="header">The RTP header (if additional context is needed).</param>
        /// <param name="data">The extension payload (should be exactly 2 bytes).</param>
        /// <returns>The extracted TWCC sequence number (as a ushort).</returns>
        public override object Unmarshal(RTPHeader header, byte[] data)
        {
            if (data.Length != RTP_HEADER_EXTENSION_SIZE)
            {
                throw new ArgumentException($"Invalid TWCC extension payload size, expected {RTP_HEADER_EXTENSION_SIZE} but got {data.Length}.");
            }

            // Combine the two bytes into a ushort (big-endian).
            ushort seqNum = (ushort)((data[0] << 8) | data[1]);
            return seqNum;
        }
    }
}
