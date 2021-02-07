//-----------------------------------------------------------------------------
// Filename: RtpVideoFramer.cs
//
// Description: Video frames can be spread across multiple RTP packets. The
// purpose of this class is to put the RTP packets together to get back the
// encoded video frame.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 04 Sep 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Net
{
    public class RtpVideoFramer
    {
        private const int MAX_FRAME_SIZE = 65536;

        private static ILogger logger = Log.Logger;

        private VideoCodecsEnum _codec;
        private byte[] _currVideoFrame = new byte[MAX_FRAME_SIZE];
        private int _currVideoFramePosn = 0;
        private H264Depacketiser _h264Depacketiser;

        public RtpVideoFramer(VideoCodecsEnum codec)
        {
            if (!(codec == VideoCodecsEnum.VP8 || codec == VideoCodecsEnum.H264))
            {
                throw new NotSupportedException("The RTP video framer currently only understands H264 and VP8 encoded frames.");
            }

            _codec = codec;

            if (_codec == VideoCodecsEnum.H264)
            {
                _h264Depacketiser = new H264Depacketiser();
            }
        }

        public byte[] GotRtpPacket(RTPPacket rtpPacket)
        {
            var payload = rtpPacket.Payload;

            var hdr = rtpPacket.Header;

            if (_codec == VideoCodecsEnum.VP8)
            {
                //logger.LogDebug($"rtp VP8 video, seqnum {hdr.SequenceNumber}, ts {hdr.Timestamp}, marker {hdr.MarkerBit}, payload {payload.Length}.");

                if (_currVideoFramePosn + payload.Length >= MAX_FRAME_SIZE)
                {
                    // Something has gone very wrong. Clear the buffer.
                    _currVideoFramePosn = 0;
                }

                // New frames must have the VP8 Payload Descriptor Start bit set.
                // The tracking of the current video frame position is to deal with a VP8 frame being split across multiple RTP packets
                // as per https://tools.ietf.org/html/rfc7741#section-4.4.
                if (_currVideoFramePosn > 0 || (payload[0] & 0x10) > 0)
                {
                    RtpVP8Header vp8Header = RtpVP8Header.GetVP8Header(payload);

                    Buffer.BlockCopy(payload, vp8Header.Length, _currVideoFrame, _currVideoFramePosn, payload.Length - vp8Header.Length);
                    _currVideoFramePosn += payload.Length - vp8Header.Length;

                    if (rtpPacket.Header.MarkerBit > 0)
                    {
                        var frame = _currVideoFrame.Take(_currVideoFramePosn).ToArray();

                        _currVideoFramePosn = 0;

                        return frame;
                    }
                }
                else
                {
                    logger.LogWarning("Discarding RTP packet, VP8 header Start bit not set.");
                    //logger.LogWarning($"rtp video, seqnum {hdr.SequenceNumber}, ts {hdr.Timestamp}, marker {hdr.MarkerBit}, payload {payload.Length}.");
                }
            }
            else if (_codec == VideoCodecsEnum.H264)
            {
                //logger.LogDebug($"rtp H264 video, seqnum {hdr.SequenceNumber}, ts {hdr.Timestamp}, marker {hdr.MarkerBit}, payload {payload.Length}.");

                //var hdr = rtpPacket.Header;
                var frameStream = _h264Depacketiser.ProcessRTPPayload(payload, hdr.SequenceNumber, hdr.Timestamp, hdr.MarkerBit, out bool isKeyFrame);

                if (frameStream != null)
                {
                    return frameStream.ToArray();
                }
            }
            else
            {
                logger.LogWarning($"rtp unknown video, seqnum {hdr.SequenceNumber}, ts {hdr.Timestamp}, marker {hdr.MarkerBit}, payload {payload.Length}.");
            }

            return null;
        }

        /// <summary>
        /// Utility function to create RtpJpegHeader either for initial packet or template for further packets
        /// 
        /// <code>
        /// 0                   1                   2                   3
        /// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// | Type-specific |              Fragment Offset                  |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// |      Type     |       Q       |     Width     |     Height    |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// </code>
        /// </summary>
        /// <param name="fragmentOffset"></param>
        /// <param name="quality"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        public static byte[] CreateLowQualityRtpJpegHeader(uint fragmentOffset, int quality, int width, int height)
        {
            byte[] rtpJpegHeader = new byte[8] { 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 };

            // Byte 0: Type specific
            //http://tools.ietf.org/search/rfc2435#section-3.1.1

            // Bytes 1 to 3: Three byte fragment offset
            //http://tools.ietf.org/search/rfc2435#section-3.1.2

            if (BitConverter.IsLittleEndian)
            {
                fragmentOffset = NetConvert.DoReverseEndian(fragmentOffset);
            }

            byte[] offsetBytes = BitConverter.GetBytes(fragmentOffset);
            rtpJpegHeader[1] = offsetBytes[2];
            rtpJpegHeader[2] = offsetBytes[1];
            rtpJpegHeader[3] = offsetBytes[0];

            // Byte 4: JPEG Type.
            //http://tools.ietf.org/search/rfc2435#section-3.1.3

            //Byte 5: http://tools.ietf.org/search/rfc2435#section-3.1.4 (Q)
            rtpJpegHeader[5] = (byte)quality;

            // Byte 6: http://tools.ietf.org/search/rfc2435#section-3.1.5 (Width)
            rtpJpegHeader[6] = (byte)(width / 8);

            // Byte 7: http://tools.ietf.org/search/rfc2435#section-3.1.6 (Height)
            rtpJpegHeader[7] = (byte)(height / 8);

            return rtpJpegHeader;
        }
    }
}
