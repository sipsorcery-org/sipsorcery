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
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Net
{
    public class VideoStream : MediaStream
    {
        protected static readonly ILogger logger = LogFactory.CreateLogger<VideoStream>();
        protected RtpVideoFramer RtpVideoFramer;

        private VideoFormat sendingFormat;
        private bool sendingFormatFound = false;

        // Monotonically increasing 15-bit VP9 picture ID, stamped on the descriptor of every VP9 frame sent.
        private int _vp9PictureId = 0;

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

        public VideoStream(RtpSessionConfig config, int index) : base(config, index)
        {
            MediaType = SDPMediaTypesEnum.video;
            NegotiatedRtpEventPayloadID = 0;
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
            SendH264FrameCore(null, duration, payloadTypeID, accessUnit);
        }

        private void SendH264FrameCore(uint? rtpTimestamp, uint duration, int payloadTypeID, byte[] accessUnit)
        {
            ThrowIfNullFrame(accessUnit, nameof(accessUnit));
            SendH264FrameCore(rtpTimestamp, duration, payloadTypeID, new ReadOnlySpan<byte>(accessUnit));
        }

        private void SendH264FrameCore(uint? rtpTimestamp, uint duration, int payloadTypeID, ReadOnlySpan<byte> accessUnit)
        {
            if (CheckIfCanSendRtpRaw())
            {
                try
                {
                    // The NALs are found in place and each packet is built from its slice of the access unit, so the
                    // frame is only copied into the packets themselves.
                    var nals = new AnnexBNalScanner();

                    while (nals.TryGetNext(accessUnit, out int offset, out int length, out bool isLast))
                    {
                        SendH26XNal(rtpTimestamp, duration, payloadTypeID, accessUnit.Slice(offset, length), isLast);
                    }
                }
                catch (SocketException sockExcp)
                {
                    logger.LogError(sockExcp, "SocketException SendH264Frame.");
                }
            }
        }

        /// <summary>
        /// The codecs whose frames are parsed (VP8, H264, H265) cannot send a null frame, so their array based send
        /// methods reject it rather than treat it as an empty frame.
        /// </summary>
        private static void ThrowIfNullFrame(byte[] frame, string paramName)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(paramName);
            }
        }

        /// <summary>
        /// Sends a single H264 or H265 NAL, or an H265 aggregation packet, to the remote party: in one packet when it
        /// fits, otherwise as fragmentation units.
        /// </summary>
        /// <param name="rtpTimestamp">An explicit RTP timestamp for the NAL, or null to use the track timestamp.</param>
        /// <param name="duration">The duration in timestamp units of the payload (e.g. 3000 for 30fps). Only used
        /// when <paramref name="rtpTimestamp"/> is null.</param>
        /// <param name="payloadTypeID">The payload type ID  being used for H264 and that will be set on the RTP header.</param>
        /// <param name="nal">The buffer containing the NAL to send.</param>
        /// <param name="isLastNal">Should be set for the last NAL in the H264 access unit. Determines when the markbit gets set 
        /// and the timestamp incremented.</param>
        private void SendH26XNal(uint? rtpTimestamp, uint duration, int payloadTypeID, ReadOnlySpan<byte> nal, bool isLastNal, bool is265 = false)
        {
            //logger.LogDebug($"Send NAL {nal.Length}, is last {isLastNal}, timestamp {videoTrack.Timestamp}.");
            //logger.LogDebug($"nri {nalNri:X2}, type {nalType:X2}.");
            var naluHeaderSize = is265 ? 2 : 1;

            if (nal.Length <= RTPSession.RTP_MAX_PAYLOAD)
            {
                //var naltype = naluHeader[0] >> 1 & 0x3F;
                //logger.LogTrace("Sending NALtype {type}", naltype);

                // Sent as it is, in a single packet.
                int markerBit = isLastNal ? 1 : 0;

                //For TWCC
                SetRtpHeaderExtensionValue(TransportWideCCExtension.RTP_HEADER_EXTENSION_URI, null);

                SendRtpRaw(ReadOnlySpan<byte>.Empty, nal, rtpTimestamp ?? LocalTrack.Timestamp, markerBit, payloadTypeID);
                //logger.LogDebug($"send H264 {videoChannel.RTPLocalEndPoint}->{dstEndPoint} timestamp {videoTrack.Timestamp}, payload length {payload.Length}, seqnum {videoTrack.SeqNum}, marker {markerBit}.");
                //logger.LogDebug($"send H264 {videoChannel.RTPLocalEndPoint}->{dstEndPoint} timestamp {videoTrack.Timestamp}, STAP-A {h264RtpHdr.HexStr()}, payload length {payload.Length}, seqnum {videoTrack.SeqNum}, marker {markerBit}.");
            }
            else
            {
                // The NAL header the fragmentation unit headers are made from.
                byte nal0 = nal[0];
                byte nal1 = is265 ? nal[1] : (byte)0;

                ReadOnlySpan<byte> fragmented = nal.Slice(naluHeaderSize);
                Span<byte> fuHeader = stackalloc byte[H265Packetiser.H265_FU_HEADER_LENGTH];
                //logger.LogTrace("Fragmenting");

                // A NAL must not be sent as a single fragmentation unit, with the start and end bits in the same FU
                // header (RFC 6184 section 5.8, RFC 7798 section 4.4.3). A NAL only just over the RTP payload limit
                // (1201 bytes for H264, 1201 or 1202 for H265) fits in one FU once its NAL header is removed, so it is
                // split into two halves instead. Larger NALs are fragmented at the payload limit.
                int fragmentSize = (fragmented.Length > RTPSession.RTP_MAX_PAYLOAD) ? RTPSession.RTP_MAX_PAYLOAD : (fragmented.Length + 1) / 2;

                // Send as Fragmentation Unit A (FU-A):
                for (int index = 0; index * fragmentSize < fragmented.Length; index++)
                {
                    int offset = index * fragmentSize;
                    int payloadLength = Math.Min(fragmentSize, fragmented.Length - offset);

                    bool isFirstPacket = index == 0;
                    bool isFinalPacket = offset + payloadLength >= fragmented.Length;
                    int markerBit = (isLastNal && isFinalPacket) ? 1 : 0;

                    int fuHeaderLength = is265 ?
                        H265Packetiser.WriteH265RtpHeader(nal0, nal1, isFirstPacket, isFinalPacket, fuHeader) :
                        H264Packetiser.WriteH264RtpHeader(nal0, isFirstPacket, isFinalPacket, fuHeader);

                    //For TWCC
                    SetRtpHeaderExtensionValue(TransportWideCCExtension.RTP_HEADER_EXTENSION_URI, null);

                    SendRtpRaw(fuHeader.Slice(0, fuHeaderLength), fragmented.Slice(offset, payloadLength), rtpTimestamp ?? LocalTrack.Timestamp, markerBit, payloadTypeID);
                    //logger.LogDebug($"send H264 {videoChannel.RTPLocalEndPoint}->{dstEndPoint} timestamp {videoTrack.Timestamp}, FU-A {h264RtpHdr.HexStr()}, payload length {payloadLength}, seqnum {videoTrack.SeqNum}, marker {markerBit}.");
                }
            }

            if (isLastNal)
            {
                AdvanceTimestamp(rtpTimestamp, duration);
            }
        }

        public void SendH265Frame(uint durationRtpUnits, int payloadID, byte[] sample)
        {
            SendH265FrameCore(null, durationRtpUnits, payloadID, sample);
        }

        private void SendH265FrameCore(uint? rtpTimestamp, uint durationRtpUnits, int payloadID, byte[] sample)
        {
            ThrowIfNullFrame(sample, nameof(sample));
            SendH265FrameCore(rtpTimestamp, durationRtpUnits, payloadID, new ReadOnlySpan<byte>(sample));
        }

        private void SendH265FrameCore(uint? rtpTimestamp, uint durationRtpUnits, int payloadID, ReadOnlySpan<byte> sample)
        {
            if(CheckIfCanSendRtpRaw())
            {
                try
                {
                    // The NALs are found in place, skipping any too short to have a NAL header. Consecutive NALs small
                    // enough to share a packet are gathered into an aggregation packet in a buffer on the stack, the same
                    // way H265Packetiser.CreateAggregated groups them. Every other NAL is sent, or fragmented, straight
                    // from its slice of the access unit.
                    Span<byte> aggregation = stackalloc byte[RTPSession.RTP_MAX_PAYLOAD];
                    int aggregationLength = 0;
                    int groupCount = 0;
                    int groupLength = 0;
                    int firstOffset = 0;
                    int firstLength = 0;

                    var nals = new AnnexBNalScanner(H265Packetiser.H265_NAL_HEADER_LENGTH);

                    while (nals.TryGetNext(sample, out int offset, out int length, out bool isLast))
                    {
                        if (groupCount > 0 && H265Packetiser.FitsInAggregation(groupLength, length, RTPSession.RTP_MAX_PAYLOAD))
                        {
                            if (groupCount == 1)
                            {
                                // The first waiting NAL now has company, so it goes into the aggregation packet too.
                                aggregationLength = H265Packetiser.AppendToAggregation(aggregation, 0, sample.Slice(firstOffset, firstLength));
                            }

                            aggregationLength = H265Packetiser.AppendToAggregation(aggregation, aggregationLength, sample.Slice(offset, length));
                            groupLength = aggregationLength;
                            groupCount++;
                        }
                        else
                        {
                            if (groupCount > 0)
                            {
                                // This NAL does not fit with the waiting ones, so they are complete.
                                SendH265Group(rtpTimestamp, durationRtpUnits, payloadID, sample.Slice(firstOffset, firstLength), aggregation.Slice(0, aggregationLength), groupCount, false);
                                groupCount = 0;
                            }

                            if (H265Packetiser.FitsInAggregation(0, length, RTPSession.RTP_MAX_PAYLOAD))
                            {
                                // Small enough to share a packet with the NALs after it, so it waits for them.
                                groupCount = 1;
                                groupLength = H265Packetiser.AggregatedLength(0, length);
                                aggregationLength = 0;
                                firstOffset = offset;
                                firstLength = length;
                            }
                            else
                            {
                                SendH26XNal(rtpTimestamp, durationRtpUnits, payloadID, sample.Slice(offset, length), isLast, true);
                            }
                        }

                        if (isLast && groupCount > 0)
                        {
                            SendH265Group(rtpTimestamp, durationRtpUnits, payloadID, sample.Slice(firstOffset, firstLength), aggregation.Slice(0, aggregationLength), groupCount, true);
                        }
                    }
                }
                catch (SocketException sockExcp)
                {
                    logger.LogError(sockExcp, "SocketException SendH265Frame.");
                }
            }
        }

        /// <summary>
        /// Sends H265 NALs that were waiting to share a packet: a single one on its own, from its slice of the access
        /// unit, and two or more as the aggregation packet they were written into.
        /// </summary>
        private void SendH265Group(uint? rtpTimestamp, uint durationRtpUnits, int payloadID, ReadOnlySpan<byte> firstNal, Span<byte> aggregation, int count, bool isLast)
        {
            if (count == 1)
            {
                SendH26XNal(rtpTimestamp, durationRtpUnits, payloadID, firstNal, isLast, true);
            }
            else
            {
                H265Packetiser.WriteAggregationHeader(aggregation);
                SendH26XNal(rtpTimestamp, durationRtpUnits, payloadID, aggregation, isLast, true);
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
            SendVp8FrameCore(null, duration, payloadTypeID, buffer);
        }

        private void SendVp8FrameCore(uint? rtpTimestamp, uint duration, int payloadTypeID, byte[] buffer)
        {
            ThrowIfNullFrame(buffer, nameof(buffer));
            SendVp8FrameCore(rtpTimestamp, duration, payloadTypeID, new ReadOnlySpan<byte>(buffer));
        }

        // The one byte VP8 payload descriptor (RFC 7741) on the first packet of a frame, with the S (start of
        // partition) bit set, and on the rest of the packets.
        private static ReadOnlySpan<byte> Vp8FirstPacketDescriptor => new byte[] { 0x10 };
        private static ReadOnlySpan<byte> Vp8ContinuationPacketDescriptor => new byte[] { 0x00 };

        private void SendVp8FrameCore(uint? rtpTimestamp, uint duration, int payloadTypeID, ReadOnlySpan<byte> buffer)
        {
            if (CheckIfCanSendRtpRaw())
            {
                try
                {
                    for (int index = 0; index * RTPSession.RTP_MAX_PAYLOAD < buffer.Length; index++)
                    {
                        int offset = index * RTPSession.RTP_MAX_PAYLOAD;
                        int payloadLength = (offset + RTPSession.RTP_MAX_PAYLOAD < buffer.Length) ? RTPSession.RTP_MAX_PAYLOAD : buffer.Length - offset;

                        ReadOnlySpan<byte> vp8Header = (index == 0) ? Vp8FirstPacketDescriptor : Vp8ContinuationPacketDescriptor;

                        int markerBit = ((offset + payloadLength) >= buffer.Length) ? 1 : 0; // Set marker bit for the last packet in the frame.

                        // Each packet is built straight from its slice of the frame, so the frame is only copied into
                        // the packets themselves.
                        SetRtpHeaderExtensionValue(TransportWideCCExtension.RTP_HEADER_EXTENSION_URI, null);
                        SendRtpRaw(vp8Header, buffer.Slice(offset, payloadLength), rtpTimestamp ?? LocalTrack.Timestamp, markerBit, payloadTypeID);
                    }
                    AdvanceTimestamp(rtpTimestamp, duration);
                }
                catch (SocketException sockExcp)
                {
                    logger.LogError(sockExcp, "SocketException SendVp8Frame.");
                }
            }
        }

        /// <summary>
        /// Sends a VP9 encoded frame as one or more RTP packets.
        /// </summary>
        /// <param name="duration">The duration in timestamp units of the payload. Needs to be based on a 90KHz clock.</param>
        /// <param name="payloadTypeID">The payload ID to place in the RTP header.</param>
        /// <param name="buffer">The VP9 encoded frame.</param>
        public void SendVp9Frame(uint duration, int payloadTypeID, byte[] buffer)
        {
            SendVp9FrameCore(null, duration, payloadTypeID, buffer);
        }

        private void SendVp9FrameCore(uint? rtpTimestamp, uint duration, int payloadTypeID, byte[] buffer)
        {
            if (CheckIfCanSendRtpRaw())
            {
                try
                {
                    bool isKeyFrame = Vp9Packetiser.IsKeyFrame(buffer);
                    var packets = Vp9Packetiser.Packetize(buffer, RTPSession.RTP_MAX_PAYLOAD, isKeyFrame, _vp9PictureId);

                    for (int i = 0; i < packets.Count; i++)
                    {
                        int markerBit = (i == packets.Count - 1) ? 1 : 0; // Marker bit set on the last packet of the frame.

                        SetRtpHeaderExtensionValue(TransportWideCCExtension.RTP_HEADER_EXTENSION_URI, null);
                        SendRtpRaw(packets[i], rtpTimestamp ?? LocalTrack.Timestamp, markerBit, payloadTypeID, true);
                    }

                    if (packets.Count > 0)
                    {
                        AdvanceTimestamp(rtpTimestamp, duration);
                        _vp9PictureId = (_vp9PictureId + 1) & Vp9Packetiser.MAX_PICTURE_ID;
                    }
                }
                catch (SocketException sockExcp)
                {
                    logger.LogError(sockExcp, "SocketException SendVp9Frame.");
                }
            }
        }

        /// <summary>
        /// Sends an AV1 temporal unit as one or more RTP packets.
        /// </summary>
        /// <param name="duration"> The duration in timestamp units of the payload. Needs
        /// to be based on a 90Khz clock.</param>
        /// <param name="payloadTypeID">The payload ID to place in the RTP header.</param>
        /// <param name="temporalUnit">The AV1 encoded temporal unit, represented as a sequence of OBUs.</param>
        public void SendAv1Frame(uint duration, int payloadTypeID, byte[] temporalUnit)
        {
            SendAv1FrameCore(null, duration, payloadTypeID, temporalUnit);
        }

        private void SendAv1FrameCore(uint? rtpTimestamp, uint duration, int payloadTypeID, byte[] temporalUnit)
        {
            if (CheckIfCanSendRtpRaw())
            {
                try
                {
                    bool sentPacket = false;

                    foreach (var packet in AV1Packetiser.Packetize(temporalUnit.AsSpan(), RTPSession.RTP_MAX_PAYLOAD))
                    {
                        int markerBit = packet.IsLast ? 1 : 0;

                        SetRtpHeaderExtensionValue(TransportWideCCExtension.RTP_HEADER_EXTENSION_URI, null);
                        SendRtpRaw(packet.Payload, rtpTimestamp ?? LocalTrack.Timestamp, markerBit, payloadTypeID, true);
                        sentPacket = true;
                    }

                    if (sentPacket)
                    {
                        AdvanceTimestamp(rtpTimestamp, duration);
                    }
                }
                catch (SocketException sockExcp)
                {
                    logger.LogError(sockExcp, "SocketException SendAv1Frame.");
                }
            }
        }

        /// <summary>
        /// Sends a JPEG frame as one or more RTP packets.
        /// </summary>
        /// <param name="durationRtpUnits"> The duration in timestamp units of the payload.</param>
        /// <param name="payloadID">The payload ID to place in the RTP header.</param>
        /// <param name="sample">The JPEG encoded payload.</param>
        public void SendMJPEGFrame(uint durationRtpUnits, int payloadID, byte[] sample)
        {
            SendMJPEGFrameCore(null, durationRtpUnits, payloadID, sample);
        }

        private void SendMJPEGFrameCore(uint? rtpTimestamp, uint duration, int payloadID, byte[] sample)
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
                        SendRtpRaw(payload, rtpTimestamp ?? LocalTrack.Timestamp, 1, payloadID, true);
                    }
                    else
                    {
                        var restBytes = frameData.Data;
                        var offset = 0;
                        while (restBytes.Length > 0)
                        {
                            var dataSize = RTPSession.RTP_MAX_PAYLOAD - rtpHeader.Length;
                            var isLast = dataSize >= restBytes.Length;
                            var data = isLast ? restBytes : restBytes.AsSpan(0, dataSize).ToArray();
                            var markerBit = isLast ? 1 : 0; // Marker bit set on the last packet of the frame.
                            var payload = rtpHeader.Concat(data).ToArray();
                            SendRtpRaw(payload, rtpTimestamp ?? LocalTrack.Timestamp, markerBit, payloadID, true);

                            // The fragment offset is the position of the next packet's data within the JPEG
                            // scan data (RFC 2435), so it advances by the data sent, not including the headers.
                            offset += data.Length;
                            rtpHeader = MJPEGPacketiser.GetMJPEGRTPHeader(customData, offset);
                            restBytes = restBytes.AsSpan(data.Length).ToArray();
                        }
                    }

                    AdvanceTimestamp(rtpTimestamp, duration);
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
        public void SendVideo(uint durationRtpUnits, byte[] sample)
        {
            EnsureSendingFormat();

            int payloadID = sendingFormat.FormatID;

            switch (sendingFormat.Codec)
            {
                case VideoCodecsEnum.VP8:
                    SendVp8Frame(durationRtpUnits, payloadID, sample);
                    break;
                case VideoCodecsEnum.VP9:
                    SendVp9Frame(durationRtpUnits, payloadID, sample);
                    break;
                case VideoCodecsEnum.AV1:
                    SendAv1Frame(durationRtpUnits, payloadID, sample);
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

        /// <summary>
        /// Sends a video sample to the remote peer with an explicit RTP timestamp. Every RTP packet of the sample
        /// carries <paramref name="rtpTimestamp"/>.
        /// </summary>
        /// <param name="rtpTimestamp">The RTP timestamp for the sample, in the sending format's clock rate.</param>
        /// <param name="sample">The video sample to set as the RTP packet payload.</param>
        /// <remarks>
        /// Use this instead of the duration based method when the presentation time of each frame is known but the
        /// time until the next frame is not (e.g. live or relayed sources). Don't mix the two styles on one track: after
        /// this call the track timestamp is <paramref name="rtpTimestamp"/>, so a following duration based send would
        /// reuse it.
        /// </remarks>
        public void SendVideoAt(uint rtpTimestamp, byte[] sample)
        {
            EnsureSendingFormat();

            int payloadID = sendingFormat.FormatID;

            switch (sendingFormat.Codec)
            {
                case VideoCodecsEnum.VP8:
                    SendVp8FrameCore(rtpTimestamp, 0, payloadID, sample);
                    break;
                case VideoCodecsEnum.VP9:
                    SendVp9FrameCore(rtpTimestamp, 0, payloadID, sample);
                    break;
                case VideoCodecsEnum.AV1:
                    SendAv1FrameCore(rtpTimestamp, 0, payloadID, sample);
                    break;
                case VideoCodecsEnum.H264:
                    SendH264FrameCore(rtpTimestamp, 0, payloadID, sample);
                    break;
                case VideoCodecsEnum.H265:
                    SendH265FrameCore(rtpTimestamp, 0, payloadID, sample);
                    break;
                case VideoCodecsEnum.JPEG:
                    SendMJPEGFrameCore(rtpTimestamp, 0, payloadID, sample);
                    break;
                default:
                    throw new ApplicationException($"Unsupported video format selected {sendingFormat.FormatName}.");
            }
        }

        /// <summary>
        /// Sends a video sample to the remote peer, reading it from a span rather than an array.
        /// </summary>
        /// <param name="durationRtpUnits">The duration in RTP timestamp units of the video sample. This
        /// value is added to the previous RTP timestamp when building the RTP header.</param>
        /// <param name="sample">The encoded video frame to packetise, for example one an encoder has written into a
        /// re-used buffer.</param>
        /// <remarks>
        /// <para>
        /// The packets are identical to those <see cref="SendVideo(uint, byte[])"/> sends for the same frame. The sample
        /// is only read during the call: every RTP packet is built in a buffer of its own, which the transport owns once
        /// it is sent, so the memory behind <paramref name="sample"/> can be re-used as soon as this returns.
        /// </para>
        /// <para>
        /// For VP8, H264 and H265 each packet is built directly from its slice of the sample, which is copied once, into
        /// the packet. The exception is H265 NALs small enough to share an aggregation packet, which are gathered in a
        /// buffer on the stack first. For the other codecs the sample is copied into an array and sent as the array
        /// overload sends it.
        /// </para>
        /// </remarks>
        public void SendVideo(uint durationRtpUnits, ReadOnlySpan<byte> sample)
        {
            SendVideoCore(null, durationRtpUnits, sample);
        }

        /// <summary>
        /// Sends a video sample to the remote peer with an explicit RTP timestamp, reading it from a span rather than
        /// an array. Every RTP packet of the sample carries <paramref name="rtpTimestamp"/>.
        /// </summary>
        /// <param name="rtpTimestamp">The RTP timestamp for the sample, in the sending format's clock rate.</param>
        /// <param name="sample">The encoded video frame to packetise. Only read during the call.</param>
        /// <remarks>See <see cref="SendVideoAt(uint, byte[])"/> and <see cref="SendVideo(uint, ReadOnlySpan{byte})"/>.</remarks>
        public void SendVideoAt(uint rtpTimestamp, ReadOnlySpan<byte> sample)
        {
            SendVideoCore(rtpTimestamp, 0, sample);
        }

        private void SendVideoCore(uint? rtpTimestamp, uint durationRtpUnits, ReadOnlySpan<byte> sample)
        {
            EnsureSendingFormat();

            if (sendingFormat.Codec == VideoCodecsEnum.VP8)
            {
                SendVp8FrameCore(rtpTimestamp, durationRtpUnits, sendingFormat.FormatID, sample);
            }
            else if (sendingFormat.Codec == VideoCodecsEnum.H264)
            {
                SendH264FrameCore(rtpTimestamp, durationRtpUnits, sendingFormat.FormatID, sample);
            }
            else if (sendingFormat.Codec == VideoCodecsEnum.H265)
            {
                SendH265FrameCore(rtpTimestamp, durationRtpUnits, sendingFormat.FormatID, sample);
            }
            else if (rtpTimestamp.HasValue)
            {
                // The other codecs' packetisers work on arrays.
                SendVideoAt(rtpTimestamp.Value, sample.ToArray());
            }
            else
            {
                SendVideo(durationRtpUnits, sample.ToArray());
            }
        }

        private void EnsureSendingFormat()
        {
            if (!sendingFormatFound)
            {
                sendingFormat = GetSendingFormat().ToVideoFormat();
                sendingFormatFound = true;
            }
        }

        internal void ResetSendingFormat()
        {
            sendingFormat = VideoFormat.Empty;
            sendingFormatFound = false;
        }

        /// <summary>
        /// Moves the track timestamp on after a frame: by the frame's duration for the duration based methods (as
        /// they always have), or to the explicit timestamp the frame was sent with.
        /// </summary>
        private void AdvanceTimestamp(uint? rtpTimestamp, uint duration)
        {
            if (rtpTimestamp.HasValue)
            {
                LocalTrack.Timestamp = rtpTimestamp.Value;
            }
            else
            {
                LocalTrack.Timestamp += duration;
            }
        }

        protected override void ProcessRtpPacket(IPEndPoint remoteEndPoint, RTPPacket rtpPacket, SDPAudioVideoMediaFormat format)
        {
            ProcessVideoRtpFrame(remoteEndPoint, rtpPacket, format);
            RaiseOnRtpPacketReceivedByIndex(remoteEndPoint, rtpPacket);
        }

        public void ProcessVideoRtpFrame(IPEndPoint endpoint, RTPPacket packet, SDPAudioVideoMediaFormat format)
            => ProcessVideoRtpFrame(endpoint, packet, format, packet.Header.SequenceNumber);

        protected override void ResetFecFraming() => RtpVideoFramer = null;

        protected override Action PrepareFecRtpPacket(IPEndPoint remoteEndPoint, RTPPacket packet,
            SDPAudioVideoMediaFormat format, ushort framingSequence)
        {
            var frame = OnVideoFrameReceivedByIndex == null ? null : GetVideoRtpFrame(packet, format, framingSequence);
            return () =>
            {
                if (frame != null)
                {
                    OnVideoFrameReceivedByIndex?.Invoke(Index, remoteEndPoint, packet.Header.Timestamp, frame, format.ToVideoFormat());
                }
                RaiseOnRtpPacketReceivedByIndex(remoteEndPoint, packet);
            };
        }

        private void ProcessVideoRtpFrame(IPEndPoint endpoint, RTPPacket packet,
            SDPAudioVideoMediaFormat format, ushort framingSequence)
        {
            if (OnVideoFrameReceivedByIndex == null) return;
            var frame = GetVideoRtpFrame(packet, format, framingSequence);
            if (frame != null)
            {
                OnVideoFrameReceivedByIndex?.Invoke(Index, endpoint, packet.Header.Timestamp, frame, format.ToVideoFormat());
            }
        }

        private byte[] GetVideoRtpFrame(RTPPacket packet, SDPAudioVideoMediaFormat format, ushort framingSequence)
        {
            if (RtpVideoFramer == null)
            {
                var codec = format.ToVideoFormat().Codec;
                if (codec != VideoCodecsEnum.VP8 && codec != VideoCodecsEnum.VP9 &&
                    codec != VideoCodecsEnum.AV1 && codec != VideoCodecsEnum.H264 &&
                    codec != VideoCodecsEnum.H265 && codec != VideoCodecsEnum.JPEG)
                {
                    logger.LogWarning("Video depacketisation logic for codec {CodecName} has not been implemented, PR's welcome!", format.Name());
                    return null;
                }
                logger.LogDebug("Video depacketisation codec set to {Codec} for SSRC {SSRC}.", codec, packet.Header.SyncSource);
                RtpVideoFramer = new RtpVideoFramer(codec, MaxReconstructedVideoFrameSize);
            }
            return RtpVideoFramer.GotRtpPacket(packet, framingSequence);
        }

        public void CheckVideoFormatsNegotiation()
        {
            if (LocalTrack != null && LocalTrack.Capabilities?.Count() > 0)
            {
                OnVideoFormatsNegotiatedByIndex?.Invoke(
                            Index,
                            LocalTrack.Capabilities
                            .Where(x => x.IsVideoMediaCodec)
                            .Select(x => x.ToVideoFormat()).ToList());
            }
        }
    }
}
