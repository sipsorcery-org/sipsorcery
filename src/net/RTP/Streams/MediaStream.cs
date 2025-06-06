﻿//-----------------------------------------------------------------------------
// Filename: MediaStream.cs
//
// Description: Define a Media Stream to centralize all related objects: local/remote tracks, rtcp session, ip end point
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
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class MediaStream
    {
        protected internal class PendingPackages
        {
            public RTPHeader hdr;
            public int localPort;
            public IPEndPoint remoteEndPoint;
            public byte[] buffer;

            public PendingPackages() { }

            public PendingPackages(RTPHeader hdr, int localPort, IPEndPoint remoteEndPoint, byte[] buffer)
            {
                this.hdr = hdr;
                this.localPort = localPort;
                this.remoteEndPoint = remoteEndPoint;
                this.buffer = buffer;
            }
        }

        protected object _pendingPackagesLock = new object();
        protected List<PendingPackages> _pendingPackagesBuffer = new List<PendingPackages>();

        private static ILogger logger = Log.Logger;

        private RtpSessionConfig RtpSessionConfig;

        protected SecureContext SecureContext;
        protected SrtpHandler SrtpHandler;

        private RTPReorderBuffer RTPReorderBuffer = null;

        MediaStreamTrack m_localTrack;
        MediaStreamTrack m_remoteTrack;

        protected RTPChannel rtpChannel = null;

        protected bool _isClosed = false;
        /// <summary>
        /// Used for keeping track of TWCC packets
        /// </summary>
        private ushort _twccPacketCount = 0;

        public int Index = -1;

        /// <summary>
        /// Fires when the connection for a media type is classified as timed out due to not
        /// receiving any RTP or RTCP packets within the given period.
        /// </summary>
        public event Action<int, SDPMediaTypesEnum> OnTimeoutByIndex;

        /// <summary>
        /// Gets fired when an RTCP report is sent. This event is for diagnostics only.
        /// </summary>
        public event Action<int, SDPMediaTypesEnum, RTCPCompoundPacket> OnSendReportByIndex;

        /// <summary>
        /// Gets fired when an RTP packet is received from a remote party.
        /// Parameters are:
        ///  - index of the AudioStream or VideoStream
        ///  - Remote endpoint packet was received from,
        ///  - The media type the packet contains, will be audio or video,
        ///  - The full RTP packet.
        /// </summary>
        public event Action<int, IPEndPoint, SDPMediaTypesEnum, RTPPacket> OnRtpPacketReceivedByIndex;

        /// <summary>
        /// Gets fired when an RTP Header packet is received from a remote party.
        /// Parameters are:
        ///  - index of the AudioStream or VideoStream
        ///  - Remote endpoint packet was received from,
        ///  - The media type the packet contains, will be audio or video,
        ///  - The RTP Header exension URI.
        /// </summary>
        public event Action<int, IPEndPoint, SDPMediaTypesEnum, string, object> OnRtpHeaderReceivedByIndex;

        /// <summary>
        /// Gets fired when an RTP event is detected on the remote call party's RTP stream.
        /// </summary>
        public event Action<int, IPEndPoint, RTPEvent, RTPHeader> OnRtpEventByIndex;

        /// <summary>
        /// Gets fired when an RTCP report is received. This event is for diagnostics only.
        /// </summary>
        public event Action<int, IPEndPoint, SDPMediaTypesEnum, RTCPCompoundPacket> OnReceiveReportByIndex;

        public event Action<bool> OnIsClosedStateChanged;

        public bool AcceptRtpFromAny { get; set; } = false;

        /// <summary>
        /// Indicates whether the session has been closed. Once a session is closed it cannot
        /// be restarted.
        /// </summary>
        public bool IsClosed
        {
            get
            {
                return _isClosed;
            }
            set
            {
                if (_isClosed == value)
                {
                    return;
                }
                _isClosed = value;

                //Clear previous buffer
                ClearPendingPackages();

                OnIsClosedStateChanged?.Invoke(_isClosed);
            }
        }

        /// <summary>
        /// In order to detect RTP events from the remote party this property needs to 
        /// be negotiated to a common payload ID. RTP events are typically DTMF tones.
        /// </summary>
        public int NegotiatedRtpEventPayloadID { get; set; } = RTPSession.DEFAULT_DTMF_EVENT_PAYLOAD_ID;

        /// <summary>
        /// To type of this media
        /// </summary>
        public SDPMediaTypesEnum MediaType { get; set; }

        /// <summary>
        /// The local track. Will be null if we are not sending this media.
        /// </summary>
        public MediaStreamTrack LocalTrack
        {
            get
            {
                return m_localTrack;
            }
            set
            {
                m_localTrack = value;
                if (m_localTrack != null)
                {
                    // Need to create a sending SSRC and set it on the RTCP session. 
                    if (RtcpSession != null)
                    {
                        RtcpSession.Ssrc = m_localTrack.Ssrc;
                    }

                    if (MediaType == SDPMediaTypesEnum.audio)
                    {
                        if (m_localTrack.Capabilities != null && !m_localTrack.NoDtmfSupport &&
                            !m_localTrack.Capabilities.Any(x => x.ID == RTPSession.DEFAULT_DTMF_EVENT_PAYLOAD_ID))
                        {
                            m_localTrack.Capabilities.Add(DefaultRTPEventFormat);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The remote track. Will be null if the remote party is not sending this media
        /// </summary>
        public MediaStreamTrack RemoteTrack
        {
            get
            {
                return m_remoteTrack;
            }
            set
            {
                m_remoteTrack = value;
            }
        }

        /// <summary>
        /// The reporting session for this media stream.
        /// </summary>
        public RTCPSession RtcpSession { get; set; }

        /// <summary>
        /// The remote RTP end point this stream is sending media to.
        /// </summary>
        public IPEndPoint DestinationEndPoint { get; set; }

        /// <summary>
        /// The remote RTP control end point this stream is sending to RTCP reports for the media stream to.
        /// </summary>
        public IPEndPoint ControlDestinationEndPoint { get; set; }

        /// <summary>
        /// Default RTP event format that we support.
        /// </summary>
        public static SDPAudioVideoMediaFormat DefaultRTPEventFormat
        {
            get
            {
                return new SDPAudioVideoMediaFormat(
                                SDPMediaTypesEnum.audio,
                                RTPSession.DEFAULT_DTMF_EVENT_PAYLOAD_ID,
                                SDP.TELEPHONE_EVENT_ATTRIBUTE,
                                RTPSession.DEFAULT_AUDIO_CLOCK_RATE,
                                SDPAudioVideoMediaFormat.DEFAULT_AUDIO_CHANNEL_COUNT,
                                "0-16");
            }
        }

        public MediaStream(RtpSessionConfig config, int index)
        {
            RtpSessionConfig = config;
            this.Index = index;
        }

        public void AddBuffer(TimeSpan dropPacketTimeout)
        {
            RTPReorderBuffer = new RTPReorderBuffer(dropPacketTimeout);
        }

        public void RemoveBuffer(TimeSpan dropPacketTimeout)
        {
            RTPReorderBuffer = null;
        }

        public bool UseBuffer()
        {
            return RTPReorderBuffer != null;
        }

        public RTPReorderBuffer GetBuffer()
        {
            return RTPReorderBuffer;
        }

        public void SetSecurityContext(ProtectRtpPacket protectRtp, ProtectRtpPacket unprotectRtp, ProtectRtpPacket protectRtcp, ProtectRtpPacket unprotectRtcp)
        {
            if (SecureContext != null)
            {
                logger.LogTrace("Tried adding new SecureContext for media type {MediaType}, but one already existed", MediaType);
            }

            SecureContext = new SecureContext(protectRtp, unprotectRtp, protectRtcp, unprotectRtcp);

            DispatchPendingPackages();
        }

        public SecureContext GetSecurityContext()
        {
            return SecureContext;
        }

        public bool IsSecurityContextReady()
        {
            return SecureContext != null;
        }

        private (bool, byte[]) UnprotectBuffer(byte[] buffer)
        {
            if (SecureContext != null)
            {
                int res = SecureContext.UnprotectRtpPacket(buffer, buffer.Length, out int outBufLen);

                if (res == 0)
                {
                    return (true, buffer.Take(outBufLen).ToArray());
                }
                else
                {
                    logger.LogWarning("SRTP unprotect failed for {MediaType}, result {Result}.", MediaType, res);
                }
            }
            return (false, buffer);
        }

        public bool EnsureBufferUnprotected(byte[] buf, RTPHeader header, out RTPPacket packet)
        {
            if (RtpSessionConfig.IsSecure || RtpSessionConfig.UseSdpCryptoNegotiation)
            {
                var (succeeded, newBuffer) = UnprotectBuffer(buf);
                if (!succeeded)
                {
                    packet = null;
                    return false;
                }
                packet = new RTPPacket(newBuffer);
            }
            else
            {
                packet = new RTPPacket(buf);
            }
            packet.Header.ReceivedTime = header.ReceivedTime;
            return true;
        }

        public SrtpHandler GetOrCreateSrtpHandler()
        {
            if (SrtpHandler == null)
            {
                SrtpHandler = new SrtpHandler();
            }
            return SrtpHandler;
        }

        public void AddRtpChannel(RTPChannel rtpChannel)
        {
            this.rtpChannel = rtpChannel;
        }

        public bool HasRtpChannel()
        {
            return rtpChannel != null;
        }

        public RTPChannel GetRTPChannel()
        {
            return rtpChannel;
        }

        protected bool CheckIfCanSendRtpRaw()
        {
            if (IsClosed)
            {
                logger.LogWarning("SendRtpRaw was called for a {MediaType} packet on a closed RTP session.", MediaType);
                return false;
            }

            if (LocalTrack == null)
            {
                logger.LogWarning("SendRtpRaw was called for a {MediaType} packet on an RTP session without a local track.", MediaType);
                return false;
            }

            if ((LocalTrack.StreamStatus == MediaStreamStatusEnum.RecvOnly) || (LocalTrack.StreamStatus == MediaStreamStatusEnum.Inactive))
            {
                logger.LogWarning("SendRtpRaw was called for a {MediaType} packet on an RTP session with a Stream Status set to {StreamStatus}", MediaType, LocalTrack.StreamStatus);
                return false;
            }

            if ((RtpSessionConfig.IsSecure || RtpSessionConfig.UseSdpCryptoNegotiation) && SecureContext?.ProtectRtpPacket == null)
            {
                logger.LogWarning("SendRtpPacket cannot be called on a secure session before calling SetSecurityContext.");
                return false;
            }

            return true;
        }

        private static byte[] Combine(params byte[][] arrays)
        {
            byte[] rv = new byte[arrays.Sum(a => a.Length)];
            int offset = 0;
            foreach (byte[] array in arrays)
            {
                System.Buffer.BlockCopy(array, 0, rv, offset, array.Length);
                offset += array.Length;
            }
            return rv;
        }

        protected void SendRtpRaw(ArraySegment<byte> data, uint timestamp, int markerBit, int payloadType, Boolean checkDone, ushort? seqNum = null)
        {
            if (checkDone || CheckIfCanSendRtpRaw())
            {
                ProtectRtpPacket protectRtpPacket = SecureContext?.ProtectRtpPacket;
                int srtpProtectionLength = (protectRtpPacket != null) ? RTPSession.SRTP_MAX_PREFIX_LENGTH : 0;

                RTPPacket rtpPacket = new RTPPacket(data, srtpProtectionLength);

                rtpPacket.Header.SyncSource = LocalTrack.Ssrc;
                rtpPacket.Header.SequenceNumber = seqNum ?? LocalTrack.GetNextSeqNum();
                rtpPacket.Header.Timestamp = timestamp;
                rtpPacket.Header.MarkerBit = markerBit;
                rtpPacket.Header.PayloadType = payloadType;

                /*  https://datatracker.ietf.org/doc/html/rfc5285#section-4.2
                    
                    An example header extension, with three extension elements, some
                    padding, and including the required RTP fields, follows:

                    0                   1                   2                   3
                    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
                    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                    |       0xBE    |    0xDE       |           length=3            |
                    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                    |  ID   | L=0   |     data      |  ID   |  L=1  |   data...     |
                    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                    |     ...data   |    0 (pad)    |    0 (pad)    |  ID   | L=3   |
                    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                    |                          data                                 |
                    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                */
                if (LocalTrack?.HeaderExtensions?.Values.Count > 0)
                {
                    byte[] payload = null;
                    foreach (var ext in LocalTrack.HeaderExtensions.Values)
                    {
                        // We support up to 14 extensions .... Not clear at all how to manage more ...
                        if ((ext.Id < 1) || (ext.Id > 14))
                        {
                            continue;
                        }

                        // Get extension payload and combine it to global payload
                        var extPayLoad = ext.Marshal();
                        if (payload == null)
                        {
                            payload = extPayLoad;
                        }
                        else
                        {
                            payload = Combine(payload, extPayLoad);
                        }
                    }

                    if (payload?.Length > 0)
                    {
                        // Need to round to 4 bytes boundaries
                        var roundedExtSize = payload.Length % 4;
                        if (roundedExtSize > 0)
                        {
                            var padding = Enumerable.Repeat((byte)0, 4 - roundedExtSize).ToArray();
                            payload = Combine(payload, padding);
                        }

                        rtpPacket.Header.HeaderExtensionFlag = 1; // We have at least one extension
                        rtpPacket.Header.ExtensionLength = (ushort)(payload.Length / 4);  // payload length / 4 
                        rtpPacket.Header.ExtensionProfile = RTPHeader.ONE_BYTE_EXTENSION_PROFILE; // We support up to 14 extensions .... Not clear at all how to manage more ...
                        rtpPacket.Header.ExtensionPayload = payload;
                    }
                }
                else
                {
                    rtpPacket.Header.HeaderExtensionFlag = 0;
                }

                var rtpBuffer = rtpPacket.GetBytes();

                if (protectRtpPacket == null)
                {
                    rtpChannel.Send(RTPChannelSocketsEnum.RTP, DestinationEndPoint, rtpBuffer);
                }
                else
                {
                    int rtperr = protectRtpPacket(rtpBuffer, rtpBuffer.Length - srtpProtectionLength, out int outBufLen);
                    if (rtperr != 0)
                    {
                        logger.LogError("SendRTPPacket protection failed, result {RtpError}.", rtperr);
                    }
                    else
                    {
                        rtpChannel.Send(RTPChannelSocketsEnum.RTP, DestinationEndPoint, rtpBuffer.Take(outBufLen).ToArray());
                    }
                }

                RtcpSession?.RecordRtpPacketSend(rtpPacket);
            }
        }

        protected void SendRtpRaw(byte[] data, uint timestamp, int markerBit, int payloadType, Boolean checkDone, ushort? seqNum = null)
        {
            SendRtpRaw(new ArraySegment<byte>(data), timestamp, markerBit, payloadType, checkDone, seqNum);
        }

        /// <summary>
        /// To set a new value to a RTP Header extension.
        /// 
        /// According the extension the Object expected as value is different - check on each extension
        /// </summary>
        /// <param name="uri">The URI of the extension to use</param>
        /// <param name="value">Object to set on the extension (check extension to know object type) </param>
        public void SetRtpHeaderExtensionValue(String uri, Object value)
        {
            try
            {
                var ext = LocalTrack?.HeaderExtensions?.Values?.FirstOrDefault(ext => ext.Uri == uri);
                if (ext != null)
                {
                    switch (uri)
                    {
                        case CVOExtension.RTP_HEADER_EXTENSION_URI:
                            if (ext is CVOExtension cvoExtension)
                            {
                                cvoExtension.Set(value);
                            }
                            break;

                        case AudioLevelExtension.RTP_HEADER_EXTENSION_URI:
                            if (ext is AudioLevelExtension audioLevelExtension)
                            {
                                audioLevelExtension.Set(value);
                            }
                            break;

                        case TransportWideCCExtension.RTP_HEADER_EXTENSION_URI:
                            //case TransportWideCCExtension.RTP_HEADER_EXTENSION_URI_ALT:
                            if (ext is TransportWideCCExtension transportWideCCExtension)
                            {
                                transportWideCCExtension.Set(_twccPacketCount++);
                            }
                            break;

                        // Not necessary to set something in AbsSendTimeExtension - just to be coherent here
                        case AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI:
                            if (ext is AbsSendTimeExtension absSendTimeExtension)
                            {
                                absSendTimeExtension.Set(value);
                            }
                            break;
                    }
                }
            }
            catch
            {

            }
        }

        /// <summary>
        /// Allows additional control for sending raw RTP payloads. No framing or other processing is carried out.
        /// </summary>
        /// <param name="data">The RTP packet payload.</param>
        /// <param name="timestamp">The timestamp to set on the RTP header.</param>
        /// <param name="markerBit">The value to set on the RTP header marker bit, should be 0 or 1.</param>
        /// <param name="payloadType">The payload ID to set in the RTP header.</param>
        /// <param name="seqNum"> The RTP sequence number </param>
        public void SendRtpRaw(byte[] data, uint timestamp, int markerBit, int payloadType, ushort seqNum)
        {
            SendRtpRaw(data, timestamp, markerBit, payloadType, false, seqNum);
        }

        /// <summary>
        /// Allows additional control for sending raw RTP payloads. No framing or other processing is carried out.
        /// </summary>
        /// <param name="data">The RTP packet payload.</param>
        /// <param name="timestamp">The timestamp to set on the RTP header.</param>
        /// <param name="markerBit">The value to set on the RTP header marker bit, should be 0 or 1.</param>
        /// <param name="payloadType">The payload ID to set in the RTP header.</param>
        public void SendRtpRaw(byte[] data, uint timestamp, int markerBit, int payloadType)
        {
            SendRtpRaw(data, timestamp, markerBit, payloadType, false);
        }

        /// <summary>
        /// Allows additional control for sending raw RTCP payloads
        /// </summary>
        /// <param name="rtcpBytes">Raw RTCP report data to send.</param>
        public void SendRtcpRaw(byte[] rtcpBytes)
        {
            if (SendRtcpReport(rtcpBytes))
            {
                RTCPCompoundPacket rtcpCompoundPacket = null;
                try
                {
                    rtcpCompoundPacket = new RTCPCompoundPacket(rtcpBytes);
                }
                catch (Exception excp)
                {
                    logger.LogWarning("Can't create RTCPCompoundPacket from the provided RTCP bytes. {Message}", excp.Message);
                }

                if (rtcpCompoundPacket != null)
                {
                    OnSendReportByIndex?.Invoke(Index, MediaType, rtcpCompoundPacket);
                }
            }
        }

        /// <summary>
        /// Sends the RTCP report to the remote call party.
        /// </summary>
        /// <param name="reportBuffer">The serialised RTCP report to send.</param>
        /// <returns>True if report was sent</returns>
        private bool SendRtcpReport(byte[] reportBuffer)
        {
            if ((RtpSessionConfig.IsSecure || RtpSessionConfig.UseSdpCryptoNegotiation) && !IsSecurityContextReady())
            {
                logger.LogWarning("SendRtcpReport cannot be called on a secure session before calling SetSecurityContext.");
                return false;
            }
            else if (ControlDestinationEndPoint != null)
            {
                //logger.LogDebug("SendRtcpReport: {ReportBytes}", reportBytes.HexStr());

                var sendOnSocket = RtpSessionConfig.IsRtcpMultiplexed ? RTPChannelSocketsEnum.RTP : RTPChannelSocketsEnum.Control;

                var protectRtcpPacket = SecureContext?.ProtectRtcpPacket;

                if (protectRtcpPacket == null)
                {
                    rtpChannel.Send(sendOnSocket, ControlDestinationEndPoint, reportBuffer);
                }
                else
                {
                    byte[] sendBuffer = new byte[reportBuffer.Length + RTPSession.SRTP_MAX_PREFIX_LENGTH];
                    Buffer.BlockCopy(reportBuffer, 0, sendBuffer, 0, reportBuffer.Length);

                    int rtperr = protectRtcpPacket(sendBuffer, sendBuffer.Length - RTPSession.SRTP_MAX_PREFIX_LENGTH, out int outBufLen);
                    if (rtperr != 0)
                    {
                        logger.LogWarning("SRTP RTCP packet protection failed, result {RtpError}.", rtperr);
                    }
                    else
                    {
                        rtpChannel.Send(sendOnSocket, ControlDestinationEndPoint, sendBuffer.Take(outBufLen).ToArray());
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Sends the RTCP report to the remote call party.
        /// </summary>
        /// <param name="report">RTCP report to send.</param>
        public void SendRtcpReport(RTCPCompoundPacket report)
        {
            if ((RtpSessionConfig.IsSecure || RtpSessionConfig.UseSdpCryptoNegotiation) && !IsSecurityContextReady() && report.Bye != null)
            {
                // Do nothing. The RTCP BYE gets generated when an RTP session is closed.
                // If that occurs before the connection was able to set up the secure context
                // there's no point trying to send it.
            }
            else
            {
                var reportBytes = report.GetBytes();
                SendRtcpReport(reportBytes);
                OnSendReportByIndex?.Invoke(Index, MediaType, report);
            }
        }

        /// <summary>
        /// Allows sending of RTCP feedback reports.
        /// </summary>
        /// <param name="feedback">The feedback report to send.</param>
        public void SendRtcpFeedback(RTCPFeedback feedback)
        {
            var reportBytes = feedback.GetBytes();
            SendRtcpReport(reportBytes);
        }

        /// <summary>
        /// Allows sending of RTCP TWCC feedback reports.
        /// </summary>
        /// <param name="feedback">The feedback report to send.</param>
        public void SendRtcpTWCCFeedback(RTCPTWCCFeedback feedback)
        {
            var reportBytes = feedback.GetBytes();
            SendRtcpReport(reportBytes);
        }

        public void OnReceiveRTPPacket(RTPHeader hdr, int localPort, IPEndPoint remoteEndPoint, byte[] buffer)
        {
            RTPPacket rtpPacket;
            if (NegotiatedRtpEventPayloadID != 0 && hdr.PayloadType == NegotiatedRtpEventPayloadID)
            {
                if (!EnsureBufferUnprotected(buffer, hdr, out rtpPacket))
                {
                    // Cache pending packages to use it later to prevent missing frames
                    // when DTLS was not completed yet as a Server bt already completed as a client
                    AddPendingPackage(hdr, localPort, remoteEndPoint, buffer);
                    return;
                }

                RaiseOnRtpEventByIndex(remoteEndPoint, new RTPEvent(rtpPacket.GetPayloadBytes()), rtpPacket.Header);
                return;
            }

            // Set the remote track SSRC so that RTCP reports can match the media type.
            if (RemoteTrack != null && RemoteTrack.Ssrc == 0 && DestinationEndPoint != null)
            {
                bool isValidSource = AdjustRemoteEndPoint(hdr.SyncSource, remoteEndPoint);

                if (isValidSource)
                {
                    logger.LogDebug("Set remote track ({MediaType} - index={Index}) SSRC to {SyncSource} remote RTP endpoint {rtpep}.", MediaType, Index, hdr.SyncSource, remoteEndPoint);
                    RemoteTrack.Ssrc = hdr.SyncSource;
                }
            }

            if (RemoteTrack != null)
            {
                LogIfWrongSeqNumber($"{MediaType}", hdr, RemoteTrack);
                ProcessHeaderExtensions(hdr, remoteEndPoint);
            }

            if (!EnsureBufferUnprotected(buffer, hdr, out rtpPacket))
            {
                return;
            }

            var format = LocalTrack?.GetFormatForPayloadID(hdr.PayloadType);

            if (rtpPacket != null && format != null)
            {
                if (UseBuffer())
                {
                    var reorderBuffer = GetBuffer();
                    reorderBuffer.Add(rtpPacket);
                    while (reorderBuffer.Get(out var bufferedPacket))
                    {
                        if (RemoteTrack != null)
                        {
                            LogIfWrongSeqNumber($"{MediaType}", bufferedPacket.Header, RemoteTrack);
                            RemoteTrack.LastRemoteSeqNum = bufferedPacket.Header.SequenceNumber;
                        }
                        ProcessRtpPacket(remoteEndPoint, bufferedPacket, format.Value);
                    }
                }
                else
                {
                    ProcessRtpPacket(remoteEndPoint, rtpPacket, format.Value);
                }

                RtcpSession?.RecordRtpPacketReceived(rtpPacket);
            }
        }

        /// <summary>
        /// Do any additional processing for the RTP packet. For vidoe streams this method will be overridden to handle video packetisation.
        /// Audio and other media types typially don't use framing but have other processing they'd like to do.
        /// </summary>
        /// <param name="remoteEndPoint">The remote peer the RTP pakcet was received from.</param>
        /// <param name="rtpPacket">The RTP apcet received.</param>
        /// <param name="format">The SDP format for the payload ID in the RTP header.</param>
        protected virtual void ProcessRtpPacket(IPEndPoint remoteEndPoint, RTPPacket rtpPacket, SDPAudioVideoMediaFormat format)
        {
            // If not overridden the default behaviour is to raise an event to inform the owner of the RTP transport
            // that a new RTP packet has been received.
            RaiseOnRtpPacketReceivedByIndex(remoteEndPoint, rtpPacket);
        }

        public void RaiseOnReceiveReportByIndex(IPEndPoint ipEndPoint, RTCPCompoundPacket rtcpPCompoundPacket)
        {
            OnReceiveReportByIndex?.Invoke(Index, ipEndPoint, MediaType, rtcpPCompoundPacket);
        }

        protected void RaiseOnRtpEventByIndex(IPEndPoint ipEndPoint, RTPEvent rtpEvent, RTPHeader rtpHeader)
        {
            OnRtpEventByIndex?.Invoke(Index, ipEndPoint, rtpEvent, rtpHeader);
        }

        protected void RaiseOnRtpPacketReceivedByIndex(IPEndPoint ipEndPoint, RTPPacket rtpPacket)
        {
            OnRtpPacketReceivedByIndex?.Invoke(Index, ipEndPoint, MediaType, rtpPacket);
        }

        private void RaiseOnTimeoutByIndex(SDPMediaTypesEnum mediaType)
        {
            OnTimeoutByIndex?.Invoke(Index, mediaType);
        }

        // Submit all previous cached packages to self
        protected virtual void DispatchPendingPackages()
        {
            PendingPackages[] pendingPackagesArray = null;

            var isContextValid = SecureContext != null && !IsClosed;

            lock (_pendingPackagesLock)
            {
                if (isContextValid)
                {
                    pendingPackagesArray = _pendingPackagesBuffer.ToArray();
                }
                _pendingPackagesBuffer.Clear();
            }
            if (isContextValid)
            {
                foreach (var pendingPackage in pendingPackagesArray)
                {
                    if (pendingPackage != null)
                    {
                        OnReceiveRTPPacket(pendingPackage.hdr, pendingPackage.localPort, pendingPackage.remoteEndPoint, pendingPackage.buffer);
                    }
                }
            }
        }

        // Clear previous buffer
        protected virtual void ClearPendingPackages()
        {
            lock (_pendingPackagesLock)
            {
                _pendingPackagesBuffer.Clear();
            }
        }

        // Cache pending packages to use it later to prevent missing frames
        // when DTLS was not completed yet as a Server but already completed as a client
        protected virtual bool AddPendingPackage(RTPHeader hdr, int localPort, IPEndPoint remoteEndPoint, byte[] buffer)
        {
            const int MAX_PENDING_PACKAGES_BUFFER_SIZE = 32;

            if (SecureContext == null && !IsClosed)
            {
                lock (_pendingPackagesLock)
                {
                    //ensure buffer max size
                    while (_pendingPackagesBuffer.Count > 0 && _pendingPackagesBuffer.Count >= MAX_PENDING_PACKAGES_BUFFER_SIZE)
                    {
                        _pendingPackagesBuffer.RemoveAt(0);
                    }
                    _pendingPackagesBuffer.Add(new PendingPackages(hdr, localPort, remoteEndPoint, buffer));
                }
                return true;
            }
            return false;
        }

        protected void LogIfWrongSeqNumber(string trackType, RTPHeader header, MediaStreamTrack track)
        {
            if (track.LastRemoteSeqNum != 0 &&
                header.SequenceNumber != (track.LastRemoteSeqNum + 1) &&
                !(header.SequenceNumber == 0 && track.LastRemoteSeqNum == ushort.MaxValue))
            {
                logger.LogWarning("{TrackType} stream sequence number jumped from {LastRemoteSeqNum} to {SequenceNumber}.", trackType, track.LastRemoteSeqNum, header.SequenceNumber);
            }
        }

        /// <summary>
        /// Adjusts the expected remote end point for a particular media type.
        /// </summary>
        /// <param name="ssrc">The SSRC from the RTP packet header.</param>
        /// <param name="receivedOnEndPoint">The actual remote end point that the RTP packet came from.</param>
        /// <returns>True if remote end point for this media type was the expected one or it was adjusted. False if
        /// the remote end point was deemed to be invalid for this media type.</returns>
        protected bool AdjustRemoteEndPoint(uint ssrc, IPEndPoint receivedOnEndPoint)
        {
            bool isValidSource = false;
            IPEndPoint expectedEndPoint = DestinationEndPoint;

            if (expectedEndPoint.Address.Equals(receivedOnEndPoint.Address) && expectedEndPoint.Port == receivedOnEndPoint.Port)
            {
                // Exact match on actual and expected destination.
                isValidSource = true;
            }
            else if (AcceptRtpFromAny || (expectedEndPoint.Address.IsPrivate() && !receivedOnEndPoint.Address.IsPrivate())
                //|| (IPAddress.Loopback.Equals(receivedOnEndPoint.Address) || IPAddress.IPv6Loopback.Equals(receivedOnEndPoint.Address
                )
            {
                // The end point doesn't match BUT we were supplied a private address in the SDP and the remote source is a public address
                // so high probability there's a NAT on the network path. Switch to the remote end point (note this can only happen once
                // and only if the SSRV is 0, i.e. this is the first RTP packet.
                // If the remote end point is a loopback address then it's likely that this is a test/development 
                // scenario and the source can be trusted.
                // AC 12 Jul 2020: Commented out the expression that allows the end point to be change just because it's a loopback address.
                // A breaking case is doing an attended transfer test where two different agents are using loopback addresses. 
                // The expression allows an older session to override the destination set by a newer remote SDP.
                // AC 18 Aug 2020: Despite the carefully crafted rules below and https://github.com/sipsorcery/sipsorcery/issues/197
                // there are still cases that were a problem in one scenario but acceptable in another. To accommodate a new property
                // was added to allow the application to decide whether the RTP end point switches should be liberal or not.
                logger.LogDebug("{MediaType} end point switched for RTP ssrc {Ssrc} from {ExpectedEndPoint} to {ReceivedOnEndPoint}.", MediaType, ssrc, expectedEndPoint, receivedOnEndPoint);

                DestinationEndPoint = receivedOnEndPoint;
                if (RtpSessionConfig.IsRtcpMultiplexed)
                {
                    ControlDestinationEndPoint = DestinationEndPoint;
                }
                else
                {
                    ControlDestinationEndPoint = new IPEndPoint(DestinationEndPoint.Address, DestinationEndPoint.Port + 1);
                }

                isValidSource = true;
            }
            else
            {
                logger.LogWarning("RTP packet with SSRC {Ssrc} received from unrecognised end point {ReceivedOnEndPoint}.", ssrc, receivedOnEndPoint);
            }

            return isValidSource;
        }

        /// <summary>
        /// Creates a new RTCP session for a media track belonging to this RTP session.
        /// </summary>
        /// <returns>A new RTCPSession object. The RTCPSession must have its Start method called
        /// in order to commence sending RTCP reports.</returns>
        public bool CreateRtcpSession()
        {
            if (RtcpSession == null)
            {
                RtcpSession = new RTCPSession(MediaType, 0);
                RtcpSession.OnTimeout += RaiseOnTimeoutByIndex;

                return true;
            }
            return false;
        }

        /// <summary>
        /// Sets the remote end points for a media type supported by this RTP session.
        /// </summary>
        /// <param name="rtpEndPoint">The remote end point for RTP packets corresponding to the media type.</param>
        /// <param name="rtcpEndPoint">The remote end point for RTCP packets corresponding to the media type.</param>
        public void SetDestination(IPEndPoint rtpEndPoint, IPEndPoint rtcpEndPoint)
        {
            DestinationEndPoint = rtpEndPoint;
            ControlDestinationEndPoint = rtcpEndPoint;
        }

        /// <summary>
        /// Attempts to get the highest priority sending format for the remote call party.
        /// </summary>
        /// <returns>The first compatible media format found for the specified media type.</returns>
        public SDPAudioVideoMediaFormat GetSendingFormat()
        {
            if (LocalTrack != null || RemoteTrack != null)
            {
                if (LocalTrack == null)
                {
                    return RemoteTrack.Capabilities.First();
                }
                else if (RemoteTrack == null)
                {
                    return LocalTrack.Capabilities.First();
                }

                SDPAudioVideoMediaFormat format;
                if (MediaType == SDPMediaTypesEnum.audio)
                {
                    format = SDPAudioVideoMediaFormat.GetCompatibleFormats(RemoteTrack.Capabilities, LocalTrack.Capabilities)
                        .Where(x => x.ID != NegotiatedRtpEventPayloadID).FirstOrDefault();
                }
                else
                {
                    format = SDPAudioVideoMediaFormat.GetCompatibleFormats(RemoteTrack.Capabilities, LocalTrack.Capabilities).First();
                }

                if (format.IsEmpty())
                {
                    // It's not expected that this occurs as a compatibility check is done when the remote session description
                    // is set. By this point a compatible codec should be available.
                    throw new ApplicationException($"No compatible sending format could be found for media {MediaType}.");
                }
                else
                {
                    return format;
                }
            }
            else
            {
                throw new ApplicationException($"Cannot get the {MediaType} sending format, missing either local or remote {MediaType} track.");
            }
        }

        public void ProcessHeaderExtensions(RTPHeader header, IPEndPoint remoteEndPoint)
        {
            header.GetHeaderExtensions().ToList().ForEach(rtpHeaderExtensionData =>
            {
                if (RemoteTrack?.HeaderExtensions?.TryGetValue(rtpHeaderExtensionData.Id, out RTPHeaderExtension rtpHeaderExtension) == true)
                {
                    var value = rtpHeaderExtension.Unmarshal(header, rtpHeaderExtensionData.Data);
                    OnRtpHeaderReceivedByIndex?.Invoke(Index, remoteEndPoint, MediaType, rtpHeaderExtension.Uri, value);
                }
            });
        }
    }
}
