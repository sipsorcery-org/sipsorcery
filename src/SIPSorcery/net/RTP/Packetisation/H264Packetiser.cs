//-----------------------------------------------------------------------------
// Filename: H264Packetiser.cs
//
// Description: Contains functions to packetise an H264 Network Abstraction
// Layer Units (NAL or NALU) into an RTP payload.
//
// See "RTP Payload Format for H.264 Video" https://tools.ietf.org/html/rfc6184
//
// Packetisation Modes (see https://tools.ietf.org/html/rfc6184#section-6.2):
// 
// - Mode 0: Single NAL Unit Mode. This is the default mode used when no 
//   packetization-mode parameter is present or when it is set to 0. Only 
//   single NAL unit packets may be used in this mode. STAPs, MTAPs and FUs
//   must not be used.
//
// - Mode 1: Non-interleaved mode. This is the mode used when the
//   packetization-mode=1. Only single NAL unit packets, STAP-As and FU-As 
//   may be used in this mode.
//
// - Mode 2: Interleaved mode. This is the mode used when the
//   packetization-mode=2. This mode is not currently supported.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 27 Dec 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace SIPSorcery.Net
{
    public class H264Packetiser
    {
        public const int H264_RTP_HEADER_LENGTH = 2;

        public struct H264Nal
        {
            public byte[] NAL { get; }
            public bool IsLast { get; }

            public H264Nal(byte[] nal, bool isLast)
            {
                NAL = nal;
                IsLast = isLast;
            }
        }

        /// <summary>
        /// Splits an Annex B access unit into its NAL units, without their start codes.
        /// </summary>
        /// <remarks>
        /// Zero bytes in front of a start code (trailing_zero_8bits) and at the end of the access unit are padding and
        /// are not included in any NAL. NALs of any length are returned, and the last NAL is always flagged as last,
        /// even when the access unit ends with a start code.
        /// </remarks>
        public static IEnumerable<H264Nal> ParseNals(byte[] accessUnit)
        {
            if (accessUnit == null)
            {
                throw new ArgumentNullException(nameof(accessUnit));
            }

            // Parse NALs from H264 access unit, encoded as an Annex B bitstream.
            // NALs are delimited by 0x000001 or 0x00000001.
            var scanner = new AnnexBNalScanner();

            while (scanner.TryGetNext(accessUnit, out int offset, out int length, out bool isLast))
            {
                yield return new H264Nal(accessUnit.AsSpan(offset, length).ToArray(), isLast);
            }
        }

        /// <summary>
        /// Constructs the RTP header for an H264 NAL. This method does NOT support
        /// aggregation packets where multiple NALs are sent as a single RTP payload.
        /// The supported H264 header type is Single-Time Aggregation Packet type A 
        /// (STAP-A) and Fragmentation Unit A (FU-A). The headers produced correspond
        /// to H264 packetization-mode=1.
        /// </summary>
        /// <remarks>
        /// RTP Payload Format for H.264 Video:
        /// https://tools.ietf.org/html/rfc6184
        /// 
        /// FFmpeg H264 RTP packetisation code:
        /// https://github.com/FFmpeg/FFmpeg/blob/master/libavformat/rtpenc_h264_hevc.c
        /// 
        /// When the payload size is less than or equal to max RTP payload, send as 
        /// Single-Time Aggregation Packet (STAP):
        /// https://tools.ietf.org/html/rfc6184#section-5.7.1
        /// 
        ///      0                   1                   2                   3
        /// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// |                          RTP Header                           |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// |STAP-A NAL HDR |         NALU 1 Size           | NALU 1 HDR    |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// 
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// |F|NRI|  Type   |                                               |
        /// +-+-+-+-+-+-+-+-+
        /// 
        /// Type = 24 for STAP-A (NOTE: this is the type of the H264 RTP header 
        /// and NOT the NAL type).
        /// 
        /// When the payload size is greater than max RTP payload, send as 
        /// Fragmentation Unit A (FU-A):
        /// https://tools.ietf.org/html/rfc6184#section-5.8
        ///      0                   1                   2                   3
        /// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// | FU indicator  |   FU header   |                               |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+ 
        /// |   Fragmentation Unit (FU) Payload
        /// |
        /// ...
        /// 
        /// 
        /// The FU indicator octet has the following format:
        ///
        /// +---------------+
        /// |0|1|2|3|4|5|6|7|
        /// +-+-+-+-+-+-+-+-+
        /// |F|NRI|  Type   |
        /// +---------------+
        /// 
        /// F and NRI bits come from the NAL being transmitted.
        /// Type = 28 for FU-A (NOTE: this is the type of the H264 RTP header 
        /// and NOT the NAL type).
        /// 
        /// The FU header has the following format:
        ///
        /// +---------------+
        /// |0|1|2|3|4|5|6|7|
        /// +-+-+-+-+-+-+-+-+
        /// |S|E|R|  Type   |
        /// +---------------+
        /// 
        /// S: Set to 1 for the start of the NAL FU (i.e. first packet in frame).
        /// E: Set to 1 for the end of the NAL FU (i.e. the last packet in the frame).
        /// R: Reserved bit must be 0.
        /// Type: The NAL unit payload type, comes from NAL packet (NOTE: this IS the type of the NAL message).
        /// </remarks>
        public static byte[] GetH264RtpHeader(byte nal0, bool isFirstPacket, bool isFinalPacket)
        {
            var header = new byte[H264_RTP_HEADER_LENGTH];
            WriteH264RtpHeader(nal0, isFirstPacket, isFinalPacket, header);
            return header;
        }

        /// <summary>
        /// Writes the FU-A header produced by <see cref="GetH264RtpHeader"/> into <paramref name="destination"/>
        /// instead of a new array.
        /// </summary>
        /// <returns>The number of bytes written, <see cref="H264_RTP_HEADER_LENGTH"/>.</returns>
        internal static int WriteH264RtpHeader(byte nal0, bool isFirstPacket, bool isFinalPacket, Span<byte> destination)
        {
            byte nalType = (byte)(nal0 & 0x1F);
            //byte nalNri = (byte)((nal0 >> 5) & 0x03);

            byte firstHdrByte = (byte)(nal0 & 0xE0); // Has either 24 (STAP-A) or 28 (FU-A) added to it.

            byte fuIndicator = (byte)(firstHdrByte + 28);
            byte fuHeader = nalType;
            if (isFirstPacket)
            {
                fuHeader += 0x80;
            }
            else if (isFinalPacket)
            {
                fuHeader += 0x40;
            }

            destination[0] = fuIndicator;
            destination[1] = fuHeader;
            return H264_RTP_HEADER_LENGTH;
        }
    }

    /// <summary>
    /// Finds the NAL units in an Annex B access unit, delimited by 0x000001 or 0x00000001 start codes, and reports
    /// each as an offset and length into the access unit rather than copying it. <see cref="H264Packetiser.ParseNals"/>
    /// and <see cref="SIPSorcery.net.RTP.Packetisation.H265Packetiser.ParseNals"/> use it too, so all three agree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The byte stream format (H.264 / H.265 Annex B) never lets a NAL unit end in a zero byte (an encoder appends 0x03
    /// when it would), so every zero byte in front of a 0x000001 belongs to the start code or is trailing_zero_8bits
    /// padding, and zero bytes at the very end of the access unit are padding too. They are not part of any NAL.
    /// </para>
    /// <para>
    /// A NAL is only reported once the next one has been found, or the end of the access unit reached, so the last NAL
    /// is always flagged as last, even when the access unit ends with a start code or with padding.
    /// </para>
    /// <para>
    /// NALs shorter than the minimum length given to the constructor are skipped as if they were not there, so the NAL
    /// flagged as last is the last one long enough to be sent. A default scanner skips only empty NALs.
    /// </para>
    /// <para>
    /// A struct holding only the scan position and the minimum length, so it can be used from iterators and costs no
    /// allocation. The same access unit must be passed to every call.
    /// </para>
    /// </remarks>
    internal struct AnnexBNalScanner
    {
        private readonly int _minLength;
        private int _index;
        private int _zeroes;
        private int _nalStart;
        private bool _endReached;
        private bool _hasPending;
        private int _pendingOffset;
        private int _pendingLength;

        /// <summary>
        /// Creates a scanner that skips NALs shorter than <paramref name="minNalLength"/> bytes, for a codec whose NAL
        /// header is longer than one byte.
        /// </summary>
        public AnnexBNalScanner(int minNalLength)
        {
            this = default;
            _minLength = minNalLength;
        }

        /// <summary>
        /// Finds the next NAL in <paramref name="accessUnit"/>.
        /// </summary>
        /// <returns>True if a NAL was found, false once there are none left.</returns>
        public bool TryGetNext(ReadOnlySpan<byte> accessUnit, out int offset, out int length, out bool isLast)
        {
            while (true)
            {
                if (FindNal(accessUnit, out int nalOffset, out int nalLength))
                {
                    if (_hasPending)
                    {
                        // Another NAL follows, so the pending one is not the last.
                        offset = _pendingOffset;
                        length = _pendingLength;
                        isLast = false;
                        _pendingOffset = nalOffset;
                        _pendingLength = nalLength;
                        return true;
                    }

                    _hasPending = true;
                    _pendingOffset = nalOffset;
                    _pendingLength = nalLength;
                }
                else
                {
                    if (_hasPending)
                    {
                        offset = _pendingOffset;
                        length = _pendingLength;
                        isLast = true;
                        _hasPending = false;
                        return true;
                    }

                    offset = 0;
                    length = 0;
                    isLast = false;
                    return false;
                }
            }
        }

        /// <summary>
        /// Scans on to the next NAL that is not empty and not shorter than the minimum length.
        /// </summary>
        private bool FindNal(ReadOnlySpan<byte> accessUnit, out int offset, out int length)
        {
            while (_index < accessUnit.Length)
            {
                byte b = accessUnit[_index++];

                if (b == 0x00)
                {
                    _zeroes++;
                }
                else if (b == 0x01 && _zeroes >= 2)
                {
                    // A start code, with all the zero bytes in front of it. The NAL before it, if any, ends where the
                    // zeros start.
                    int nalEnd = _index - 1 - _zeroes;
                    offset = _nalStart;
                    length = nalEnd - _nalStart;

                    _nalStart = _index;
                    _zeroes = 0;

                    if (IsLongEnough(length))
                    {
                        return true;
                    }
                }
                else
                {
                    _zeroes = 0;
                }
            }

            if (!_endReached)
            {
                _endReached = true;

                // The rest of the access unit, without any zero padding at the end.
                offset = _nalStart;
                length = accessUnit.Length - _zeroes - _nalStart;

                if (IsLongEnough(length))
                {
                    return true;
                }
            }

            offset = 0;
            length = 0;
            return false;
        }

        private bool IsLongEnough(int length) => length > 0 && length >= _minLength;
    }
}
