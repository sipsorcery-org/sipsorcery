//-----------------------------------------------------------------------------
// Filename: VideoStream.cs
//
// Description: Define a Video media stream (which inherits MediaStream) to focus an Video specific treatment
// The goal is to simplify RTPSession class
//
// Author(s):
// Christophe Irles
//
// History:
// 05 Apr 2022	Christophe Irles        Created (based on existing code from previous RTPSession class)
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.net.RTP.Packetisation;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Net
{
    public class VideoStream : MediaStream
    {
        protected static ILogger logger = Log.Logger;
        protected RtpVideoFramer RtpVideoFramer;

        private VideoFormat sendingFormat;
        private bool sendingFormatFound = false;

        /// <summary>
        /// Gets fired when the remote SDP is received and the set of common video formats is set.
        /// </summary>
        public event Action<int, List<VideoFormat>> OnVideoFormatsNegotiatedByIndex;

        /// <summary>
        /// Gets fired when a full video frame is reconstructed from one or more RTP packets
        /// received from the remote party.
        /// </summary>
        /// <remarks>
        ///  - Received from end point,
        ///  - The frame timestamp,
        ///  - The encoded video frame payload.
        ///  - The video format of the encoded frame.
        /// </remarks>
        public event Action<int, IPEndPoint, uint, byte[], VideoFormat> OnVideoFrameReceivedByIndex;

        /// <summary>
        /// Indicates whether this session is using video.
        /// </summary>
        public bool HasVideo
        {
            get
            {
                return (LocalTrack != null && LocalTrack.StreamStatus != MediaStreamStatusEnum.Inactive)
                  || (RemoteTrack != null && RemoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive);
            }
        }

        /// <summary>
        /// Indicates the maximum frame size that can be reconstructed from RTP packets during the depacketisation
        /// process.
        /// </summary>
        public int MaxReconstructedVideoFrameSize { get; set; } = 1048576;

        /// <summary>
        /// Helper method to send a low quality JPEG image over RTP. This method supports a very abbreviated version of RFC 2435 "RTP Payload Format for JPEG-compressed Video".
        /// It's intended as a quick convenient way to send something like a test pattern image over an RTSP connection. More than likely it won't be suitable when a high
        /// quality image is required since the header used in this method does not support quantization tables.
        /// </summary>
        /// <param name="duration">The duration in timestamp units of the payload (e.g. 3000 for 30fps).</param>
        /// <param name="jpegBytes">The raw encoded bytes of the JPEG image to transmit.</param>
        /// <param name="jpegQuality">The encoder quality of the JPEG image.</param>
        /// <param name="jpegWidth">The width of the JPEG image.</param>
        /// <param name="jpegHeight">The height of the JPEG image.</param>
        public void SendJpegFrame(uint duration, int payloadTypeID, byte[] jpegBytes, int jpegQuality, int jpegWidth, int jpegHeight)
        {
            if (CheckIfCanSendRtpRaw())
            {
                try
                {
                    for (int index = 0; index * RTPSession.RTP_MAX_PAYLOAD < jpegBytes.Length; index++)
                    {
                        uint offset = Convert.ToUInt32(index * RTPSession.RTP_MAX_PAYLOAD);
                        int payloadLength = ((index + 1) * RTPSession.RTP_MAX_PAYLOAD < jpegBytes.Length) ? RTPSession.RTP_MAX_PAYLOAD : jpegBytes.Length - index * RTPSession.RTP_MAX_PAYLOAD;
                        byte[] jpegHeader = RtpVideoFramer.CreateLowQualityRtpJpegHeader(offset, jpegQuality, jpegWidth, jpegHeight);

                        List<byte> packetPayload = new List<byte>();
                        packetPayload.AddRange(jpegHeader);
                        packetPayload.AddRange(jpegBytes.Skip(index * RTPSession.RTP_MAX_PAYLOAD).Take(payloadLength));

                        int markerBit = ((index + 1) * RTPSession.RTP_MAX_PAYLOAD < jpegBytes.Length) ? 0 : 1;

                        SendRtpRaw(packetPayload.ToArray(), LocalTrack.Timestamp, markerBit, payloadTypeID, true);
                    }

                    LocalTrack.Timestamp += duration;
                }
                catch (SocketException sockExcp)
                {
                    logger.LogError(sockExcp, "SocketException SendJpegFrame. {ErrorMessage}", sockExcp.Message);
                }
            }
        }

        /// <summary>
        /// Sends a H264 frame, represented by an Access Unit, to the remote party.
        /// </summary>
        /// <param name="duration">The duration in timestamp units of the payload (e.g. 3000 for 30fps).</param>
        /// <param name="payloadTypeID">The payload type ID  being used for H264 and that will be set on the RTP header.</param>
        /// <param name="accessUnit">The encoded H264 access unit to transmit. An access unit can contain one or more
        /// NAL's.</param>
        /// <remarks>
        /// An Access Unit can contain one or more NAL's. The NAL's have to be parsed in order to be able to package 
        /// in RTP packets.
        /// 
        /// See <see href="https://www.itu.int/rec/dologin_pub.asp?lang=e&amp;id=T-REC-H.264-201602-S!!PDF-E&amp;type=items" /> Annex B for byte stream specification.
        /// </remarks>
        // The same URL without XML escape sequences: https://www.itu.int/rec/dologin_pub.asp?lang=e&id=T-REC-H.264-201602-S!!PDF-E&type=items
        public void SendH264Frame(uint duration, int payloadTypeID, byte[] accessUnit)
        {
            if (CheckIfCanSendRtpRaw())
            {
                foreach (var nal in H264Packetiser.ParseNals(accessUnit))
                {
                    SendH26XNal(duration, payloadTypeID, nal.NAL, nal.IsLast);
                }
            }
        }

        /// <summary>
        /// Sends a single H264 NAL to the remote party.
        /// </summary>
        /// <param name="duration">The duration in timestamp units of the payload (e.g. 3000 for 30fps).</param>
        /// <param name="payloadTypeID">The payload type ID  being used for H264 and that will be set on the RTP header.</param>
        /// <param name="nal">The buffer containing the NAL to send.</param>
        /// <param name="isLastNal">Should be set for the last NAL in the H264 access unit. Determines when the markbit gets set 
        /// and the timestamp incremented.</param>
        private void SendH26XNal(uint duration, int payloadTypeID, byte[] nal, bool isLastNal, bool is265 = false)
        {
            //logger.LogDebug($"Send NAL {nal.Length}, is last {isLastNal}, timestamp {videoTrack.Timestamp}.");
            //logger.LogDebug($"nri {nalNri:X2}, type {nalType:X2}.");
            var naluHeaderSize = is265 ? 2 : 1;
            byte[] naluHeader = is265 ? nal.Take(2).ToArray() : nal.Take(1).ToArray();

            if (nal.Length <= RTPSession.RTP_MAX_PAYLOAD)
            {
                // Send as Single-Time Aggregation Packet (STAP-A).
                byte[] payload = new byte[nal.Length];
                int markerBit = isLastNal ? 1 : 0;   // There is only ever one packet in a STAP-A.
                Buffer.BlockCopy(nal, 0, payload, 0, nal.Length);

                //For TWCC
                SetRtpHeaderExtensionValue(TransportWideCCExtension.RTP_HEADER_EXTENSION_URI, null);

                SendRtpRaw(payload, LocalTrack.Timestamp, markerBit, payloadTypeID, true);
                //logger.LogDebug($"send H264 {videoChannel.RTPLocalEndPoint}->{dstEndPoint} timestamp {videoTrack.Timestamp}, payload length {payload.Length}, seqnum {videoTrack.SeqNum}, marker {markerBit}.");
                //logger.LogDebug($"send H264 {videoChannel.RTPLocalEndPoint}->{dstEndPoint} timestamp {videoTrack.Timestamp}, STAP-A {h264RtpHdr.HexStr()}, payload length {payload.Length}, seqnum {videoTrack.SeqNum}, marker {markerBit}.");
            }
            else
            {
                nal = nal.Skip(naluHeaderSize).ToArray();

                // Send as Fragmentation Unit A (FU-A):
                for (int index = 0; index * RTPSession.RTP_MAX_PAYLOAD < nal.Length; index++)
                {
                    int offset = index * RTPSession.RTP_MAX_PAYLOAD;
                    int payloadLength = ((index + 1) * RTPSession.RTP_MAX_PAYLOAD < nal.Length) ? RTPSession.RTP_MAX_PAYLOAD : nal.Length - index * RTPSession.RTP_MAX_PAYLOAD;

                    bool isFirstPacket = index == 0;
                    bool isFinalPacket = (index + 1) * RTPSession.RTP_MAX_PAYLOAD >= nal.Length;
                    int markerBit = (isLastNal && isFinalPacket) ? 1 : 0;

                    byte[] rtpHdr = is265 ? H265Packetiser.GetH265RtpHeader(naluHeader, isFirstPacket, isFinalPacket) : H264Packetiser.GetH264RtpHeader(naluHeader[0], isFirstPacket, isFinalPacket);
                    
                    byte[] payload = new byte[payloadLength + rtpHdr.Length];
                    Buffer.BlockCopy(rtpHdr, 0, payload, 0, rtpHdr.Length);
                    Buffer.BlockCopy(nal, offset, payload, rtpHdr.Length, payloadLength);

                    //For TWCC
                    SetRtpHeaderExtensionValue(TransportWideCCExtension.RTP_HEADER_EXTENSION_URI, null);

                    SendRtpRaw(payload, LocalTrack.Timestamp, markerBit, payloadTypeID, true);
                    //logger.LogDebug($"send H264 {videoChannel.RTPLocalEndPoint}->{dstEndPoint} timestamp {videoTrack.Timestamp}, FU-A {h264RtpHdr.HexStr()}, payload length {payloadLength}, seqnum {videoTrack.SeqNum}, marker {markerBit}.");
                }
            }

            if (isLastNal)
            {
                LocalTrack.Timestamp += duration;
            }
        }

        public void SendH265Frame(uint durationRtpUnits, int payloadID, byte[] sample)
        {
            if(CheckIfCanSendRtpRaw())
            {
                var nals = H265Packetiser.ParseNals(sample);
                var aggregatedNals = H265Packetiser.CreateAggregated(nals, RTPSession.RTP_MAX_PAYLOAD);
                foreach (var nal in aggregatedNals)
                {
                    SendH26XNal(durationRtpUnits, payloadID, nal.NAL, nal.IsLast, true);
                }
            }
        }

        /// <summary>
        /// Sends a VP8 frame as one or more RTP packets.
        /// </summary>
        /// <param name="duration"> The duration in timestamp units of the payload. Needs
        /// to be based on a 90Khz clock.</param>
        /// <param name="payloadTypeID">The payload ID to place in the RTP header.</param>
        /// <param name="buffer">The VP8 encoded payload.</param>
        public void SendVp8Frame(uint duration, int payloadTypeID, byte[] buffer)
        {
            if (CheckIfCanSendRtpRaw())
            {
                try
                {
                    for (int index = 0; index * RTPSession.RTP_MAX_PAYLOAD < buffer.Length; index++)
                    {
                        int offset = index * RTPSession.RTP_MAX_PAYLOAD;
                        int payloadLength = (offset + RTPSession.RTP_MAX_PAYLOAD < buffer.Length) ? RTPSession.RTP_MAX_PAYLOAD : buffer.Length - offset;

                        byte[] vp8HeaderBytes = (index == 0) ? new byte[] { 0x10 } : new byte[] { 0x00 };
                        byte[] payload = new byte[payloadLength + vp8HeaderBytes.Length];
                        Buffer.BlockCopy(vp8HeaderBytes, 0, payload, 0, vp8HeaderBytes.Length);
                        Buffer.BlockCopy(buffer, offset, payload, vp8HeaderBytes.Length, payloadLength);

                        int markerBit = ((offset + payloadLength) >= buffer.Length) ? 1 : 0; // Set marker bit for the last packet in the frame.

                        SetRtpHeaderExtensionValue(TransportWideCCExtension.RTP_HEADER_EXTENSION_URI, null);
                        SendRtpRaw(payload, LocalTrack.Timestamp, markerBit, payloadTypeID, true);
                    }
                    LocalTrack.Timestamp += duration;
                }
                catch (SocketException sockExcp)
                {
                    logger.LogError(sockExcp, "SocketException SendVp8Frame.");
                }
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="durationRtpUnits"></param>
        /// <param name="payloadID"></param>
        /// <param name="sample"></param>
        public void SendMJPEGFrame(uint durationRtpUnits, int payloadID, byte[] sample)
        {
            if (CheckIfCanSendRtpRaw())
            {
                try
                {
                    var frameData = MJPEGPacketiser.GetFrameData(sample, out var customData);

                    var rtpHeader = MJPEGPacketiser.GetMJPEGRTPHeader(customData, 0);
                    if (rtpHeader.Length + frameData.Data.Length <= RTPSession.RTP_MAX_PAYLOAD)
                    {
                        var payload = rtpHeader.Concat(frameData.Data).ToArray();
                        SendRtpRaw(payload, LocalTrack.Timestamp, 1, payloadID, true);
                    }
                    else
                    {
                        var restBytes = frameData.Data;
                        var offset = 0;
                        while (restBytes.Length > 0)
                        {
                            var dataSize = RTPSession.RTP_MAX_PAYLOAD - rtpHeader.Length;
                            var isLast = dataSize >= restBytes.Length;
                            var data = isLast ? restBytes : restBytes.Take(dataSize).ToArray();
                            var markerBit = isLast ? 0 : 1;
                            var payload = rtpHeader.Concat(data).ToArray();
                            SendRtpRaw(payload, LocalTrack.Timestamp, markerBit, payloadID, true);

                            offset += RTPSession.RTP_MAX_PAYLOAD;
                            rtpHeader = MJPEGPacketiser.GetMJPEGRTPHeader(customData, offset);
                            restBytes = restBytes.Skip(data.Length).ToArray();
                        }
                    }
                }
                catch (SocketException sockExcp)
                {
                    logger.LogError("SocketException SendMJEPGFrame. " + sockExcp.Message);
                }
            }
        }

        /// <summary>
        /// Sends a video sample to the remote peer.
        /// </summary>
        /// <param name="durationRtpUnits">The duration in RTP timestamp units of the video sample. This
        /// value is added to the previous RTP timestamp when building the RTP header.</param>
        /// <param name="sample">The video sample to set as the RTP packet payload.</param>
        ///
        public void SendVideo(uint durationRtpUnits, byte[] sample)
        {
            if (!sendingFormatFound)
            {
                sendingFormat = GetSendingFormat().ToVideoFormat();
                sendingFormatFound = true;
            }

            int payloadID = sendingFormat.FormatID;

            switch (sendingFormat.Codec)
            {
                case VideoCodecsEnum.VP8:
                    SendVp8Frame(durationRtpUnits, payloadID, sample);
                    break;
                case VideoCodecsEnum.H264:
                    SendH264Frame(durationRtpUnits, payloadID, sample);
                    break;
                case VideoCodecsEnum.H265:
                    SendH265Frame(durationRtpUnits, payloadID, sample);
                    break;
                case VideoCodecsEnum.JPEG:
                    SendMJPEGFrame(durationRtpUnits, payloadID, sample);
                    break;
                default:
                    throw new ApplicationException($"Unsupported video format selected {sendingFormat.FormatName}.");
            }
        }

        public void ProcessVideoRtpFrame(IPEndPoint endpoint, RTPPacket packet, SDPAudioVideoMediaFormat format)
        {
            if (OnVideoFrameReceivedByIndex == null)
            {
                return;
            }

            if (RtpVideoFramer != null)
            {
                var frame = RtpVideoFramer.GotRtpPacket(packet);
                if (frame != null)
                {
                    OnVideoFrameReceivedByIndex?.Invoke(Index, endpoint, packet.Header.Timestamp, frame, format.ToVideoFormat());
                }
            }
            else
            {
                if (format.ToVideoFormat().Codec == VideoCodecsEnum.VP8 ||
                    format.ToVideoFormat().Codec == VideoCodecsEnum.H264 ||
                    format.ToVideoFormat().Codec == VideoCodecsEnum.H265 ||
                    format.ToVideoFormat().Codec == VideoCodecsEnum.JPEG)
                {
                    logger.LogDebug("Video depacketisation codec set to {Codec} for SSRC {SSRC}.", format.ToVideoFormat().Codec, packet.Header.SyncSource);

                    RtpVideoFramer = new RtpVideoFramer(format.ToVideoFormat().Codec, MaxReconstructedVideoFrameSize);

                    var frame = RtpVideoFramer.GotRtpPacket(packet);
                    if (frame != null)
                    {
                        OnVideoFrameReceivedByIndex?.Invoke(Index, endpoint, packet.Header.Timestamp, frame, format.ToVideoFormat());
                    }
                }
                else
                {
                    logger.LogWarning("Video depacketisation logic for codec {CodecName} has not been implemented, PR's welcome!", format.Name());
                }
            }
        }

        public void CheckVideoFormatsNegotiation()
        {
            if (LocalTrack != null && LocalTrack.Capabilities?.Count() > 0)
            {
                OnVideoFormatsNegotiatedByIndex?.Invoke(
                            Index,
                            LocalTrack.Capabilities
                            .Select(x => x.ToVideoFormat()).ToList());
            }
        }

        public VideoStream(RtpSessionConfig config, int index) : base(config, index)
        {
            MediaType = SDPMediaTypesEnum.video;
            NegotiatedRtpEventPayloadID = 0;
        }
    }
}
