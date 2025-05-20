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
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public class MediaStream
{
    protected internal sealed class PendingPackages
    {
        public RTPHeader hdr { get; }
        public int localPort { get; }
        public IPEndPoint remoteEndPoint { get; }
        public byte[] buffer { get; }

        public PendingPackages(RTPHeader hdr, int localPort, IPEndPoint remoteEndPoint, byte[] buffer)
        {
            this.hdr = hdr;
            this.localPort = localPort;
            this.remoteEndPoint = remoteEndPoint;
            this.buffer = buffer;
        }

        public PendingPackages(RTPHeader hdr, int localPort, IPEndPoint remoteEndPoint, ReadOnlySpan<byte> buffer)
            : this(hdr, localPort, remoteEndPoint, buffer.ToArray())
        {
        }
    }

    protected object _pendingPackagesLock = new object();
    protected List<PendingPackages> _pendingPackagesBuffer = new List<PendingPackages>();

    private static ILogger logger = Log.Logger;

    private RtpSessionConfig RtpSessionConfig;

    protected SecureContext? SecureContext;
    protected SrtpHandler? SrtpHandler;

    private RTPReorderBuffer? RTPReorderBuffer;

    private MediaStreamTrack? m_localTrack;
    private MediaStreamTrack? m_remoteTrack;

    protected RTPChannel? rtpChannel;

    protected bool _isClosed;
    /// <summary>
    /// Used for keeping track of TWCC packets
    /// </summary>
    private ushort _twccPacketCount;

    public int Index = -1;

    /// <summary>
    /// Fires when the connection for a media type is classified as timed out due to not
    /// receiving any RTP or RTCP packets within the given period.
    /// </summary>
    public event Action<int, SDPMediaTypesEnum>? OnTimeoutByIndex;

    /// <summary>
    /// Gets fired when an RTCP report is sent. This event is for diagnostics only.
    /// </summary>
    public event Action<int, SDPMediaTypesEnum, RTCPCompoundPacket>? OnSendReportByIndex;

    /// <summary>
    /// Gets fired when an RTP packet is received from a remote party.
    /// Parameters are:
    ///  - index of the AudioStream or VideoStream
    ///  - Remote endpoint packet was received from,
    ///  - The media type the packet contains, will be audio or video,
    ///  - The full RTP packet.
    /// </summary>
    public event Action<int, IPEndPoint, SDPMediaTypesEnum, RTPPacket>? OnRtpPacketReceivedByIndex;

    /// <summary>
    /// Gets fired when an RTP Header packet is received from a remote party.
    /// Parameters are:
    ///  - index of the AudioStream or VideoStream
    ///  - Remote endpoint packet was received from,
    ///  - The media type the packet contains, will be audio or video,
    ///  - The RTP Header exension URI.
    /// </summary>
    public event Action<int, IPEndPoint, SDPMediaTypesEnum, string, object>? OnRtpHeaderReceivedByIndex;

    /// <summary>
    /// Gets fired when an RTP event is detected on the remote call party's RTP stream.
    /// </summary>
    public event Action<int, IPEndPoint, RTPEvent, RTPHeader>? OnRtpEventByIndex;

    /// <summary>
    /// Gets fired when an RTCP report is received. This event is for diagnostics only.
    /// </summary>
    public event Action<int, IPEndPoint, SDPMediaTypesEnum, RTCPCompoundPacket>? OnReceiveReportByIndex;

    public event Action<bool>? OnIsClosedStateChanged;

    public bool AcceptRtpFromAny { get; set; }

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
    public MediaStreamTrack? LocalTrack
    {
        get
        {
            return m_localTrack;
        }
        set
        {
            m_localTrack = value;
            if (m_localTrack is { })
            {
                // Need to create a sending SSRC and set it on the RTCP session. 
                if (RtcpSession is { })
                {
                    RtcpSession.Ssrc = m_localTrack.Ssrc;
                }

                if (MediaType == SDPMediaTypesEnum.audio)
                {
                    if (m_localTrack.Capabilities is { Count: > 0 } && !m_localTrack.NoDtmfSupport &&
                        !m_localTrack.Capabilities.Exists(x => x.ID == RTPSession.DEFAULT_DTMF_EVENT_PAYLOAD_ID))
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
    public MediaStreamTrack? RemoteTrack
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
    public RTCPSession? RtcpSession { get; set; }

    /// <summary>
    /// The remote RTP end point this stream is sending media to.
    /// </summary>
    public IPEndPoint? DestinationEndPoint { get; set; }

    /// <summary>
    /// The remote RTP control end point this stream is sending to RTCP reports for the media stream to.
    /// </summary>
    public IPEndPoint? ControlDestinationEndPoint { get; set; }

    /// <summary>
    /// This endpoint is used when a relay server (TURN) is being used for the RTP session. All RTP packets
    /// will be sent to the relay end point instead of the DestinationEndPoint.
    /// </summary>
    public TurnRelayEndPoint? RtpRelayEndPoint { get; set; }

    /// <summary>
    /// This endpoint is used when a relay server (TURN) is being used for the RTCP session. All RTCP packets
    /// will be sent to the relay end point instead of the ControlDestinationEndPoint.
    /// </summary>
    public IPEndPoint? RelayControlDestinationEndPoint { get; set; }

    /// <summary>
    /// If set to true indicates the RTP and RTCP sockets are for a relay server (TURN).
    /// All traffic for the session should then be sent to/from the relay and not updated.
    /// </summary>
    public bool IsUsingRelayEndPoint => RtpRelayEndPoint != null;

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
        return RTPReorderBuffer is { };
    }

    public RTPReorderBuffer? GetBuffer()
    {
        return RTPReorderBuffer;
    }

    public void SetSecurityContext(ProtectRtpPacket protectRtp, ProtectRtpPacket unprotectRtp, ProtectRtpPacket protectRtcp, ProtectRtpPacket unprotectRtcp)
    {
        if (SecureContext is { })
        {
            logger.LogRtpSessionSecureContextAlreadyExists(MediaType);
        }

        SecureContext = new SecureContext(protectRtp, unprotectRtp, protectRtcp, unprotectRtcp);

        DispatchPendingPackages();
    }

    public SecureContext? GetSecurityContext()
    {
        return SecureContext;
    }

    public bool IsSecurityContextReady()
    {
        return SecureContext is { };
    }

    public SrtpHandler GetOrCreateSrtpHandler()
    {
        if (SrtpHandler is null)
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
        return rtpChannel is { };
    }

    public RTPChannel? GetRTPChannel()
    {
        return rtpChannel;
    }

    protected bool CheckIfCanSendRtpRaw()
    {
        if (IsClosed)
        {
            logger.LogRtpSessionSendRtpRawOnClosedSession(MediaType);
            return false;
        }

        if (LocalTrack is null)
        {
            logger.LogRtpSessionSendRtpRawNoLocalTrack(MediaType);
            return false;
        }

        if (LocalTrack.StreamStatus is MediaStreamStatusEnum.RecvOnly or MediaStreamStatusEnum.Inactive)
        {
            logger.LogRtpSessionSendRtpRawInactiveStream(MediaType, LocalTrack.StreamStatus);
            return false;
        }

        if ((RtpSessionConfig.IsSecure || RtpSessionConfig.UseSdpCryptoNegotiation) && SecureContext?.ProtectRtpPacket is null)
        {
            logger.LogRtpSessionSendRtpPacketSecureContextNotReady();
            return false;
        }

        return true;
    }

    protected void SendRtpRaw(ReadOnlySpan<byte> data, uint timestamp, int markerBit, int payloadType, bool checkDone, ushort? seqNum = null)
    {
        if (checkDone || CheckIfCanSendRtpRaw())
        {
            var protectRtpPacket = SecureContext?.ProtectRtpPacket;
            var srtpProtectionLength = (protectRtpPacket is { }) ? RTPSession.SRTP_MAX_PREFIX_LENGTH : 0;

            var packetPayloadLength = data.Length + srtpProtectionLength;
            var packetPayload = new Memory<byte>(new byte[packetPayloadLength]);
            var rtpPacket = new RTPPacket(new RTPHeader(), packetPayload);
            Debug.Assert(LocalTrack is { });
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
                using var extensionBuffer = new PooledSegmentedBuffer<byte>();

                foreach (var ext in LocalTrack.HeaderExtensions.Values)
                {
                    // We support up to 14 extensions .... Not clear at all how to manage more ...
                    if (ext.Id is < 1 or > 14)
                    {
                        continue;
                    }

                    var extPayLoad = ext.Marshal();
                    extPayLoad.CopyTo(extensionBuffer.GetSpan(extPayLoad.Length));
                    extensionBuffer.Advance(extPayLoad.Length);
                }

                var payloadLength = extensionBuffer.Length;
                if (payloadLength > 0)
                {
                    // Need to round to 4 bytes boundaries
                    var roundedExtSize = payloadLength % 4;
                    if (roundedExtSize > 0)
                    {
                        var paddingLength = 4 - roundedExtSize;
                        extensionBuffer.GetSpan(paddingLength)[..paddingLength].Clear(); // Add zero padding
                        extensionBuffer.Advance(paddingLength);
                    }

                    var payload = extensionBuffer.ToArray();

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

            data.CopyTo(packetPayload.Span);

            var rtpPacketSize = rtpPacket.GetByteCount();
            var buffer = ArrayPool<byte>.Shared.Rent(rtpPacketSize);

            try
            {
                var rtpPacketBytesWritten = rtpPacket.WriteBytes(buffer);

                if (protectRtpPacket is null)
                {
                    Debug.Assert(rtpChannel is { });
                    Debug.Assert(DestinationEndPoint is { });
                    rtpChannel.Send(
                        RTPChannelSocketsEnum.RTP,
                        DestinationEndPoint,
                        new ReadOnlyMemory<byte>(buffer, 0, rtpPacketBytesWritten));
                }
                else
                {
                    var rtperr = protectRtpPacket(buffer, rtpPacketBytesWritten - srtpProtectionLength, out var outBufLen);
                    if (rtperr != 0)
                    {
                        logger.LogRtpChannelSendRtpPacketProtectionFailed(rtperr);
                    }
                    else
                    {
                        Debug.Assert(rtpChannel is { });
                        Debug.Assert(DestinationEndPoint is { });
                        rtpChannel.Send(
                            RTPChannelSocketsEnum.RTP,
                            DestinationEndPoint,
                            new ReadOnlyMemory<byte>(buffer, 0, outBufLen));
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            RtcpSession?.RecordRtpPacketSend(rtpPacket);
        }
    }

    protected void SendRtpRaw(byte[] data, uint timestamp, int markerBit, int payloadType, bool checkDone, ushort? seqNum = null)
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
    public void SetRtpHeaderExtensionValue(string uri, object? value)
    {
        try
        {
            if (LocalTrack?.HeaderExtensions?.Values is null)
            {
                return;
            }

            foreach (var ext in LocalTrack.HeaderExtensions.Values)
            {
                if (ext.Uri != uri)
                {
                    continue;
                }

                switch (ext)
                {
                    case CVOExtension cvoExtension when uri == CVOExtension.RTP_HEADER_EXTENSION_URI:
                        cvoExtension.Set(value);
                        return;

                    case AudioLevelExtension audioLevelExtension when uri == AudioLevelExtension.RTP_HEADER_EXTENSION_URI:
                        audioLevelExtension.Set(value);
                        return;

                    //case TransportWideCCExtension.RTP_HEADER_EXTENSION_URI_ALT:
                    case TransportWideCCExtension transportWideCCExtension when uri == TransportWideCCExtension.RTP_HEADER_EXTENSION_URI:
                        transportWideCCExtension.Set(_twccPacketCount++);
                        return;

                    // Not necessary to set something in AbsSendTimeExtension - just to be coherent here
                    case AbsSendTimeExtension absSendTimeExtension when uri == AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI:
                        absSendTimeExtension.Set(value);
                        return;

                    default:
                        return;
                }
            }
        }
        catch
        {
            // Consider logging the exception or handling it appropriately
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
    public void SendRtpRaw(ReadOnlySpan<byte> data, uint timestamp, int markerBit, int payloadType, ushort seqNum)
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
    public void SendRtpRaw(ReadOnlySpan<byte> data, uint timestamp, int markerBit, int payloadType)
    {
        SendRtpRaw(data, timestamp, markerBit, payloadType, false);
    }

    /// <summary>
    /// Allows additional control for sending raw RTCP payloads.
    /// </summary>
    /// <param name="rtcpBytes">Raw RTCP report data to send.</param>
    public void SendRtcpRaw(ReadOnlySpan<byte> rtcpBytes)
    {
        if (SendRtcpReportCore(rtcpBytes))
        {
            RTCPCompoundPacket? rtcpCompoundPacket = null;
            try
            {
                rtcpCompoundPacket = new RTCPCompoundPacket(rtcpBytes);
            }
            catch (Exception excp)
            {
                logger.LogRtpCannotCreateRtcpCompoundPacket(excp);
            }

            if (rtcpCompoundPacket is { })
            {
                OnSendReportByIndex?.Invoke(Index, MediaType, rtcpCompoundPacket);
            }
        }
    }

    /// <summary>
    /// Sends the RTCP report to the remote call party.
    /// </summary>
    /// <param name="rtcpPayload">The unprotected RTCP payload to transmit.</param>
    /// <returns>
    /// <see langword="true"/> if the report was (or was considered) sent. Returns <see langword="false"/> only when SRTP is required
    /// but the security context is not yet ready. If no <see cref="ControlDestinationEndPoint"/>
    /// is set the method returns <see langword="true"/> (nothing to send).
    /// </returns>
    private bool SendRtcpReportCore(ReadOnlySpan<byte> rtcpPayload)
    {
        if ((RtpSessionConfig.IsSecure || RtpSessionConfig.UseSdpCryptoNegotiation) && !IsSecurityContextReady())
        {
            logger.LogRtpSrtpReportNotReady();
            return false;
        }
        else if (ControlDestinationEndPoint is { })
        {
            var sendOnSocket = RtpSessionConfig.IsRtcpMultiplexed ? RTPChannelSocketsEnum.RTP : RTPChannelSocketsEnum.Control;
            var protectRtcpPacket = SecureContext?.ProtectRtcpPacket;
            if (protectRtcpPacket is null)
            {
                Debug.Assert(rtpChannel is { });
                var memoryOwner = MemoryPool<byte>.Shared.Rent(rtcpPayload.Length);
                rtcpPayload.CopyTo(memoryOwner.Memory.Span);
                rtpChannel.Send(sendOnSocket, ControlDestinationEndPoint, memoryOwner.Memory.Slice(0, rtcpPayload.Length), memoryOwner);
            }
            else
            {
                var unprotectedLength = rtcpPayload.Length;
                var memoryOwner = MemoryPool<byte>.Shared.Rent(unprotectedLength + RTPSession.SRTP_MAX_PREFIX_LENGTH);
                _ = MemoryMarshal.TryGetArray<byte>(memoryOwner.Memory, out var segment);
                rtcpPayload.CopyTo(memoryOwner.Memory.Span); // copy unprotected payload first
                var rtperr = protectRtcpPacket(segment.Array!, unprotectedLength, out var outBufLen);
                if (rtperr != 0)
                {
                    memoryOwner.Dispose();
                    logger.LogRtpSrtpRtcpProtectFailed(rtperr);
                }
                else
                {
                    Debug.Assert(rtpChannel is { });
                    rtpChannel.Send(sendOnSocket, ControlDestinationEndPoint, memoryOwner.Memory.Slice(0, outBufLen), memoryOwner);
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Sends an RTCP compound or feedback report to the remote party.
    /// </summary>
    /// <param name="report">The RTCP report implementing <see cref="IByteSerializable"/> to serialise and send (unprotected size).
    /// When SRTP is active additional space (SRTP_MAX_PREFIX_LENGTH) is temporarily rented to
    /// accommodate authentication/tag data.</param>
    /// <returns>
    /// True if the report was (or was considered) sent. Returns false only when SRTP is required
    /// but the security context is not yet ready. If no <see cref="ControlDestinationEndPoint"/>
    /// is set the method returns true (nothing to send).
    /// </returns>
    private bool SendRtcpReportCore(IByteSerializable report)
    {
        if ((RtpSessionConfig.IsSecure || RtpSessionConfig.UseSdpCryptoNegotiation) && !IsSecurityContextReady())
        {
            logger.LogRtpSrtpReportNotReady();
            return false;
        }
        else if (ControlDestinationEndPoint is { })
        {
            var sendOnSocket = RtpSessionConfig.IsRtcpMultiplexed ? RTPChannelSocketsEnum.RTP : RTPChannelSocketsEnum.Control;
            var protectRtcpPacket = SecureContext?.ProtectRtcpPacket;

            var reportLength = report.GetByteCount();

            Debug.Assert(rtpChannel is { });

            if (protectRtcpPacket is null)
            {
                var memoryOwner = MemoryPool<byte>.Shared.Rent(reportLength);
                report.WriteBytes(memoryOwner.Memory.Span);
                rtpChannel.Send(sendOnSocket, ControlDestinationEndPoint, memoryOwner.Memory.Slice(0, reportLength), null);
            }
            else
            {
                var memoryOwner = MemoryPool<byte>.Shared.Rent(reportLength + RTPSession.SRTP_MAX_PREFIX_LENGTH);
                report.WriteBytes(memoryOwner.Memory.Span); // copy unprotected payload first

                _ = MemoryMarshal.TryGetArray<byte>(memoryOwner.Memory, out var segment);
                var rtperr = protectRtcpPacket(segment.Array!, reportLength, out var outBufLen);
                if (rtperr != 0)
                {
                    memoryOwner.Dispose();
                    logger.LogRtpSrtpRtcpProtectFailed(rtperr);
                }
                else
                {
                    Debug.Assert(rtpChannel is { });
                    rtpChannel.Send(sendOnSocket, ControlDestinationEndPoint, memoryOwner.Memory.Slice(0, outBufLen), memoryOwner);
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
        if ((RtpSessionConfig.IsSecure || RtpSessionConfig.UseSdpCryptoNegotiation) && !IsSecurityContextReady() && report.Bye is { })
        {
            // Do nothing. The RTCP BYE gets generated when an RTP session is closed.
            // If that occurs before the connection was able to set up the secure context
            // there's no point trying to send it.
        }
        else
        {
            SendRtcpReportCore(report);
            OnSendReportByIndex?.Invoke(Index, MediaType, report);
        }
    }

    /// <summary>
    /// Allows sending of RTCP feedback reports.
    /// </summary>
    /// <param name="feedback">The feedback report to send.</param>
    public void SendRtcpFeedback(RTCPFeedback feedback)
    {
        SendRtcpReportCore(feedback);
    }

    /// <summary>
    /// Allows sending of RTCP TWCC feedback reports.
    /// </summary>
    /// <param name="feedback">The feedback report to send.</param>
    public void SendRtcpTWCCFeedback(RTCPTWCCFeedback feedback)
    {
        SendRtcpReportCore(feedback);
    }

    public void OnReceiveRTPPacket(RTPHeader hdr, int localPort, IPEndPoint remoteEndPoint, ReadOnlyMemory<byte> buffer, VideoStream? videoStream = null)
    {
        if (NegotiatedRtpEventPayloadID != 0 && hdr.PayloadType == NegotiatedRtpEventPayloadID)
        {
            if (!EnsureBufferUnprotected(buffer, hdr, out var rtpPacket))
            {
                Debug.Assert(videoStream is { });

                // Cache pending packages to use it later to prevent missing frames
                // when DTLS was not completed yet as a Server bt already completed as a client
                AddPendingPackage(hdr, localPort, remoteEndPoint, buffer.Span, videoStream);
                return;
            }

            Debug.Assert(rtpPacket is { });

            RaiseOnRtpEventByIndex(remoteEndPoint, new RTPEvent(rtpPacket.Payload.Span), rtpPacket.Header);
            return;
        }

        // Set the remote track SSRC so that RTCP reports can match the media type.
        if (RemoteTrack is { } && RemoteTrack.Ssrc == 0 && DestinationEndPoint is { })
        {
            var isValidSource = AdjustRemoteEndPoint(hdr.SyncSource, remoteEndPoint);

            if (isValidSource)
            {
                logger.LogRtpSessionSetRemoteTrackSsrc(MediaType, Index, hdr.SyncSource);
                RemoteTrack.Ssrc = hdr.SyncSource;
            }
        }

        if (RemoteTrack is { })
        {
            LogIfWrongSeqNumber($"{MediaType}", hdr, RemoteTrack);
            ProcessHeaderExtensions(hdr, remoteEndPoint);
        }

        {
            if (!EnsureBufferUnprotected(buffer, hdr, out var rtpPacket))
            {
                return;
            }

            // When receiving an Payload from other peer, it will be related to our LocalDescription,
            // not to RemoteDescription (as proved by Azure WebRTC Implementation)
            var format = LocalTrack?.GetFormatForPayloadID(hdr.PayloadType);
            if ((rtpPacket is { }) && (format is { }))
            {
                if (UseBuffer())
                {
                    var reorderBuffer = GetBuffer();
                    Debug.Assert(reorderBuffer is { });
                    reorderBuffer.Add(rtpPacket);
                    while (reorderBuffer.Get(out var bufferedPacket))
                    {
                        if (RemoteTrack is { })
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

        bool EnsureBufferUnprotected(ReadOnlyMemory<byte> buf, RTPHeader header, [NotNullWhen(true)] out RTPPacket? packet)
        {
            if (RtpSessionConfig.IsSecure || RtpSessionConfig.UseSdpCryptoNegotiation)
            {
                if (SecureContext is { })
                {
                    if (MemoryMarshal.TryGetArray(buf, out var segment) && segment.Offset == 0 && segment.Array is { })
                    {
                        packet = CreateRtpPacket(segment.Array, segment.Count);
                        if (packet is null)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        var tempBuf = ArrayPool<byte>.Shared.Rent(buf.Length);
                        try
                        {
                            buf.CopyTo(tempBuf);
                            packet = CreateRtpPacket(tempBuf, buf.Length);
                            if (packet is null)
                            {
                                return false;
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(tempBuf);
                        }
                    }

                    RTPPacket? CreateRtpPacket(byte[] array, int length)
                    {
                        var res = SecureContext.UnprotectRtpPacket(array, length, out var outBufLen);
                        if (res != 0)
                        {
                            logger.LogRtpSrtpRtcpUnprotectFailed(MediaType, res);
                            return null;
                        }

                        return new RTPPacket(array.AsSpan(0, outBufLen).ToArray());
                    }
                }
                else
                {
                    packet = null;
                    return false;
                }
            }
            else
            {
                packet = new RTPPacket(buf);
            }

            packet.Header.ReceivedTime = header.ReceivedTime;
            return true;
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
        PendingPackages[]? pendingPackagesArray = null;

        var isContextValid = SecureContext is { } && !IsClosed;

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
            Debug.Assert(pendingPackagesArray is { });
            foreach (var pendingPackage in pendingPackagesArray)
            {
                if (pendingPackage is { })
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
    protected virtual bool AddPendingPackage(RTPHeader hdr, int localPort, IPEndPoint remoteEndPoint, ReadOnlySpan<byte> buffer, VideoStream? videoStream = null)
    {
        const int MAX_PENDING_PACKAGES_BUFFER_SIZE = 32;

        if (SecureContext is null && !IsClosed)
        {
            lock (_pendingPackagesLock)
            {
                //ensure buffer max size
                while (_pendingPackagesBuffer.Count is > 0 and >= MAX_PENDING_PACKAGES_BUFFER_SIZE)
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
            logger.LogRtpSequenceNumberJumped(trackType, track.LastRemoteSeqNum, header.SequenceNumber);
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
        var isValidSource = false;
        var expectedEndPoint = DestinationEndPoint;

        Debug.Assert(expectedEndPoint is { });
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
            logger.LogRtpSessionEndPointSwitched(MediaType, ssrc, expectedEndPoint, receivedOnEndPoint);

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
            logger.LogRtpSessionUnrecognisedEndPoint(ssrc, receivedOnEndPoint);
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
        if (RtcpSession is null)
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
    public void SetDestination(IPEndPoint? rtpEndPoint, IPEndPoint? rtcpEndPoint)
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
        if (LocalTrack is { } || RemoteTrack is { })
        {
            if (LocalTrack is null)
            {
                Debug.Assert(RemoteTrack is { Capabilities: { Count: > 0 } });
                return RemoteTrack.Capabilities[0];
            }
            else if (RemoteTrack is null)
            {
                Debug.Assert(LocalTrack is { Capabilities: { Count: > 0 } });
                return LocalTrack.Capabilities[0];
            }

            var format = MediaType == SDPMediaTypesEnum.audio
                ? SDPAudioVideoMediaFormat.GetFirstCompatibleFormatExcluding(RemoteTrack.Capabilities, LocalTrack.Capabilities, NegotiatedRtpEventPayloadID)
                : SDPAudioVideoMediaFormat.GetFirstCompatibleFormat(RemoteTrack.Capabilities, LocalTrack.Capabilities);
            if (format.IsEmpty())
            {
                // It's not expected that this occurs as a compatibility check is done when the remote session description
                // is set. By this point a compatible codec should be available.
                throw new SipSorceryException($"No compatible sending format could be found for media {MediaType}.");
            }
            else
            {
                return format;
            }
        }
        else
        {
            throw new SipSorceryException($"Cannot get the {MediaType} sending format, missing either local or remote {MediaType} track.");
        }
    }

    public void ProcessHeaderExtensions(RTPHeader header, IPEndPoint remoteEndPoint)
    {
        if (OnRtpHeaderReceivedByIndex is { } onRtpHeaderReceivedByIndex
            && RemoteTrack?.HeaderExtensions is { Count: 0 } headerExtensions)
        {
            // Only now do the expensive header extension parsing
            foreach (var rtpHeaderExtensionData in header.GetHeaderExtensions())
            {
                if (headerExtensions.TryGetValue(rtpHeaderExtensionData.Id, out var rtpHeaderExtension))
                {
                    var value = rtpHeaderExtension.Unmarshal(header, rtpHeaderExtensionData.Data);
                    onRtpHeaderReceivedByIndex(Index, remoteEndPoint, MediaType, rtpHeaderExtension.Uri, value);
                }
            }
        }
    }

    /// <summary>
    /// Gets the RTP port to use in the SDP offer or answer.
    /// </summary>
    public int GetRtpPortForSessionDescription()
    {
        if (IsUsingRelayEndPoint)
        {
            return RtpRelayEndPoint.RemotePeerRelayEndPoint.Port;
        }

        return rtpChannel switch
        {
            null => 0,
            _ when rtpChannel.RTPSrflxEndPoint != null => rtpChannel.RTPSrflxEndPoint.Port,
            _ => rtpChannel.RTPPort
        };
    }
}
