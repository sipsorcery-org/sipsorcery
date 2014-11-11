//-----------------------------------------------------------------------------
// Filename: RTPVP8Header.cs
//
// Description: Represents the RTP header to use for a VP8 encoded payload as per
// http://tools.ietf.org/html/draft-ietf-payload-vp8-12.
//
//
// History:
// 11 Nov 2014	Aaron Clauson	Created.
//
// License: 
// Aaron Clauson
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.Net
{
     /// <summary>
    /// Exmaples of size Payload Header size calculations:
    /// 
    /// For length of first parition 54: S0 = 4, S1 = 0x32, S2 = 0.
    /// For length of first parition 1777: S0 = 1, S1 = 0xde, S2 = 0.
    /// 
     /// </summary>
    public class RTPVP8Header
    {
        // Payload Descriptor Fields.
        public bool ExtendedControlBitsPresent;     // Indicated whether extended control bits are present.
        public bool NonReferenceFrame;              // When set indicates the frame can be discarded wihtout affecting any other frames.
        public bool StartOfVP8Partition;            // Should be set when the first payload octet is the start of a new VP8 partition.
        public byte PartitionIndex;                 // Denotes the VP8 partition index that the first payload octet of the packet belongs to.

        // Payload Header Fields.
        public int FirstPartitionSize;              // The size of the first partition in bytes is calculated from the 19 bits in Size0, SIze1 & Size2 as: size = Size0 + (8 x Size1) + (2048 8 Size2).
        public bool ShowFrame;
        public int VersionNumber;
        public bool IsKeyFrame;

        public RTPVP8Header()
        {

        }

        public byte[] GetBytes()
        {
            if (!StartOfVP8Partition)
            {
                // No partition header on continuation packets.
                return new byte[] { 0x00 };
            }
            else
            {
                byte s2 = (byte)(FirstPartitionSize / 2048);
                byte s1 = (byte)((FirstPartitionSize - (s2 * 2048)) / 8);
                byte s0 = (byte)(((FirstPartitionSize - (s2 * 2048) - (s1 * 8)) << 5) & 0xf0);

                byte firstBytePH = (byte)(s0 + ((IsKeyFrame) ? 0x00 : 0x01));

                return new byte[] { /* Payload Descriptor */ 0x10,
                                /* Payload Header */ firstBytePH, s1, s2 };
            }
        }
    }
}

