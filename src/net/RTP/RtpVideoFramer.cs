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
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorcery.Net
{
    public class RtpVideoFramer
    {
        private const int MAX_FRAME_SIZE = 65536;

        private static ILogger logger = Log.Logger;

        private VideoCodecsEnum _codec;
        private byte[] _currVideoFrame = new byte[MAX_FRAME_SIZE];
        private int _currVideoFramePosn = 0;
        private H264PayloadProcessor _h264Depacketiser;

        public RtpVideoFramer(VideoCodecsEnum codec)
        {
            if (!(codec == VideoCodecsEnum.VP8 || codec == VideoCodecsEnum.H264))
            {
                throw new NotSupportedException("The RTP video framer currently only understands H264 and VP8 encoded frames.");
            }

            _codec = codec;

            if (_codec == VideoCodecsEnum.H264)
            {
                _h264Depacketiser = new H264PayloadProcessor();
            }
        }

        public byte[] GotRtpPacket(RTPPacket rtpPacket)
        {
            var payload = rtpPacket.Payload;

            //var hdr = rtpPacket.Header;
            //logger.LogDebug($"rtp video, seqnum {hdr.SequenceNumber}, ts {hdr.Timestamp}, marker {hdr.MarkerBit}, payload {payload.Length}.");

            if (_codec == VideoCodecsEnum.VP8)
            {
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
                var hdr = rtpPacket.Header;
                var frameStream = _h264Depacketiser.ProcessRTPPayload(payload, hdr.SequenceNumber, hdr.Timestamp, hdr.MarkerBit, out bool isKeyFrame);

                if(frameStream != null)
                {
                    return frameStream.ToArray();
                }
            }

            return null;
        }
    }
}
