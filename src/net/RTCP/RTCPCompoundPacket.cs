﻿//-----------------------------------------------------------------------------
// Filename: RTCPCompoundPacket.cs
//
// Description: Represents an RTCP compound packet consisting of 1 or more
// RTCP packets combined together in a single buffer.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 30 Dec 2019	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Represents an RTCP compound packet consisting of 1 or more
    /// RTCP packets combined together in a single buffer. According to RFC3550 RTCP 
    /// transmissions should always have at least 2 RTCP packets (a sender/receiver report
    /// and an SDES report). This implementation does not enforce that constraint for
    /// received reports but does for sends.
    /// </summary>
    public class RTCPCompoundPacket
    {
        private static ILogger logger = Log.Logger;

        public RTCPSenderReport SenderReport { get; private set; }
        public RTCPReceiverReport ReceiverReport { get; private set; }
        public RTCPSDesReport SDesReport { get; private set; }
        public RTCPBye Bye { get; set; }

        public RTCPCompoundPacket(RTCPSenderReport senderReport, RTCPSDesReport sdesReport)
        {
            SenderReport = senderReport;
            SDesReport = sdesReport;
        }

        /// <summary>
        /// Creates a new RTCP compound packet from a serialised buffer.
        /// </summary>
        /// <param name="packet">The serialised RTCP compound packet to parse.</param>
        public RTCPCompoundPacket(byte[] packet)
        {
            // The payload type field is the second byte in the RTCP header.
            int offset = 0;
            while(offset < packet.Length)
            {
                if(packet.Length - offset < RTCPHeader.HEADER_BYTES_LENGTH)
                {
                    // Not enough bytes left for a RTCP header.
                    break;
                }
                else
                {
                    if(offset != 0)
                    {
                        packet = packet.Skip(offset).ToArray();
                    }

                    byte packetTypeID = packet[1];
                    switch (packetTypeID)
                    {
                        case (byte)RTCPReportTypesEnum.SR:
                            SenderReport = new RTCPSenderReport(packet);
                            int srLength = (SenderReport != null) ? SenderReport.GetBytes().Length : Int32.MaxValue;
                            offset += srLength;
                            break;
                        case (byte)RTCPReportTypesEnum.RR:
                            ReceiverReport = new RTCPReceiverReport(packet);
                            int rrLength = (ReceiverReport != null) ? ReceiverReport.GetBytes().Length : Int32.MaxValue;
                            offset += rrLength;
                            break;
                        case (byte)RTCPReportTypesEnum.SDES:
                            SDesReport = new RTCPSDesReport(packet);
                            int sdesLength = (SDesReport != null) ? SDesReport.GetBytes().Length : Int32.MaxValue;
                            offset += sdesLength;
                            break;
                        case (byte)RTCPReportTypesEnum.BYE:
                            Bye = new RTCPBye(packet);
                            int byeLength = (Bye != null) ? Bye.GetBytes().Length : Int32.MaxValue;
                            offset += byeLength;
                            break;
                        default:
                            logger.LogWarning($"RTCPCompoundPacket did not recognise packet type ID {packetTypeID}.");
                            offset = Int32.MaxValue;
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Serialises a compound RTCP packet to a byte array ready for transmission.
        /// </summary>
        /// <returns>A byte array representing a serialised compound RTCP packet.</returns>
        public byte[] GetBytes()
        {
            if (SenderReport == null && ReceiverReport == null)
            {
                throw new ApplicationException("An RTCP compound packet must have either a Sender or Receiver report set.");
            }
            else if(SDesReport == null)
            {
                throw new ApplicationException("An RTCP compound packet must have an SDES report set.");
            }

            List<byte> compoundBuffer = new List<byte>();
            compoundBuffer.AddRange((SenderReport != null) ? SenderReport.GetBytes() : ReceiverReport.GetBytes());
            compoundBuffer.AddRange(SDesReport.GetBytes());
            
            if(Bye != null)
            {
                compoundBuffer.AddRange(Bye.GetBytes());
            }

            return compoundBuffer.ToArray();
        }
    }
}
