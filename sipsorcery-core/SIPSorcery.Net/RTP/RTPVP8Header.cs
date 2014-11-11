//-----------------------------------------------------------------------------
// Filename: RTPVP8Header.cs
//
// Description: Represents the RTP header to use for a VP8 encoded payload as per
// http://tools.ietf.org/html/draft-ietf-payload-vp8-12.
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
    public class RTPVP8Header
    {
        // Payload Descriptor Fields.
        public bool ExtendedControlBitsPresent;     // Indicated whether extended control bits are present.
        public bool NonReferenceFrame;              // When set indicates the frame can be discarded wihtout affecting any other frames.
        public bool StartOfVP8Partition;            // Should be set when the first payload octet is the start of a new VP8 partition.
        public byte PartitionIndex;                 // Denotes the VP8 partition index that the first payload octet of the packet belongs to.

        // Payload Header Fields.
        public int Size0;                           // The size of the first partition in bytes is calculated from the 19 bits in Size0, SIze1 & Size2 as: size = Size0 + 8 x Size1 + 2048 8 Size2.
        public int Size1;
        public int Size2;
        public bool ShowFrame;
        public int VersionNumber;
        public bool IsKeyFrame;

        public RTPVP8Header()
        {

        }
    }
}

