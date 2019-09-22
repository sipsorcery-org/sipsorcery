//-----------------------------------------------------------------------------
// Filename: RTPHeader.cs
//
// Description: RTP Header as defined in RFC3550.
// 
//
// RTP Header:
// 0                   1                   2                   3
// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
// |V=2|P|X|  CC   |M|     PT      |       sequence number         |
// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
// |                           timestamp                           |
// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
// |           synchronization source (SSRC) identifier            |
// +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
// |            contributing source (CSRC) identifiers             |
// |                             ....                              |
// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
// (V)ersion (2 bits) = 2
// (P)adding (1 bit) = Indicates whether the packet contains additional padding octets.
// e(X)tension (1 bit) = If set the fixed header must be followed by exactly one header extension.
// CSRC Count (CC) (4 bits) = Number of Contributing Source identifiers following fixed header.
// (M)arker (1 bit) = Used by profiles to enable marks to be set in the data.
// Payload Type (PT) (7 bits) = RTP payload type.
//  - GSM: 000 0011 (3)
//  - PCMU (G711): 000 0000 (0)
// Sequence Number (16 bits) = Increments by one for each RTP packet sent, initial value random.
// Timestamp (32 bits) = The sampling instant of the first bit in the RTP data packet.
// Synchronisation Source Id (SSRC) (32 bits) = The unique synchronisation source for the data stream.
// Contributing Source Identifier Ids (0 to 15 items 32 bits each, see CC) = List of contributing sources for the payload in this field.
//
//
// Wallclock time (absolute date and time) is represented using the
// timestamp format of the Network Time Protocol (NTP), which is in
// seconds relative to 0h UTC on 1 January 1900 [4].  The full
// resolution NTP timestamp is a 64-bit unsigned fixed-point number with
// the integer part in the first 32 bits and the fractional part in the
// last 32 bits.  In some fields where a more compact representation is
// appropriate, only the middle 32 bits are used; that is, the low 16
// bits of the integer part and the high 16 bits of the fractional part.
// The high 16 bits of the integer part must be determined
// independently.
//
// History:
// 22 May 2005	Aaron Clauson	Created.
// 11 Aug 2019  Aaron Clauson   Added full license header.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2005-2019 Aaron Clauson (aaron@sipsorcery.com), Montreux, Switzerland (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class RTPHeader
    {
        public const int MIN_HEADER_LEN = 12;

        //public readonly static DateTime ZeroTime = new DateTime(2007, 1, 1).ToUniversalTime();

        public const int RTP_VERSION = 2;

        public int Version = RTP_VERSION;						// 2 bits.
        public int PaddingFlag = 0;								// 1 bit.
        public int HeaderExtensionFlag = 0;						// 1 bit.
        public int CSRCCount = 0;								// 4 bits
        public int MarkerBit = 0;								// 1 bit.
        public int PayloadType = (int)RTPPayloadTypesEnum.PCMU;	// 7 bits.
        public UInt16 SequenceNumber;							// 16 bits.
        public uint Timestamp;									// 32 bits.
        public uint SyncSource;									// 32 bits.
        public int[] CSRCList;									// 32 bits.
        public UInt16 ExtensionProfile;                         // 16 bits.
        public UInt16 ExtensionLength;                          // 16 bits,  length of the header extensions in 32 bit words.
        public byte[] ExtensionPayload;

        public int Length
        {
            get { return MIN_HEADER_LEN + (CSRCCount * 4) + ((HeaderExtensionFlag == 0) ? 0 : 4 + (ExtensionLength * 4)); }
        }

        public RTPHeader()
        {
            SequenceNumber = Crypto.GetRandomUInt16();
            SyncSource = Crypto.GetRandomUInt();
            Timestamp = Crypto.GetRandomUInt();
        }

        /// <summary>
        /// Extract and load the RTP header from an RTP packet.
        /// </summary>
        /// <param name="packet"></param>
        public RTPHeader(byte[] packet)
        {
            if (packet.Length < MIN_HEADER_LEN)
            {
                throw new ApplicationException("The packet did not contain the minimum number of bytes for an RTP header packet.");
            }

            UInt16 firstWord = BitConverter.ToUInt16(packet, 0);

            if (BitConverter.IsLittleEndian)
            {
                firstWord = NetConvert.DoReverseEndian(firstWord);
                SequenceNumber = NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, 2));
                Timestamp = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 4));
                SyncSource = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 8));
            }
            else
            {
                SequenceNumber = BitConverter.ToUInt16(packet, 2);
                Timestamp = BitConverter.ToUInt32(packet, 4);
                SyncSource = BitConverter.ToUInt32(packet, 8);
            }

            Version = firstWord >> 14;
            PaddingFlag = (firstWord >> 13) & 0x1;
            HeaderExtensionFlag = (firstWord >> 12) & 0x1;
            CSRCCount = (firstWord >> 8) & 0xf;
            MarkerBit = (firstWord >> 7) & 0x1;
            PayloadType = firstWord & 0x7f;

            int headerAndCSRCLength = 12 + 4 * CSRCCount;

            if (HeaderExtensionFlag == 1 && (packet.Length >= (headerAndCSRCLength + 4)))
            {
                if (BitConverter.IsLittleEndian)
                {
                    ExtensionProfile = NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, 12 + 4 * CSRCCount));
                    ExtensionLength = NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, 14 + 4 * CSRCCount));
                }
                else
                {
                    ExtensionProfile = BitConverter.ToUInt16(packet, 12 + 4 * CSRCCount);
                    ExtensionLength = BitConverter.ToUInt16(packet, 14 + 4 * CSRCCount);
                }

                if(ExtensionLength > 0 && packet.Length >= (headerAndCSRCLength + 4 + ExtensionLength * 4))
                {
                    ExtensionPayload = new byte[ExtensionLength * 4];
                    Buffer.BlockCopy(packet, headerAndCSRCLength + 4, ExtensionPayload, 0, ExtensionLength * 4);
                }
            }
        }

        public byte[] GetHeader(UInt16 sequenceNumber, uint timestamp, uint syncSource)
        {
            SequenceNumber = sequenceNumber;
            Timestamp = timestamp;
            SyncSource = syncSource;

            return GetBytes();
        }

        public byte[] GetBytes()
        {
            byte[] header = new byte[Length];

            UInt16 firstWord = Convert.ToUInt16(Version * 16384 + PaddingFlag * 8192 + HeaderExtensionFlag * 4096 + CSRCCount * 256 + MarkerBit * 128 + PayloadType);

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(firstWord)), 0, header, 0, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SequenceNumber)), 0, header, 2, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(Timestamp)), 0, header, 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SyncSource)), 0, header, 8, 4);

                if (HeaderExtensionFlag == 1)
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(ExtensionProfile)), 0, header, 12 + 4 * CSRCCount, 2);
                    Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(ExtensionLength)), 0, header, 14 + 4 * CSRCCount, 2);
                }
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(firstWord), 0, header, 0, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(SequenceNumber), 0, header, 2, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(Timestamp), 0, header, 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(SyncSource), 0, header, 8, 4);

                if (HeaderExtensionFlag == 1)
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(ExtensionProfile), 0, header, 12 + 4 * CSRCCount, 2);
                    Buffer.BlockCopy(BitConverter.GetBytes(ExtensionLength), 0, header, 14 + 4 * CSRCCount, 2);
                }
            }

            if (ExtensionLength > 0 && ExtensionPayload != null)
            {
                Buffer.BlockCopy(ExtensionPayload, 0, header, 16 + 4 * CSRCCount, ExtensionLength * 4);
            }

            return header;
        }

        /*public static uint GetWallclockUTCStamp(DateTime time)
        {
            return Convert.ToUInt32(time.ToUniversalTime().Subtract(DateTime.Now.Date).TotalMilliseconds);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="timestamp"></param>
        /// <returns>The timestamp as a UTC DateTime object.</returns>
        public static DateTime GetWallclockUTCTimeFromStamp(uint timestamp)
        {
            return DateTime.Now.Date.AddMilliseconds(timestamp);
        }*/
    }
}
