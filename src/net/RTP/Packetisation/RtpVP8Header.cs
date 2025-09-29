﻿//-----------------------------------------------------------------------------
// Filename: RtpVP8Header.cs
//
// Description: Represents the RTP header to use for a VP8 encoded payload as per
// https://tools.ietf.org/html/rfc7741.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 11 Nov 2014	Aaron Clauson	Created, Hobart, Australia.
// 11 Aug 2019  Aaron Clauson   Added full license header.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Representation of the VP8 RTP header as specified in RFC7741
    /// https://tools.ietf.org/html/rfc7741.
    /// </summary>
    public class RtpVP8Header
    {
        // Payload Descriptor Fields.
        public bool ExtendedControlBitsPresent;     // Indicated whether extended control bits are present.
        public bool NonReferenceFrame;              // When set indicates the frame can be discarded without affecting any other frames.
        public bool StartOfVP8Partition;            // Should be set when the first payload octet is the start of a new VP8 partition.
        public byte PartitionIndex;                 // Denotes the VP8 partition index that the first payload octet of the packet belongs to.
        public bool IsPictureIDPresent;
        public bool IsTL0PICIDXPresent;
        public bool IsTIDPresent;
        public bool IsKEYIDXPresent;
        public ushort PictureID;

        // Payload Header Fields.
        public int FirstPartitionSize;              // The size of the first partition in bytes is calculated from the 19 bits in Size0, SIze1 & Size2 as: size = Size0 + (8 x Size1) + (2048 8 Size2).
        public bool ShowFrame;
        public int VersionNumber;
        public bool IsKeyFrame;

        public int Length { get; private set; }

        private int _payloadDescriptorLength;
        public int PayloadDescriptorLength
        {
            get { return _payloadDescriptorLength; }
        }

        public RtpVP8Header()
        { }

        public static RtpVP8Header GetVP8Header(ReadOnlySpan<byte> rtpPayload)
        {
            var vp8Header = new RtpVP8Header();
            var payloadHeaderStartIndex = 1;

            // First byte of payload descriptor.
            vp8Header.ExtendedControlBitsPresent = ((rtpPayload[0] >> 7) & 0x01) == 1;
            vp8Header.StartOfVP8Partition = ((rtpPayload[0] >> 4) & 0x01) == 1;
            vp8Header.Length = 1;

            // Is second byte being used.
            if (vp8Header.ExtendedControlBitsPresent)
            {
                vp8Header.IsPictureIDPresent = ((rtpPayload[1] >> 7) & 0x01) == 1;
                vp8Header.IsTL0PICIDXPresent = ((rtpPayload[1] >> 6) & 0x01) == 1;
                vp8Header.IsTIDPresent = ((rtpPayload[1] >> 5) & 0x01) == 1;
                vp8Header.IsKEYIDXPresent = ((rtpPayload[1] >> 4) & 0x01) == 1;
                vp8Header.Length = 2;
                payloadHeaderStartIndex = 2;
            }

            // Is the picture ID being used.
            if (vp8Header.IsPictureIDPresent)
            {
                if (((rtpPayload[2] >> 7) & 0x01) == 1)
                {
                    // The Picture ID is using two bytes.
                    vp8Header.Length = 4;
                    payloadHeaderStartIndex = 4;
                    vp8Header.PictureID = Unsafe.ReadUnaligned<ushort>(ref MemoryMarshal.GetReference((rtpPayload.Slice(2)))); // BitConverter.ToUInt16(rtpPayload, 2);
                }
                else
                {
                    // The picture ID is using one byte.
                    vp8Header.PictureID = rtpPayload[2];
                    vp8Header.Length = 3;
                    payloadHeaderStartIndex = 3;
                }
            }

            if (vp8Header.IsTL0PICIDXPresent)
            {
                vp8Header.Length++;
                payloadHeaderStartIndex++;
            }
            if (vp8Header.IsTIDPresent || vp8Header.IsKEYIDXPresent)
            {
                vp8Header.Length++;
                payloadHeaderStartIndex++;
            }

            vp8Header._payloadDescriptorLength = payloadHeaderStartIndex;

            var isPID0 = ((rtpPayload[0] & (1 << 2)) == 0) && ((rtpPayload[0] & (1 << 1)) == 0) && ((rtpPayload[0] & (1 << 0)) == 0);
            if (vp8Header.StartOfVP8Partition && isPID0)
            {
                vp8Header.IsKeyFrame = (rtpPayload[payloadHeaderStartIndex] & (1 << 0)) == 0;
            }

            return vp8Header;
        }
    }
}
