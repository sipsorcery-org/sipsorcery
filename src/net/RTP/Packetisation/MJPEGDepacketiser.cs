//-----------------------------------------------------------------------------
// Filename: MJPEGDepacketiser.cs
//
// Description: The MJPEGDepacketiser class is responsible for processing RTP payloads containing MJPEG data.
// It handles the extraction and reassembly of JPEG frames from RTP packets, including the management
// of JPEG headers, quantization tables, and Huffman tables. The class provides methods for reading
// various integer sizes from byte arrays, comparing byte sequences, and generating necessary JPEG
// headers and tables for proper frame reconstruction.
//
// Author(s):
// Morten Palner Drescher (mdr@milestone.dk)
//
// History:
// 23 Jan 2025 Morten Palner Drescher  Created, Copenhagen, Denmark.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------
using System;
using System.IO;

namespace SIPSorcery.net.RTP.Packetisation
{
    /// <summary>
    /// Based on https://github.com/BogdanovKirill/RtspClientSharp/blob/master/RtspClientSharp/MediaParsers/MJPEGVideoPayloadParser.cs 
    /// Distributed under MIT License
    /// 
    /// @author mdr@milestone.dk
    /// </summary>
    public class MJPEGDepacketiser
    {
        #region Payload helper fields
        private const int JpegHeaderSize = 8;
        private const int JpegMaxSize = 16 * 1024 * 1024;

        private static byte[] StartMarkerBytes = { 0xFF, 0xD8 };
        private static byte[] EndMarkerBytes = { 0xFF, 0xD9 };

        private static ArraySegment<byte> JpegEndMarkerByteSegment =
            new ArraySegment<byte>(EndMarkerBytes);

        private static byte[] DefaultQuantizers =
        {
            16, 11, 12, 14, 12, 10, 16, 14,
            13, 14, 18, 17, 16, 19, 24, 40,
            26, 24, 22, 22, 24, 49, 35, 37,
            29, 40, 58, 51, 61, 60, 57, 51,
            56, 55, 64, 72, 92, 78, 64, 68,
            87, 69, 55, 56, 80, 109, 81, 87,
            95, 98, 103, 104, 103, 62, 77, 113,
            121, 112, 100, 120, 92, 101, 103, 99,
            17, 18, 18, 24, 21, 24, 47, 26,
            26, 47, 99, 66, 56, 66, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99
        };

        private static byte[] LumDcCodelens =
        {
            0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0
        };

        private static byte[] LumDcSymbols =
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11
        };

        private static byte[] LumAcCodelens =
        {
            0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7d
        };

        private static byte[] LumAcSymbols =
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

        private static byte[] ChmDcCodelens =
        {
            0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0
        };

