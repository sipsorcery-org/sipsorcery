//-----------------------------------------------------------------------------
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
using System.Text;

namespace SIPSorcery.Media
{
  /// <summary>
  /// Representation of the VP8 RTP header as specified in RFC7741
  /// https://tools.ietf.org/html/rfc7741.
  /// </summary>
  public class RtpVP8Header
  {

    private bool IsMBitSet;

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
    public byte TemporalLayerIndex;
    public bool LayerSync;
    public byte TL0PICIDX;
    public byte KEYIDX;

    public int PayloadDescriptorLength;

    // Payload Header Fields.
    public int FirstPartitionSize;              // The size of the first partition in bytes is calculated from the 19 bits in Size0, SIze1 & Size2 as: size = Size0 + (8 x Size1) + (2048 8 Size2).
    public bool ShowFrame;
    public int VersionNumber;
    public bool IsKeyFrame;

    // Complete length of header
    public int Length;

    byte[] b = null;
    public RtpVP8Header(byte[] bytes)
    {

      b = bytes;
      // First byte of payload descriptor.
      ExtendedControlBitsPresent = (bytes[0] & 0x80) != 0;
      NonReferenceFrame = (bytes[0] & 0x20) != 0;
      StartOfVP8Partition = (bytes[0] & 0x10) != 0;

      PartitionIndex = (byte)(bytes[0] & 0x0f);

      // Is second byte being used.
      if (ExtendedControlBitsPresent)
      {
        IsPictureIDPresent = (bytes[0] & 0x80) != 0;
        IsTL0PICIDXPresent = (bytes[0] & 0x40) != 0;
        IsTIDPresent = (bytes[0] & 0x20) != 0;
        IsKEYIDXPresent = (bytes[0] & 0x410) != 0;
        PayloadDescriptorLength = 2;
        // Is the picture ID being used.
        if (IsPictureIDPresent)
        {
          IsMBitSet = (bytes[PayloadDescriptorLength] & 0x80) != 0;
          if (IsMBitSet)
          {

            // The Picure ID is using two bytes.
            PictureID = (ushort)(((byte)(bytes[PayloadDescriptorLength] & 0x7f) << 8) + bytes[PayloadDescriptorLength + 1]);
            PayloadDescriptorLength = 4;
          }
          else
          {

            // The picture ID is using one byte.
            PictureID = bytes[PayloadDescriptorLength];
            PayloadDescriptorLength = 3;
          }
        }

        if (IsTL0PICIDXPresent)
        {
          TL0PICIDX = bytes[PayloadDescriptorLength - 1];
          PayloadDescriptorLength += 1;
        }

        if (IsTIDPresent || IsKEYIDXPresent)
        {
          TemporalLayerIndex = (byte)((bytes[PayloadDescriptorLength] & 0xC0) >> 6);
          LayerSync = ((byte)bytes[PayloadDescriptorLength] & 0x20) != 0;
          KEYIDX = (byte)(bytes[PayloadDescriptorLength] & 0x1f);
        }
      }
      else
      {
        PayloadDescriptorLength = 1;
      }
      Length = PayloadDescriptorLength;

      if (StartOfVP8Partition && PartitionIndex == 0)
      {
        byte payloadHeaderByte = bytes[PayloadDescriptorLength];
        ShowFrame = (payloadHeaderByte & 0x10) != 0;

        VersionNumber = (payloadHeaderByte & 0x0e) >> 1;
        IsKeyFrame = (payloadHeaderByte & 0x01) == 0; // inverse Key Frame
        FirstPartitionSize = (payloadHeaderByte & 0xe0) >> 5 + 8 * bytes[PayloadDescriptorLength + 1] + 2048 * bytes[PayloadDescriptorLength + 2];
      }
    }


    public override string ToString()
    {
      if (b != null)
      {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(Convert.ToString(b[0], 2).PadLeft(8, '0'));
        sb.AppendLine(Convert.ToString(b[1], 2).PadLeft(8, '0'));
        sb.AppendLine(Convert.ToString(b[2], 2).PadLeft(8, '0'));
        sb.AppendLine(Convert.ToString(b[3], 2).PadLeft(8, '0'));
        return sb.ToString();
      }
      return base.ToString();
    }
  }
}
