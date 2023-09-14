//-----------------------------------------------------------------------------
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
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.net.RTP
{
    public class MediaStream
    {
        protected internal class PendingPackages
        {
            public RTPHeader hdr;
            public int localPort;
            public IPEndPoint remoteEndPoint;
            public byte[] buffer;
            public VideoStream videoStream;

            public PendingPackages() { }

            public PendingPackages(RTPHeader hdr, int localPort, IPEndPoint remoteEndPoint, byte[] buffer, VideoStream videoStream)
            {
                this.hdr = hdr;
                this.localPort = localPort;
                this.remoteEndPoint = remoteEndPoint;
                this.buffer = buffer;
                this.videoStream = videoStream;
            }
        }

        protected object _pendingPackagesLock = new object();
        protected List<PendingPackages> _pendingPackagesBuffer = new List<PendingPackages>();

        private static ILogger logger = Log.Logger;

        private uint m_lastRtpTimestamp;

        private RtpSessionConfig RtpSessionConfig;

        protected SecureContext SecureContext;
        protected SrtpHandler SrtpHandler;

        private RTPReorderBuffer RTPReorderBuffer = null;

        MediaStreamTrack m_localTrack;

        protected RTPChannel rtpChannel = null;

        protected bool _isClosed = false;

        public int Index = -1;

        #region EVENTS

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
        ///  - Remote endpoint packet was received from,
        ///  - The media type the packet contains, will be audio or video,
        ///  - The full RTP packet.
        /// </summary>
        public event Action<int, IPEndPoint, SDPMediaTypesEnum, RTPPacket> OnRtpPacketReceivedByIndex;

        /// <summary>
        /// Gets fired when an RTP event is detected on the remote call party's RTP stream.
        /// </summary>
        public event Action<int, IPEndPoint, RTPEvent, RTPHeader> OnRtpEventByIndex;

        /// <summary>
        /// Gets fired when an RTCP report is received. This event is for diagnostics only.
        /// </summary>
        public event Action<int, IPEndPoint, SDPMediaTypesEnum, RTCPCompoundPacket> OnReceiveReportByIndex;

        public event Action<bool> OnIsClosedStateChanged;

        #endregion EVENTS

        #region PROPERTIES

        public Boolean AcceptRtpFromAny { get; set; } = false;

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
        /// be set to the payload ID they are using.
        /// </summary>
        public int RemoteRtpEventPayloadID { get; set; } = RTPSession.DEFAULT_DTMF_EVENT_PAYLOAD_ID;

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
                            !m_localTrack.Capabilities.Any(x => x.ID == RTPSession.DTMF_EVENT_PAYLOAD_ID))
                        {
                            SDPAudioVideoMediaFormat rtpEventFormat = new SDPAudioVideoMediaFormat(
                                SDPMediaTypesEnum.audio,
                                RTPSession.DTMF_EVENT_PAYLOAD_ID,
                                SDP.TELEPHONE_EVENT_ATTRIBUTE,
                                RTPSession.DEFAULT_AUDIO_CLOCK_RATE,
                                SDPAudioVideoMediaFormat.DEFAULT_AUDIO_CHANNEL_COUNT,
                                "0-16");
                            m_localTrack.Capabilities.Add(rtpEventFormat);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The remote video track. Will be null if the remote party is not sending this media
        /// </summary>
        public MediaStreamTrack RemoteTrack { get; set; }

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

        #endregion PROPERTIES

        #region REORDER BUFFER

        public void AddBuffer(TimeSpan dropPacketTimeout)
        {
            RTPReorderBuffer = new RTPReorderBuffer(dropPacketTimeout);
        }

        public void RemoveBuffer(TimeSpan dropPacketTimeout)
        {
            RTPReorderBuffer = null;
        }

        public Boolean UseBuffer()
        {
            return RTPReorderBuffer != null;
        }

        public RTPReorderBuffer GetBuffer()
        {
            return RTPReorderBuffer;
        }

        #endregion REORDER BUFFER

        #region SECURITY CONTEXT

        public void SetSecurityContext(ProtectRtpPacket protectRtp, ProtectRtpPacket unprotectRtp, ProtectRtpPacket protectRtcp, ProtectRtpPacket unprotectRtcp)
        {
            if (SecureContext != null)
            {
                logger.LogTrace($"Tried adding new SecureContext for media type {MediaType}, but one already existed");
            }

            SecureContext = new SecureContext(protectRtp, unprotectRtp, protectRtcp, unprotectRtcp);

            DispatchPendingPackages();
        }

        public SecureContext GetSecurityContext()
        {
            return SecureContext;
        }

        public Boolean IsSecurityContextReady()
        {
            return (SecureContext != null);
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
                    logger.LogWarning($"SRTP unprotect failed for {MediaType}, result {res}.");
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

        #endregion SECURITY CONTEXT

        #region RTP CHANNEL

        public void AddRtpChannel(RTPChannel rtpChannel)
        {
            this.rtpChannel = rtpChannel;
        }

        public Boolean HasRtpChannel()
        {
            return rtpChannel != null;
        }

        public RTPChannel GetRTPChannel()
        {
            return rtpChannel;
        }

        #endregion RTP CHANNEL

        #region SEND PACKET

        protected Boolean CheckIfCanSendRtpRaw()
        {
            if (IsClosed)
            {
                logger.LogWarning($"SendRtpRaw was called for an {MediaType} packet on an closed RTP session.");
                return false;
            }

            if (LocalTrack == null)
            {
                logger.LogWarning($"SendRtpRaw was called for an {MediaType} packet on an RTP session without a local track.");
                return false;
            }

            if ((LocalTrack.StreamStatus == MediaStreamStatusEnum.RecvOnly) || (LocalTrack.StreamStatus == MediaStreamStatusEnum.Inactive))
            {
                logger.LogWarning($"SendRtpRaw was called for an {MediaType} packet on an RTP session with a Stream Status set to {LocalTrack.StreamStatus}");
                return false;
            }

            if ((RtpSessionConfig.IsSecure || RtpSessionConfig.UseSdpCryptoNegotiation) && SecureContext?.ProtectRtpPacket == null)
            {
                logger.LogWarning("SendRtpPacket cannot be called on a secure session before calling SetSecurityContext.");
                return false;
            }

            return true;
        }

        protected void SendRtpRaw(byte[] data, uint timestamp, int markerBit, int payloadType, Boolean checkDone, ushort? seqNum = null)
        {
            if (checkDone || CheckIfCanSendRtpRaw())
            {
                ProtectRtpPacket protectRtpPacket = SecureContext?.ProtectRtpPacket;
                int srtpProtectionLength = (protectRtpPacket != null) ? RTPSession.SRTP_MAX_PREFIX_LENGTH : 0;

                RTPPacket rtpPacket = new RTPPacket(data.Length + srtpProtectionLength);
                rtpPacket.Header.SyncSource = LocalTrack.Ssrc;
                rtpPacket.Header.SequenceNumber = seqNum ?? LocalTrack.GetNextSeqNum();
                rtpPacket.Header.Timestamp = timestamp;
                rtpPacket.Header.MarkerBit = markerBit;
                rtpPacket.Header.PayloadType = payloadType;

                Buffer.BlockCopy(data, 0, rtpPacket.Payload, 0, data.Length);

                var rtpBuffer = rtpPacket.GetBytes();

                if (protectRtpPacket == null)
                {
                    rtpChannel.Send(RTPChannelSocketsEnum.RTP, DestinationEndPoint, rtpBuffer);
                }
                else
                {
                    int outBufLen = 0;
                    int rtperr = protectRtpPacket(rtpBuffer, rtpBuffer.Length - srtpProtectionLength, out outBufLen);
                    if (rtperr != 0)
                    {
                        logger.LogError("SendRTPPacket protection failed, result " + rtperr + ".");
                    }
                    else
                    {
                        rtpChannel.Send(RTPChannelSocketsEnum.RTP, DestinationEndPoint, rtpBuffer.Take(outBufLen).ToArray());
                    }
                }
                m_lastRtpTimestamp = timestamp;

                RtcpSession?.RecordRtpPacketSend(rtpPacket);
            }
        }

        /// <summary>
        /// Allows additional control for sending raw RTP payloads. No framing or other processing is carried out.
        /// </summary>
        /// <param name="mediaType">The media type of the RTP packet being sent. Must be audio or video.</param>
        /// <param name="payload">The RTP packet payload.</param>
        /// <param name="timestamp">The timestamp to set on the RTP header.</param>
        /// <param name="markerBit">The value to set on the RTP header marker bit, should be 0 or 1.</param>
        /// <param name="payloadTypeID">The payload ID to set in the RTP header.</param>
        /// <param name="seqNum"> The RTP sequence number </param>
        public void SendRtpRaw(byte[] data, uint timestamp, int markerBit, int payloadType, ushort seqNum)
        {
            SendRtpRaw(data, timestamp, markerBit, payloadType, false, seqNum);
        }

        /// <summary>
        /// Allows additional control for sending raw RTP payloads. No framing or other processing is carried out.
        /// </summary>
        /// <param name="mediaType">The media type of the RTP packet being sent. Must be audio or video.</param>
        /// <param name="payload">The RTP packet payload.</param>
        /// <param name="timestamp">The timestamp to set on the RTP header.</param>
        /// <param name="markerBit">The value to set on the RTP header marker bit, should be 0 or 1.</param>
        /// <param name="payloadTypeID">The payload ID to set in the RTP header.</param>
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
                    logger.LogWarning($"Can't create RTCPCompoundPacket from the provided RTCP bytes. {excp.Message}");
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
                //logger.LogDebug($"SendRtcpReport: {reportBytes.HexStr()}");

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

                    int outBufLen = 0;
                    int rtperr = protectRtcpPacket(sendBuffer, sendBuffer.Length - RTPSession.SRTP_MAX_PREFIX_LENGTH, out outBufLen);
                    if (rtperr != 0)
                    {
                        logger.LogWarning("SRTP RTCP packet protection failed, result " + rtperr + ".");
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
        /// <param name="mediaType">The media type of the RTCP report  being sent. Must be audio or video.</param>
        /// <param name="feedback">The feedback report to send.</param>
        public void SendRtcpFeedback(RTCPFeedback feedback)
        {
            var reportBytes = feedback.GetBytes();
            SendRtcpReport(reportBytes);
        }

        #endregion SEND PACKET

        #region RECEIVE PACKET

        public void OnReceiveRTPPacket(RTPHeader hdr, int localPort, IPEndPoint remoteEndPoint, byte[] buffer, VideoStream videoStream = null)
        {
            RTPPacket rtpPacket = null;
            if (RemoteRtpEventPayloadID != 0 && hdr.PayloadType == RemoteRtpEventPayloadID)
            {
                if (!EnsureBufferUnprotected(buffer, hdr, out rtpPacket))
                {
                    // Cache pending packages to use it later to prevent missing frames
                    // when DTLS was not completed yet as a Server bt already completed as a client
                    AddPendingPackage(hdr, localPort, remoteEndPoint, buffer, videoStream);
                    return;
                }

                RaiseOnRtpEventByIndex(remoteEndPoint, new RTPEvent(rtpPacket.Payload), rtpPacket.Header);
                return;
            }

            // Set the remote track SSRC so that RTCP reports can match the media type.
            if (RemoteTrack != null && RemoteTrack.Ssrc == 0 && DestinationEndPoint != null)
            {
                bool isValidSource = AdjustRemoteEndPoint(hdr.SyncSource, remoteEndPoint);

                if (isValidSource)
                {
                    logger.LogDebug($"Set remote track ({MediaType} - index={Index}) SSRC to {hdr.SyncSource}.");
                    RemoteTrack.Ssrc = hdr.SyncSource;
                }
            }


            // Note AC 24 Dec 2020: The problem with waiting until the remote description is set is that the remote peer often starts sending
            // RTP packets at the same time it signals its SDP offer or answer. Generally this is not a problem for audio but for video streams
            // the first RTP packet(s) are the key frame and if they are ignored the video stream will take additional time or manual 
            // intervention to synchronise.
            //if (RemoteDescription != null)
            //{

            // Don't hand RTP packets to the application until the remote description has been set. Without it
            // things like the common codec, DTMF support etc. are not known.

            //SDPMediaTypesEnum mediaType = (rtpMediaType.HasValue) ? rtpMediaType.Value : DEFAULT_MEDIA_TYPE;

            // For video RTP packets an attempt will be made to collate into frames. It's up to the application
            // whether it wants to subscribe to frames of RTP packets.

            rtpPacket = null;
            if (RemoteTrack != null)
            {
                LogIfWrongSeqNumber($"{MediaType}", hdr, RemoteTrack);
                ProcessHeaderExtensions(hdr);
            }
            if (!EnsureBufferUnprotected(buffer, hdr, out rtpPacket))
            {
                return;
            }

            // When receiving an Payload from other peer, it will be related to our LocalDescription,
            // not to RemoteDescription (as proved by Azure WebRTC Implementation)
            var format = LocalTrack?.GetFormatForPayloadID(hdr.PayloadType);
            if ((rtpPacket != null) && (format != null))
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
                        videoStream?.ProcessVideoRtpFrame(remoteEndPoint, bufferedPacket, format.Value);
                        RaiseOnRtpPacketReceivedByIndex(remoteEndPoint, bufferedPacket);
                    }
                }
                else
                {
                    videoStream?.ProcessVideoRtpFrame(remoteEndPoint, rtpPacket, format.Value);
                    RaiseOnRtpPacketReceivedByIndex(remoteEndPoint, rtpPacket);
                }

                RtcpSession?.RecordRtpPacketReceived(rtpPacket);
            }
        }

        #endregion RECEIVE PACKET

        #region TO RAISE EVENTS FROM INHERITED CLASS

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

        #endregion TO RAISE EVENTS FROM INHERITED CLASS

        #region PENDING PACKAGES LOGIC

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
                        OnReceiveRTPPacket(pendingPackage.hdr, pendingPackage.localPort, pendingPackage.remoteEndPoint, pendingPackage.buffer, pendingPackage.videoStream);
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
        protected virtual bool AddPendingPackage(RTPHeader hdr, int localPort, IPEndPoint remoteEndPoint, byte[] buffer, VideoStream videoStream = null)
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
                    _pendingPackagesBuffer.Add(new PendingPackages(hdr, localPort, remoteEndPoint, buffer, videoStream));
                }
                return true;
            }
            return false;
        }

        #endregion

        protected void LogIfWrongSeqNumber(string trackType, RTPHeader header, MediaStreamTrack track)
        {
            if (track.LastRemoteSeqNum != 0 &&
                header.SequenceNumber != (track.LastRemoteSeqNum + 1) &&
                !(header.SequenceNumber == 0 && track.LastRemoteSeqNum == ushort.MaxValue))
            {
                logger.LogWarning($"{trackType} stream sequence number jumped from {track.LastRemoteSeqNum} to {header.SequenceNumber}.");
            }
        }

        /// <summary>
        /// Adjusts the expected remote end point for a particular media type.
        /// </summary>
        /// <param name="mediaType">The media type of the RTP packet received.</param>
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
                logger.LogDebug($"{MediaType} end point switched for RTP ssrc {ssrc} from {expectedEndPoint} to {receivedOnEndPoint}.");

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
                logger.LogWarning($"RTP packet with SSRC {ssrc} received from unrecognised end point {receivedOnEndPoint}.");
            }

            return isValidSource;
        }

        /// <summary>
        /// Creates a new RTCP session for a media track belonging to this RTP session.
        /// </summary>
        /// <param name="mediaType">The media type to create the RTP session for. Must be
        /// audio or video.</param>
        /// <returns>A new RTCPSession object. The RTCPSession must have its Start method called
        /// in order to commence sending RTCP reports.</returns>
        public Boolean CreateRtcpSession()
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
        /// <param name="mediaType">The media type, must be audio or video, to set the remote end point for.</param>
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
                        .Where(x => x.ID != RemoteRtpEventPayloadID).FirstOrDefault();
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

        public void ProcessHeaderExtensions(RTPHeader header)
        {
            header.GetHeaderExtensions().ToList().ForEach(x =>
            {
                if (RemoteTrack != null)
                {
                    var ntpTimestamp = x.GetNtpTimestamp(RemoteTrack.HeaderExtensions);
                    if (ntpTimestamp.HasValue)
                    {
                        RemoteTrack.LastAbsoluteCaptureTimestamp = new TimestampPair() { NtpTimestamp = ntpTimestamp.Value, RtpTimestamp = header.Timestamp };
                    }
                }
            });
        }

        public MediaStream(RtpSessionConfig config, int index)
        {
            RtpSessionConfig = config;
            this.Index = index;
        }
    }
}
