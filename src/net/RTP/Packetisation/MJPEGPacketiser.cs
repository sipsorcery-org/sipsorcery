﻿//-----------------------------------------------------------------------------
// Filename: MJPEGPacketiser.cs
//
// Description: The MJPEGPacketiser class provides functionality to packetize MJPEG (Motion JPEG) data for transmission over RTP (Real-time Transport Protocol).
// It includes methods to create RTP headers for MJPEG frames, process JPEG markers, and extract frame data.
// The class supports handling of quantization tables and restart markers in MJPEG streams.
// This class offers only support for YUV color space.
//
// Author(s):
// Morten Palner Drescher (mdr@milestone.dk)
//
// History:
// 21 Jan 2025    Morten Palner Drescher	Created, Copenhagen, Denmark.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;

namespace SIPSorcery.net.RTP.Packetisation
{
    /// <summary>
    /// This class offers packetisation of MJPEG to be sent over RTP.
    /// @author mdr@milestone.dk
    /// </summary>
    public class MJPEGPacketiser
    {
        private const int QTableHeaderLength = 2;
        private const int QTableLength8Bit = 64;
        private const int QTableParamsLength = 1;
        private const int QTableBlockLength8Bit = QTableLength8Bit + QTableParamsLength;
        private const int JpegNumberOfComponents = 3; //Only support for YUV

        public class Marker
        {
            public byte[] MarkerBytes = Array.Empty<byte>();
            public byte Type;
            public int StartPosition = -1;
        }
        public struct MJPEG
        {
            public MJPEG()
            {
                MjpegHeader = new MJPEGHeader();
                MjpegHeaderQTable = new MJPEGHeaderQTable();
                MjpegHeaderRestartMarker = new MJPEGHeaderRestartMarker();
                QTables = new List<byte[]>();
                HasRestartMarker = false;
            }
            public MJPEGHeader MjpegHeader;
            public MJPEGHeaderQTable MjpegHeaderQTable;
            public MJPEGHeaderRestartMarker MjpegHeaderRestartMarker;
            public bool HasRestartMarker;
            public List<byte[]> QTables;
        }

        public class MJPEGData
        {
            public required byte[] Data;
        }

        public class MJPEGHeader
        {
            public byte tspec = (int)JpegHeaderTypesSpec.jpegHeaderTypeSpec_Progressive;
            public byte offsetHigh;
            public byte offsetMid;
            public byte offsetLow;
            public byte type = (int)JpegHeaderTypes.jpegHeaderType_422;
            public byte q;
            public byte width;
            public byte height;

            public void SetOffset(int offset)
            {
                offsetHigh = (byte)((offset >> 16) & 0xFF);
                offsetMid = (byte)((offset >> 8) & 0xFF);
                offsetLow = (byte)((offset >> 0) & 0xFF);
            }

            public void SetWidth(int width)
            {
                this.width = (byte)(width >> 3);
            }
            public void SetHeight(int height)
            {
                this.height = (byte)(height >> 3);
            }
        }

        public class MJPEGHeaderQTable
        {
            public byte mbz;
            public byte precision;
            public byte lengthHigh;
            public byte lengthLow;

            public static int GetSize() { return 4; }

            public void SetLength(int length)
            {
                lengthHigh = (byte)((length >> 8) & 0xff);
                lengthLow = (byte)((length >> 0) & 0xff);
            }

            public int GetLength()
            {
                return (lengthHigh << 8) | lengthLow;
            }
        }

        public class MJPEGHeaderRestartMarker
        {
            public ushort RestartInterval;
            public byte IsFirst = 1;
            public byte IsLast = 1;
            public int RestartCount = 0x3FFF;
        }

        public enum JpegComponents
        {
            jpegComponent_Y = 0,
            jpegComponent_U,
            jpegComponent_V
        }

        public enum JpegHeaderTypes
        {
            jpegHeaderType_422 = 0,
            jpegHeaderType_420,
            jpegHeaderType_422wRestartMarkers = 64,
            jpegHeaderType_420wRestartMarkers = 65
        }

        public enum JpegHeaderTypesSpec
        {
            jpegHeaderTypeSpec_Progressive = 0,
            jpegHeaderTypeSpec_OddFiled,
            jpegHeaderTypeSpec_EvenFiled,
            jpegHeaderTypeSpec_OddAndEvenFileds
        }

