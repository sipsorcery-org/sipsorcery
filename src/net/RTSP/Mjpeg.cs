//-----------------------------------------------------------------------------
// Filename: Mjpeg.cs
//
// Description: A rudimentary decoder for an mjpeg RTP stream.
// 
// History:
// 19 Jun 2015	Aaron Clauson	Derived almost completely from https://net7mma.codeplex.com (license @ https://net7mma.codeplex.com/license).
//
// License: 
/*
This file came from Managed Media Aggregation, You can always find the latest version @ https://net7mma.codeplex.com/
  
 Julius.Friedman@gmail.com / (SR. Software Engineer ASTI Transportation Inc. http://www.asti-trans.com)

Permission is hereby granted, free of charge, 
 * to any person obtaining a copy of this software and associated documentation files (the "Software"), 
 * to deal in the Software without restriction, 
 * including without limitation the rights to :
 * use, 
 * copy, 
 * modify, 
 * merge, 
 * publish, 
 * distribute, 
 * sublicense, 
 * and/or sell copies of the Software, 
 * and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
 * 
 * 
 * JuliusFriedman@gmail.com should be contacted for further details.

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
 * 
 * IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, 
 * DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, 
 * TORT OR OTHERWISE, 
 * ARISING FROM, 
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 * 
 * v//
 */
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class Mjpeg
    {
        private static ILogger logger = Log.Logger;

        public sealed class Tags
        {
            static Tags() { }

            public const byte Prefix = 0xff;

            public const byte TextComment = 0xfe;

            public const byte StartOfFrame = 0xc0;

            public const byte HuffmanTable = 0xc4;

            public const byte StartOfInformation = 0xd8;

            public const byte AppFirst = 0xe0;

            public const byte AppLast = 0xee;

            public const byte EndOfInformation = 0xd9;

            public const byte QuantizationTable = 0xdb;

            public const byte DataRestartInterval = 0xdd;

            public const byte StartOfScan = 0xda;
        }

        //static byte[] dc_luminance = { 0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0 };
        static byte[] bits_dc_luminance = { 0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0 };

        static byte[] val_dc = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

        //static byte[] lum_ac_codelens = { 0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7d };
        static byte[] bits_ac_luminance = { 0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7d };

        static byte[] val_ac_luminance =
        {
            0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12,
            0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
            0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xa1, 0x08,
            0x23, 0x42, 0xb1, 0xc1, 0x15, 0x52, 0xd1, 0xf0,
            0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0a, 0x16,
            0x17, 0x18, 0x19, 0x1a, 0x25, 0x26, 0x27, 0x28,
            0x29, 0x2a, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
            0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
            0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
            0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
            0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79,
            0x7a, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
            0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98,
            0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7,
            0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6,
            0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5,
            0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4,
            0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda, 0xe1, 0xe2,
            0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea,
            0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
            0xf9, 0xfa
        };

        //static byte[] dc_chrominance = { 0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0 };
        static byte[] bits_dc_chrominance = { 0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0 };

        static byte[] chm_dc_symbols = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

        //static byte[] bits_ac_chrominance = { 0, 0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77 };
        static byte[] bits_ac_chrominance = { 0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77 };

        static byte[] val_ac_chrominance = {
            0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21,
            0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
            0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91,
            0xa1, 0xb1, 0xc1, 0x09, 0x23, 0x33, 0x52, 0xf0,
            0x15, 0x62, 0x72, 0xd1, 0x0a, 0x16, 0x24, 0x34,
            0xe1, 0x25, 0xf1, 0x17, 0x18, 0x19, 0x1a, 0x26,
            0x27, 0x28, 0x29, 0x2a, 0x35, 0x36, 0x37, 0x38,
            0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
            0x49, 0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
            0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
            0x69, 0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78,
            0x79, 0x7a, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
            0x88, 0x89, 0x8a, 0x92, 0x93, 0x94, 0x95, 0x96,
            0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5,
            0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4,
            0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3,
            0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2,
            0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda,
            0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9,
            0xea, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
            0xf9, 0xfa
        };

        // The default 'luma' and 'chroma' quantizer tables, in zigzag order:
        static byte[] defaultQuantizers = new byte[]
        {
           // luma table:
           16, 11, 12, 14, 12, 10, 16, 14,
           13, 14, 18, 17, 16, 19, 24, 40,
           26, 24, 22, 22, 24, 49, 35, 37,
           29, 40, 58, 51, 61, 60, 57, 51,
           56, 55, 64, 72, 92, 78, 64, 68,
           87, 69, 55, 56, 80, 109, 81, 87,
           95, 98, 103, 104, 103, 62, 77, 113,
           121, 112, 100, 120, 92, 101, 103, 99,
           // chroma table:
           17, 18, 18, 24, 21, 24, 47, 26,
           26, 47, 99, 66, 56, 66, 99, 99,
           99, 99, 99, 99, 99, 99, 99, 99,
           99, 99, 99, 99, 99, 99, 99, 99,
           99, 99, 99, 99, 99, 99, 99, 99,
           99, 99, 99, 99, 99, 99, 99, 99,
           99, 99, 99, 99, 99, 99, 99, 99,
           99, 99, 99, 99, 99, 99, 99, 99
        };

        static byte[] CreateJFIFHeader(uint type, uint width, uint height, ArraySegment<byte> tables, byte precision, ushort dri)
        {
            List<byte> result = new List<byte>();
            result.Add(Tags.Prefix);
            result.Add(Tags.StartOfInformation);

            result.Add(Tags.Prefix);
            result.Add(Tags.AppFirst);//AppFirst
            result.Add(0x00);
            result.Add(0x10);//length
            result.Add((byte)'J'); //Always equals "JFXX" (with zero following) (0x4A46585800)
            result.Add((byte)'F');
            result.Add((byte)'I');
            result.Add((byte)'F');
            result.Add(0x00);

            result.Add(0x01);//Version Major
            result.Add(0x01);//Version Minor

            result.Add(0x00);//Units

            result.Add(0x00);//Horizontal
            result.Add(0x01);

            result.Add(0x00);//Vertical
            result.Add(0x01);

            result.Add(0x00);//No thumb
            result.Add(0x00);//Thumb Data

            //Data Restart Invert val
            if (dri > 0)
            {
                result.AddRange(CreateDataRestartIntervalMarker(dri));
            }

            //Quantization Tables
            result.AddRange(CreateQuantizationTablesMarker(tables, precision));

            //Huffman Tables
            ushort huffmanLength = (ushort)(6 +
                bits_dc_luminance.Length + val_dc.Length +
                bits_dc_chrominance.Length + val_dc.Length +
                bits_ac_luminance.Length + val_ac_luminance.Length +
                bits_ac_chrominance.Length + val_ac_chrominance.Length);

            result.Add(Tags.Prefix);
            result.Add(Tags.HuffmanTable);
            result.Add((byte)(huffmanLength >> 8));
            result.Add((byte)huffmanLength);
            result.AddRange(CreateHuffmanTableMarker(bits_dc_luminance, val_dc, 0, 0));
            result.AddRange(CreateHuffmanTableMarker(bits_dc_chrominance, val_dc, 0, 1));
            result.AddRange(CreateHuffmanTableMarker(bits_ac_luminance, val_ac_luminance, 1, 0));
            result.AddRange(CreateHuffmanTableMarker(bits_ac_chrominance, val_ac_chrominance, 1, 1));

            //Start Of Frame
            result.Add(Tags.Prefix);
            result.Add(Tags.StartOfFrame);//SOF
            result.Add(0x00); //Length
            result.Add(0x11); // 17
            result.Add(0x08); //Bits Per Component
            result.Add((byte)(height >> 8)); //Height
            result.Add((byte)height);
            result.Add((byte)(width >> 8)); //Width
            result.Add((byte)width);

            result.Add(0x03);//Number of components
            result.Add(0x01);//Component Number
            result.Add((byte)(type > 0 ? 0x22 : 0x21)); //Horizontal or Vertical Sample  

            result.Add(0x00);//Matrix Number (Quant Table Id)?
            result.Add(0x02);//Component Number
            result.Add(0x11);//Horizontal or Vertical Sample

            //ToDo - Handle 16 Bit Precision
            result.Add(0);//Matrix Number

            result.Add(0x03);//Component Number
            result.Add(0x11);//Horizontal or Vertical Sample

            //ToDo - Handle 16 Bit Precision
            //result.Add(1);//Matrix Number      
            result.Add(0);//Matrix Number      

            //Start Of Scan
            result.Add(Tags.Prefix);
            result.Add(Tags.StartOfScan);//Marker SOS
            result.Add(0x00); //Length
            result.Add(0x0c); //Length - 12
            result.Add(0x03); //Number of components
            result.Add(0x01); //Component Number
            result.Add(0x00); //Matrix Number
            result.Add(0x02); //Component Number
            result.Add(0x11); //Horizontal or Vertical Sample
            result.Add(0x03); //Component Number
            result.Add(0x11); //Horizontal or Vertical Sample
            result.Add(0x00); //Start of spectral
            result.Add(0x3f); //End of spectral (63)
            result.Add(0x00); //Successive approximation bit position (high, low)

            return result.ToArray();
        }

        /// <summary>
        /// Creates a Luma and Chroma Table in ZigZag order using the default quantizer
        /// </summary>
        /// <param name="Q">The quality factor</param>
        /// <returns>64 luma bytes and 64 chroma</returns>
        static byte[] CreateQuantizationTables(uint type, uint Q, byte precision)
        {
            Q &= 128;

            //Factor restricted to range of 1 and 99
            int factor = (int)Math.Max(Math.Min(1, Q), 99);

            //Seed quantization value
            int q = (Q < 50 ? q = 5000 / factor : 200 - factor * 2);

            //Create 2 quantization tables from Seed quality value using the RFC quantizers
            int tableSize = defaultQuantizers.Length / 2;
            byte[] resultTables = new byte[tableSize * 2];
            for (int i = 0, j = tableSize; i < tableSize; ++i, ++j)
            {
                if (precision == 0)
                {
                    //Clamp with Min, Max
                    //Luma
                    resultTables[i] = (byte)Math.Min(Math.Max((defaultQuantizers[i] * q + 50) / 100, 1), byte.MaxValue);
                    //Chroma
                    resultTables[j] = (byte)Math.Min(Math.Max((defaultQuantizers[j] * q + 50) / 100, 1), byte.MaxValue);
                }
                else
                {
                    //Luma
                    BitConverter.GetBytes(NetConvert.DoReverseEndian((ushort)Math.Min(Math.Max((defaultQuantizers[i] * q + 50) / 100, 1), byte.MaxValue))).CopyTo(resultTables, i);
                    i++;

                    //Chroma
                    BitConverter.GetBytes(NetConvert.DoReverseEndian((ushort)Math.Min(Math.Max((defaultQuantizers[j] * q + 50) / 100, 1), byte.MaxValue))).CopyTo(resultTables, j);
                    j++;
                }
            }

            return resultTables;
        }

        /// <summary>
        /// Creates a Jpeg QuantizationTableMarker for each table given in the tables
        /// </summary>
        /// <param name="tables">The tables verbatim, either 1 or 2 (Luminance and Chrominance)</param>
        /// <returns>The table with marker and prefix/returns>
        static byte[] CreateQuantizationTablesMarker(ArraySegment<byte> tables, byte precision)
        {
            //List<byte> result = new List<byte>();

            int tableCount = tables.Count / (precision > 0 ? 128 : 64);

            //??Some might have more then 2?
            if (tableCount > 2)
            {
                throw new ArgumentOutOfRangeException("tableCount");
            }

            int tableSize = tables.Count / tableCount;

            //Each tag is 4 bytes (prefix and tag) + 2 for len = 4 + 1 for Precision and TableId 
            byte[] result = new byte[(5 * tableCount) + (tableSize * tableCount)];

            result[0] = Tags.Prefix;
            result[1] = Tags.QuantizationTable;
            result[2] = 0;//Len
            result[3] = (byte)(tableSize + 3);
            result[4] = (byte)(precision << 4 | 0); // Precision and TableId

            //First table. Type - Luminance usually when two
            System.Array.Copy(tables.Array, tables.Offset, result, 5, tableSize);

            if (tableCount > 1)
            {
                result[tableSize + 5] = Tags.Prefix;
                result[tableSize + 6] = Tags.QuantizationTable;
                result[tableSize + 7] = 0;//Len
                result[tableSize + 8] = (byte)(tableSize + 3);
                result[tableSize + 9] = (byte)(precision << 4 | 1);//Precision 0, and table Id

                //Second Table. Type - Chrominance usually when two
                System.Array.Copy(tables.Array, tables.Offset + tableSize, result, 10 + tableSize, tableSize);
            }

            return result;
        }

        static byte[] CreateHuffmanTableMarker(byte[] codeLens, byte[] symbols, int tableClass, int tableID)
        {
            List<byte> result = new List<byte>();
            //result.Add(Tags.Prefix);
            //result.Add(Tags.HuffmanTable);
            //result.Add(0x00); //Length
            //result.Add((byte)(3 + codeLens.Length + symbols.Length)); //Length
            result.Add((byte)((tableClass << 4) | tableID)); //Id
            result.AddRange(codeLens);//Data
            result.AddRange(symbols);
            return result.ToArray();
        }

        static byte[] CreateDataRestartIntervalMarker(ushort dri)
        {
            return new byte[] { Tags.Prefix, Tags.DataRestartInterval, 0x00, 0x04, (byte)(dri >> 8), (byte)(dri) };
        }

        /// <summary>
        /// Writes the packets to a memory stream and creates the default header and quantization tables if necessary.
        /// Assigns Image from the result
        /// </summary>
        public static byte[] ProcessMjpegFrame(List<RTPPacket> framePackets)
        {
            uint TypeSpecific, FragmentOffset, Type, type, Quality, Width, Height;
            ushort RestartInterval = 0, RestartCount = 0;
            //A byte which is bit mapped
            byte PrecisionTable = 0;
            ArraySegment<byte> tables = default;

            //Using a new MemoryStream for a Buffer
            using (System.IO.MemoryStream Buffer = new System.IO.MemoryStream())
            {
                //Loop each packet
                foreach (RTPPacket packet in framePackets.OrderBy(x => x.Header.SequenceNumber))
                {
                    //Payload starts at offset 0
                    int offset = 0;

                    //Handle Extension Headers
                    //if (packet.Extensions)
                    //{
                    //    This could be OnVif extension etc
                    //    http://www.onvif.org/specs/stream/ONVIF-Streaming-Spec-v220.pdf
                    //    Decode
                    //    packet.ExtensionBytes;
                    //    In a Derived Implementation
                    //}

                    //Decode RtpJpeg Header

                    TypeSpecific = (uint)(packet.Payload[offset++]);
                    FragmentOffset = (uint)(packet.Payload[offset++] << 16 | packet.Payload[offset++] << 8 | packet.Payload[offset++]);

                    #region RFC2435 -  The Type Field

                    /*
                     4.1.  The Type Field

          The Type field defines the abbreviated table-specification and
          additional JFIF-style parameters not defined by JPEG, since they are
          not present in the body of the transmitted JPEG data.

          Three ranges of the type field are currently defined. Types 0-63 are
          reserved as fixed, well-known mappings to be defined by this document
          and future revisions of this document. Types 64-127 are the same as
          types 0-63, except that restart markers are present in the JPEG data
          and a Restart Marker header appears immediately following the main
          JPEG header. Types 128-255 are free to be dynamically defined by a
          session setup protocol (which is beyond the scope of this document).

          Of the first group of fixed mappings, types 0 and 1 are currently
          defined, along with the corresponding types 64 and 65 that indicate
          the presence of restart markers.  They correspond to an abbreviated
          table-specification indicating the "Baseline DCT sequential" mode,
          8-bit samples, square pixels, three components in the YUV color
          space, standard Huffman tables as defined in [1, Annex K.3], and a
          single interleaved scan with a scan component selector indicating
          components 1, 2, and 3 in that order.  The Y, U, and V color planes
          correspond to component numbers 1, 2, and 3, respectively.  Component
          1 (i.e., the luminance plane) uses Huffman table number 0 and
          quantization table number 0 (defined below) and components 2 and 3
          (i.e., the chrominance planes) use Huffman table number 1 and
          quantization table number 1 (defined below).

          Type numbers 2-5 are reserved and SHOULD NOT be used.  Applications
          based on previous versions of this document (RFC 2035) should be
          updated to indicate the presence of restart markers with type 64 or
          65 and the Restart Marker header.

          The two RTP/JPEG types currently defined are described below:

                            horizontal   vertical   Quantization
           types  component samp. fact. samp. fact. table number
          +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
          |       |  1 (Y)  |     2     |     1     |     0     |
          | 0, 64 |  2 (U)  |     1     |     1     |     1     |
          |       |  3 (V)  |     1     |     1     |     1     |
          +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
          |       |  1 (Y)  |     2     |     2     |     0     |
          | 1, 65 |  2 (U)  |     1     |     1     |     1     |
          |       |  3 (V)  |     1     |     1     |     1     |
          +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

          These sampling factors indicate that the chrominance components of
          type 0 video is downsampled horizontally by 2 (often called 4:2:2)
          while the chrominance components of type 1 video are downsampled both
          horizontally and vertically by 2 (often called 4:2:0).

          Types 0 and 1 can be used to carry both progressively scanned and
          interlaced image data.  This is encoded using the Type-specific field
          in the main JPEG header.  The following values are defined:

          0 : Image is progressively scanned.  On a computer monitor, it can
          be displayed as-is at the specified width and height.

          1 : Image is an odd field of an interlaced video signal.  The
          height specified in the main JPEG header is half of the height
          of the entire displayed image.  This field should be de-
          interlaced with the even field following it such that lines
          from each of the images alternate.  Corresponding lines from
          the even field should appear just above those same lines from
          the odd field.

          2 : Image is an even field of an interlaced video signal.

          3 : Image is a single field from an interlaced video signal, but
          it should be displayed full frame as if it were received as
          both the odd & even fields of the frame.  On a computer
          monitor, each line in the image should be displayed twice,
          doubling the height of the image.
                     */

                    #endregion

                    Type = (uint)(packet.Payload[offset++]);
                    type = Type & 1;
                    if (type > 3 || type > 6)
                    {
                        throw new ArgumentException("Type numbers 2-5 are reserved and SHOULD NOT be used.  Applications on RFC 2035 should be updated to indicate the presence of restart markers with type 64 or 65 and the Restart Marker header.");
                    }

                    Quality = (uint)packet.Payload[offset++];
                    Width = (uint)(packet.Payload[offset++] * 8); // This should have been 128 or > and the standard would have worked for all resolutions
                    Height = (uint)(packet.Payload[offset++] * 8);// Now in certain highres profiles you will need an OnVif extension before the RtpJpeg Header
                                                                  //It is worth noting Rtp does not care what you send and more tags such as comments and or higher resolution pictures may be sent and these values will simply be ignored.

                    if (Width == 0 || Height == 0)
                    {
                        logger.LogWarning("ProcessMjpegFrame could not determine either the width or height of the jpeg frame (width={0}, height={1}).", Width, Height);
                    }

                    //Restart Interval 64 - 127
                    if (Type > 63 && Type < 128)
                    {
                        /*
                           This header MUST be present immediately after the main JPEG header
                           when using types 64-127.  It provides the additional information
                           required to properly decode a data stream containing restart markers.

                            0                   1                   2                   3
                            0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
                           +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                           |       Restart Interval        |F|L|       Restart Count       |
                           +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                         */
                        RestartInterval = (ushort)(packet.Payload[offset++] << 8 | packet.Payload[offset++]);
                        RestartCount = (ushort)((packet.Payload[offset++] << 8 | packet.Payload[offset++]) & 0x3fff);
                    }

                    //QTables Only occur in the first packet
                    if (FragmentOffset == 0)
                    {
                        //If the quality > 127 there are usually Quantization Tables
                        if (Quality > 127)
                        {
                            if ((packet.Payload[offset++]) != 0)
                            {
                                //Must Be Zero is Not Zero
                                if (System.Diagnostics.Debugger.IsAttached)
                                {
                                    System.Diagnostics.Debugger.Break();
                                }
                            }

                            //Precision
                            PrecisionTable = (packet.Payload[offset++]);

                            #region RFC2435 Length Field

                            /*

              The Length field is set to the length in bytes of the quantization
              table data to follow.  The Length field MAY be set to zero to
              indicate that no quantization table data is included in this frame.
              See section 4.2 for more information.  If the Length field in a
              received packet is larger than the remaining number of bytes, the
              packet MUST be discarded.

              When table data is included, the number of tables present depends on
              the JPEG type field.  For example, type 0 uses two tables (one for
              the luminance component and one shared by the chrominance
              components).  Each table is an array of 64 values given in zig-zag
              order, identical to the format used in a JFIF DQT marker segment.

              For each quantization table present, a bit in the Precision field
              specifies the size of the coefficients in that table.  If the bit is
              zero, the coefficients are 8 bits yielding a table length of 64
              bytes.  If the bit is one, the coefficients are 16 bits for a table
              length of 128 bytes.  For 16 bit tables, the coefficients are
              presented in network byte order.  The rightmost bit in the Precision
              field (bit 15 in the diagram above) corresponds to the first table
              and each additional table uses the next bit to the left.  Bits beyond
              those corresponding to the tables needed by the type in use MUST be
              ignored.

                                 */

                            #endregion

                            //Length of all tables
                            ushort Length = (ushort)(packet.Payload[offset++] << 8 | packet.Payload[offset++]);

                            //If there is Table Data Read it
                            if (Length > 0)
                            {
                                tables = new ArraySegment<byte>(packet.Payload, offset, (int)Length);
                                offset += (int)Length;
                            }
                            else if (Length > packet.Payload.Length - offset)
                            {
                                continue; // The packet must be discarded
                            }
                            else // Create it from the Quality
                            {
                                tables = new ArraySegment<byte>(CreateQuantizationTables(Quality, type, PrecisionTable));
                            }
                        }
                        else // Create from the Quality
                        {
                            tables = new ArraySegment<byte>(CreateQuantizationTables(type, Quality, PrecisionTable));
                        }

                        byte[] header = CreateJFIFHeader(type, Width, Height, tables, PrecisionTable, RestartInterval);
                        Buffer.Write(header, 0, header.Length);
                    }

                    //Write the Payload data from the offset
                    Buffer.Write(packet.Payload, offset, packet.Payload.Length - offset);
                }

                //Check for EOI Marker
                Buffer.Seek(-1, System.IO.SeekOrigin.Current);

                if (Buffer.ReadByte() != Tags.EndOfInformation)
                {
                    Buffer.WriteByte(Tags.Prefix);
                    Buffer.WriteByte(Tags.EndOfInformation);
                }

                //Go back to the beginning
                Buffer.Position = 0;

                //This article explains in detail what exactly happens: http://support.microsoft.com/kb/814675
                //In short, for a lifetime of an Image constructed from a stream, the stream must not be destroyed.
                //Image = new System.Drawing.Bitmap(System.Drawing.Image.FromStream(Buffer, true, true));
                //DO NOT USE THE EMBEDDED COLOR MANGEMENT
                //Image = System.Drawing.Image.FromStream(Buffer, false, true);

                return Buffer.ToArray();
            }
        }
    }
}
