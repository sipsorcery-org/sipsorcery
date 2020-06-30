//-----------------------------------------------------------------------------
// Filename: RTCPHeader.cs
//
// Description: RTCP Header as defined in RFC3550.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com
//
// History:
// 22 Feb 2007	Aaron Clauson	Created, Hobart, Australia.
// 29 Jun 2020  Aaron Clauson   Added support for feedback report types.
//
// Notes:
//
//      RTCP Header
//        0                   1                   2                   3
//        0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//header |V=2|P|    RC   |   PT=SR=200   |             Length            |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                         Payload                               |
//       +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//
// (V)ersion (2 bits) = 2
// (P)adding (1 bit) = Indicates whether the packet contains additional padding octets.
// Reception Report Count (RC) (5 bits) = The number of reception report blocks contained in this packet. A
//      value of zero is valid.
// Packet Type (PT) (8 bits) = Contains the constant 200 to identify this as an RTCP SR packet.
// Length (16 bits) = The length of this RTCP packet in 32-bit words minus one, including the header and any padding.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// The different types of RTCP packets as defined in RFC3550.
    /// </summary>
    public enum RTCPReportTypesEnum : byte
    {
        SR = 200,     // Send Report.
        RR = 201,     // Receiver Report.
        SDES = 202,   // Session Description.
        BYE = 203,    // Goodbye.
        APP = 204,    // Application-defined.

        // From RFC5760: https://tools.ietf.org/html/rfc5760
        // "RTP Control Protocol (RTCP) Extensions for
        // Single-Source Multicast Sessions with Unicast Feedback"

        RTPFB = 205,    // Generic RTP feedback 
        PSFB = 206,     // Payload-specific feedback 
        XR = 207,       // RTCP Extension
    }

    public class RTCPReportTypes
    {
        public static RTCPReportTypesEnum GetRTCPReportTypeForId(ushort rtcpReportTypeId)
        {
            return (RTCPReportTypesEnum)Enum.Parse(typeof(RTCPReportTypesEnum), rtcpReportTypeId.ToString(), true);
        }
    }

    /// <summary>
    /// RTCP Header as defined in RFC3550.
    /// </summary>
    public class RTCPHeader
    {
        public const int HEADER_BYTES_LENGTH = 4;
        public const int MAX_RECEPTIONREPORT_COUNT = 32;
        public const int RTCP_VERSION = 2;

        public int Version { get; private set; } = RTCP_VERSION;         // 2 bits.
        public int PaddingFlag { get; private set; } = 0;                 // 1 bit.
        public int ReceptionReportCount { get; private set; } = 0;        // 5 bits.
        public RTCPReportTypesEnum PacketType { get; private set; }       // 8 bits.
        public UInt16 Length { get; private set; }                        // 16 bits.

        /// <summary>
        /// The Feedback Message Type is used for RFC4585 transport layer feedback reports.
        /// When used this field gets set in place of the Reception Report Counter field.
        /// </summary>
        public RTCPFeedbackTypesEnum FeedbackMessageType { get; private set; } = RTCPFeedbackTypesEnum.unassigned;

        /// <summary>
        /// The Payload Feedback Message Type is used for RFC4585 payload layer feedback reports.
        /// When used this field gets set in place of the Reception Report Counter field.
        /// </summary>
        public PSFBFeedbackTypesEnum PayloadFeedbackMessageType { get; private set; } = PSFBFeedbackTypesEnum.unassigned;

        public RTCPHeader(RTCPFeedbackTypesEnum feedbackType)
        {
            PacketType = RTCPReportTypesEnum.RTPFB;
            FeedbackMessageType = feedbackType;
        }

        public RTCPHeader(PSFBFeedbackTypesEnum feedbackType)
        {
            PacketType = RTCPReportTypesEnum.PSFB;
            PayloadFeedbackMessageType = feedbackType;
        }

        public RTCPHeader(RTCPReportTypesEnum packetType, int reportCount)
        {
            PacketType = packetType;
            ReceptionReportCount = reportCount;
        }

        /// <summary>
        /// Identifies whether an RTCP header is for a standard RTCP packet or for an
        /// RTCP feedback report.
        /// </summary>
        /// <returns>True if the header is for an RTCP feedback report or false if not.</returns>
        public bool IsFeedbackReport()
        {
            if (PacketType == RTCPReportTypesEnum.RTPFB ||
                PacketType == RTCPReportTypesEnum.PSFB)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Extract and load the RTCP header from an RTCP packet.
        /// </summary>
        /// <param name="packet"></param>
        public RTCPHeader(byte[] packet)
        {
            if (packet.Length < HEADER_BYTES_LENGTH)
            {
                throw new ApplicationException("The packet did not contain the minimum number of bytes for an RTCP header packet.");
            }

            UInt16 firstWord = BitConverter.ToUInt16(packet, 0);

            if (BitConverter.IsLittleEndian)
            {
                firstWord = NetConvert.DoReverseEndian(firstWord);
                Length = NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, 2));
            }
            else
            {
                Length = BitConverter.ToUInt16(packet, 2);
            }

            Version = Convert.ToInt32(firstWord >> 14);
            PaddingFlag = Convert.ToInt32((firstWord >> 13) & 0x1);
            PacketType = (RTCPReportTypesEnum)(firstWord & 0x00ff);

            if (IsFeedbackReport())
            {
                if (PacketType == RTCPReportTypesEnum.RTPFB)
                {
                    FeedbackMessageType = (RTCPFeedbackTypesEnum)((firstWord >> 8) & 0x1f);
                }
                else
                {
                    PayloadFeedbackMessageType = (PSFBFeedbackTypesEnum)((firstWord >> 8) & 0x1f);
                }
            }
            else
            {
                ReceptionReportCount = Convert.ToInt32((firstWord >> 8) & 0x1f);
            }
        }

        public byte[] GetHeader(int receptionReportCount, UInt16 length)
        {
            if (receptionReportCount > MAX_RECEPTIONREPORT_COUNT)
            {
                throw new ApplicationException("The Reception Report Count value cannot be larger than " + MAX_RECEPTIONREPORT_COUNT + ".");
            }

            ReceptionReportCount = receptionReportCount;
            Length = length;

            return GetBytes();
        }

        /// <summary>
        /// The length of this RTCP packet in 32-bit words minus one,
        /// including the header and any padding.
        /// </summary>
        public void SetLength(ushort length)
        {
            Length = length;
        }

        public byte[] GetBytes()
        {
            byte[] header = new byte[4];

            UInt32 firstWord = ((uint)Version << 30) + ((uint)PaddingFlag << 29) + ((uint)PacketType << 16) + Length;

            if (IsFeedbackReport())
            {
                if (PacketType == RTCPReportTypesEnum.RTPFB)
                {
                    firstWord += (uint)FeedbackMessageType << 24;
                }
                else
                {
                    firstWord += (uint)PayloadFeedbackMessageType << 24;
                }
            }
            else
            {
                firstWord += (uint)ReceptionReportCount << 24;
            }

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(firstWord)), 0, header, 0, 4);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(firstWord), 0, header, 0, 4);
            }

            return header;
        }
    }
}