        public enum JpegMarkerTypes
        {
            jmt_BeginMarker = 0xFF,
            jmt_NotAmarker = 0x00,

            jmt_SOI = 0xD8,
            jmt_EOI = 0xD9,

            jmt_SOF0 = 0xC0,
            jmt_SOF1 = 0xC1,

            jmt_DQT = 0xDB,

            jmt_SOS = 0xDA,
            jmt_DRI = 0xDD
        }

        /// <summary>
        /// Calculates the total number of bytes required to encode the MJPEG RTP header,
        /// including the base JPEG header, an optional restart marker header, and optional quantization tables.
        /// </summary>
        /// <remarks>
        /// This method is useful for preallocating the exact buffer size needed to serialize
        /// the MJPEG RTP header without relying on dynamic memory growth or pooling.
        /// 
        /// Each packet contains a special JPEG header which immediately follows
        /// the rtp header.the first 8 bytes of this header, called the "main
        /// jpeg header", are as follows:
        /// <code>
        /// 0                   1                   2                   3
        /// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// | type-specific |              fragment offset                  |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// |      type     |       q       |     width     |     height    |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// </code>
        /// All fields in this header except for the fragment offset field must
        /// remain the same in all packets that correspond to the same jpeg
        /// frame.
        /// A restart marker header and/or quantization table header may follow
        /// this header, depending on the values of the type and q fields.
        /// </remarks>
        /// <param name="customData">The MJPEG metadata containing header fields, restart markers, and quantization tables.</param>
        /// <param name="offset">
        /// The fragment offset of the current RTP packet. Quantization tables are only included if this is 0,
        /// as they are typically sent only in the first packet of a fragmented MJPEG frame.
        /// </param>
        /// <returns>The total number of bytes required to encode the MJPEG RTP header.</returns>
        public static int CalculateMJPEGRTPHeaderLength(MJPEG customData, int offset)
        {
            var totalLength = 8; // Base JPEG header

            if (customData.HasRestartMarker)
            {
                totalLength += 4; // 2 bytes for RestartInterval + 2 bytes for marker info
            }

            var includeQTables =
                customData.MjpegHeaderQTable.GetLength() > 0 &&
                customData.QTables.Count > 0 &&
                offset == 0;

            if (includeQTables)
            {
                totalLength += 4; // QTable header
                foreach (var qTable in customData.QTables)
                {
                    totalLength += qTable.Length;
                }
            }

            return totalLength;
        }

        /// <summary>
        /// Writes an MJPEG RTP header into the provided <see cref="Span{Byte}"/> buffer.
        /// This includes the base JPEG header, and optionally a restart marker and quantization tables.
        /// </summary>
        /// <remarks>
        /// Each packet contains a special JPEG header which immediately follows
        /// the rtp header.the first 8 bytes of this header, called the "main
        /// jpeg header", are as follows:
        /// <code>
        /// 0                   1                   2                   3
        /// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// | type-specific |              fragment offset                  |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// |      type     |       q       |     width     |     height    |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// </code>
        /// All fields in this header except for the fragment offset field must
        /// remain the same in all packets that correspond to the same jpeg
        /// frame.
        /// A restart marker header and/or quantization table header may follow
        /// this header, depending on the values of the type and q fields.
        /// </remarks>
        /// <param name="customData">The MJPEG metadata to serialize into the buffer.</param>
        /// <param name="offset">The fragment offset of the current RTP packet.</param>
        /// <param name="destination">
        /// The buffer to write the MJPEG RTP header into. Must be at least <see cref="CalculateMJPEGRTPHeaderLength"/> bytes long.
        /// </param>
        /// <returns>The number of bytes written to the buffer.</returns>
        public static int WriteMJPEGRTPHeader(MJPEG customData, int offset, Span<byte> destination)
        {
            customData.MjpegHeader.SetOffset(offset);

            var position = 0;

            // Base JPEG header (8 bytes)
            destination[position++] = customData.MjpegHeader.tspec;
            destination[position++] = customData.MjpegHeader.offsetHigh;
            destination[position++] = customData.MjpegHeader.offsetMid;
            destination[position++] = customData.MjpegHeader.offsetLow;
            destination[position++] = customData.MjpegHeader.type;
            destination[position++] = customData.MjpegHeader.q;
            destination[position++] = customData.MjpegHeader.width;
            destination[position++] = customData.MjpegHeader.height;

            // Restart Marker (optional)
            if (customData.HasRestartMarker)
            {
                BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(position), (ushort)customData.MjpegHeaderRestartMarker.RestartInterval);
                position += 2;

                var markerInfo = (ushort)(
                    ((customData.MjpegHeaderRestartMarker.IsFirst & 0xF) << 8) |
                    ((customData.MjpegHeaderRestartMarker.IsLast & 0xF) << 7) |
                    (customData.MjpegHeaderRestartMarker.RestartCount & 0xF)
                );
                BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(position), markerInfo);
                position += 2;
            }

