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
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
        [Obsolete("Use SendJpegFrame(uint, int, ReadOnlySpan<byte>, int, int, int) instead", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public void SendJpegFrame(uint duration, int payloadTypeID, byte[] jpegBytes, int jpegQuality, int jpegWidth, int jpegHeight)
        {
            SendJpegFrame(duration, payloadTypeID, new ReadOnlySpan<byte>(jpegBytes), jpegQuality, jpegWidth, jpegHeight);
        }

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
        public void SendJpegFrame(uint duration, int payloadTypeID, ReadOnlySpan<byte> jpegBytes, int jpegQuality, int jpegWidth, int jpegHeight)
        {
            if (CheckIfCanSendRtpRaw())
            {
                try
                {
                    var maxPayload = RTPSession.RTP_MAX_PAYLOAD;
                    var headerLength = 8;

                    using var memoryOwner = MemoryPool<byte>.Shared.Rent(headerLength + maxPayload);
                    var payloadSpan = memoryOwner.Memory.Span;

                    var offset = 0u;

                    while (!jpegBytes.IsEmpty)
                    {
                        var payloadLength = Math.Min(maxPayload, jpegBytes.Length);

                        // Write JPEG RTP header directly into the buffer
                        RtpVideoFramer.WriteLowQualityRtpJpegHeader(payloadSpan.Slice(0, headerLength), offset, jpegQuality, jpegWidth, jpegHeight);

                        // Copy JPEG payload
                        jpegBytes.Slice(0, payloadLength).CopyTo(payloadSpan.Slice(headerLength));

                        var isLastPacket = payloadLength >= jpegBytes.Length;
                        var markerBit = isLastPacket ? 1 : 0;

                        SendRtpRaw(payloadSpan.Slice(0, headerLength + payloadLength), LocalTrack.Timestamp, markerBit, payloadTypeID, true);

                        jpegBytes = jpegBytes.Slice(payloadLength);
                        offset += (uint)payloadLength;
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
        private void SendH26XNal(uint duration, int payloadTypeID, ReadOnlySpan<byte> nal, bool isLastNal, bool is265 = false)
        {
                var naluHeaderSize = is265 ? 2 : 1;
            var naluHeader = nal.Slice(0, naluHeaderSize);

            if (nal.Length <= RTPSession.RTP_MAX_PAYLOAD)
            {
                    // Send as Single-Time Aggregation Packet (STAP-A).
                var markerBit = isLastNal ? 1 : 0;
                var payload = ArrayPool<byte>.Shared.Rent(nal.Length);

                try
                {
                    nal.CopyTo(payload);

                    SetRtpHeaderExtensionValue(TransportWideCCExtension.RTP_HEADER_EXTENSION_URI, null);
                    SendRtpRaw(payload.AsSpan(0, nal.Length), LocalTrack.Timestamp, markerBit, payloadTypeID, true);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(payload);
                }
            }
            else
            {
                var nalPayload = nal.Slice(naluHeaderSize);

                // Send as Fragmentation Unit A (FU-A):
                for (var index = 0; index * RTPSession.RTP_MAX_PAYLOAD < nalPayload.Length; index++)
                {
                    var offset = index * RTPSession.RTP_MAX_PAYLOAD;
                    var payloadLength = Math.Min(RTPSession.RTP_MAX_PAYLOAD, nalPayload.Length - offset);

                    var isFirstPacket = index == 0;
                    var isFinalPacket = offset + payloadLength >= nalPayload.Length;
                    var markerBit = (isLastNal && isFinalPacket) ? 1 : 0;

                    var rtpHdr = is265
                        ? H265Packetiser.GetH265RtpHeader(naluHeader.ToArray(), isFirstPacket, isFinalPacket) 
                        : H264Packetiser.GetH264RtpHeader(naluHeader[0], isFirstPacket, isFinalPacket);

                    var payload = ArrayPool<byte>.Shared.Rent(payloadLength + rtpHdr.Length);

                    try
                    {
                        rtpHdr.CopyTo(payload.AsSpan(0, rtpHdr.Length));
                        nalPayload.Slice(offset, payloadLength).CopyTo(payload.AsSpan(rtpHdr.Length, payloadLength));

                    SetRtpHeaderExtensionValue(TransportWideCCExtension.RTP_HEADER_EXTENSION_URI, null);
                        SendRtpRaw(payload.AsSpan(0, payloadLength + rtpHdr.Length), LocalTrack.Timestamp, markerBit, payloadTypeID, true);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(payload);
                    }
                }
            }

            if (isLastNal)
            {
                LocalTrack.Timestamp += duration;
            }
        }

        public void SendH265Frame(uint durationRtpUnits, int payloadID, byte[] sample)
        {
            if (CheckIfCanSendRtpRaw())
            {
                var nals = H265Packetiser.ParseNals(sample);

                // aggregation is only on 2 or more small nals
                if (nals.Where(x => x.NAL.Length < RTPSession.RTP_MAX_PAYLOAD).Count() > 1)
                {
                    //logger.LogTrace("(ou) Trying aggregating {nals} nals", nals.Count());
                    nals = H265Packetiser.CreateAggregated(nals, RTPSession.RTP_MAX_PAYLOAD);
                }

                //var i = 1;
                foreach (var nal in nals)
                {
                    //logger.LogTrace("(out) SEND {bits}({of}/{all})", nal.NAL.Length, i++, nals.Count());
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
        [Obsolete("Use SendVp8Frame(uint, int, ReadOnlySpan<byte>) instead", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public void SendVp8Frame(uint duration, int payloadTypeID, byte[] buffer)
            => SendVp8Frame(duration, payloadTypeID, new ReadOnlySpan<byte>(buffer));

        /// <summary>
        /// Sends a VP8 frame as one or more RTP packets.
        /// </summary>
        /// <param name="duration"> The duration in timestamp units of the payload. Needs
        /// to be based on a 90Khz clock.</param>
        /// <param name="payloadTypeID">The payload ID to place in the RTP header.</param>
        /// <param name="buffer">The VP8 encoded payload.</param>
        public void SendVp8Frame(uint duration, int payloadTypeID, ReadOnlySpan<byte> buffer)
        {
            if (CheckIfCanSendRtpRaw())
            {
                try
                {
                    using var memoryOwner = MemoryPool<byte>.Shared.Rent(RTPSession.RTP_MAX_PAYLOAD + 1);
                    var payloadSpan = memoryOwner.Memory.Span;
                    payloadSpan[0] = (byte)0x10;

                    while (!buffer.IsEmpty)
                    {
                        var payloadLength = Math.Min(RTPSession.RTP_MAX_PAYLOAD, buffer.Length);

                        buffer.Slice(0, payloadLength).CopyTo(payloadSpan.Slice(1));

                        var markerBit = (payloadLength >= buffer.Length) ? 1 : 0; // Set marker bit for the last packet in the frame.

                        SetRtpHeaderExtensionValue(TransportWideCCExtension.RTP_HEADER_EXTENSION_URI, null);
                        SendRtpRaw(payloadSpan.Slice(0, payloadLength + 1), LocalTrack.Timestamp, markerBit, payloadTypeID, true);

                        payloadSpan[0] = (byte)0x00;
                        buffer = buffer.Slice(payloadLength);
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
        /// Sends an MJPEG frame as one or more RTP packets according to RFC 2435.
        /// </summary>
        /// <param name="durationRtpUnits">The duration in RTP timestamp units of the payload.</param>
        /// <param name="payloadID">The payload ID to place in the RTP header.</param>
        /// <param name="sample">The MJPEG encoded payload.</param>
        [Obsolete("Use SendMJPEGFrame(uint, int, ReadOnlySpan<byte>) instead", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public void SendMJPEGFrame(uint durationRtpUnits, int payloadID, byte[] sample)
            => SendMJPEGFrame(durationRtpUnits, payloadID, new ReadOnlySpan<byte>(sample));

        /// <summary>
        /// Sends an MJPEG frame as one or more RTP packets according to RFC 2435.
        /// </summary>
        /// <param name="durationRtpUnits">The duration in RTP timestamp units of the payload.</param>
        /// <param name="payloadID">The payload ID to place in the RTP header.</param>
        /// <param name="sample">The MJPEG encoded payload.</param>
        public void SendMJPEGFrame(uint durationRtpUnits, int payloadID, ReadOnlySpan<byte> sample)
        {
            if (CheckIfCanSendRtpRaw())
            {
                try
                {
                    var (frameData, customData) = MJPEGPacketiser.GetFrameData(sample);
                    Debug.Assert(frameData is not null);
                    var rtpHeaderLength = MJPEGPacketiser.CalculateMJPEGRTPHeaderLength(customData, 0);

                    var totalLength = rtpHeaderLength + frameData.Data.Length;

                    if (totalLength <= RTPSession.RTP_MAX_PAYLOAD)
                    {
                        using var memoryOwner = MemoryPool<byte>.Shared.Rent(totalLength);
                        var payloadMemory = memoryOwner.Memory.Slice(0, totalLength);
                        var payloadSpan = payloadMemory.Span;
                        
                        MJPEGPacketiser.WriteMJPEGRTPHeader(customData, 0, payloadSpan);
                        frameData.Data.CopyTo(payloadSpan.Slice(rtpHeaderLength));
                        
                        SetRtpHeaderExtensionValue(TransportWideCCExtension.RTP_HEADER_EXTENSION_URI, null);
                        SendRtpRaw(payloadSpan, LocalTrack.Timestamp, 1, payloadID, true);
                    }
                    else
                    {
                        var restBytes = frameData.Data.AsSpan();
                        var offset = 0;
                        
                        while (restBytes.Length > 0)
                        {
                            var dataSize = RTPSession.RTP_MAX_PAYLOAD - rtpHeaderLength;
                            var isLast = dataSize >= restBytes.Length;
                            var dataLength = isLast ? restBytes.Length : dataSize;

                            totalLength = rtpHeaderLength + dataLength;

                            using var memoryOwner = MemoryPool<byte>.Shared.Rent(totalLength);
                            var payloadMemory = memoryOwner.Memory.Slice(0, totalLength);
                            var payloadSpan = payloadMemory.Span;

                            MJPEGPacketiser.WriteMJPEGRTPHeader(customData, offset, payloadSpan);
                            restBytes.Slice(0, dataLength).CopyTo(payloadSpan.Slice(rtpHeaderLength));

                            var markerBit = isLast ? 1 : 0;  // Marker bit should be 1 for the last packet

                            SetRtpHeaderExtensionValue(TransportWideCCExtension.RTP_HEADER_EXTENSION_URI, null);
                            SendRtpRaw(payloadSpan, LocalTrack.Timestamp, markerBit, payloadID, true);

                            offset += dataLength;
                            restBytes = restBytes.Slice(dataLength);
                        }
                    }
                    
                    LocalTrack.Timestamp += durationRtpUnits;
                }
                catch (SocketException sockExcp)
                {
                    logger.LogError(sockExcp, "SocketException SendMJPEGFrame. {ErrorMessage}", sockExcp.Message);
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