        private static byte[] ChmDcSymbols =
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11
        };

        private static byte[] ChmAcCodelens =
        {
            0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77
        };

        private static byte[] ChmAcSymbols =
        {
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

        private MemoryStream _frameStream = new MemoryStream();
        private MemoryStream _returnFrame = new MemoryStream();
        private bool _resetReturnFrame;

        private int _currentDri;
        private int _currentQ;
        private int _currentType;
        private int _currentFrameWidth;
        private int _currentFrameHeight;

        private bool _hasExternalQuantizationTable;

        private byte[] _jpegHeaderBytes = new byte[0];
        private ArraySegment<byte> _jpegHeaderBytesSegment;

        private byte[] _quantizationTables = new byte[0];
        private int _quantizationTablesLength;

        
        #endregion

        public virtual MemoryStream ProcessRTPPayload(byte[] rtpPayload, ushort seqNum, uint timestamp, int markbit, out bool isKeyFrame)
        {
            //MJPEG only contains full frames
            isKeyFrame = true;

            if (_resetReturnFrame)
            {
                _returnFrame = new MemoryStream();
                _resetReturnFrame = false;
            }
            int offset = 1;
            int fragmentOffset = ReadUInt24(rtpPayload, offset);
            offset += 3;

            int type = rtpPayload[offset++];
            int q = rtpPayload[offset++];
            int width = rtpPayload[offset++] * 8;
            int height = rtpPayload[offset++] * 8;
            int dri = 0;

            if(type > 63)
            {
                dri = ReadUInt16(rtpPayload, offset);
                offset += 4;
            }

            if(fragmentOffset == 0)
            {
                if(_frameStream.Position != 0)
                {
                    GenerateFrame();
                }

                bool quantizationTablesChanged = false;

                if (q > 127)
                {
                    int mbz = rtpPayload[offset];

                    if(mbz == 0)
                    {
                        _hasExternalQuantizationTable = true;

                        int quantizationTablesLength = ReadUInt16(rtpPayload, offset + 2);
                        offset += 4;

                        if(!AreBytesEqual(rtpPayload, offset, quantizationTablesLength, _quantizationTables, 0, _quantizationTablesLength))
                        {
                            if(_quantizationTablesLength < quantizationTablesLength)
                            {
                                _quantizationTables = new byte[quantizationTablesLength];
                            }

                            Buffer.BlockCopy(rtpPayload, offset, _quantizationTables, 0, quantizationTablesLength);
                            _quantizationTablesLength = quantizationTablesLength;
                            quantizationTablesChanged = true;
                        }

                        offset += quantizationTablesLength;

                    }
                }

                if(quantizationTablesChanged || _currentType != type || _currentQ != q ||
                    _currentFrameWidth != width || _currentFrameHeight != height || _currentDri != dri)
                {
                    _currentType = type;
                    _currentQ = q;
                    _currentFrameWidth = width;
                    _currentFrameHeight = height;
                    _currentDri = dri;

                    ReInitializeJpegHeader();
                }

                _frameStream.Write(_jpegHeaderBytesSegment.Array, _jpegHeaderBytesSegment.Offset, _jpegHeaderBytesSegment.Count);
            }

            //if(fragmentOffset != 0 && _frameStream.Position == 0)
            //{
            //    return;
            //}

            int dataSize = rtpPayload.Length - offset;

            _frameStream.Write(rtpPayload, offset, dataSize);

            if(_returnFrame.Length > 0)
            {
                _resetReturnFrame = true;
                return _returnFrame;
            }
            return null;
        }

        private uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset] << 24 |
                           buffer[offset + 1] << 16 |
                           buffer[offset + 2] << 8 |
                           buffer[offset + 3]);
        }

        private int ReadUInt24(byte[] buffer, int offset)
        {
            return buffer[offset] << 16 |
                   buffer[offset + 1] << 8 |
                   buffer[offset + 2];
        }

        private int ReadUInt16(byte[] buffer, int offset)
        {
            return (buffer[offset] << 8) | buffer[offset + 1];
        }

        private bool AreBytesEqual(byte[] bytes1, int offset1, int count1, byte[] bytes2, int offset2, int count2)
        {
            if (count1 != count2)
            {
                return false;
            }

            for (int i = 0; i < count1; i++)
            {
                if (bytes1[offset1 + i] != bytes2[offset2 + i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool EndsWith(byte[] array, int offset, int count, byte[] pattern)
        {
            int patternLength = pattern.Length;

            if (count < patternLength)
            {
                return false;
            }

            offset = offset + count - patternLength;

            for (int i = 0; i < patternLength; i++, offset++)
            {
                if (array[offset] != pattern[i])
                {
                    return false;
                }
            }

            return true;
        }

        private void ReInitializeJpegHeader()
        {
            if (!_hasExternalQuantizationTable)
            {
                GenerateQuantizationTables(_currentQ);
            }

            var jpegHeaderSize = GetJpegHeaderSize(_currentDri);

            _jpegHeaderBytes = new byte[jpegHeaderSize];
            _jpegHeaderBytesSegment = new ArraySegment<byte>(_jpegHeaderBytes);

            FillJpegHeader(_jpegHeaderBytes, _currentType, _currentFrameWidth, _currentFrameHeight, _currentDri);
        }

        private void GenerateQuantizationTables(int factor)
        {
            _quantizationTablesLength = 128;

            if (_quantizationTables.Length < _quantizationTablesLength)
            {
                _quantizationTables = new byte[_quantizationTablesLength];
            }

            int q;

            if (factor < 1)
            {
                factor = 1;
            }
            else if (factor > 99)
            {
                factor = 99;
            }

            if (factor < 50)
            {
                q = 5000 / factor;
            }
            else
            {
                q = 200 - factor * 2;
            }

            for (var i = 0; i < 128; ++i)
            {
                int newVal = (DefaultQuantizers[i] * q + 50) / 100;

                if (newVal < 1)
                {
                    newVal = 1;
                }
                else if (newVal > 255)
                {
                    newVal = 255;
                }

                _quantizationTables[i] = (byte)newVal;
            }
        }

        private int GetJpegHeaderSize(int dri)
        {
            int qtlen = _quantizationTablesLength;

            int qtlenHalf = qtlen / 2;
            qtlen = qtlenHalf * 2;

            int qtablesCount = qtlen > 64 ? 2 : 1;
            return 485 + qtablesCount * 5 + qtlen + (dri > 0 ? 6 : 0);
        }

        private void FillJpegHeader(byte[] buffer, int type, int width, int height, int dri)
        {
            int qtablesCount = _quantizationTablesLength > 64 ? 2 : 1;
            int offset = 0;

            buffer[offset++] = 0xFF;
            buffer[offset++] = 0xD8;
            buffer[offset++] = 0xFF;
            buffer[offset++] = 0xe0;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x10;
            buffer[offset++] = (byte)'J';
            buffer[offset++] = (byte)'F';
            buffer[offset++] = (byte)'I';
            buffer[offset++] = (byte)'F';
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x01;
            buffer[offset++] = 0x01;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x01;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x01;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x00;

            if (dri > 0)
            {
                buffer[offset++] = 0xFF;
                buffer[offset++] = 0xdd;
                buffer[offset++] = 0x00;
                buffer[offset++] = 0x04;
                buffer[offset++] = (byte)(dri >> 8);
                buffer[offset++] = (byte)dri;
            }

            int tableSize = qtablesCount == 1 ? _quantizationTablesLength : _quantizationTablesLength / 2;
            buffer[offset++] = 0xFF;
            buffer[offset++] = 0xdb;
            buffer[offset++] = 0x00;
            buffer[offset++] = (byte)(tableSize + 3);
            buffer[offset++] = 0x00;

            int qtablesOffset = 0;
            Buffer.BlockCopy(_quantizationTables, qtablesOffset, buffer, offset, tableSize);
            qtablesOffset += tableSize;
            offset += tableSize;

            if (qtablesCount > 1)
            {
                tableSize = _quantizationTablesLength - _quantizationTablesLength / 2;

                buffer[offset++] = 0xFF;
                buffer[offset++] = 0xdb;
                buffer[offset++] = 0x00;
                buffer[offset++] = (byte)(tableSize + 3);
                buffer[offset++] = 0x01;
                Buffer.BlockCopy(_quantizationTables, qtablesOffset, buffer, offset, tableSize);
                offset += tableSize;
            }

            buffer[offset++] = 0xFF;
            buffer[offset++] = 0xc0;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x11;
            buffer[offset++] = 0x08;
            buffer[offset++] = (byte)(height >> 8);
            buffer[offset++] = (byte)height;
            buffer[offset++] = (byte)(width >> 8);
            buffer[offset++] = (byte)width;
            buffer[offset++] = 0x03;
            buffer[offset++] = 0x01;
            buffer[offset++] = (type & 1) != 0 ? (byte)0x22 : (byte)0x21;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x02;
            buffer[offset++] = 0x11;
            buffer[offset++] = qtablesCount == 1 ? (byte)0x00 : (byte)0x01;
            buffer[offset++] = 0x03;
            buffer[offset++] = 0x11;
            buffer[offset++] = qtablesCount == 1 ? (byte)0x00 : (byte)0x01;

            CreateHuffmanHeader(buffer, offset, LumDcCodelens, LumDcCodelens.Length, LumDcSymbols, LumDcSymbols.Length,
                0, 0);
            offset += 5 + LumDcCodelens.Length + LumDcSymbols.Length;

            CreateHuffmanHeader(buffer, offset, LumAcCodelens, LumAcCodelens.Length, LumAcSymbols, LumAcSymbols.Length,
                0, 1);
            offset += 5 + LumAcCodelens.Length + LumAcSymbols.Length;

            CreateHuffmanHeader(buffer, offset, ChmDcCodelens, ChmDcCodelens.Length, ChmDcSymbols, ChmDcSymbols.Length,
                1, 0);
            offset += 5 + ChmDcCodelens.Length + ChmDcSymbols.Length;

            CreateHuffmanHeader(buffer, offset, ChmAcCodelens, ChmAcCodelens.Length, ChmAcSymbols, ChmAcSymbols.Length,
                1, 1);
            offset += 5 + ChmAcCodelens.Length + ChmAcSymbols.Length;

            buffer[offset++] = 0xFF;
            buffer[offset++] = 0xda;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x0C;
            buffer[offset++] = 0x03;
            buffer[offset++] = 0x01;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x02;
            buffer[offset++] = 0x11;
            buffer[offset++] = 0x03;
            buffer[offset++] = 0x11;
            buffer[offset++] = 0x00;
            buffer[offset++] = 0x3F;
            buffer[offset] = 0x00;
        }

        private static void CreateHuffmanHeader(byte[] buffer, int offset, byte[] codelens, int ncodes, byte[] symbols,
            int nsymbols, int tableNo, int tableClass)
        {
            buffer[offset++] = 0xff;
            buffer[offset++] = 0xc4;
            buffer[offset++] = 0;
            buffer[offset++] = (byte)(3 + ncodes + nsymbols);
            buffer[offset++] = (byte)((tableClass << 4) | tableNo);
            Buffer.BlockCopy(codelens, 0, buffer, offset, ncodes);
            offset += ncodes;
            Buffer.BlockCopy(symbols, 0, buffer, offset, nsymbols);
        }

        private void GenerateFrame()
        {
            if (!EndsWith(_frameStream.GetBuffer(), 0,
                (int)_frameStream.Position, EndMarkerBytes))
            {
                _frameStream.Write(JpegEndMarkerByteSegment.Array, JpegEndMarkerByteSegment.Offset, JpegEndMarkerByteSegment.Count);
            }

            _returnFrame.Write(_frameStream.ToArray(), 0, (int)_frameStream.Length);
            _frameStream = new MemoryStream();
        }
    }
}