            // Quantization Tables (optional)
            var includeQTables =
                customData.MjpegHeaderQTable.GetLength() > 0 &&
                customData.QTables.Count > 0 &&
                offset == 0;

            if (includeQTables)
            {
                destination[position++] = customData.MjpegHeaderQTable.mbz;
                destination[position++] = customData.MjpegHeaderQTable.precision;
                destination[position++] = customData.MjpegHeaderQTable.lengthHigh;
                destination[position++] = customData.MjpegHeaderQTable.lengthLow;

                foreach (var qTable in customData.QTables)
                {
                    qTable.CopyTo(destination.Slice(position));
                    position += qTable.Length;
                }
            }

            return position;
        }

        /// <summary>
        /// Scans the frame for markers and builds the RTPHeader data.
        /// Returns the raw frame data.
        /// </summary>
        /// <param name="jpegFrame">The entire frame data</param>
        /// <returns></returns>
        public static (MJPEGData? frameData, MJPEG customData) GetFrameData(ReadOnlySpan<byte> jpegFrame)
        {
            var customData = new MJPEG();
            var index = 0;
            var length = jpegFrame.Length;
            var markers = new List<Marker>();
            var currentMarker = new Marker();

            while (index < length)
            {
                if (index + 1 < length &&
                    ContainsMarker(jpegFrame[index], JpegMarkerTypes.jmt_BeginMarker) &&
                    !ContainsMarker(jpegFrame[index + 1], JpegMarkerTypes.jmt_NotAmarker) &&
                    jpegFrame[index + 1] != 0xFF)
                {
                    if (((jpegFrame[index + 1] & 0xF0) == 0xD0) && ((jpegFrame[index + 1] & 0x0F) <= 0x07))
                    {
                        customData.HasRestartMarker = true;
                        index += 2;
                    }
                    else
                    {
                        if (currentMarker.StartPosition > 0)
                        {
                            currentMarker.MarkerBytes = jpegFrame.Slice(currentMarker.StartPosition, index + 1 - currentMarker.StartPosition).ToArray();
                            markers.Add(currentMarker);
                            currentMarker = new Marker();
                        }

                        currentMarker.Type = jpegFrame[index + 1];
                        currentMarker.StartPosition = index + 2;
                        index += 2;
                    }
                }
                else
                {
                    index++;
                }
            }

            if (currentMarker.StartPosition > 0)
            {
                currentMarker.MarkerBytes = jpegFrame.Slice(currentMarker.StartPosition, length - currentMarker.StartPosition).ToArray();
                markers.Add(currentMarker);
            }

            var mjpeg = default(MJPEGData);
            foreach (var marker in markers)
            {
                switch (marker.Type)
                {
                    case (byte)JpegMarkerTypes.jmt_SOF0:
                    case (byte)JpegMarkerTypes.jmt_SOF1:
                        ProcessJpegSof(marker, customData);
                        break;
                    case (byte)JpegMarkerTypes.jmt_DQT:
                        ProcessJpegDqt(marker, customData);
                        break;
                    case (byte)JpegMarkerTypes.jmt_SOS:
                        mjpeg = ProcessJpegSos(marker, customData);
                        break;
                    case (byte)JpegMarkerTypes.jmt_DRI:
                        ProcessJpegDri(marker, customData);
                        break;
                }
            }

            return (mjpeg, customData);
        }

        private static bool ContainsMarker(byte testValue, JpegMarkerTypes marker)
        {
            return testValue == (byte)marker;
        }

        private static void ProcessJpegDri(Marker marker, MJPEG mjpeg)
        {
            var restartInterval = (marker.MarkerBytes[2] << 8) | marker.MarkerBytes[3];
            mjpeg.MjpegHeaderRestartMarker.RestartInterval = (ushort)IPAddress.HostToNetworkOrder(restartInterval);
        }

        private static MJPEGData ProcessJpegSos(Marker marker, MJPEG mjpeg)
        {
            var hdrLength = (marker.MarkerBytes[0] << 8) | marker.MarkerBytes[1];
            var bytes = marker.MarkerBytes.AsSpan(hdrLength).ToArray();
            return new MJPEGData() { Data = bytes };
        }

        private static void ProcessJpegDqt(Marker marker, MJPEG mjpeg)
        {
            mjpeg.MjpegHeader.q = 255;
            var hdrLength = (marker.MarkerBytes[0] << 8) | marker.MarkerBytes[1];
            var precision = (byte)(marker.MarkerBytes[2] >> 4);
            mjpeg.MjpegHeaderQTable.precision = precision;

            if ((hdrLength - QTableHeaderLength) % QTableBlockLength8Bit != 0)
            {
                return;
            }
            var qCount = (hdrLength - QTableHeaderLength) / QTableBlockLength8Bit;
            for (var i = 0; i < qCount; i++)
            {
                var bytes = marker.MarkerBytes.AsSpan(QTableHeaderLength + i * QTableBlockLength8Bit + QTableParamsLength, QTableLength8Bit).ToArray();
                mjpeg.QTables.Add(bytes);
                mjpeg.MjpegHeaderQTable.SetLength(mjpeg.MjpegHeaderQTable.GetLength() + bytes.Length);
            }
        }

        private static void ProcessJpegSof(Marker marker, MJPEG mjpeg)
        {
            mjpeg.MjpegHeader.type = mjpeg.HasRestartMarker ? (byte)JpegHeaderTypes.jpegHeaderType_422wRestartMarkers : (byte)JpegHeaderTypes.jpegHeaderType_422;
            mjpeg.MjpegHeader.q = 255;

            var hdrLength = (marker.MarkerBytes[0] << 8) | marker.MarkerBytes[1];

            var precision = marker.MarkerBytes[2];

            var height = (marker.MarkerBytes[3] << 8) | marker.MarkerBytes[4];

            var width = (marker.MarkerBytes[5] << 8) | marker.MarkerBytes[6];

            var numberF = marker.MarkerBytes[7];

            if (JpegNumberOfComponents == numberF)
            {
                byte[] horizontalSf = { 0, 0, 0 };
                byte[] verticalSf = { 0, 0, 0 };
                var startOffset = 8 + 1;
                var increment = 3;

                for (int index = 0; index < 3; index++)
                {
                    var HVByte = marker.MarkerBytes[startOffset + index * increment];
                    horizontalSf[index] = (byte)((HVByte >> 4) & 0x0F);
                    verticalSf[index] = (byte)(HVByte & 0x0F);
                }
                if (CheckSfValues(horizontalSf, 2, 1, 1) && CheckSfValues(verticalSf, 1, 1, 1))
                {
                    mjpeg.MjpegHeader.type = mjpeg.HasRestartMarker ? (byte)JpegHeaderTypes.jpegHeaderType_422wRestartMarkers : (byte)JpegHeaderTypes.jpegHeaderType_422;
                }
                else if (CheckSfValues(horizontalSf, 2, 1, 1) && CheckSfValues(verticalSf, 2, 1, 1))
                {
                    mjpeg.MjpegHeader.type = mjpeg.HasRestartMarker ? (byte)JpegHeaderTypes.jpegHeaderType_420wRestartMarkers : (byte)JpegHeaderTypes.jpegHeaderType_420;
                }

                mjpeg.MjpegHeader.SetWidth(width);
                mjpeg.MjpegHeader.SetHeight(height);
            }
        }

        private static bool CheckSfValues(byte[] sf, int yValue, int uValue, int vValue)
        {
            return sf[(int)JpegComponents.jpegComponent_Y] == yValue && sf[(int)JpegComponents.jpegComponent_U] == uValue && sf[(int)JpegComponents.jpegComponent_V] == vValue;
        }
    }
}
